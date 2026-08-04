using BepInEx.Logging;
using Fusion;
using Gameplay.Player.Components;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using Networking.Lobby;
using Networking.Party;
using TMPro;
using UI;
using UI.Buttons;
using UI.Views.Lobby;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SneakOut.NetworkHostSelector;

internal static class NetworkHostSelectorRuntime
{
    private const string PropertyState = "sohs_s";
    private const string PropertyPeers = "sohs_e";
    private const string PropertyRequest = "sohs_q";
    private const float NetworkTickInterval = 0.25f;
    private const float UiRefreshInterval = 0.25f;
    private const float HelloInterval = 1f;
    private const float AckInterval = 0.5f;
    private const float ButtonHeight = 36f;
    private const float ButtonGap = 6f;

    private static readonly Dictionary<IntPtr, NetworkHostSelectorUiState> UiStateByView = new();
    private static readonly Dictionary<IntPtr, SpookedNetworkPlayer> ObservedPlayers = new();

    private static ManualLogSource? _logger;
    private static NetworkHostSelectorConfig? _configuration;
    private static Harmony? _harmony;
    private static GameUIManager? _gameUiManager;
    private static bool _watcherInstalled;
    private static IntPtr _runnerPointer;
    private static float _nextNetworkTick;
    private static float _nextUiRefreshAt;
    private static float _nextHelloAt;
    private static float _nextAckAt;
    private static string _coordinatorMembership = string.Empty;
    private static bool _coordinatorCompatible;
    private static int _coordinatorRevision;
    private static int _coordinatorTargetRaw;
    private static string _coordinatorTargetUserId = string.Empty;
    private static int _publishedRevision = -1;
    private static int _publishedTargetRaw = int.MinValue;
    private static string _publishedTargetUserId = string.Empty;
    private static string _publishedMembership = string.Empty;
    private static bool _publishedCompatible;
    private static bool _publishedReady;
    private static int _observedRevision;
    private static int _observedTargetRaw;
    private static string _observedTargetUserId = string.Empty;
    private static string _observedMembership = string.Empty;
    private static bool _observedCompatible;
    private static bool _observedReady;
    private static bool _observedValid;
    private static int _lastAckedRevision = -1;
    private static int _pendingRequestedRaw = int.MinValue;
    private static int _requestSequence;
    private static string _lastHandledRequest = string.Empty;
    private static bool _localOnlySession;
    private static IReadOnlyList<Participant> _cachedParticipants = Array.Empty<Participant>();

    public static void Initialize(ManualLogSource logger, NetworkHostSelectorConfig configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _harmony ??= new Harmony(NetworkHostSelectorPlugin.PluginGuid);
        _harmony.PatchAll();
        EnsureWatcher();
    }

    public static void BindPortalManager(GameUIManager gameUiManager)
    {
        _gameUiManager = gameUiManager;
    }

    public static void ObservePlayer(SpookedNetworkPlayer player)
    {
        if (player is not null && player.Pointer != IntPtr.Zero)
        {
            ObservedPlayers[player.Pointer] = player;
        }
    }

    public static void ForgetPlayer(SpookedNetworkPlayer player)
    {
        if (player is not null)
        {
            ObservedPlayers.Remove(player.Pointer);
        }
    }

    private static bool Enabled => _configuration is not null && _configuration.EnableMod.Value;

    private static bool LoggingEnabled => _configuration is not null && _configuration.EnableLogging.Value;

    private static void EnsureWatcher()
    {
        if (_watcherInstalled)
        {
            return;
        }

        ClassInjector.RegisterTypeInIl2Cpp<NetworkHostSelectorWatcher>();
        var watcherObject = new GameObject("NetworkHostSelectorWatcher");
        watcherObject.hideFlags = HideFlags.HideAndDontSave;
        UnityEngine.Object.DontDestroyOnLoad(watcherObject);
        watcherObject.AddComponent<NetworkHostSelectorWatcher>();
        _watcherInstalled = true;
    }

    private static void WatcherTick()
    {
        if (!Enabled)
        {
            ReleaseAllUi();
            return;
        }

        var now = Time.unscaledTime;
        if (now >= _nextNetworkTick)
        {
            _nextNetworkTick = now + NetworkTickInterval;
            TickNetwork(now);
        }

        if (now < _nextUiRefreshAt)
        {
            return;
        }
        _nextUiRefreshAt = now + UiRefreshInterval;

        var view = _gameUiManager?._portalPlayView;
        if (view is null
            || view.Pointer == IntPtr.Zero
            || view._playButton is null
            || !view.gameObject.activeInHierarchy)
        {
            return;
        }

        var state = EnsureButton(view);
        if (state is not null)
        {
            LayoutButton(state);
            RefreshButton(state);
        }
    }

