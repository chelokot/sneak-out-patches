using BepInEx.Logging;
using Fusion;
using Gameplay.Match.MatchState;
using HarmonyLib;
using Networking.Lobby;
using Networking.Party;
using SneakOut.NetworkHostSelector;
using System.Collections.Generic;
using Types;

namespace SneakOut.UniformSeekerRandom;

internal static class UniformSeekerRandomRuntime
{
    private static readonly System.Random Random = new();

    private static ManualLogSource? _logger;
    private static Harmony? _harmony;
    private static UniformSeekerRandomConfig? _configuration;
    private static MatchStateMachine? _activeStateMachine;
    private static IntPtr _activeShouldStartStatePointer;
    private static bool _launchQuorumReady;
    private static string _launchDecision = "not-captured";
    private static string _launchHostId = string.Empty;
    private static int _launchRevision;
    private static int _launchCommonCapabilities;
    private static string _lastReplicatedSeekerLog = string.Empty;

    public static bool IsEnabled => _configuration is not null && _configuration.EnableMod.Value;

    private static bool LoggingEnabled =>
        _configuration is not null && _configuration.EnableLogging.Value;

    public static void Initialize(ManualLogSource logger, UniformSeekerRandomConfig configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _harmony ??= new Harmony(UniformSeekerRandomPlugin.PluginGuid);
        _harmony.PatchAll();
        LogInfo(
            $"TRACE initialized protocol={HostSelectionProtocol.Version} "
            + $"capability={HostSelectionProtocol.UniformSeekerRandomCapability} "
            + $"enabled={configuration.EnableMod.Value} diagnostics={configuration.EnableLogging.Value}");
    }

    public static void CaptureLaunchHandshake(string hostId)
    {
        _launchQuorumReady = false;
        _launchDecision = "capture-started";
        _launchHostId = hostId ?? string.Empty;
        _launchRevision = 0;
        _launchCommonCapabilities = 0;
        _lastReplicatedSeekerLog = string.Empty;

        var privateGame = IsPrivateGameSelected();
        var runner = PhotonLobby.Runner;
        var runnerUsable = IsUsableLobbyRunner(runner);
        var localRaw = runnerUsable ? runner!.LocalPlayer.RawEncoded : 0;
        var playerCount = runnerUsable ? runner!.SessionInfo.PlayerCount : 0;
        LogInfo(
            $"HANDSHAKE capture requested hostId={FormatValue(_launchHostId)} "
            + $"privateGame={privateGame} runnerUsable={runnerUsable} "
            + $"localRaw={localRaw} playerCount={playerCount}");

        if (!IsEnabled)
        {
            FinishLaunchCapture("mod-disabled", ready: false);
            return;
        }
        if (!privateGame)
        {
            FinishLaunchCapture("public-game", ready: false);
            return;
        }
        if (!runnerUsable)
        {
            FinishLaunchCapture("lobby-runner-unavailable", ready: false);
            return;
        }

        if (playerCount == 1)
        {
            var party = PgosLobby.Instance;
            var localOnlyValid = party is not null
                && party.AmITeamLeader
                && !string.IsNullOrWhiteSpace(party.TeamLeaderId)
                && string.Equals(party.TeamLeaderId, _launchHostId, StringComparison.Ordinal);
            FinishLaunchCapture(
                localOnlyValid
                    ? "private-local-only"
                    : "local-only-host-identity-mismatch",
                localOnlyValid);
            return;
        }

        try
        {
            var properties = runner!.SessionInfo.Properties;
            if (properties is null)
            {
                FinishLaunchCapture("session-properties-unavailable", ready: false);
                return;
            }
            if (!TryReadString(properties, HostSelectionProtocol.PropertyState, out var encodedState))
            {
                FinishLaunchCapture("leader-host-state-missing", ready: false);
                return;
            }
            if (!HostSelectionProtocol.TryParseState(encodedState, out var state))
            {
                LogInfo($"HANDSHAKE received state rejected encoded={FormatValue(encodedState)}");
                FinishLaunchCapture("leader-host-state-invalid", ready: false);
                return;
            }

            _launchRevision = state.Revision;
            _launchCommonCapabilities = state.CommonCapabilities;
            var hostMatches = !string.IsNullOrWhiteSpace(_launchHostId)
                && string.Equals(state.TargetUserId, _launchHostId, StringComparison.Ordinal);
            var capabilityPresent =
                (state.CommonCapabilities & HostSelectionProtocol.UniformSeekerRandomCapability) != 0;
            var ready = state.Compatible
                && state.Ready
                && hostMatches
                && capabilityPresent;
            LogInfo(
                $"HANDSHAKE received state revision={state.Revision} "
                + $"targetRaw={state.TargetPlayerRaw} targetUserId={FormatValue(state.TargetUserId)} "
                + $"membership={state.Membership} compatible={state.Compatible} ready={state.Ready} "
                + $"commonCapabilities={state.CommonCapabilities} validation="
                + $"hostMatch:{hostMatches},uniformCapability:{capabilityPresent},accepted:{ready}");
            FinishLaunchCapture(
                ready
                    ? "private-compatible-uniform-quorum"
                    : DescribeRejectedState(state, hostMatches, capabilityPresent),
                ready);
        }
        catch (Exception exception)
        {
            LogError("Capturing uniform-seeker launch handshake failed", exception);
            FinishLaunchCapture("handshake-exception", ready: false);
        }
    }

