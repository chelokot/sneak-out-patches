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
    private const string UniformSeekerRandomAssemblyName = "SneakOut.UniformSeekerRandom";
    private const string UniformSeekerRandomPluginType =
        "SneakOut.UniformSeekerRandom.UniformSeekerRandomPlugin";
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
    private static int _coordinatorCommonCapabilities;
    private static int _coordinatorRevision;
    private static int _coordinatorTargetRaw;
    private static string _coordinatorTargetUserId = string.Empty;
    private static int _publishedRevision = -1;
    private static int _publishedTargetRaw = int.MinValue;
    private static string _publishedTargetUserId = string.Empty;
    private static string _publishedMembership = string.Empty;
    private static int _publishedCommonCapabilities = -1;
    private static bool _publishedCompatible;
    private static bool _publishedReady;
    private static int _observedRevision;
    private static int _observedTargetRaw;
    private static string _observedTargetUserId = string.Empty;
    private static string _observedMembership = string.Empty;
    private static int _observedCommonCapabilities;
    private static bool _observedCompatible;
    private static bool _observedReady;
    private static bool _observedValid;
    private static int _lastAckedRevision = -1;
    private static bool _localOnlySession;
    private static IReadOnlyList<LeaderHostParticipant> _cachedParticipants = Array.Empty<LeaderHostParticipant>();
    private static string _lastParticipantSnapshotLog = string.Empty;
    private static string _lastPeerRegistryLog = string.Empty;
    private static string _lastObservedStateLog = string.Empty;
    private static string _lastLeaderResolutionLog = string.Empty;
    private static string _lastCoordinatorQuorumLog = string.Empty;
    private static string _lastPeerPublicationLog = string.Empty;
    private static string _lastCapabilityDetectionLog = string.Empty;

    public static void Initialize(ManualLogSource logger, NetworkHostSelectorConfig configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _harmony ??= new Harmony(NetworkHostSelectorPlugin.PluginGuid);
        _harmony.PatchAll();
        EnsureWatcher();
        LogInfo(
            $"TRACE initialized protocol={HostSelectionProtocol.Version} "
            + $"enabled={configuration.EnableMod.Value} diagnostics={configuration.EnableLogging.Value} "
            + $"localCapabilities={GetLocalCapabilities()}");
    }

    public static void ObservePlayer(SpookedNetworkPlayer player)
    {
        if (player is not null && player.Pointer != IntPtr.Zero)
        {
            ObservedPlayers[player.Pointer] = player;
            LogObservedPlayer("spawned", player);
        }
    }

    public static void ForgetPlayer(SpookedNetworkPlayer player)
    {
        if (player is not null)
        {
            LogObservedPlayer("despawned", player);
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
            LogInfo(
                $"RUNNER attached session={FormatValue(runner.SessionInfo.Name)} "
                + $"localRaw={runner.LocalPlayer.RawEncoded} server={runner.IsServer} "
                + $"sharedMaster={runner.IsSharedModeMasterClient} playerCount={runner.SessionInfo.PlayerCount}");
        }

        var participants = GetParticipants(runner);
        _cachedParticipants = participants;
        var participantSnapshotComplete = LeaderHostParticipantPolicy.IsComplete(
            runner.SessionInfo.PlayerCount,
            participants.Count);
        LogParticipantSnapshot(runner, participants, participantSnapshotComplete);
        if (!participantSnapshotComplete)
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
            _coordinatorCommonCapabilities = GetLocalCapabilities();
            _coordinatorTargetRaw = leader.PlayerRaw;
            _coordinatorTargetUserId = leader.UserId;
            SetObservedState(
                _coordinatorRevision,
                leader.PlayerRaw,
                leader.UserId,
                membership,
                _coordinatorCommonCapabilities,
                compatible: true,
                ready: true,
                valid: true);
            LogTransition(
                ref _lastCoordinatorQuorumLog,
                $"HANDSHAKE local-only result=ready targetRaw={leader.PlayerRaw} "
                + $"targetUserId={FormatValue(leader.UserId)} membership={membership} "
                + $"commonCapabilities={_coordinatorCommonCapabilities}");
            return;
        }

        if (now >= _nextHelloAt)
        {
            _nextHelloAt = now + HelloInterval;
            PublishPeerStatus(runner, participants, membership, acknowledgedRevision: -1);
        }

        ReadObservedState(runner, participants);
        LogPeerRegistry(runner, participants, membership);
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
            LogInfo(
                $"HANDSHAKE acknowledgement requested revision={_observedRevision} "
                + $"targetRaw={_observedTargetRaw} targetUserId={FormatValue(_observedTargetUserId)} "
                + $"membership={_observedMembership}");
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

        var leaderResolved = TryResolvePartyCreator(
            participants.Select(participant => (participant.Raw, participant.UserId)),
            runner.LocalPlayer.RawEncoded,
            out var leader);
        var party = PgosLobby.Instance;
        LogTransition(
            ref _lastLeaderResolutionLog,
            $"COORDINATOR leader-resolution resolved={leaderResolved} localRaw={runner.LocalPlayer.RawEncoded} "
            + $"amPartyLeader={party?.AmITeamLeader ?? false} "
            + $"partyLeaderId={FormatValue(party?.TeamLeaderId)} "
            + $"targetRaw={(leaderResolved ? leader.PlayerRaw : 0)} "
            + $"targetUserId={FormatValue(leaderResolved ? leader.UserId : string.Empty)}");
        if (leaderResolved
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

        var commonCapabilities = compatible ? GetLocalCapabilities() : 0;
        if (compatible && properties is not null)
        {
            foreach (var participant in participants)
            {
                if (participant.Raw == runner.LocalPlayer.RawEncoded)
                {
                    continue;
                }

                if (!TryReadPeer(properties, participant.Raw, out var peer))
                {
                    commonCapabilities = 0;
                    break;
                }
                commonCapabilities &= peer.Capabilities;
            }
        }
        if (commonCapabilities != _coordinatorCommonCapabilities)
        {
            _coordinatorCommonCapabilities = commonCapabilities;
            _coordinatorRevision++;
            _lastAckedRevision = -1;
            LogInfo(
                $"Common multiplayer feature capabilities changed to {_coordinatorCommonCapabilities}; "
                + "compatibility will be reconfirmed");
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
        LogTransition(
            ref _lastCoordinatorQuorumLog,
            $"COORDINATOR quorum revision={_coordinatorRevision} membership={_coordinatorMembership} "
            + $"compatible={compatible} ready={ready} targetRaw={_coordinatorTargetRaw} "
            + $"commonCapabilities={_coordinatorCommonCapabilities} "
            + $"targetUserId={FormatValue(_coordinatorTargetUserId)} "
            + $"peers=[{DescribePeerStatuses(runner, participants, _coordinatorMembership, _coordinatorRevision)}]");
        PublishState(runner, ready);
    }

    private static void PublishState(NetworkRunner runner, bool ready)
    {
        if (_publishedRevision == _coordinatorRevision
            && _publishedTargetRaw == _coordinatorTargetRaw
            && string.Equals(_publishedTargetUserId, _coordinatorTargetUserId, StringComparison.Ordinal)
            && string.Equals(_publishedMembership, _coordinatorMembership, StringComparison.Ordinal)
            && _publishedCommonCapabilities == _coordinatorCommonCapabilities
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
            _publishedCommonCapabilities = _coordinatorCommonCapabilities;
            _publishedCompatible = true;
            _publishedReady = true;
            SetObservedState(
                _coordinatorRevision,
                _coordinatorTargetRaw,
                _coordinatorTargetUserId,
                _coordinatorMembership,
                _coordinatorCommonCapabilities,
                compatible: true,
                ready: true,
                valid: true);
            LogInfo(
                $"TX STATE local-only revision={_coordinatorRevision} targetRaw={_coordinatorTargetRaw} "
                + $"targetUserId={FormatValue(_coordinatorTargetUserId)} "
                + $"membership={_coordinatorMembership} "
                + $"commonCapabilities={_coordinatorCommonCapabilities} compatible=True ready=True");
            return;
        }

        var encodedState = HostSelectionProtocol.CreateState(
            _coordinatorRevision,
            _coordinatorTargetRaw,
            _coordinatorTargetUserId,
            _coordinatorMembership,
            _coordinatorCommonCapabilities,
            _coordinatorCompatible,
            ready);
        var properties = new Il2CppSystem.Collections.Generic.Dictionary<string, SessionProperty>();
        properties[HostSelectionProtocol.PropertyState] = encodedState;
        LogInfo(
            $"TX STATE requested revision={_coordinatorRevision} targetRaw={_coordinatorTargetRaw} "
            + $"targetUserId={FormatValue(_coordinatorTargetUserId)} membership={_coordinatorMembership} "
            + $"commonCapabilities={_coordinatorCommonCapabilities} "
            + $"compatible={_coordinatorCompatible} ready={ready}");
        var accepted = runner.SessionInfo.UpdateCustomProperties(properties);
        LogInfo($"TX STATE result acceptedByFusion={accepted} encoded={encodedState}");
        if (!accepted)
        {
            return;
        }

        _publishedRevision = _coordinatorRevision;
        _publishedTargetRaw = _coordinatorTargetRaw;
        _publishedTargetUserId = _coordinatorTargetUserId;
        _publishedMembership = _coordinatorMembership;
        _publishedCommonCapabilities = _coordinatorCommonCapabilities;
        _publishedCompatible = _coordinatorCompatible;
        _publishedReady = ready;
        SetObservedState(
            _coordinatorRevision,
            _coordinatorTargetRaw,
            _coordinatorTargetUserId,
            _coordinatorMembership,
            _coordinatorCommonCapabilities,
            _coordinatorCompatible,
            ready,
            valid: true);
    }

    private static void ReadObservedState(NetworkRunner runner, IReadOnlyList<LeaderHostParticipant> participants)
    {
        try
        {
            var properties = runner.SessionInfo.Properties;
            if (properties is null)
            {
                _observedValid = false;
                LogTransition(ref _lastObservedStateLog, "RX STATE unavailable reason=no-properties");
                return;
            }
            if (!TryReadString(properties, HostSelectionProtocol.PropertyState, out var encodedState))
            {
                _observedValid = false;
                LogTransition(ref _lastObservedStateLog, "RX STATE unavailable reason=property-missing");
                return;
            }
            if (!HostSelectionProtocol.TryParseState(encodedState, out var state))
            {
                _observedValid = false;
                LogTransition(
                    ref _lastObservedStateLog,
                    $"RX STATE rejected reason=empty-or-invalid encoded={FormatValue(encodedState)}");
                return;
            }

            var localMembership = ComputeMembership(participants);
            var expectedLeaderId = PgosLobby.Instance?.TeamLeaderId ?? string.Empty;
            var membershipMatches = string.Equals(
                state.Membership,
                localMembership,
                StringComparison.Ordinal);
            var leaderIdMatches = !string.IsNullOrWhiteSpace(expectedLeaderId)
                && string.Equals(state.TargetUserId, expectedLeaderId, StringComparison.Ordinal);
            var targetPresent = participants.Any(participant => participant.Raw == state.TargetPlayerRaw);
            var targetValid = state.TargetPlayerRaw != 0
                && leaderIdMatches
                && targetPresent;
            var valid = membershipMatches && targetValid;
            LogTransition(
                ref _lastObservedStateLog,
                $"RX STATE revision={state.Revision} targetRaw={state.TargetPlayerRaw} "
                + $"targetUserId={FormatValue(state.TargetUserId)} membership={state.Membership} "
                + $"commonCapabilities={state.CommonCapabilities} "
                + $"compatible={state.Compatible} ready={state.Ready} validation="
                + $"membershipMatch:{membershipMatches},leaderIdMatch:{leaderIdMatches},"
                + $"targetPresent:{targetPresent},valid:{valid} "
                + $"expectedLeaderId={FormatValue(expectedLeaderId)} localMembership={localMembership}");
            SetObservedState(
                state.Revision,
                state.TargetPlayerRaw,
                state.TargetUserId,
                state.Membership,
                state.CommonCapabilities,
                state.Compatible,
                state.Ready,
                valid);
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
        int commonCapabilities,
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
        _observedCommonCapabilities = commonCapabilities;
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
            var reason = local is null
                ? "local-participant-missing"
                : "local-user-id-missing";
            LogTransition(
                ref _lastPeerPublicationLog,
                $"TX PEER blocked reason={reason} localRaw={localRaw} "
                + $"participants=[{FormatParticipants(participants)}]");
            return;
        }

        var properties = runner.SessionInfo.Properties;
        var localCapabilities = GetLocalCapabilities();
        var registry = properties is not null
            && TryReadString(properties, HostSelectionProtocol.PropertyPeers, out var currentRegistry)
                ? currentRegistry
                : string.Empty;
        var existingAck = -1;
        if (HostSelectionProtocol.TryGetPeer(registry, localRaw, out var existing)
            && string.Equals(existing.UserId, local.UserId, StringComparison.Ordinal)
            && string.Equals(existing.Membership, membership, StringComparison.Ordinal)
            && existing.Capabilities == localCapabilities)
        {
            existingAck = existing.AcknowledgedRevision;
        }
        var value = HostSelectionProtocol.UpsertPeer(
            registry,
            localRaw,
            local.UserId,
            membership,
            localCapabilities,
            Math.Max(existingAck, acknowledgedRevision));
        var effectiveAck = Math.Max(existingAck, acknowledgedRevision);
        var messageType = effectiveAck >= 0 ? "ACK" : "HELLO";
        if (string.Equals(value, registry, StringComparison.Ordinal))
        {
            LogTransition(
                ref _lastPeerPublicationLog,
                $"TX {messageType} current localRaw={localRaw} userId={FormatValue(local.UserId)} "
                + $"membership={membership} capabilities={localCapabilities} "
                + $"acknowledgedRevision={effectiveAck}");
            return;
        }
        LogInfo(
            $"TX {messageType} requested localRaw={localRaw} userId={FormatValue(local.UserId)} "
            + $"membership={membership} capabilities={localCapabilities} "
            + $"acknowledgedRevision={effectiveAck}");
        var accepted = UpdateProperty(runner, HostSelectionProtocol.PropertyPeers, value);
        LogInfo(
            $"TX {messageType} result acceptedByFusion={accepted} "
            + $"localRaw={localRaw} membership={membership} capabilities={localCapabilities} "
            + $"acknowledgedRevision={effectiveAck}");
        if (accepted)
        {
            _lastPeerPublicationLog = string.Empty;
        }
    }

    public static void InitializeSessionProperties(StartGameArgs args)
    {
        if (!Enabled)
        {
            return;
        }

        LogInfo(
            $"START role={args.GameMode} session={FormatValue(args.SessionName)} "
            + $"sessionPropertiesAvailable={args.SessionProperties is not null}");
        if (args.SessionProperties is null)
        {
            return;
        }

        // Fusion allows only ten custom properties total and the game already consumes five.
        // Keep the entire protocol in two fixed strings: coordinator state and a compact peer
        // registry. Reserving per-player keys both exceeded the room limit and caused live
        // updates to be rejected by Photon.
        EnsureSessionProperty(args, HostSelectionProtocol.PropertyState, string.Empty);
        EnsureSessionProperty(args, HostSelectionProtocol.PropertyPeers, string.Empty);
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
            // In Client/Server mode Fusion only exposes PlayerRef-to-user-id lookups to the
            // server. Every peer can still read its own authenticated user id directly.
            if (playerRef == runner.LocalPlayer)
            {
                return runner.UserId ?? string.Empty;
            }
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
            participants.Select(participant => participant.Raw));
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
        var previousRunnerPointer = _runnerPointer;
        _runnerPointer = runnerPointer;
        _cachedParticipants = Array.Empty<LeaderHostParticipant>();
        _nextHelloAt = 0f;
        _nextAckAt = 0f;
        _coordinatorMembership = string.Empty;
        _coordinatorCompatible = false;
        _coordinatorCommonCapabilities = 0;
        _coordinatorRevision = 1;
        _coordinatorTargetRaw = 0;
        _coordinatorTargetUserId = string.Empty;
        _publishedRevision = -1;
        _publishedTargetRaw = int.MinValue;
        _publishedTargetUserId = string.Empty;
        _publishedMembership = string.Empty;
        _publishedCommonCapabilities = -1;
        _publishedCompatible = false;
        _publishedReady = false;
        _observedRevision = 0;
        _observedTargetRaw = 0;
        _observedTargetUserId = string.Empty;
        _observedMembership = string.Empty;
        _observedCommonCapabilities = 0;
        _observedCompatible = false;
        _observedReady = false;
        _observedValid = false;
        _lastAckedRevision = -1;
        _localOnlySession = false;
        _lastParticipantSnapshotLog = string.Empty;
        _lastPeerRegistryLog = string.Empty;
        _lastObservedStateLog = string.Empty;
        _lastLeaderResolutionLog = string.Empty;
        _lastCoordinatorQuorumLog = string.Empty;
        _lastPeerPublicationLog = string.Empty;
        if (runnerPointer == IntPtr.Zero && previousRunnerPointer != IntPtr.Zero)
        {
            LogInfo($"RUNNER detached previousPointer=0x{previousRunnerPointer.ToInt64():X}");
        }
    }

    public static void OverrideMatchHost(ref string hostId)
    {
        if (!Enabled)
        {
            return;
        }
        var originalHostId = hostId;
        RefreshObservedFromCurrentRunner();
        var privateGame = IsPrivateGameSelected();
        var localSocialUserId = GetLocalSocialUserId();
        var runner = PhotonLobby.Runner;
        var localRaw = IsUsableLobbyRunner(runner)
            ? runner!.LocalPlayer.RawEncoded
            : 0;
        LogInfo(
            $"DECISION requested backendHostId={FormatValue(originalHostId)} "
            + $"localSocialUserId={FormatValue(localSocialUserId)} localRaw={localRaw} "
            + $"privateGame={privateGame} state="
            + $"valid:{_observedValid},compatible:{_observedCompatible},ready:{_observedReady},"
            + $"commonCapabilities:{_observedCommonCapabilities},"
            + $"revision:{_observedRevision},targetRaw:{_observedTargetRaw},"
            + $"targetUserId:{FormatValue(_observedTargetUserId)},membership:{FormatValue(_observedMembership)}");
        if (!_observedValid
            || !_observedCompatible
            || !_observedReady
            || _observedTargetRaw == 0
            || string.IsNullOrWhiteSpace(_observedTargetUserId))
        {
            LogInfo(
                $"DECISION final action=PRESERVE reason={DescribeDisarmedReasons()} "
                + $"finalHostId={FormatValue(hostId)} localRole={ResolveLocalRole(hostId, localSocialUserId)}");
            return;
        }

        if (!LeaderHostPolicy.ShouldOverrideAssignedHost(
                privateGame,
                originalHostId,
                _observedTargetUserId))
        {
            if (!privateGame
                && !string.Equals(originalHostId, _observedTargetUserId, StringComparison.Ordinal))
            {
                LogInfo("Public matchmaking detected; preserving the backend-assigned match host");
            }
            var reason = !privateGame
                ? "public-game"
                : "backend-already-selected-party-leader";
            LogInfo(
                $"DECISION final action=PRESERVE reason={reason} "
                + $"finalHostId={FormatValue(hostId)} localRole={ResolveLocalRole(hostId, localSocialUserId)}");
            return;
        }

        hostId = _observedTargetUserId;
        LogInfo($"Match Fusion host overridden {originalHostId} -> {hostId}");
        LogInfo(
            $"DECISION final action=OVERRIDE reason=private-compatible-quorum "
            + $"backendHostId={FormatValue(originalHostId)} finalHostId={FormatValue(hostId)} "
            + $"localRole={ResolveLocalRole(hostId, localSocialUserId)}");
    }

    private static bool IsPrivateGameSelected()
    {
        try
        {
            var gameState = PgosLobby.Instance?._gameState;
            return gameState is not null
                && gameState.Pointer != IntPtr.Zero
                && gameState.PrivateGameCheckbox;
        }
        catch (Exception exception)
        {
            LogError("Reading the private-game state failed; preserving the backend-assigned host", exception);
            return false;
        }
    }

    private static void RefreshObservedFromCurrentRunner()
    {
        var runner = PhotonLobby.Runner;
        if (!IsUsableLobbyRunner(runner) || runner.Pointer != _runnerPointer)
        {
            _observedValid = false;
            LogInfo(
                $"DECISION refresh failed reason=runner-unavailable-or-changed "
                + $"trackedPointer=0x{_runnerPointer.ToInt64():X}");
            return;
        }
        var participants = GetParticipants(runner);
        if (!LeaderHostParticipantPolicy.IsComplete(
                runner.SessionInfo.PlayerCount,
                participants.Count))
        {
            _observedValid = false;
            LogInfo(
                $"DECISION refresh failed reason=incomplete-participant-snapshot "
                + $"sessionPlayerCount={runner.SessionInfo.PlayerCount} observedRealPlayers={participants.Count} "
                + $"participants=[{FormatParticipants(participants)}]");
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
                LogInfo(
                    $"DECISION refresh failed reason=local-only-party-creator-unresolved "
                    + $"localRaw={runner.LocalPlayer.RawEncoded}");
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
                GetLocalCapabilities(),
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

    private static void LogObservedPlayer(string lifecycle, SpookedNetworkPlayer player)
    {
        try
        {
            var playerRef = player.PlayerRef;
            LogInfo(
                $"PLAYER {lifecycle} pointer=0x{player.Pointer.ToInt64():X} "
                + $"raw={playerRef.RawEncoded} playerId={playerRef.PlayerId} "
                + $"name={FormatValue(player.Nickname)} real={playerRef.IsRealPlayer} bot={player.IsBot}");
        }
        catch
        {
            LogInfo($"PLAYER {lifecycle} pointer=0x{player.Pointer.ToInt64():X} details=unavailable");
        }
    }

    private static void LogParticipantSnapshot(
        NetworkRunner runner,
        IReadOnlyList<LeaderHostParticipant> participants,
        bool complete)
    {
        var membership = ComputeMembership(participants);
        LogTransition(
            ref _lastParticipantSnapshotLog,
            $"SNAPSHOT sessionPlayerCount={runner.SessionInfo.PlayerCount} "
            + $"observedRealPlayers={participants.Count} complete={complete} membership={membership} "
            + $"participants=[{FormatParticipants(participants)}]");
    }

    private static void LogPeerRegistry(
        NetworkRunner runner,
        IReadOnlyList<LeaderHostParticipant> participants,
        string membership)
    {
        var properties = runner.SessionInfo.Properties;
        if (properties is null)
        {
            LogTransition(ref _lastPeerRegistryLog, "RX PEERS unavailable reason=no-properties");
            return;
        }
        if (!TryReadString(properties, HostSelectionProtocol.PropertyPeers, out var registry))
        {
            LogTransition(ref _lastPeerRegistryLog, "RX PEERS unavailable reason=property-missing");
            return;
        }

        LogTransition(
            ref _lastPeerRegistryLog,
            $"RX PEERS membership={membership} encoded={FormatValue(registry)} "
            + $"statuses=[{DescribePeerStatuses(runner, participants, membership, _observedRevision)}]");
    }

    private static string DescribePeerStatuses(
        NetworkRunner runner,
        IReadOnlyList<LeaderHostParticipant> participants,
        string membership,
        int expectedRevision)
    {
        var properties = runner.SessionInfo.Properties;
        return string.Join(
            "; ",
            participants.Select(participant =>
            {
                var prefix = $"raw={participant.Raw},name={FormatValue(participant.Name)},"
                    + $"userId={FormatValue(participant.UserId)}";
                if (participant.Raw == runner.LocalPlayer.RawEncoded)
                {
                    return $"{prefix},status=local-implicit,capabilities={GetLocalCapabilities()}";
                }
                if (properties is null || !TryReadPeer(properties, participant.Raw, out var peer))
                {
                    return $"{prefix},status=missing";
                }

                var identityMatches = string.Equals(
                    peer.UserId,
                    participant.UserId,
                    StringComparison.Ordinal);
                var membershipMatches = string.Equals(
                    peer.Membership,
                    membership,
                    StringComparison.Ordinal);
                var revisionMatches = peer.AcknowledgedRevision == expectedRevision;
                return $"{prefix},status=received,peerUserId={FormatValue(peer.UserId)},"
                    + $"peerMembership={peer.Membership},capabilities={peer.Capabilities},"
                    + $"ack={peer.AcknowledgedRevision},"
                    + $"identityMatch={identityMatches},membershipMatch={membershipMatches},"
                    + $"revisionMatch={revisionMatches}";
            }));
    }

    private static string FormatParticipants(IEnumerable<LeaderHostParticipant> participants)
    {
        return string.Join(
            "; ",
            participants.Select(participant =>
                $"raw={participant.Raw},name={FormatValue(participant.Name)},"
                + $"fusionUserId={FormatValue(participant.UserId)},"
                + $"real={participant.IsRealPlayer},bot={participant.IsBot}"));
    }

    private static string GetLocalSocialUserId()
    {
        try
        {
            return PgosLobby.Instance?._nakamaService?.UserId ?? string.Empty;
        }
        catch (Exception exception)
        {
            LogError("Reading the local social user id failed", exception);
            return string.Empty;
        }
    }

    private static int GetLocalCapabilities()
    {
        try
        {
            var pluginAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(assembly => string.Equals(
                    assembly.GetName().Name,
                    UniformSeekerRandomAssemblyName,
                    StringComparison.Ordinal));
            var pluginType = pluginAssembly?.GetType(
                UniformSeekerRandomPluginType,
                throwOnError: false,
                ignoreCase: false);
            var enabledProperty = pluginType?.GetProperty(
                "IsFeatureEnabled",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            var uniformSeekerEnabled = enabledProperty?.GetValue(null) is true;
            var capabilities = uniformSeekerEnabled
                ? HostSelectionProtocol.UniformSeekerRandomCapability
                : 0;
            LogTransition(
                ref _lastCapabilityDetectionLog,
                $"CAPABILITIES local uniformSeekerRandom={uniformSeekerEnabled} mask={capabilities}");
            return capabilities;
        }
        catch (Exception exception)
        {
            LogTransition(
                ref _lastCapabilityDetectionLog,
                $"CAPABILITIES local detection-failed error={FormatValue(exception.Message)} mask=0");
            return 0;
        }
    }

    private static string ResolveLocalRole(string finalHostId, string localSocialUserId)
    {
        if (string.IsNullOrWhiteSpace(finalHostId) || string.IsNullOrWhiteSpace(localSocialUserId))
        {
            return "Unknown";
        }

        return string.Equals(finalHostId, localSocialUserId, StringComparison.Ordinal)
            ? "Host"
            : "Client";
    }

    private static string DescribeDisarmedReasons()
    {
        var reasons = new List<string>();
        if (!_observedValid)
        {
            reasons.Add("state-invalid");
        }
        if (!_observedCompatible)
        {
            reasons.Add("compatibility-quorum-incomplete");
        }
        if (!_observedReady)
        {
            reasons.Add("acknowledgement-quorum-incomplete");
        }
        if (_observedTargetRaw == 0)
        {
            reasons.Add("target-player-missing");
        }
        if (string.IsNullOrWhiteSpace(_observedTargetUserId))
        {
            reasons.Add("target-user-id-missing");
        }
        return reasons.Count == 0 ? "unknown" : string.Join(",", reasons);
    }

    private static string FormatValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "<empty>";
        }

        return value
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace(";", ",", StringComparison.Ordinal);
    }

    private static void LogTransition(ref string previous, string message)
    {
        if (!LoggingEnabled || string.Equals(previous, message, StringComparison.Ordinal))
        {
            return;
        }

        previous = message;
        _logger?.LogInfo(message);
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
        return TryReadString(properties, HostSelectionProtocol.PropertyPeers, out var registry)
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