    private static void TickNetwork(float now)
    {
        var runner = PhotonLobby.Runner;
        if (!IsUsableLobbyRunner(runner))
        {
            if (_runnerPointer != IntPtr.Zero)
            {
                ResetForRunner(IntPtr.Zero);
            }
            return;
        }

        if (_runnerPointer != runner.Pointer)
        {
            ResetForRunner(runner.Pointer);
        }

        var participants = GetParticipants(runner);
        _cachedParticipants = participants;
        if (participants.Count == 0)
        {
            return;
        }

        var localRaw = runner.LocalPlayer.RawEncoded;
        _localOnlySession = participants.Count == 1 && participants[0].Raw == localRaw;
        var membership = ComputeMembership(participants);
        if (_localOnlySession)
        {
            _coordinatorMembership = membership;
            _coordinatorCompatible = true;
            _coordinatorTargetRaw = 0;
            _coordinatorTargetUserId = string.Empty;
            SetObservedState(
                _coordinatorRevision,
                0,
                string.Empty,
                membership,
                compatible: true,
                ready: true,
                valid: true);
            _pendingRequestedRaw = int.MinValue;
            return;
        }

        if (now >= _nextHelloAt)
        {
            _nextHelloAt = now + HelloInterval;
            PublishPeerStatus(runner, participants, membership, acknowledgedRevision: -1);
        }

        ReadObservedState(runner, participants);
        if (IsCoordinator(runner))
        {
            ReadSelectionRequest(runner, participants, membership);
            TickCoordinator(runner, participants);
        }

        if (_observedValid
            && _observedCompatible
            && !_observedReady
            && _observedTargetRaw != 0
            && _observedRevision != _lastAckedRevision
            && now >= _nextAckAt)
        {
            _nextAckAt = now + AckInterval;
            _lastAckedRevision = _observedRevision;
            PublishAck(runner);
        }
    }

    private static void TickCoordinator(NetworkRunner runner, IReadOnlyList<Participant> participants)
    {
        var membership = ComputeMembership(participants);
        if (!string.Equals(membership, _coordinatorMembership, StringComparison.Ordinal))
        {
            _coordinatorMembership = membership;
            _coordinatorTargetRaw = 0;
            _coordinatorTargetUserId = string.Empty;
            _coordinatorRevision++;
            _pendingRequestedRaw = int.MinValue;
            _lastHandledRequest = string.Empty;
            LogInfo("Lobby membership changed; network host selection returned to Automatic");
        }

        var properties = runner.SessionInfo.Properties;
        // The local participant inherently runs this plugin; it must not wait for its own
        // round-trip through Photon custom properties. In a bot-only test lobby there is no
        // remote participant to synchronize with, so the state is fully local and immediately
        // ready. This also fixes the nonsensical "MODS 0/1" status.
        var compatible = _localOnlySession
            || properties is not null && participants.All(participant =>
                participant.Raw == runner.LocalPlayer.RawEncoded
                || TryReadPeer(properties, participant.Raw, out var peer)
                && string.Equals(peer.UserId, participant.UserId, StringComparison.Ordinal)
                && string.Equals(peer.Membership, _coordinatorMembership, StringComparison.Ordinal));
        if (compatible != _coordinatorCompatible)
        {
            _coordinatorCompatible = compatible;
            _coordinatorRevision++;
            _lastAckedRevision = -1;
            LogInfo(compatible
                ? "Every real lobby participant confirmed Network Host Selector compatibility"
                : "Network host selection disarmed because a participant has not confirmed compatibility");
        }

        if (_coordinatorTargetRaw != 0
            && participants.All(participant => participant.Raw != _coordinatorTargetRaw))
        {
            _coordinatorTargetRaw = 0;
            _coordinatorTargetUserId = string.Empty;
            _coordinatorRevision++;
        }

        var ready = compatible && (_coordinatorTargetRaw == 0
            || properties is not null && participants.All(participant =>
                participant.Raw == runner.LocalPlayer.RawEncoded
                || TryReadPeer(properties, participant.Raw, out var peer)
                && string.Equals(peer.UserId, participant.UserId, StringComparison.Ordinal)
                && string.Equals(peer.Membership, _coordinatorMembership, StringComparison.Ordinal)
                && peer.AcknowledgedRevision == _coordinatorRevision));
        PublishState(runner, ready);
    }

