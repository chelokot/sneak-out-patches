using BepInEx.Logging;
using Fusion;
using Gameplay.Player.Components;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using Networking.Lobby;
using Networking.Party;
using TMPro;
using UI.Views;
using UnityEngine;

namespace SneakOut.NetworkHostSelector;

internal static class NetworkHostSelectorRuntime
{
    private const string PropertyState = "sohs_s";
    private const string PropertyPeers = "sohs_e";
    private const float NetworkTickInterval = 0.25f;
    private const float HelloInterval = 1f;
    private const float AckInterval = 0.5f;

    private static readonly Dictionary<IntPtr, SpookedNetworkPlayer> ObservedPlayers = new();

    private static ManualLogSource? _logger;
    private static NetworkHostSelectorConfig? _configuration;
    private static Harmony? _harmony;
    private static bool _watcherInstalled;
    private static IntPtr _runnerPointer;
    private static float _nextNetworkTick;
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
    private static bool _localOnlySession;
    private static IReadOnlyList<LeaderHostParticipant> _cachedParticipants = Array.Empty<LeaderHostParticipant>();

    public static void Initialize(ManualLogSource logger, NetworkHostSelectorConfig configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _harmony ??= new Harmony(NetworkHostSelectorPlugin.PluginGuid);
        _harmony.PatchAll();
        EnsureWatcher();
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
            return;
        }