    public static void BeginShouldStartTick(
        ShouldStartState shouldStartState,
        MatchStateMachine stateMachine)
    {
        _activeShouldStartStatePointer = shouldStartState?.Pointer ?? IntPtr.Zero;
        _activeStateMachine = stateMachine;
        if (stateMachine is null || stateMachine.Pointer == IntPtr.Zero)
        {
            LogInfo(
                $"TICK enter shouldStart=0x{_activeShouldStartStatePointer.ToInt64():X} "
                + "stateMachine=unavailable");
            return;
        }

        LogInfo(
            $"TICK enter shouldStart=0x{_activeShouldStartStatePointer.ToInt64():X} "
            + $"stateMachine=0x{stateMachine.Pointer.ToInt64():X} "
            + $"stateAuthority={stateMachine.HasStateAuthority} server={stateMachine.Runner?.IsServer ?? false} "
            + $"matchState={stateMachine.MatchStateType} replicatedSeeker={stateMachine.SeekerChosenRefId} "
            + $"localChosenSeeker={stateMachine._gameState?.ChosenSeekerId ?? -1} "
            + $"launchReady={_launchQuorumReady} launchDecision={_launchDecision}");
    }

    public static void EndShouldStartTick(
        ShouldStartState shouldStartState,
        MatchStateMachine stateMachine)
    {
        try
        {
            if (stateMachine is not null && stateMachine.Pointer != IntPtr.Zero)
            {
                LogInfo(
                    $"TICK exit shouldStart=0x{shouldStartState.Pointer.ToInt64():X} "
                    + $"stateMachine=0x{stateMachine.Pointer.ToInt64():X} "
                    + $"stateAuthority={stateMachine.HasStateAuthority} "
                    + $"replicatedSeeker={stateMachine.SeekerChosenRefId} "
                    + $"localChosenSeeker={stateMachine._gameState?.ChosenSeekerId ?? -1}");
            }
        }
        finally
        {
            if (shouldStartState is null
                || shouldStartState.Pointer == _activeShouldStartStatePointer)
            {
                _activeStateMachine = null;
                _activeShouldStartStatePointer = IntPtr.Zero;
            }
        }
    }

    public static bool TryHandleUniformHunterRandom(ShouldStartState shouldStartState, ref int result)
    {
        if (!IsEnabled)
        {
            return false;
        }

        var stateMachine = _activeStateMachine;
        var gameMode = shouldStartState._gameState.GameMode;
        var stateAuthority = stateMachine is not null
            && stateMachine.Pointer != IntPtr.Zero
            && stateMachine.HasStateAuthority;
        LogInfo(
            $"SELECTION requested mode={gameMode} stateMachine="
            + $"{(stateMachine is null ? "<none>" : $"0x{stateMachine.Pointer.ToInt64():X}")} "
            + $"stateAuthority={stateAuthority} launchReady={_launchQuorumReady} "
            + $"launchDecision={_launchDecision} launchHostId={FormatValue(_launchHostId)} "
            + $"revision={_launchRevision} commonCapabilities={_launchCommonCapabilities}");

        if (gameMode == GameModeType.Berek)
        {
            LogInfo("SELECTION final action=STOCK reason=crown-mode");
            return false;
        }
        if (!_launchQuorumReady)
        {
            LogInfo($"SELECTION final action=STOCK reason=launch-quorum-not-ready:{_launchDecision}");
            return false;
        }
        if (stateMachine is null || stateMachine.Pointer == IntPtr.Zero)
        {
            LogInfo("SELECTION final action=STOCK reason=no-active-match-state-machine");
            return false;
        }
        if (!stateAuthority)
        {
            LogInfo("SELECTION final action=STOCK reason=not-state-authority");
            return false;
        }

        try
        {
            var candidateInternalIds = CollectSeekerCandidateInternalIds(
                shouldStartState,
                out var candidateSource,
                out var playerSnapshot);
            LogInfo(
                $"CANDIDATES source={candidateSource} eligibleCount={candidateInternalIds.Count} "
                + $"eligible=[{string.Join(",", candidateInternalIds)}] players=[{playerSnapshot}]");
            if (candidateInternalIds.Count == 0)
            {
                LogInfo("SELECTION final action=STOCK reason=no-eligible-candidates");
                return false;
            }

            result = candidateInternalIds[Random.Next(candidateInternalIds.Count)];
            LogInfo(
                $"SELECTION final action=OVERRIDE selectedInternalId={result} "
                + $"candidateCount={candidateInternalIds.Count} authority=True "
                + "transport=stock-seeker-replication");
            return true;
        }
        catch (Exception exception)
        {
            LogError("Uniform seeker selection failed; preserving stock selection", exception);
            LogInfo("SELECTION final action=STOCK reason=selection-exception");
            return false;
        }
    }