    private static void PublishState(NetworkRunner runner, bool ready)
    {
        if (_publishedRevision == _coordinatorRevision
            && _publishedTargetRaw == _coordinatorTargetRaw
            && string.Equals(_publishedTargetUserId, _coordinatorTargetUserId, StringComparison.Ordinal)
            && string.Equals(_publishedMembership, _coordinatorMembership, StringComparison.Ordinal)
            && _publishedCompatible == _coordinatorCompatible
            && _publishedReady == ready)
        {
            return;
        }

        if (_localOnlySession)
        {
            _publishedRevision = _coordinatorRevision;
            _publishedTargetRaw = _coordinatorTargetRaw;
            _publishedTargetUserId = _coordinatorTargetUserId;
            _publishedMembership = _coordinatorMembership;
            _publishedCompatible = true;
            _publishedReady = true;
            SetObservedState(
                _coordinatorRevision,
                _coordinatorTargetRaw,
                _coordinatorTargetUserId,
                _coordinatorMembership,
                compatible: true,
                ready: true,
                valid: true);
            _pendingRequestedRaw = int.MinValue;
            return;
        }

        var properties = new Il2CppSystem.Collections.Generic.Dictionary<string, SessionProperty>();
        properties[PropertyState] = HostSelectionProtocol.CreateState(
            _coordinatorRevision,
            _coordinatorTargetRaw,
            _coordinatorTargetUserId,
            _coordinatorMembership,
            _coordinatorCompatible,
            ready);
        if (!runner.SessionInfo.UpdateCustomProperties(properties))
        {
            return;
        }

        _publishedRevision = _coordinatorRevision;
        _publishedTargetRaw = _coordinatorTargetRaw;
        _publishedTargetUserId = _coordinatorTargetUserId;
        _publishedMembership = _coordinatorMembership;
        _publishedCompatible = _coordinatorCompatible;
        _publishedReady = ready;
        SetObservedState(
            _coordinatorRevision,
            _coordinatorTargetRaw,
            _coordinatorTargetUserId,
            _coordinatorMembership,
            _coordinatorCompatible,
            ready,
            valid: true);
        if (ready)
        {
            _pendingRequestedRaw = int.MinValue;
        }
    }

    private static void ReadObservedState(NetworkRunner runner, IReadOnlyList<Participant> participants)
    {
        try
        {
            var properties = runner.SessionInfo.Properties;
            if (properties is null
                || !TryReadString(properties, PropertyState, out var encodedState)
                || !HostSelectionProtocol.TryParseState(encodedState, out var state))
            {
                _observedValid = false;
                return;
            }

            var localMembership = ComputeMembership(participants);
            var targetValid = state.TargetPlayerRaw == 0 || participants.Any(participant =>
                participant.Raw == state.TargetPlayerRaw
                && string.Equals(participant.UserId, state.TargetUserId, StringComparison.Ordinal));
            SetObservedState(
                state.Revision,
                state.TargetPlayerRaw,
                state.TargetUserId,
                state.Membership,
                state.Compatible,
                state.Ready,
                string.Equals(state.Membership, localMembership, StringComparison.Ordinal) && targetValid);
        }
        catch (Exception exception)
        {
            _observedValid = false;
            LogError("Reading synchronized host selection failed", exception);
        }
    }

    private static void SetObservedState(
        int revision,
        int targetRaw,
        string targetUserId,
        string membership,
        bool compatible,
        bool ready,
        bool valid)
    {
        if (revision != _observedRevision)
        {
            _lastAckedRevision = -1;
        }
        _observedRevision = revision;
        _observedTargetRaw = targetRaw;
        _observedTargetUserId = targetUserId;
        _observedMembership = membership;
        _observedCompatible = compatible;
        _observedReady = ready;
        _observedValid = valid;
    }