        var now = Time.unscaledTime;
        if (now >= _nextNetworkTick)
        {
            _nextNetworkTick = now + NetworkTickInterval;
            TickNetwork(now);
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
        if (!LeaderHostParticipantPolicy.IsComplete(
                runner.SessionInfo.PlayerCount,
                participants.Count))
        {
            _cachedParticipants = Array.Empty<LeaderHostParticipant>();
            _localOnlySession = false;
            _observedValid = false;
            return;
        }

        var localRaw = runner.LocalPlayer.RawEncoded;
        _localOnlySession = participants.Count == 1 && participants[0].Raw == localRaw;
        var membership = ComputeMembership(participants);
        if (_localOnlySession)
        {
            if (!TryResolvePartyCreator(
                    participants.Select(participant => (participant.Raw, participant.UserId)),
                    localRaw,
                    out var leader))
            {
                return;
            }
            _coordinatorMembership = membership;
            _coordinatorCompatible = true;
            _coordinatorTargetRaw = leader.PlayerRaw;
            _coordinatorTargetUserId = leader.UserId;
            SetObservedState(
                _coordinatorRevision,
                leader.PlayerRaw,
                leader.UserId,
                membership,
                compatible: true,
                ready: true,
                valid: true);
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

    private static void TickCoordinator(NetworkRunner runner, IReadOnlyList<LeaderHostParticipant> participants)
    {
        var membership = ComputeMembership(participants);
        if (!string.Equals(membership, _coordinatorMembership, StringComparison.Ordinal))
        {
            _coordinatorMembership = membership;
            _coordinatorRevision++;
            _lastAckedRevision = -1;
            LogInfo("Lobby membership changed; Leader Host compatibility will be reconfirmed");
        }

        if (TryResolvePartyCreator(
                participants.Select(participant => (participant.Raw, participant.UserId)),
                runner.LocalPlayer.RawEncoded,
                out var leader)
            && (_coordinatorTargetRaw != leader.PlayerRaw
                || !string.Equals(_coordinatorTargetUserId, leader.UserId, StringComparison.Ordinal)))
        {
            _coordinatorTargetRaw = leader.PlayerRaw;
            _coordinatorTargetUserId = leader.UserId;
            _coordinatorRevision++;
            _lastAckedRevision = -1;
            LogInfo($"Party creator fixed as match host ({leader.UserId})");
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
                ? "Every real lobby participant confirmed Leader Host compatibility"
                : "Leader Host disarmed because a participant has not confirmed compatibility");
        }

        var ready = compatible
            && _coordinatorTargetRaw != 0
            && !string.IsNullOrWhiteSpace(_coordinatorTargetUserId)
            && properties is not null
            && participants.All(participant =>
                participant.Raw == runner.LocalPlayer.RawEncoded
                || TryReadPeer(properties, participant.Raw, out var peer)
                && string.Equals(peer.UserId, participant.UserId, StringComparison.Ordinal)
                && string.Equals(peer.Membership, _coordinatorMembership, StringComparison.Ordinal)
                && peer.AcknowledgedRevision == _coordinatorRevision);
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
    }

    private static void ReadObservedState(NetworkRunner runner, IReadOnlyList<LeaderHostParticipant> participants)
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
            var expectedLeaderId = PgosLobby.Instance?.TeamLeaderId ?? string.Empty;
            var targetValid = state.TargetPlayerRaw != 0
                && !string.IsNullOrWhiteSpace(expectedLeaderId)
                && string.Equals(state.TargetUserId, expectedLeaderId, StringComparison.Ordinal)
                && participants.Any(participant => participant.Raw == state.TargetPlayerRaw);
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
        IReadOnlyList<LeaderHostParticipant> participants,
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
        // Keep the entire protocol in two fixed strings: coordinator state and a compact peer
        // registry. Reserving per-player keys both exceeded the room limit and caused live
        // updates to be rejected by Photon.
        EnsureSessionProperty(args, PropertyState, string.Empty);
        EnsureSessionProperty(args, PropertyPeers, string.Empty);
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

    private static IReadOnlyList<LeaderHostParticipant> GetParticipants(NetworkRunner runner)
    {
        var stalePointers = new List<IntPtr>();
        var observedParticipants = new List<LeaderHostParticipant>();
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
                if (player.Runner is null || player.Runner.Pointer != runner.Pointer)
                {
                    continue;
                }

                var playerRef = player.PlayerRef;
                var userId = GetPlayerUserId(runner, playerRef);
                var name = string.IsNullOrWhiteSpace(player.Nickname)
                    ? $"PLAYER {playerRef.PlayerId}"
                    : player.Nickname;
                observedParticipants.Add(new LeaderHostParticipant(
                    playerRef.RawEncoded,
                    userId,
                    name,
                    playerRef.IsRealPlayer,
                    player.IsBot));
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

        return LeaderHostParticipantPolicy.CreateSnapshot(observedParticipants);
    }

    private static string GetPlayerUserId(NetworkRunner runner, PlayerRef playerRef)
    {
        try
        {
            return runner.GetPlayerUserId(playerRef) ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ComputeMembership(IEnumerable<LeaderHostParticipant> participants)
    {
        return HostSelectionProtocol.ComputeMembershipSignature(
            participants.Select(participant => (participant.Raw, participant.UserId)));
    }

    private static bool TryResolvePartyCreator(
        IEnumerable<(int PlayerRaw, string UserId)> participants,
        int creatorPlayerRaw,
        out LeaderHostTarget target)
    {
        var party = PgosLobby.Instance;
        if (party is null || !party.AmITeamLeader)
        {
            target = default;
            return false;
        }

        return LeaderHostPolicy.TryResolve(
            participants,
            creatorPlayerRaw,
            party.TeamLeaderId ?? string.Empty,
            out target);
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
        _cachedParticipants = Array.Empty<LeaderHostParticipant>();
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
        _localOnlySession = false;
    }

    public static bool AllowPortalPlay()
    {
        if (!Enabled)
        {
            return true;
        }
        RefreshObservedFromCurrentRunner();
        if (_observedValid
            && _observedCompatible
            && _observedReady
            && _observedTargetRaw != 0
            && !string.IsNullOrWhiteSpace(_observedTargetUserId))
        {
            return true;
        }

        _logger?.LogWarning("PLAY held until every real participant acknowledges the party creator as match host");
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
        if (!LeaderHostParticipantPolicy.IsComplete(
                runner.SessionInfo.PlayerCount,
                participants.Count))
        {
            _observedValid = false;
            return;
        }
        if (participants.Count == 1 && participants[0].Raw == runner.LocalPlayer.RawEncoded)
        {
            if (!TryResolvePartyCreator(
                    participants.Select(participant => (participant.Raw, participant.UserId)),
                    runner.LocalPlayer.RawEncoded,
                    out var leader))
            {
                _observedValid = false;
                return;
            }
            _localOnlySession = true;
            _coordinatorTargetRaw = leader.PlayerRaw;
            _coordinatorTargetUserId = leader.UserId;
            SetObservedState(
                _coordinatorRevision,
                leader.PlayerRaw,
                leader.UserId,
                ComputeMembership(participants),
                compatible: true,
                ready: true,
                valid: true);
            return;
        }
        ReadObservedState(runner, participants);
    }

    public static void RefreshHostLabel(PingView view)
    {
        try
        {
            var label = view?._map;
            if (label is null || label.Pointer == IntPtr.Zero)
            {
                return;
            }

            var hostName = Enabled ? ResolveHostDisplayName() : string.Empty;
            var text = LeaderHostHudText.Compose(label.text, hostName);
            if (!string.Equals(label.text, text, StringComparison.Ordinal))
            {
                label.text = text;
            }
            if (!string.IsNullOrWhiteSpace(hostName))
            {
                label.enableWordWrapping = false;
                label.overflowMode = TextOverflowModes.Overflow;
            }
        }
        catch (Exception exception)
        {
            LogError("Leader Host HUD refresh failed", exception);
        }
    }

    private static string ResolveHostDisplayName()
    {
        var targetRaw = _observedTargetRaw != 0
            ? _observedTargetRaw
            : _coordinatorTargetRaw;
        if (targetRaw == 0)
        {
            return string.Empty;
        }

        return _cachedParticipants
            .FirstOrDefault(participant => participant.Raw == targetRaw)
            ?.Name
            ?? string.Empty;
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
                LogError("Leader Host watcher failed", exception);
            }
        }
    }
}