    public static void ObserveReplicatedSeeker(MatchStateMachine stateMachine)
    {
        if (!LoggingEnabled || stateMachine is null || stateMachine.Pointer == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var message =
                $"REPLICATION observed stateMachine=0x{stateMachine.Pointer.ToInt64():X} "
                + $"stateAuthority={stateMachine.HasStateAuthority} server={stateMachine.Runner?.IsServer ?? false} "
                + $"matchState={stateMachine.MatchStateType} "
                + $"replicatedSeeker={stateMachine.SeekerChosenRefId} "
                + $"localChosenSeeker={stateMachine._gameState?.ChosenSeekerId ?? -1} "
                + $"replicatedPlayer={DescribeSelectedPlayer(stateMachine, stateMachine.SeekerChosenRefId)}";
            LogTransition(ref _lastReplicatedSeekerLog, message);
        }
        catch (Exception exception)
        {
            LogError("Observing replicated seeker state failed", exception);
        }
    }

    private static List<int> CollectSeekerCandidateInternalIds(
        ShouldStartState shouldStartState,
        out string candidateSource,
        out string playerSnapshot)
    {
        var preferredCandidateInternalIds = new List<int>();
        var fallbackCandidateInternalIds = new List<int>();
        var playerDescriptions = new List<string>();
        var networkPlayers = shouldStartState._networkPlayerRegistry._components;
        for (var playerIndex = 0; playerIndex < networkPlayers.Length; playerIndex++)
        {
            var networkPlayer = networkPlayers[playerIndex];
            if (networkPlayer is null)
            {
                playerDescriptions.Add($"index={playerIndex},null=True");
                continue;
            }

            var playerRef = networkPlayer.PlayerRef;
            playerDescriptions.Add(
                $"index={playerIndex},internalId={networkPlayer.InternalId},"
                + $"raw={playerRef.RawEncoded},name={FormatValue(networkPlayer.Nickname)},"
                + $"real={playerRef.IsRealPlayer},bot={networkPlayer.IsBot},"
                + $"canBeSeeker={networkPlayer.CanBeSeeker}");
            if (!networkPlayer.IsBot)
            {
                fallbackCandidateInternalIds.Add(networkPlayer.InternalId);
            }

            if (networkPlayer.CanBeSeeker)
            {
                preferredCandidateInternalIds.Add(networkPlayer.InternalId);
            }
        }

        playerSnapshot = string.Join("; ", playerDescriptions);
        candidateSource = preferredCandidateInternalIds.Count > 0
            ? "can-be-seeker"
            : "non-bot-fallback";
        return preferredCandidateInternalIds.Count > 0
            ? preferredCandidateInternalIds
            : fallbackCandidateInternalIds;
    }

    private static void FinishLaunchCapture(string decision, bool ready)
    {
        _launchDecision = decision;
        _launchQuorumReady = ready;
        LogInfo(
            $"HANDSHAKE final ready={ready} decision={decision} "
            + $"hostId={FormatValue(_launchHostId)} revision={_launchRevision} "
            + $"commonCapabilities={_launchCommonCapabilities}");
    }

    private static string DescribeSelectedPlayer(MatchStateMachine stateMachine, int internalId)
    {
        var networkPlayers = stateMachine.ShouldStartState?._networkPlayerRegistry?._components;
        if (networkPlayers is null)
        {
            return "<registry-unavailable>";
        }

        for (var playerIndex = 0; playerIndex < networkPlayers.Length; playerIndex++)
        {
            var networkPlayer = networkPlayers[playerIndex];
            if (networkPlayer is null || networkPlayer.InternalId != internalId)
            {
                continue;
            }

            return $"internalId:{internalId},raw:{networkPlayer.PlayerRef.RawEncoded},"
                + $"name:{FormatValue(networkPlayer.Nickname)},bot:{networkPlayer.IsBot}";
        }
        return $"internalId:{internalId},unmapped:True";
    }

    private static string DescribeRejectedState(
        HostSelectionState state,
        bool hostMatches,
        bool capabilityPresent)
    {
        var reasons = new List<string>();
        if (!state.Compatible)
        {
            reasons.Add("leader-host-incompatible");
        }
        if (!state.Ready)
        {
            reasons.Add("leader-host-not-ready");
        }
        if (!hostMatches)
        {
            reasons.Add("final-host-mismatch");
        }
        if (!capabilityPresent)
        {
            reasons.Add("uniform-capability-incomplete");
        }
        return reasons.Count == 0 ? "state-rejected" : string.Join(",", reasons);
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
            LogError("Reading private-game state failed", exception);
            return false;
        }
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
}