    private static void PublishPeerStatus(
        NetworkRunner runner,
        IReadOnlyList<Participant> participants,
        string membership,
        int acknowledgedRevision)
    {
        var localRaw = runner.LocalPlayer.RawEncoded;
        var local = participants.FirstOrDefault(participant => participant.Raw == localRaw);
        if (local is null || string.IsNullOrWhiteSpace(local.UserId))
        {
            return;
        }

        var properties = runner.SessionInfo.Properties;
        var registry = properties is not null
            && TryReadString(properties, PropertyPeers, out var currentRegistry)
                ? currentRegistry
                : string.Empty;
        var existingAck = -1;
        if (HostSelectionProtocol.TryGetPeer(registry, localRaw, out var existing)
            && string.Equals(existing.UserId, local.UserId, StringComparison.Ordinal)
            && string.Equals(existing.Membership, membership, StringComparison.Ordinal))
        {
            existingAck = existing.AcknowledgedRevision;
        }
        var value = HostSelectionProtocol.UpsertPeer(
            registry,
            localRaw,
            local.UserId,
            membership,
            Math.Max(existingAck, acknowledgedRevision));
        if (string.Equals(value, registry, StringComparison.Ordinal))
        {
            return;
        }
        UpdateProperty(runner, PropertyPeers, value);
    }

    public static void InitializeSessionProperties(StartGameArgs args)
    {
        if (!Enabled || args.SessionProperties is null)
        {
            return;
        }

        // Fusion allows only ten custom properties total and the game already consumes five.
        // Keep the entire protocol in three fixed strings: coordinator state, a compact peer
        // registry, and the latest party-leader request. Reserving per-player keys both exceeded
        // the room limit and caused live updates to be rejected by Photon.
        EnsureSessionProperty(args, PropertyState, string.Empty);
        EnsureSessionProperty(args, PropertyPeers, string.Empty);
        EnsureSessionProperty(args, PropertyRequest, string.Empty);
    }

    private static void EnsureSessionProperty(StartGameArgs args, string key, SessionProperty value)
    {
        if (!args.SessionProperties.ContainsKey(key))
        {
            args.SessionProperties[key] = value;
        }
    }

    private static void PublishAck(NetworkRunner runner)
    {
        PublishPeerStatus(
            runner,
            _cachedParticipants,
            _observedMembership,
            _observedRevision);
    }

    private static void PublishSelectionRequest(
        NetworkRunner runner,
        string membership,
        int targetRaw,
        string targetUserId)
    {
        _requestSequence++;
        UpdateProperty(
            runner,
            PropertyRequest,
            HostSelectionProtocol.CreateRequest(
                membership,
                _requestSequence,
                targetRaw,
                targetUserId));
    }

    private static void ReadSelectionRequest(
        NetworkRunner runner,
        IReadOnlyList<Participant> participants,
        string membership)
    {
        var properties = runner.SessionInfo.Properties;
        if (properties is null
            || !TryReadString(properties, PropertyRequest, out var encoded)
            || string.Equals(encoded, _lastHandledRequest, StringComparison.Ordinal)
            || !HostSelectionProtocol.TryParseRequest(encoded, out var request)
            || !string.Equals(request.Membership, membership, StringComparison.Ordinal))
        {
            return;
        }
        _lastHandledRequest = encoded;

        var requested = participants.FirstOrDefault(participant =>
            participant.Raw == request.TargetPlayerRaw
            && string.Equals(participant.UserId, request.TargetUserId, StringComparison.Ordinal));
        if (request.TargetPlayerRaw != 0 && requested is null)
        {
            return;
        }
        AcceptSelectionRequest(runner, participants, request.TargetPlayerRaw);
    }

    private static bool UpdateProperty(NetworkRunner runner, string key, string value)
    {
        try
        {
            var properties = new Il2CppSystem.Collections.Generic.Dictionary<string, SessionProperty>();
            properties[key] = value;
            return runner.SessionInfo.UpdateCustomProperties(properties);
        }
        catch (Exception exception)
        {
            LogError($"Publishing session property {key} failed", exception);
            return false;
        }
    }

    private static void AcceptSelectionRequest(
        NetworkRunner runner,
        IReadOnlyList<Participant> participants,
        int targetRaw)
    {
        if (!_coordinatorCompatible)
        {
            return;
        }

        var target = participants.FirstOrDefault(participant => participant.Raw == targetRaw);
        if (targetRaw != 0 && target is null)
        {
            return;
        }

        var targetUserId = target?.UserId ?? string.Empty;
        if (targetRaw != 0 && string.IsNullOrWhiteSpace(targetUserId))
        {
            return;
        }
        if (_coordinatorTargetRaw == targetRaw
            && string.Equals(_coordinatorTargetUserId, targetUserId, StringComparison.Ordinal))
        {
            return;
        }

        _coordinatorTargetRaw = targetRaw;
        _coordinatorTargetUserId = targetUserId;
        _coordinatorRevision++;
        _lastAckedRevision = -1;
        _pendingRequestedRaw = targetRaw;
        LogInfo(target is null
            ? "Party leader selected Automatic network host"
            : $"Party leader proposed network host {target.Name} ({target.UserId})");
        PublishState(runner, ready: targetRaw == 0);
    }

    private static List<Participant> GetParticipants(NetworkRunner runner)
    {
        var stalePointers = new List<IntPtr>();
        var livePlayers = new List<SpookedNetworkPlayer>();
        foreach (var pair in ObservedPlayers)
        {
            var player = pair.Value;
            try
            {
                if (player is null || player.Pointer == IntPtr.Zero)
                {
                    stalePointers.Add(pair.Key);
                    continue;
                }
                if (player.Runner is not null && player.Runner.Pointer == runner.Pointer)
                {
                    livePlayers.Add(player);
                }
            }
            catch
            {
                stalePointers.Add(pair.Key);
            }
        }
        foreach (var pointer in stalePointers)
        {
            ObservedPlayers.Remove(pointer);
        }

        var playerObjects = livePlayers
            .GroupBy(player => player.PlayerRef.RawEncoded)
            .ToDictionary(group => group.Key, group => group.First());
        var botRaws = playerObjects.Values
            .Where(player => player.IsBot)
            .Select(player => player.PlayerRef.RawEncoded)
            .ToHashSet();
        var participants = new List<Participant>();
        foreach (var playerRef in GetActivePlayers(runner))
        {
            if (!playerRef.IsRealPlayer || botRaws.Contains(playerRef.RawEncoded))
            {
                continue;
            }

            string userId;
            try
            {
                userId = runner.GetPlayerUserId(playerRef) ?? string.Empty;
            }
            catch
            {
                userId = string.Empty;
            }
            var name = playerObjects.TryGetValue(playerRef.RawEncoded, out var playerObject)
                && !string.IsNullOrWhiteSpace(playerObject.Nickname)
                    ? playerObject.Nickname
                    : $"PLAYER {playerRef.PlayerId}";
            var rttMs = 0;
            try
            {
                rttMs = Mathf.Max(0, Mathf.RoundToInt((float)runner.GetPlayerRtt(playerRef) * 1000f));
            }
            catch
            {
                // RTT is presentation-only and must never affect eligibility.
            }
            participants.Add(new Participant(playerRef, playerRef.RawEncoded, userId, name, rttMs));
        }
        return participants.OrderBy(participant => participant.Raw).ToList();
    }

    private static List<PlayerRef> GetActivePlayers(NetworkRunner runner)
    {
        var result = new List<PlayerRef>();
        var genericEnumerator = runner.ActivePlayers.GetEnumerator();
        var enumerator = new Il2CppSystem.Collections.IEnumerator(genericEnumerator.Pointer);
        while (enumerator.MoveNext())
        {
            result.Add(genericEnumerator.Current);
        }
        return result;
    }

    private static string ComputeMembership(IEnumerable<Participant> participants)
    {
        return HostSelectionProtocol.ComputeMembershipSignature(
            participants.Select(participant => (participant.Raw, participant.UserId)));
    }

    private static bool IsCoordinator(NetworkRunner runner)
    {
        return runner.IsServer || runner.IsSharedModeMasterClient;
    }

    private static bool IsUsableLobbyRunner(NetworkRunner? runner)
    {
        return runner is not null
            && runner.Pointer != IntPtr.Zero
            && runner.IsRunning
            && runner.IsInSession
            && runner.SessionInfo is not null
            && runner.SessionInfo.IsValid;
    }

    private static void ResetForRunner(IntPtr runnerPointer)
    {
        _runnerPointer = runnerPointer;
        _cachedParticipants = Array.Empty<Participant>();
        _nextHelloAt = 0f;
        _nextAckAt = 0f;
        _coordinatorMembership = string.Empty;
        _coordinatorCompatible = false;
        _coordinatorRevision = 1;
        _coordinatorTargetRaw = 0;
        _coordinatorTargetUserId = string.Empty;
        _publishedRevision = -1;
        _publishedTargetRaw = int.MinValue;
        _publishedTargetUserId = string.Empty;
        _publishedMembership = string.Empty;
        _publishedCompatible = false;
        _publishedReady = false;
        _observedRevision = 0;
        _observedTargetRaw = 0;
        _observedTargetUserId = string.Empty;
        _observedMembership = string.Empty;
        _observedCompatible = false;
        _observedReady = false;
        _observedValid = false;
        _lastAckedRevision = -1;
        _pendingRequestedRaw = int.MinValue;
        _requestSequence = 0;
        _lastHandledRequest = string.Empty;
        _localOnlySession = false;
    }

    public static bool AllowPortalPlay()
    {
        if (!Enabled)
        {
            return true;
        }
        RefreshObservedFromCurrentRunner();
        if (_observedTargetRaw == 0)
        {
            return true;
        }
        if (_observedValid && _observedCompatible && _observedReady)
        {
            return true;
        }

        _logger?.LogWarning("PLAY held until the selected network host is acknowledged by every real participant");
        return false;
    }

    public static void OverrideMatchHost(ref string hostId)
    {
        if (!Enabled)
        {
            return;
        }
        RefreshObservedFromCurrentRunner();
        if (!_observedValid
            || !_observedCompatible
            || !_observedReady
            || _observedTargetRaw == 0
            || string.IsNullOrWhiteSpace(_observedTargetUserId))
        {
            return;
        }

        var originalHostId = hostId;
        hostId = _observedTargetUserId;
        LogInfo($"Match Fusion host overridden {originalHostId} -> {hostId}");
    }

    private static void RefreshObservedFromCurrentRunner()
    {
        var runner = PhotonLobby.Runner;
        if (!IsUsableLobbyRunner(runner) || runner.Pointer != _runnerPointer)
        {
            _observedValid = false;
            return;
        }
        var participants = GetParticipants(runner);
        if (participants.Count == 1 && participants[0].Raw == runner.LocalPlayer.RawEncoded)
        {
            _localOnlySession = true;
            SetObservedState(
                _coordinatorRevision,
                _coordinatorTargetRaw,
                _coordinatorTargetUserId,
                ComputeMembership(participants),
                compatible: true,
                ready: true,
                valid: true);
            return;
        }
        ReadObservedState(runner, participants);
    }

    private static NetworkHostSelectorUiState? EnsureButton(PortalPlayView view)
    {
        if (UiStateByView.TryGetValue(view.Pointer, out var existing) && existing.IsAlive)
        {
            return existing;
        }
        if (view._playButton is null)
        {
            return null;
        }

        var parent = view._playButton.transform.parent;
        if (parent is null)
        {
            return null;
        }
        try
        {
            var root = UnityEngine.Object.Instantiate(view._playButton.gameObject, parent, false);
            root.name = "NetworkHostSelectorButton";
            var button = root.GetComponent<SpookedOutlineButton>();
            var label = root.GetComponentInChildren<TMP_Text>(true);
            var rect = root.GetComponent<RectTransform>();
            if (button is null || label is null || rect is null)
            {
                UnityEngine.Object.Destroy(root);
                return null;
            }

            button.onClick = new Button.ButtonClickedEvent();
            var clickAction = (UnityAction)CycleHost;
            button.onClick.AddListener(clickAction);
            label.fontSize = 14f;
            label.fontSizeMin = 9f;
            label.fontSizeMax = 14f;
            label.enableAutoSizing = true;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            FitStockButtonLayers(rect, button, label);
            var state = new NetworkHostSelectorUiState(view, root, button, label, clickAction);
            UiStateByView[view.Pointer] = state;
            return state;
        }
        catch (Exception exception)
        {
            LogError("Network host button creation failed", exception);
            return null;
        }
    }

    private static void LayoutButton(NetworkHostSelectorUiState state)
    {
        var playRect = state.View._playButton?.GetComponent<RectTransform>();
        var rect = state.RootObject.GetComponent<RectTransform>();
        if (playRect is null || rect is null)
        {
            return;
        }

        var playWidth = playRect.rect.width;
        var width = Mathf.Max(72f, (playWidth - ButtonGap * 2f) / 3f);
        var toolbarY = playRect.rect.height * 0.5f + ButtonHeight * 0.5f + 10f;
        var y = toolbarY;
        var siblingRoot = playRect.parent;
        var botButton = siblingRoot?.Find("LobbyTestBotButton");
        if (botButton is not null && botButton.gameObject.activeInHierarchy)
        {
            y += ButtonHeight + ButtonGap;
            var portalControls = siblingRoot?.Find("CodexPortalControls");
            if (portalControls is not null)
            {
                for (var childIndex = 0; childIndex < portalControls.childCount; childIndex++)
                {
                    var mapRect = portalControls.GetChild(childIndex)?.GetComponent<RectTransform>();
                    if (mapRect is null
                        || !mapRect.gameObject.activeInHierarchy
                        || !mapRect.gameObject.name.StartsWith("CodexPortalMap_", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    y = Mathf.Max(
                        y,
                        mapRect.anchoredPosition.y
                        + mapRect.rect.height * 0.5f
                        + ButtonGap
                        + ButtonHeight * 0.5f);
                }
            }
        }

        var targetSize = new Vector2(width, ButtonHeight);
        var targetPosition = playRect.localPosition + new Vector3(width + ButtonGap, y, 0f);
        if ((rect.sizeDelta - targetSize).sqrMagnitude < 0.01f
            && (rect.localPosition - targetPosition).sqrMagnitude < 0.01f)
        {
            return;
        }

        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        rect.sizeDelta = targetSize;
        rect.localPosition = targetPosition;
        rect.SetAsLastSibling();
    }

    private static void RefreshButton(NetworkHostSelectorUiState state)
    {
        var runner = PhotonLobby.Runner;
        if (!IsUsableLobbyRunner(runner))
        {
            SetInteractable(state.Button, false);
            SetButtonPresentation(state, "HOST: AUTOMATIC", new Color(0.16f, 0.18f, 0.22f, 0.95f));
            return;
        }

        var participants = runner.Pointer == _runnerPointer
            ? _cachedParticipants
            : Array.Empty<Participant>();
        var membership = ComputeMembership(participants);
        var properties = runner.SessionInfo.Properties;
        var localRaw = runner.LocalPlayer.RawEncoded;
        var confirmed = participants.Count(participant =>
                participant.Raw == localRaw
                || properties is not null
                && TryReadPeer(properties, participant.Raw, out var peer)
                && string.Equals(peer.UserId, participant.UserId, StringComparison.Ordinal)
                && string.Equals(peer.Membership, membership, StringComparison.Ordinal));
        var leader = PgosLobby.Instance is { } pgosLobby && pgosLobby.AmITeamLeader;
        var canSelect = leader && participants.Count > 0 && _observedValid && _observedCompatible;
        SetInteractable(state.Button, canSelect);

        if (!_observedValid || !_observedCompatible)
        {
            SetButtonPresentation(state, $"HOST: MODS {confirmed}/{participants.Count}", new Color(0.55f, 0.34f, 0.08f, 1f));
            return;
        }
        if (!_observedReady || _pendingRequestedRaw != int.MinValue)
        {
            SetButtonPresentation(state, "HOST: SYNCING...", new Color(0.55f, 0.34f, 0.08f, 1f));
            return;
        }
        if (_observedTargetRaw == 0)
        {
            SetButtonPresentation(state, "HOST: AUTOMATIC", new Color(0.16f, 0.18f, 0.22f, 0.95f));
            return;
        }

        var selected = participants.FirstOrDefault(participant => participant.Raw == _observedTargetRaw);
        var label = selected is null
            ? "HOST: AUTOMATIC"
            : $"HOST: {selected.Name.ToUpperInvariant()}  {selected.RttMs}ms";
        SetButtonPresentation(state, label, new Color(0.08627451f, 0.5372549f, 0.654902f, 1f));
    }

    private static void CycleHost()
    {
        var runner = PhotonLobby.Runner;
        if (!IsUsableLobbyRunner(runner)
            || PgosLobby.Instance is not { AmITeamLeader: true }
            || !_observedValid
            || !_observedCompatible)
        {
            return;
        }

        var participants = GetParticipants(runner)
            .Where(participant => !string.IsNullOrWhiteSpace(participant.UserId))
            .OrderBy(participant => participant.RttMs)
            .ThenBy(participant => participant.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var choices = new List<int> { 0 };
        choices.AddRange(participants.Select(participant => participant.Raw));
        var current = _pendingRequestedRaw != int.MinValue ? _pendingRequestedRaw : _observedTargetRaw;
        var index = choices.IndexOf(current);
        var targetRaw = choices[(index + 1 + choices.Count) % choices.Count];
        _pendingRequestedRaw = targetRaw;
        if (IsCoordinator(runner))
        {
            AcceptSelectionRequest(runner, participants, targetRaw);
        }
        else
        {
            var selected = participants.FirstOrDefault(participant => participant.Raw == targetRaw);
            PublishSelectionRequest(
                runner,
                ComputeMembership(participants),
                targetRaw,
                selected?.UserId ?? string.Empty);
        }
        RefreshAllButtons();
    }

    private static void RefreshAllButtons()
    {
        foreach (var state in UiStateByView.Values.ToArray())
        {
            if (state.IsAlive)
            {
                RefreshButton(state);
            }
        }
    }

    private static void SetButtonColor(SpookedOutlineButton button, Color color)
    {
        var background = button._targetColorImage ?? button.targetGraphic as Image;
        if (background is not null && background.color != color)
        {
            background.color = color;
        }
    }

    private static void SetInteractable(SpookedOutlineButton button, bool interactable)
    {
        if (button.interactable != interactable)
        {
            button.interactable = interactable;
        }
    }

    private static void SetButtonPresentation(NetworkHostSelectorUiState state, string label, Color color)
    {
        if (!string.Equals(state.Label.text, label, StringComparison.Ordinal))
        {
            state.Label.text = label;
        }
        SetButtonColor(state.Button, color);
    }

    private static void FitStockButtonLayers(RectTransform root, SpookedOutlineButton button, TMP_Text label)
    {
        StretchToRoot(button._targetColorImage?.rectTransform, root);
        StretchToRoot(button._targetOutlineImage?.rectTransform, root);
        StretchToRoot(label.rectTransform, root);
    }

    private static void StretchToRoot(RectTransform? leaf, RectTransform root)
    {
        var current = leaf;
        while (current is not null && current.Pointer != root.Pointer)
        {
            current.anchorMin = Vector2.zero;
            current.anchorMax = Vector2.one;
            current.offsetMin = Vector2.zero;
            current.offsetMax = Vector2.zero;
            current.anchoredPosition = Vector2.zero;
            current.localScale = Vector3.one;
            current = current.parent?.GetComponent<RectTransform>();
        }
    }

    private static void ReleaseAllUi()
    {
        foreach (var state in UiStateByView.Values.ToArray())
        {
            if (state.IsAlive)
            {
                state.Button.onClick.RemoveListener(state.ClickAction);
                UnityEngine.Object.Destroy(state.RootObject);
            }
        }
        UiStateByView.Clear();
    }

    private static bool TryReadInt(
        Il2CppSystem.Collections.ObjectModel.ReadOnlyDictionary<string, SessionProperty> properties,
        string key,
        out int value)
    {
        value = 0;
        if (!properties.TryGetValue(key, out var property) || property is null || !property.IsInt)
        {
            return false;
        }
        value = property;
        return true;
    }

    private static bool TryReadString(
        Il2CppSystem.Collections.ObjectModel.ReadOnlyDictionary<string, SessionProperty> properties,
        string key,
        out string value)
    {
        value = string.Empty;
        if (!properties.TryGetValue(key, out var property) || property is null || !property.IsString)
        {
            return false;
        }
        value = property ?? string.Empty;
        return true;
    }

    private static bool TryReadPeer(
        Il2CppSystem.Collections.ObjectModel.ReadOnlyDictionary<string, SessionProperty> properties,
        int playerRaw,
        out HostSelectionPeer peer)
    {
        peer = default;
        return TryReadString(properties, PropertyPeers, out var registry)
            && HostSelectionProtocol.TryGetPeer(registry, playerRaw, out peer);
    }

    private static bool TryReadBool(
        Il2CppSystem.Collections.ObjectModel.ReadOnlyDictionary<string, SessionProperty> properties,
        string key,
        out bool value)
    {
        value = false;
        if (!properties.TryGetValue(key, out var property) || property is null || !property.Isbool)
        {
            return false;
        }
        value = property;
        return true;
    }

    private static void LogInfo(string message)
    {
        if (LoggingEnabled)
        {
            _logger?.LogInfo(message);
        }
    }

    private static void LogError(string message, Exception exception)
    {
        _logger?.LogError($"{message}: {exception}");
    }

    private sealed record Participant(PlayerRef PlayerRef, int Raw, string UserId, string Name, int RttMs);

    private sealed class NetworkHostSelectorWatcher : MonoBehaviour
    {
        public NetworkHostSelectorWatcher(IntPtr pointer) : base(pointer)
        {
        }

        public NetworkHostSelectorWatcher() : base(ClassInjector.DerivedConstructorPointer<NetworkHostSelectorWatcher>())
        {
            ClassInjector.DerivedConstructorBody(this);
        }

        private void Update()
        {
            try
            {
                WatcherTick();
            }
            catch (Exception exception)
            {
                LogError("Network host selector watcher failed", exception);
            }
        }
    }
}
