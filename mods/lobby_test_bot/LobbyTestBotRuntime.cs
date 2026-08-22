using BepInEx;
using BepInEx.Logging;
using Events;
using Fusion;
using Gameplay.ArrowIndicators;
using Gameplay.Buffs;
using Gameplay.Interactions;
using Gameplay.Player;
using Gameplay.Player.Components;
using Gameplay.Player.Customization;
using Gameplay.Spawn;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using Kinguinverse.DataUtils.Events;
using EmoteType = Kinguinverse.WebServiceProvider.Types_v2.EmoteType;
using Networking;
using Networking.Matchmaking;
using Networking.Matchmaking.Match;
using SneakOut.PortalSettings;
using TMPro;
using Types;
using UI.Buttons;
using UI.Views.Lobby;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SneakOut.LobbyTestBot;

internal static class LobbyTestBotRuntime
{
    private enum PendingOperation
    {
        None,
        Add,
        Remove
    }

    private const float NativeSwitchWidth = 315.3f;
    private const float NativeSwitchHeight = 37.4f;
    private const float ExpandedDummySwitchY = 5.6f;
    private const float CollapsedDummySwitchY = -11.4f;
    private const float RoleSwitchY = -38f;
    private const float PendingTimeout = 8f;
    private const float RefreshInterval = 0.4f;

    private static readonly Color SelectedSegmentColor = new(0.08627451f, 0.5372549f, 0.654902f, 1f);
    private static readonly Color DeselectedSegmentColor = new(0.16f, 0.18f, 0.22f, 0.95f);

    private static readonly Dictionary<IntPtr, LobbyTestBotUiState> UiStateByView = new();

    private static ManualLogSource? _logger;
    private static LobbyTestBotConfig? _configuration;
    private static Harmony? _harmony;
    private static UI.GameUIManager? _gameUiManager;
    private static SceneSpawner? _sceneSpawner;
    private static float _nextSceneSpawnerRecoveryAt;
    private static bool _watcherInstalled;
    private static bool _controlsPrepared;
    private static PendingOperation _pendingOperation;
    private static float _pendingStartedAt;
    private static int _requestedPlayerRefId;
    private static IntPtr _requestedSpawnerPointer;
    private static IntPtr _diagnosticPublishedLobbySpawnerPointer;
    private static IntPtr _diagnosticOpenedPortalSpawnerPointer;
    private static float _diagnosticPortalOpenedAt;
    private static float _diagnosticPortalCaptureAt;
    private static float _diagnosticUiActionNotBefore;
    private static bool _diagnosticPortalCaptured;
    private static SpookedOutlineButton? _diagnosticHoveredButton;
    private static float _diagnosticHoverReleaseAt;
    private static float _diagnosticMatchCaptureAt;
    private static bool _diagnosticMatchCaptured;
    private static float _diagnosticBotPresentCaptureAt = -1f;
    private static float _diagnosticBotRemovedCaptureAt = -1f;
    private static float _lobbyPlayersPanelRefreshAt = -1f;
    private static string? _diagnosticFlowCaptureDirectory;
    private static float _diagnosticFlowCaptureStartedAt = -1f;
    private static float _diagnosticFlowCaptureNextAt;
    private static int _diagnosticFlowCaptureIndex;
    private static float _diagnosticWalkStartsAt = -1f;
    private static float _diagnosticWalkEndsAt = -1f;
    private static float _diagnosticPortalOpenNotBefore;
    private static float _nextManagedOutfitRefreshAt = -1f;
    private static float _managedOutfitRefreshDeadline = -1f;
    private static bool _managedPrefabRefreshRequested;
    private static float _managedPrefabRefreshRequestedAt = -1f;
    private static bool _managedDirectPrefabSpawnRequested;
    private static UnderlyingPrefabComponent? _managedCharacterPrefabSpawnTarget;
    private static NetworkRunner.OnBeforeSpawned? _managedCharacterPrefabInitializer;
    private static IntPtr _diagnosticAutoStartSpawnerPointer;
    private static float _diagnosticBotReadyAt;
    private static bool _diagnosticModeRequested;
    private static bool _diagnosticModeSelectionFailureLogged;
    private static bool _diagnosticPrivateToggleRequested;
    private static bool _diagnosticPlayRequested;
    private static bool _diagnosticRemoveRequested;
    private static int _diagnosticLocalPlayerRefId;
    private static IntPtr _managedSpawnerPointer;
    private static IntPtr _managedPlayerPointer;
    private static IntPtr _managedNetworkObjectPointer;
    private static int _managedPlayerRefId;
    private static int _managedFusionPlayerRefRaw;
    private static bool _managedBotControlActive;
    private static int _managedBotControlPlayerRefRaw;
    private static Gameplay.CameraManager? _managedBotControlCameraManager;
    private static float _managedHunterVisualRepairAt = -1f;
    private static float _managedHunterVisualRepairDeadline = -1f;
    private static readonly Dictionary<IntPtr, NetworkObject> ManagedBotControlAuthorityObjects = new();
    private static readonly HashSet<IntPtr> ManagedAnimatorPointers = new();
    private static readonly HashSet<IntPtr> LoggedAnimatorPointers = new();
    private static readonly HashSet<IntPtr> DeferredPlayerIndicatorPointers = new();
    private static float _nextDeferredPlayerIndicatorRetryAt;
    private static bool _managedMatchJoinStarted;
    private static bool _carryBotIntoMatch;
    private static GameModeType _managedMatchMode;
    private static bool _managedHunterConfirmationSent;
    private static float _managedHunterConfirmationRetryAt;
    [ThreadStatic]
    private static bool _managedMatchStartGuardScope;
    [ThreadStatic]
    private static bool _managedCharacterPrefabSpawnScope;
    [ThreadStatic]
    private static int _managedBotSeekerCageLocalScopeDepth;
    [ThreadStatic]
    private static int _managedBotSeekerCageLocalInternalId;

    public static void Initialize(ManualLogSource logger, LobbyTestBotConfig configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _harmony ??= new Harmony(LobbyTestBotPlugin.PluginGuid);
        _harmony.PatchAll();
        EnsureWatcher();
    }

    public static void BindPortalManager(UI.GameUIManager gameUiManager)
    {
        _gameUiManager = gameUiManager;
        _controlsPrepared = false;
        if (LoggingEnabled)
        {
            _logger?.LogInfo("Lobby bot captured GameUIManager");
        }
    }

    public static void RememberSceneSpawner(SceneSpawner sceneSpawner)
    {
        if (sceneSpawner is not null && sceneSpawner.Pointer != IntPtr.Zero)
        {
            _sceneSpawner = sceneSpawner;
            _nextSceneSpawnerRecoveryAt = 0f;
        }
    }

    public static bool PreparePlayerIndicators(PlayersPositionIndicator indicator)
    {
        var ready = PlayerIndicatorsHaveDependencies(indicator);
        if (!ready)
        {
            DeferredPlayerIndicatorPointers.Add(indicator.Pointer);
        }

        if (LoggingEnabled)
        {
            var entries = indicator._playerIndicators;
            _logger?.LogInfo(
                "Player indicator readiness before stock ManagerAwake: "
                + $"settings={indicator._settings is not null}, "
                + $"registry={indicator._networkPlayerRegistry is not null}, "
                + $"gameState={indicator._gameState is not null}, "
                + $"entries={entries?.Length ?? -1}, "
                + $"nullEntries={entries?.Count(entry => entry is null || entry.Pointer == IntPtr.Zero) ?? -1}, "
                + $"deferred={!ready}");
        }

        return ready;
    }

    public static bool PlayerIndicatorsHaveDependencies(PlayersPositionIndicator indicator)
    {
        return indicator.Pointer != IntPtr.Zero
            && indicator._settings is not null
            && indicator._networkPlayerRegistry is not null
            && indicator._gameState is not null
            && indicator._playerIndicators is not null;
    }

    public static bool ShouldRunManagedBotVisibilityUpdate(EntityVisibilityComponent visibility)
    {
        if (!Enabled
            || !_managedBotControlActive
            || visibility is null
            || visibility.Pointer == IntPtr.Zero)
        {
            return true;
        }

        var player = visibility._spookedNetworkPlayer;
        if (!IsManagedBot(player))
        {
            return true;
        }

        // Stock visibility is evaluated from the original local player's point of view. While
        // the camera and controls are on the bot, preserve the noisy/silent half of the state but
        // keep its controlled character prefab rendered. Stock visibility resumes on switch-back.
        if (visibility._stateType == VisibilityStateType.InvisibleNoisy)
        {
            visibility.ForceVisibilityChange(VisibilityStateType.VisibleNoisy);
        }
        else if (visibility._stateType == VisibilityStateType.InvisibleSilent)
        {
            visibility.ForceVisibilityChange(VisibilityStateType.VisibleSilent);
        }

        return false;
    }

    private static void EnsureWatcher()
    {
        if (_watcherInstalled)
        {
            return;
        }

        ClassInjector.RegisterTypeInIl2Cpp<LobbyBotLifecycleWatcher>();
        var watcherObject = new GameObject("LobbyTestBotWatcher");
        UnityEngine.Object.DontDestroyOnLoad(watcherObject);
        watcherObject.hideFlags = HideFlags.HideAndDontSave;
        watcherObject.AddComponent<LobbyBotLifecycleWatcher>();
        _watcherInstalled = true;
    }

    private static void WatcherTick()
    {
        try
        {
            TryCaptureDiagnosticFlow();
            TryRefreshLobbyPlayersPanel();
            TryInitializeDeferredPlayerIndicators();
            ObservePendingOperation();
            MaintainMatchBot();
            TryToggleManagedBotControl();
            TryRepairManagedHunterVisual();
            TryRefreshPendingManagedBotOutfit();
            TryCaptureDiagnosticBotIdentity();
            TryCaptureDiagnosticMatch();
        }
        catch (Exception exception)
        {
            LogError("Lobby bot network lifecycle watcher failed", exception);
        }

        var manager = _gameUiManager;
        if (manager is null || manager.Pointer == IntPtr.Zero)
        {
            return;
        }

        var view = manager._portalPlayView;
        if (view is null || view.Pointer == IntPtr.Zero || view._playButton is null)
        {
            return;
        }

        try
        {
            if (!_controlsPrepared)
            {
                PreparePortalControls();
                _controlsPrepared = UiStateByView.TryGetValue(view.Pointer, out var state) && state.IsAlive;
            }

            if (_controlsPrepared)
            {
                TryRunDiagnosticAutoOpen(view);
                TryReleaseDiagnosticPortalHover();
                TryCaptureDiagnosticPortal();
                TickPortal(view);
                TryRunDiagnosticAutoRemove(view);
                TryRunDiagnosticAutoStart(view);
            }
        }
        catch (Exception exception)
        {
            LogError("Lobby bot lifecycle watcher failed", exception);
        }
    }

    private static void WatcherLateTick()
    {
        if (!_managedBotControlActive)
        {
            return;
        }

        var bot = FindManagedBot();
        if (bot is null)
        {
            return;
        }

        try
        {
            if (!TryGetManagedBotVisual(bot, out _, out var visual))
            {
                return;
            }

            var botTransform = bot.transform;
            var parent = visual.parent;
            if (parent is null || parent == null || parent.Pointer != botTransform.Pointer)
            {
                visual.SetParent(botTransform, false);
            }

            // The character prefab is a separate network object. Its NetworkTransform can restore
            // the spawn pose after the player root has simulated, so pin the stock current visual
            // to its authored local origin after Fusion has rendered the frame.
            visual.localPosition = Vector3.zero;
            visual.localRotation = Quaternion.identity;
        }
        catch (Exception exception)
        {
            if (LoggingEnabled)
            {
                _logger?.LogWarning($"Managed bot visual changed during late-frame sync: {exception.Message}");
            }
        }
    }

    private static void TryInitializeDeferredPlayerIndicators()
    {
        if (DeferredPlayerIndicatorPointers.Count == 0
            || Time.unscaledTime < _nextDeferredPlayerIndicatorRetryAt)
        {
            return;
        }

        _nextDeferredPlayerIndicatorRetryAt = Time.unscaledTime + 0.5f;
        foreach (var indicator in Resources.FindObjectsOfTypeAll<PlayersPositionIndicator>())
        {
            if (indicator is null
                || !DeferredPlayerIndicatorPointers.Contains(indicator.Pointer)
                || !PlayerIndicatorsHaveDependencies(indicator))
            {
                continue;
            }

            indicator.ManagerAwake();
            DeferredPlayerIndicatorPointers.Remove(indicator.Pointer);
            if (LoggingEnabled)
            {
                _logger?.LogInfo("Initialized deferred player indicators after their injected dependencies became ready");
            }
        }
    }

    public static void PreparePortalControls()
    {
        var view = _gameUiManager?._portalPlayView;
        if (view is not null)
        {
            OpenPortal(view);
        }
    }

    private static bool Enabled => _configuration is not null && _configuration.EnableMod.Value;

    private static bool LoggingEnabled => _configuration is not null && _configuration.EnableLogging.Value;

    public static bool ManagedMatchStartInProgress => _managedMatchJoinStarted;

    private static string BotNickname => _configuration!.BotNickname.Value.Trim();

    private static bool BotPrefersHunter =>
        _configuration?.RolePreference.Value == BotRolePreference.HunterPriority;

    public static void OpenPortal(PortalPlayView view)
    {
        if (!Enabled)
        {
            ReleasePortal(view);
            return;
        }

        try
        {
            var state = EnsureButton(view);
            if (state is null)
            {
                return;
            }

            RefreshButton(state);
        }
        catch (Exception exception)
        {
            LogError("Lobby bot portal setup failed", exception);
        }
    }

    public static void TickPortal(PortalPlayView view)
    {
        if (!Enabled)
        {
            ReleasePortal(view);
            return;
        }

        try
        {
            if (!UiStateByView.TryGetValue(view.Pointer, out var state) || !state.IsAlive)
            {
                OpenPortal(view);
                return;
            }

            var now = Time.unscaledTime;
            if (now < state.NextRefreshTime)
            {
                return;
            }

            state.NextRefreshTime = now + RefreshInterval;
            TryRunDiagnosticAutoAdd();
            RefreshButton(state);
        }
        catch (Exception exception)
        {
            LogError("Lobby bot portal update failed", exception);
        }
    }

    public static void ReleasePortal(PortalPlayView view)
    {
        if (!UiStateByView.Remove(view.Pointer, out var state))
        {
            return;
        }

        if (state.IsAlive)
        {
            state.DummySwitch.Button.onClick.RemoveListener(state.DummyClickAction);
            state.RoleSwitch.Button.onClick.RemoveListener(state.RoleClickAction);
            UnityEngine.Object.Destroy(state.Section);
        }
    }

    public static void ObservePlayerDespawned(SpookedNetworkPlayer player)
    {
        if (!Enabled || !IsManagedBot(player))
        {
            return;
        }

        if (_pendingOperation == PendingOperation.Remove
            && player.KinguinverseId == _requestedPlayerRefId)
        {
            if (LoggingEnabled)
            {
                _logger?.LogInfo(
                    $"Authoritative bot despawn completed: playerRef={player.KinguinverseId}, "
                    + $"internalId={player.InternalId}");
            }

            ClearPendingOperation();
        }
        else if (LoggingEnabled)
        {
            _logger?.LogInfo(
                $"Managed bot despawned outside a pending request: playerRef={player.KinguinverseId}, "
                + $"internalId={player.InternalId}");
        }

        var preserveMatchIntent = _carryBotIntoMatch && _managedMatchJoinStarted;
        if (!preserveMatchIntent && _configuration?.CaptureFlowSequence.Value == true)
        {
            _diagnosticBotRemovedCaptureAt = Time.unscaledTime + 1f;
        }
        ForgetManagedBot(preserveMatchIntent);
        _lobbyPlayersPanelRefreshAt = Time.unscaledTime + 0.1f;
        RefreshAllButtons();
    }

    private static void TryRefreshLobbyPlayersPanel()
    {
        if (_lobbyPlayersPanelRefreshAt < 0f || Time.unscaledTime < _lobbyPlayersPanelRefreshAt)
        {
            return;
        }

        _lobbyPlayersPanelRefreshAt = -1f;
        var panel = _gameUiManager?._lobbyPlayersPanel;
        if (panel is null || panel.Pointer == IntPtr.Zero)
        {
            return;
        }

        AccessTools.DeclaredMethod(typeof(UI.LobbyPlayersPanel), "Refresh").Invoke(
            panel,
            Array.Empty<object>());
        if (LoggingEnabled)
        {
            _logger?.LogInfo("Refreshed lobby player cards after authoritative bot removal");
        }
    }

    public static void IncludeManagedBotInPartyCount(ref int teamCount)
    {
        var needsSyntheticCount = !_managedMatchJoinStarted || _managedMatchStartGuardScope;
        if (Enabled
            && needsSyntheticCount
            && _managedPlayerPointer != IntPtr.Zero
            && teamCount < 2)
        {
            teamCount = 2;
        }
    }

    public static bool BeginManagedBotMatchStart(Matchmaker matchmaker, GameModeType requestedMode)
    {
        var gameState = matchmaker._gameState;
        var shouldHandle = Enabled
            && !_managedMatchJoinStarted
            && FindManagedBot() is not null
            && gameState is not null
            && gameState.CurrentScene == SceneType.Lobby
            && gameState.GameStateType == GameStateType.Lobby
            && gameState.PrivateGameCheckbox;
        _managedMatchStartGuardScope = shouldHandle;
        if (shouldHandle)
        {
            _managedMatchMode = requestedMode;
            _managedMatchJoinStarted = true;
            _carryBotIntoMatch = true;
            _managedHunterConfirmationSent = false;
            _managedHunterConfirmationRetryAt = 0f;
            gameState!.GameMode = requestedMode;
        }
        return shouldHandle;
    }

    public static void FinishManagedBotMatchStart(Matchmaker matchmaker, bool shouldHandle)
    {
        _managedMatchStartGuardScope = false;
        if (!shouldHandle || FindManagedBot() is null)
        {
            return;
        }

        if (!matchmaker.ShouldImmediateStartMatch())
        {
            _managedMatchJoinStarted = false;
            _carryBotIntoMatch = false;
            _logger?.LogWarning("Lobby bot match start was not immediate after private-game validation");
            return;
        }

        _logger?.LogInfo(
            "Started bot-only private match through the stock Matchmaker.PrepareMatch path: "
            + $"mode={_managedMatchMode}");
    }

    public static ManagedBotResolverState? ExcludeManagedBotFromHostResolution(
        LobbySessionMatchResolver resolver)
    {
        if (!Enabled || !_managedMatchJoinStarted)
        {
            return null;
        }

        var bot = FindManagedBot();
        if (bot is null)
        {
            return null;
        }

        var registry = resolver._registry;
        var internalId = bot.InternalId;
        var botWasRegistered = internalId >= 0
            && internalId < registry._components.Length
            && registry._components[internalId] is { } registered
            && registered.Pointer == bot.Pointer;
        if (!botWasRegistered)
        {
            return null;
        }

        // The stock resolver selects a network player as the Photon host. A synthetic bot has no
        // Nakama/social host id, so let the stock PrepareMatch flow resolve only real party members.
        registry.ClearPlayer(internalId);
        if (LoggingEnabled)
        {
            _logger?.LogInfo("Temporarily excluded the managed bot from stock host resolution");
        }

        return new ManagedBotResolverState(registry, bot, internalId);
    }

    public static void RestoreManagedBotAfterHostResolution(ManagedBotResolverState? state)
    {
        if (state is null)
        {
            return;
        }

        state.Registry[state.InternalId] = state.Bot;
    }

    public static void EndManagedBotMatchStartScope()
    {
        _managedMatchStartGuardScope = false;
    }

    public static void AllowManagedBotMatchStart(ref bool stateBlocked)
    {
        if (_managedMatchStartGuardScope)
        {
            stateBlocked = false;
        }
    }

    public static bool CanUpdateDangerAudio(NetworkPlayerRegistry? registry)
    {
        if (!_managedMatchJoinStarted)
        {
            return true;
        }

        var internalId = Game.Game.InternalId;
        return registry is not null
            && internalId >= 0
            && internalId < registry._components.Length
            && registry._components[internalId] is not null;
    }

    public static void ObservePlayerInitialized(SpookedNetworkPlayer player)
    {
        if (!Enabled
            || player.IsBot
            || !player.HasInputAuthority)
        {
            return;
        }

        if (_configuration is null || !_configuration.AutoAddBotWhenLobbyReady.Value)
        {
            return;
        }

        try
        {
            _diagnosticLocalPlayerRefId = player.KinguinverseId;
            var sceneSpawner = ResolveAuthoritativeLobbySpawner();
            if (sceneSpawner is null)
            {
                _logger?.LogWarning(
                    "Diagnostic auto-add skipped: lobby state authority was unavailable when the local player initialized");
                return;
            }

            if (_configuration.AutoWalkBeforePortal.Value)
            {
                _diagnosticWalkStartsAt = Time.unscaledTime + 0.75f;
                _diagnosticWalkEndsAt = _diagnosticWalkStartsAt + 2.5f;
                _diagnosticPortalOpenNotBefore = _diagnosticWalkEndsAt + 0.5f;
                _logger?.LogInfo("Diagnostic visual flow scheduled 2.5 seconds of stock local movement");
            }

            TryRunDiagnosticAutoAdd();
        }
        catch (Exception exception)
        {
            LogError("Diagnostic lobby bot auto-add failed", exception);
        }
    }

    public static void ApplyDiagnosticMovement(PlayerInputController inputController)
    {
        if (_configuration is null
            || !_configuration.CaptureFlowSequence.Value
            || !_configuration.AutoWalkBeforePortal.Value
            || !inputController.HasInputAuthority
            || inputController._gameState is null
            || inputController._gameState.CurrentScene != SceneType.Lobby)
        {
            return;
        }

        var now = Time.unscaledTime;
        if (now < _diagnosticWalkStartsAt || now >= _diagnosticWalkEndsAt)
        {
            return;
        }

        inputController._moveDirection = Vector2.up;
        inputController._sprint = false;
    }

    public static void CorrectManagedBotMouseAim(PlayerInputController inputController)
    {
        if (!Enabled
            || !_managedBotControlActive
            || inputController is null
            || inputController.Pointer == IntPtr.Zero
            || !inputController._isAiming
            || !IsManagedBot(inputController._spookedNetworkPlayer)
            || inputController._desiredGamepadAim.sqrMagnitude > 0.0001f)
        {
            return;
        }

        var bot = inputController._spookedNetworkPlayer;
        var offset = inputController._lastMouseRaycastPoint - bot.transform.position;
        var horizontal = new Vector2(offset.x, offset.z);
        if (horizontal.sqrMagnitude > 0.0001f)
        {
            inputController._mouseDirection = horizontal.normalized;
        }
    }

    public static ManagedBotInputScope? BeginManagedBotInputScope(
        PlayerInputController inputController)
    {
        if (!TryResolveManagedBotControl(inputController, out var bot))
        {
            return null;
        }

        var originalPlayer = inputController._spookedNetworkPlayer;
        inputController._spookedNetworkPlayer = bot;
        return new ManagedBotInputScope(originalPlayer);
    }

    public static void EndManagedBotInputScope(
        PlayerInputController inputController,
        ManagedBotInputScope? scope,
        bool clearOriginalInput)
    {
        if (scope is null || scope.Restored)
        {
            return;
        }

        scope.Restored = true;
        try
        {
            if (clearOriginalInput)
            {
                ClearPlayerInput(scope.OriginalPlayer);
            }
        }
        finally
        {
            if (inputController is not null && inputController.Pointer != IntPtr.Zero)
            {
                inputController._spookedNetworkPlayer = scope.OriginalPlayer;
            }
        }
    }

    public static bool TryRouteManagedBotInteraction(
        PlayerInputController inputController,
        Types.InputActionType inputActionType)
    {
        if (!TryResolveManagedBotControl(inputController, out var bot))
        {
            return false;
        }

        try
        {
            if (inputActionType == Types.InputActionType.None)
            {
                return true;
            }

            var interactive = bot.EntityInteractiveComponent;
            if (interactive is null || interactive.Pointer == IntPtr.Zero)
            {
                _logger?.LogWarning("Managed bot interaction input was ignored: its interactive component is unavailable");
                return true;
            }

            interactive.RPC_ClientInput(inputActionType);
        }
        catch (Exception exception)
        {
            LogError("Managed bot interaction input failed", exception);
        }

        return true;
    }

    public static bool TryRouteManagedBotKill(PlayerInputController inputController)
    {
        if (!TryResolveManagedBotControl(inputController, out var bot))
        {
            return false;
        }

        try
        {
            var seekerCage = FindManagedBotSeekerCage(bot);
            if (seekerCage is not null)
            {
                if (seekerCage._lastStepViewActive)
                {
                    seekerCage.OnInput();
                }

                return true;
            }

            var botInputController = bot.GetComponent<PlayerInputController>();
            if (botInputController is null || botInputController.Pointer == IntPtr.Zero)
            {
                _logger?.LogWarning("Managed bot kill input was ignored: its input controller is unavailable");
                return true;
            }

            botInputController.RPC_ClientAttack();
        }
        catch (Exception exception)
        {
            LogError("Managed bot kill input failed", exception);
        }

        return true;
    }

    public static bool TryRouteManagedBotSkill(
        PlayerInputController inputController,
        bool secondSkill)
    {
        if (!TryResolveManagedBotControl(inputController, out var bot))
        {
            return false;
        }

        try
        {
            var skills = bot.EntitySkillsComponent;
            if (skills is null || skills.Pointer == IntPtr.Zero)
            {
                _logger?.LogWarning("Managed bot skill input was ignored: its skills component is unavailable");
                return true;
            }

            if (secondSkill)
            {
                skills.OnSecondSkillStartButton();
            }
            else
            {
                skills.OnFirstSkillStartButton();
            }
        }
        catch (Exception exception)
        {
            LogError("Managed bot skill input failed", exception);
        }

        return true;
    }

    public static bool TryRouteManagedBotEmote(
        PlayerEmoteController emoteController,
        EmoteType emoteType)
    {
        if (!Enabled
            || !_managedBotControlActive
            || emoteController is null
            || emoteController.Pointer == IntPtr.Zero)
        {
            return false;
        }

        var bot = FindManagedBot();
        if (bot is null)
        {
            StopManagedBotControl(null, "managed bot is no longer available");
            return false;
        }

        // The nested call on the bot controller must reach the stock PlayEmote implementation.
        if (emoteController._internalId == bot.InternalId)
        {
            return false;
        }

        try
        {
            var botEmoteController = FindManagedBotEmoteController(bot);
            if (botEmoteController is null)
            {
                _logger?.LogWarning("Managed bot emote input was ignored: its emote controller is unavailable");
                return true;
            }

            botEmoteController.PlayEmote(emoteType);
        }
        catch (Exception exception)
        {
            LogError("Managed bot emote input failed", exception);
        }

        return true;
    }

    public static bool BeginManagedBotSeekerCageLocalScope(SeekerCage seekerCage)
    {
        if (!Enabled
            || !_managedBotControlActive
            || seekerCage is null
            || seekerCage.Pointer == IntPtr.Zero
            || !seekerCage._cageActive)
        {
            return false;
        }

        var bot = FindManagedBot();
        if (bot is null || seekerCage._seekerId != bot.InternalId)
        {
            return false;
        }

        _managedBotSeekerCageLocalInternalId = bot.InternalId;
        _managedBotSeekerCageLocalScopeDepth++;
        return true;
    }

    public static void EndManagedBotSeekerCageLocalScope(bool scopeEntered)
    {
        if (!scopeEntered || _managedBotSeekerCageLocalScopeDepth <= 0)
        {
            return;
        }

        _managedBotSeekerCageLocalScopeDepth--;
        if (_managedBotSeekerCageLocalScopeDepth == 0)
        {
            _managedBotSeekerCageLocalInternalId = 0;
        }
    }

    public static void OverrideManagedBotSeekerCageLocalId(int internalId, ref bool isLocal)
    {
        if (!isLocal
            && _managedBotSeekerCageLocalScopeDepth > 0
            && internalId == _managedBotSeekerCageLocalInternalId)
        {
            isLocal = true;
        }
    }

    private static SeekerCage? FindManagedBotSeekerCage(SpookedNetworkPlayer bot)
    {
        var internalId = bot.InternalId;
        return Resources.FindObjectsOfTypeAll<SeekerCage>()
            .FirstOrDefault(seekerCage => seekerCage is not null
                && seekerCage.Pointer != IntPtr.Zero
                && seekerCage._cageActive
                && seekerCage._seekerId == internalId);
    }

    private static bool TryResolveManagedBotControl(
        PlayerInputController inputController,
        out SpookedNetworkPlayer bot)
    {
        bot = null!;
        if (!Enabled
            || !_managedBotControlActive
            || inputController is null
            || inputController.Pointer == IntPtr.Zero
            || !inputController.HasInputAuthority)
        {
            return false;
        }

        var managedBot = FindManagedBot();
        if (managedBot is null)
        {
            StopManagedBotControl(null, "managed bot is no longer available");
            return false;
        }

        // Once control authority is assigned, the bot's own controller also reports local input
        // authority. Only redirect the original local player's controller; otherwise both objects
        // consume and publish the same frame of input.
        if (IsManagedBot(inputController._spookedNetworkPlayer)
            || inputController.Object?.Pointer == managedBot.Object?.Pointer)
        {
            return false;
        }

        bot = managedBot;
        return true;
    }

    private static void TryToggleManagedBotControl()
    {
        if (!Enabled)
        {
            StopManagedBotControl(null, "mod disabled");
            return;
        }

        var keyboard = Keyboard.current;
        var mainPlusPressed = keyboard is not null
            && keyboard.equalsKey.wasPressedThisFrame
            && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
        if (keyboard is null
            || (!keyboard.numpadPlusKey.wasPressedThisFrame
                && !mainPlusPressed))
        {
            return;
        }

        var bot = FindManagedBot();
        if (bot is null)
        {
            StopManagedBotControl(null, "managed bot is no longer available");
            return;
        }

        if (_managedBotControlActive)
        {
            StopManagedBotControl(bot, "Plus toggled back");
            ClearAllLocalPlayerInputs(bot);
            return;
        }

        if (!TryStartManagedBotControl(bot))
        {
            return;
        }

        ClearAllLocalPlayerInputs(bot);
        _logger?.LogInfo("Input control switched to managed test bot");
    }

    private static bool TryStartManagedBotControl(SpookedNetworkPlayer bot)
    {
        var registry = bot._networkPlayerRegistry;
        var localPlayer = registry?._components?
            .FirstOrDefault(player => player is not null
                && !player.IsBot
                && player.HasInputAuthority
                && player.Object is not null
                && player.Object.IsValid);
        var localAuthority = localPlayer?.Object?.InputAuthority ?? PlayerRef.None;
        if (localPlayer is null || localAuthority.IsNone)
        {
            _logger?.LogWarning("Managed bot control was not enabled: the local Fusion player authority is unavailable");
            return false;
        }

        ReleaseManagedBotControlAuthority();
        try
        {
            foreach (var networkObject in FindManagedBotControlNetworkObjects(bot))
            {
                if (networkObject.InputAuthority.RawEncoded == localAuthority.RawEncoded)
                {
                    continue;
                }

                if (!networkObject.InputAuthority.IsNone || !networkObject.HasStateAuthority)
                {
                    throw new InvalidOperationException(
                        $"Cannot assign local input authority to managed bot object {networkObject.Id}: "
                        + $"current={networkObject.InputAuthority.RawEncoded}, stateAuthority={networkObject.HasStateAuthority}");
                }

                networkObject.AssignInputAuthority(localAuthority);
                ManagedBotControlAuthorityObjects[networkObject.Pointer] = networkObject;
            }

            if (bot.Object is null
                || bot.Object.InputAuthority.RawEncoded != localAuthority.RawEncoded)
            {
                throw new InvalidOperationException("Managed bot player object did not accept local input authority");
            }

            _managedBotControlPlayerRefRaw = localAuthority.RawEncoded;
            _managedBotControlActive = true;
            SetManagedBotCameraTarget(bot);
            return true;
        }
        catch (Exception exception)
        {
            ReleaseManagedBotControlAuthority();
            LogError("Managed bot control authority transfer failed", exception);
            return false;
        }
    }

    private static IEnumerable<NetworkObject> FindManagedBotControlNetworkObjects(
        SpookedNetworkPlayer bot)
    {
        var objects = new Dictionary<IntPtr, NetworkObject>();
        AddNetworkObject(objects, bot.Object);
        AddNetworkObject(objects, bot.EntityInteractiveComponent?.Object);
        AddNetworkObject(objects, bot.EntitySkillsComponent?.Object);
        AddNetworkObject(objects, bot.EntityItemsComponent?.Object);
        AddNetworkObject(objects, bot.EntityNetworkAnimatorComponent?.Object);
        AddNetworkObject(objects, bot.GetComponentInChildren<PlayerInputController>(true)?.Object);
        AddNetworkObject(objects, FindManagedBotEmoteController(bot)?.Object);
        return objects.Values;
    }

    private static void AddNetworkObject(
        Dictionary<IntPtr, NetworkObject> objects,
        NetworkObject? networkObject)
    {
        if (networkObject is not null
            && networkObject.Pointer != IntPtr.Zero
            && networkObject.IsValid)
        {
            objects[networkObject.Pointer] = networkObject;
        }
    }

    private static PlayerEmoteController? FindManagedBotEmoteController(SpookedNetworkPlayer bot)
    {
        var controllers = bot.GetComponentsInChildren<PlayerEmoteController>(true);
        return controllers.FirstOrDefault(controller => controller is not null
                && controller.Pointer != IntPtr.Zero
                && controller._internalId == bot.InternalId)
            ?? controllers.FirstOrDefault(controller => controller is not null
                && controller.Pointer != IntPtr.Zero);
    }

    private static void EnsureManagedBotControlAuthority(SpookedNetworkPlayer bot)
    {
        if (!_managedBotControlActive
            || bot.Object is null
            || !bot.Object.IsValid
            || bot.Object.InputAuthority.IsNone)
        {
            return;
        }

        var localAuthority = bot.Object.InputAuthority;
        foreach (var networkObject in FindManagedBotControlNetworkObjects(bot))
        {
            if (networkObject.InputAuthority.RawEncoded == localAuthority.RawEncoded)
            {
                continue;
            }

            if (!networkObject.InputAuthority.IsNone || !networkObject.HasStateAuthority)
            {
                _logger?.LogWarning(
                    $"Managed bot child object {networkObject.Id} could not receive late input authority: "
                    + $"current={networkObject.InputAuthority.RawEncoded}, stateAuthority={networkObject.HasStateAuthority}");
                continue;
            }

            networkObject.AssignInputAuthority(localAuthority);
            ManagedBotControlAuthorityObjects[networkObject.Pointer] = networkObject;
        }
    }

    private static void ClearAllLocalPlayerInputs(SpookedNetworkPlayer bot)
    {
        ClearPlayerInput(bot);
        var registry = bot._networkPlayerRegistry;
        if (registry is null || registry._components is null)
        {
            return;
        }

        foreach (var player in registry._components)
        {
            if (player is not null && !player.IsBot && player.HasInputAuthority)
            {
                ClearPlayerInput(player);
            }
        }
    }

    private static void StopManagedBotControl(SpookedNetworkPlayer? bot, string reason)
    {
        if (!_managedBotControlActive
            && ManagedBotControlAuthorityObjects.Count == 0
            && _managedBotControlCameraManager is null)
        {
            return;
        }

        ClearPlayerInput(bot ?? FindManagedBot());
        _managedBotControlActive = false;
        ResetManagedBotCameraTarget();
        ReleaseManagedBotControlAuthority();
        _logger?.LogInfo($"Input control returned to local player: {reason}");
    }

    private static void SetManagedBotCameraTarget(SpookedNetworkPlayer bot)
    {
        try
        {
            var cameraManager = bot.UnderlyingPrefabComponent?._cameraManager
                ?? Resources.FindObjectsOfTypeAll<Gameplay.Camera.SceneCameraManager>()
                    .FirstOrDefault(candidate => candidate is not null
                        && candidate.Pointer != IntPtr.Zero)?._cameraManager;
            if (cameraManager is null
                || cameraManager == null
                || cameraManager.Pointer == IntPtr.Zero)
            {
                _logger?.LogWarning("Managed bot camera target was not changed: the stock camera manager is unavailable");
                return;
            }

            cameraManager.SetCameraTarget(bot.transform);
            _managedBotControlCameraManager = cameraManager;
        }
        catch (Exception exception)
        {
            LogError("Managed bot camera target switch failed", exception);
        }
    }

    private static void ResetManagedBotCameraTarget()
    {
        var cameraManager = _managedBotControlCameraManager;
        _managedBotControlCameraManager = null;
        try
        {
            if (cameraManager is not null
                && cameraManager != null
                && cameraManager.Pointer != IntPtr.Zero)
            {
                cameraManager.ResetToPlayer();
            }
        }
        catch (Exception exception)
        {
            LogError("Managed bot camera target reset failed", exception);
        }
    }

    private static bool TryGetManagedBotVisual(
        SpookedNetworkPlayer bot,
        out EntityNetworkAnimatorComponent animator,
        out Transform visual)
    {
        animator = bot.EntityNetworkAnimatorComponent;
        visual = null!;
        if (animator is null || animator == null || animator.Pointer == IntPtr.Zero)
        {
            return false;
        }

        visual = animator.transform;
        return visual is not null && visual != null && visual.Pointer != IntPtr.Zero;
    }

    private static bool ManagedBotVisualMatchesCharacter(
        SpookedNetworkPlayer bot,
        EntityNetworkAnimatorComponent animator)
    {
        if (bot.CharacterType != CharacterType.murderer_ripper)
        {
            return true;
        }

        var objectName = animator.gameObject?.name;
        return objectName is not null
            && objectName.IndexOf("MurdererRipper", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void ScheduleManagedHunterVisualRepair()
    {
        var now = Time.unscaledTime;
        _managedHunterVisualRepairAt = now + 0.25f;
        _managedHunterVisualRepairDeadline = now + 5f;
        _managedPrefabRefreshRequested = true;
        _managedPrefabRefreshRequestedAt = now;
        _managedDirectPrefabSpawnRequested = false;
    }

    private static void TryRepairManagedHunterVisual()
    {
        if (_managedHunterVisualRepairAt < 0f
            || Time.unscaledTime < _managedHunterVisualRepairAt)
        {
            return;
        }

        _managedHunterVisualRepairAt = Time.unscaledTime + 0.25f;
        try
        {
            var bot = FindManagedBot();
            if (bot is null)
            {
                _managedHunterVisualRepairAt = -1f;
                _managedHunterVisualRepairDeadline = -1f;
                return;
            }

            if (bot.CharacterType == CharacterType.murderer_ripper
                && TryGetManagedBotVisual(bot, out var animator, out var visual)
                && ManagedBotVisualMatchesCharacter(bot, animator))
            {
                var botTransform = bot.transform;
                var parent = visual.parent;
                if (parent is null || parent == null || parent.Pointer != botTransform.Pointer)
                {
                    visual.SetParent(botTransform, false);
                }
                visual.localPosition = Vector3.zero;
                visual.localRotation = Quaternion.identity;
                EnsureManagedBotControlAuthority(bot);
                ManagedAnimatorPointers.Add(animator.Pointer);
                CleanupSupersededManagedBotVisuals(bot, animator);
                _managedHunterVisualRepairAt = -1f;
                _managedHunterVisualRepairDeadline = -1f;
                _logger?.LogInfo("Managed bot hunter visual is bound to the controlled player root");
                return;
            }

            if (Time.unscaledTime >= _managedHunterVisualRepairDeadline)
            {
                _managedHunterVisualRepairAt = -1f;
                _managedHunterVisualRepairDeadline = -1f;
                _logger?.LogWarning("Managed bot hunter visual did not become available before the repair deadline");
                return;
            }

            if (bot.CharacterType == CharacterType.murderer_ripper)
            {
                TrySpawnManagedBotCharacterPrefab(bot);
            }
        }
        catch (Exception exception)
        {
            if (Time.unscaledTime >= _managedHunterVisualRepairDeadline)
            {
                _managedHunterVisualRepairAt = -1f;
                _managedHunterVisualRepairDeadline = -1f;
                _logger?.LogWarning(
                    $"Managed bot hunter visual repair stopped after repeated replacement errors: {exception.Message}");
                return;
            }
            if (LoggingEnabled)
            {
                _logger?.LogWarning($"Managed bot hunter visual repair will retry: {exception.Message}");
            }
        }
    }

    private static void ReleaseManagedBotControlAuthority()
    {
        var assignedPlayerRefRaw = _managedBotControlPlayerRefRaw;
        foreach (var networkObject in ManagedBotControlAuthorityObjects.Values)
        {
            try
            {
                if (networkObject is not null
                    && networkObject.Pointer != IntPtr.Zero
                    && networkObject.IsValid
                    && networkObject.HasStateAuthority
                    && (assignedPlayerRefRaw == 0
                        || networkObject.InputAuthority.RawEncoded == assignedPlayerRefRaw))
                {
                    networkObject.RemoveInputAuthority();
                }
            }
            catch (Exception exception)
            {
                LogError("Managed bot input authority cleanup failed", exception);
            }
        }

        ManagedBotControlAuthorityObjects.Clear();
        _managedBotControlPlayerRefRaw = 0;
    }

    private static void ClearPlayerInput(SpookedNetworkPlayer? player)
    {
        if (player is null || player.Pointer == IntPtr.Zero || player._spookedInputs is null)
        {
            return;
        }

        var internalId = player.InternalId;
        if (internalId >= 0)
        {
            player._spookedInputs.ClearPlayerInputs(ref internalId);
        }
    }

    public static void ObserveBotSpawnCompleted(NetworkObject networkObject)
    {
        if (!Enabled || _pendingOperation != PendingOperation.Add)
        {
            return;
        }

        try
        {
            var bot = networkObject.GetComponent<SpookedNetworkPlayer>();
            var sceneSpawner = ResolveAuthoritativeSpawner();
            if (sceneSpawner is null || sceneSpawner.Pointer != _requestedSpawnerPointer)
            {
                return;
            }

            if (bot is null
                || !bot.IsBot
                || bot.KinguinverseId != _requestedPlayerRefId)
            {
                return;
            }

            // This closure runs before Fusion invokes the component's native Spawned lifecycle.
            // Mark it ready now, but let that stock lifecycle perform its one authoritative
            // registry/UI publication. ObservePendingOperation only fills in a missing entry.
            var characterData = bot._CharacterData;
            characterData.HeadType = Kinguinverse.WebServiceProvider.Types_v2.SkinPartType.None;
            characterData.TorsoType = Kinguinverse.WebServiceProvider.Types_v2.SkinPartType.None;
            characterData.ArmsType = Kinguinverse.WebServiceProvider.Types_v2.SkinPartType.None;
            characterData.LegsType = Kinguinverse.WebServiceProvider.Types_v2.SkinPartType.None;
            characterData.BackType = Kinguinverse.WebServiceProvider.Types_v2.SkinPartType.None;
            characterData.WholeType = Kinguinverse.WebServiceProvider.Types_v2.SkinPartType.None;
            bot._CharacterData = characterData;
            bot.IsReady = true;
            bot.SpawnedReady = true;
        }
        catch (Exception exception)
        {
            LogError("Authoritative lobby bot completion observation failed", exception);
        }
    }

    public static void ObserveUnderlyingPrefabSpawned(
        UnderlyingPrefabComponent underlyingPrefab,
        GameObject prefabInstance)
    {
        if (!Enabled || prefabInstance is null || prefabInstance.Pointer == IntPtr.Zero)
        {
            return;
        }

        try
        {
            foreach (var controller in prefabInstance.GetComponentsInChildren<PlayerCharacterPrefabController>(true))
            {
                if (controller is not null && controller.Pointer != IntPtr.Zero)
                {
                    NormalizeCostumeRenderers(controller, controller._currentCharacterData);
                }
            }

            var player = underlyingPrefab._spookedNetworkPlayer;
            if (player is null
                || !player.IsBot
                || (!IsManagedBot(player) && player.KinguinverseId != _requestedPlayerRefId))
            {
                return;
            }

            SynchronizeManagedBotCharacterData(player);
            var refreshedControllers = RefreshCharacterControllers(
                player,
                prefabInstance.GetComponentsInChildren<PlayerCharacterPrefabController>(true));
            foreach (var networkAnimator in prefabInstance.GetComponentsInChildren<EntityNetworkAnimatorComponent>(true))
            {
                if (networkAnimator is not null && networkAnimator.Pointer != IntPtr.Zero)
                {
                    ManagedAnimatorPointers.Add(networkAnimator.Pointer);
                }
            }
            EnsureManagedBotControlAuthority(player);
            if (refreshedControllers == 0)
            {
                _logger?.LogWarning(
                    "Managed bot prefab callback contained no PlayerCharacterPrefabController; scheduling a retry");
                _nextManagedOutfitRefreshAt = Time.unscaledTime + 0.25f;
                _managedOutfitRefreshDeadline = Time.unscaledTime + 10f;
                return;
            }

            _nextManagedOutfitRefreshAt = -1f;
            _managedOutfitRefreshDeadline = -1f;
            var animator = player.EntityNetworkAnimatorComponent;
            if (animator is not null)
            {
                ManagedAnimatorPointers.Add(animator.Pointer);
            }
            if (LoggingEnabled)
            {
                _logger?.LogInfo(
                    $"Refreshed managed bot outfit from prefab callback on {refreshedControllers} character controller(s)");
            }
        }
        catch (Exception exception)
        {
            LogError("Managed bot prefab outfit refresh failed", exception);
        }
    }

    public static bool ShouldRunNetworkAnimator(
        EntityNetworkAnimatorComponent animator,
        bool allowWhileControlled)
    {
        if (!Enabled)
        {
            return true;
        }

        if (_managedCharacterPrefabSpawnScope)
        {
            return false;
        }

        if (ManagedAnimatorPointers.Contains(animator.Pointer))
        {
            return allowWhileControlled && ManagedBotAnimatorIsReady(animator);
        }

        var player = animator._spookedNetworkPlayer;
        if (player is null)
        {
            player = animator.GetComponentInParent<SpookedNetworkPlayer>();
        }

        if (IsManagedBot(player))
        {
            ManagedAnimatorPointers.Add(animator.Pointer);
            return allowWhileControlled && ManagedBotAnimatorIsReady(animator);
        }

        var networkObject = animator.Object;
        if (player is null
            && networkObject is not null
            && networkObject.IsValid
            && networkObject.InputAuthority.IsNone)
        {
            var managedBot = FindManagedBot();
            if (managedBot is not null
                && (animator.transform.position - managedBot.transform.position).sqrMagnitude < 0.25f)
            {
                ManagedAnimatorPointers.Add(animator.Pointer);
                return allowWhileControlled && ManagedBotAnimatorIsReady(animator);
            }
        }

        if (LoggingEnabled
            && _managedPlayerPointer != IntPtr.Zero
            && LoggedAnimatorPointers.Add(animator.Pointer))
        {
            var linkedPlayer = animator._spookedNetworkPlayer;
            _logger?.LogInfo(
                "Animator identity probe: "
                + $"pointer={animator.Pointer}, name={animator.gameObject.name}, "
                + $"linkedPlayer={linkedPlayer?.KinguinverseId.ToString() ?? "null"}, "
                + $"object={networkObject?.Id.ToString() ?? "null"}, "
                + $"inputAuthority={networkObject?.InputAuthority.RawEncoded.ToString() ?? "null"}, "
                + $"managedPlayerRef={_managedPlayerRefId}, managedFusionRef={_managedFusionPlayerRefRaw}, "
                + $"position={animator.transform.position}");
        }

        if (_managedFusionPlayerRefRaw != 0
            && networkObject is not null
            && networkObject.IsValid
            && networkObject.InputAuthority.RawEncoded == _managedFusionPlayerRefRaw)
        {
            ManagedAnimatorPointers.Add(animator.Pointer);
            return allowWhileControlled && ManagedBotAnimatorIsReady(animator);
        }

        if (_managedNetworkObjectPointer != IntPtr.Zero
            && networkObject is not null
            && networkObject.Pointer == _managedNetworkObjectPointer)
        {
            ManagedAnimatorPointers.Add(animator.Pointer);
            return allowWhileControlled && ManagedBotAnimatorIsReady(animator);
        }

        // Never suppress an unassociated animator. During scene transitions the stock local
        // character animator can temporarily have no player backlink; treating every such
        // animator as the dummy leaves the local prefab uninitialized with all authored costume
        // renderers visible. Only pointers positively tied to the managed bot are skipped above.
        return true;
    }

    private static bool ManagedBotAnimatorIsReady(EntityNetworkAnimatorComponent animator)
    {
        return _managedBotControlActive
            && animator._spookedNetworkPlayer is not null
            && animator._spookedInputs is not null
            && animator._animator is not null
            && animator._animations is not null;
    }

    public static bool TryOverrideDiagnosticMap(ref SceneType sceneType)
    {
        if (_configuration is null
            || !_configuration.AutoAddBotWhenLobbyReady.Value
            || !_configuration.AutoStartPrivateMatchWhenBotReady.Value
            || _configuration.AutoStartMap.Value == DiagnosticMap.Random)
        {
            return false;
        }

        sceneType = _managedMatchMode == GameModeType.Berek
            ? SceneType.Map05_TagGame
            : _configuration.AutoStartMap.Value switch
        {
            DiagnosticMap.Map01 => SceneType.Map01,
            DiagnosticMap.Map02 => SceneType.Map02,
            DiagnosticMap.Map03 => SceneType.Map03,
            DiagnosticMap.Map04 => SceneType.Map04,
            DiagnosticMap.MapEast01 => SceneType.Map_East01,
            DiagnosticMap.MapEast02 => SceneType.Map_East02,
            DiagnosticMap.MapSchool01 => SceneType.Map_School01,
            DiagnosticMap.MapSchool02 => SceneType.Map_School02,
            _ => sceneType,
        };
        if (LoggingEnabled)
        {
            _logger?.LogInfo($"Diagnostic auto-start selected fixed map {sceneType}");
        }

        return true;
    }

    private static LobbyTestBotUiState? EnsureButton(PortalPlayView view)
    {
        if (view.Pointer == IntPtr.Zero || view._playButton is null)
        {
            return null;
        }

        if (UiStateByView.TryGetValue(view.Pointer, out var existingState) && existingState.IsAlive)
        {
            return existingState;
        }

        var nativeSection = PortalSettingsLayout.CreateNativeSection(
            view,
            PortalSettingsLayout.DummySectionName,
            "Dummy bot");
        if (nativeSection is null)
        {
            _logger?.LogWarning("Lobby bot controls skipped: native settings row was not found");
            return null;
        }

        var dummySwitch = PortalSettingsLayout.CreateNativeSwitch(
            view,
            nativeSection.Root.transform,
            "LobbyTestBotDummySwitch",
            "OFF",
            "ON",
            fontSize: 10f);
        var roleSwitch = PortalSettingsLayout.CreateNativeSwitch(
            view,
            nativeSection.Root.transform,
            "LobbyTestBotRoleSwitch",
            "PENGUIN",
            "HUNTER",
            fontSize: 10f,
            usePreferredRoleTemplate: true);
        if (dummySwitch is null || roleSwitch is null)
        {
            UnityEngine.Object.Destroy(nativeSection.Root);
            _logger?.LogWarning("Lobby bot controls skipped: a native switch was incomplete");
            return null;
        }

        PortalSettingsLayout.UseNativeSwitchIcons(
            dummySwitch,
            "X_ICON",
            "AddFriendButtonIcon");

        var dummyClickAction = (UnityAction)(() => SetDummyEnabled(FindManagedBot() is null));
        var roleClickAction = (UnityAction)(() => SetBotRole(!BotPrefersHunter));
        dummySwitch.Button.onClick.AddListener(dummyClickAction);
        roleSwitch.Button.onClick.AddListener(roleClickAction);

        var state = new LobbyTestBotUiState(
            view,
            nativeSection.Root,
            nativeSection.Title,
            dummySwitch,
            roleSwitch,
            dummyClickAction,
            roleClickAction);
        UiStateByView[view.Pointer] = state;

        return state;
    }

    private static void LayoutButton(
        PortalPlayView view,
        LobbyTestBotUiState state,
        bool roleVisible)
    {
        state.RoleSwitch.Root.SetActive(roleVisible);
        PortalSettingsLayout.Apply(view);
        PortalSettingsLayout.LayoutNativeSwitch(
            state.DummySwitch,
            0f,
            roleVisible ? ExpandedDummySwitchY : CollapsedDummySwitchY,
            NativeSwitchWidth,
            NativeSwitchHeight);
        PortalSettingsLayout.LayoutNativeSwitch(
            state.RoleSwitch,
            0f,
            RoleSwitchY,
            NativeSwitchWidth,
            NativeSwitchHeight);
        PortalSettingsLayout.SetNativeSwitchIconSize(
            state.DummySwitch,
            left: true,
            size: 24f);
    }

    private static void SetDummyEnabled(bool enabled)
    {
        try
        {
            if (!Enabled || _pendingOperation != PendingOperation.None)
            {
                return;
            }

            var bot = FindManagedBot();
            if (enabled && bot is null)
            {
                TryAddBot();
            }
            else if (!enabled && bot is not null)
            {
                TryRemoveBot(bot);
            }

            RefreshAllButtons();
        }
        catch (Exception exception)
        {
            LogError("Lobby bot toggle failed", exception);
            ClearPendingOperation();
            RefreshAllButtons();
        }
    }

    private static void SetBotRole(bool hunter)
    {
        if (!Enabled || _configuration is null || _pendingOperation != PendingOperation.None)
        {
            return;
        }

        _configuration.RolePreference.Value = hunter
            ? BotRolePreference.HunterPriority
            : BotRolePreference.Penguin;

        var bot = FindManagedBot();
        if (bot is not null)
        {
            bot.CanBeSeeker = BotPrefersHunter;
        }

        if (LoggingEnabled)
        {
            _logger?.LogInfo($"Managed bot role preference changed to {_configuration.RolePreference.Value}");
        }
        RefreshAllButtons();
    }

    public static bool TryPrioritizeManagedBotAsSeeker(
        Gameplay.Match.MatchState.ShouldStartState shouldStartState,
        ref int result)
    {
        if (!Enabled
            || !BotPrefersHunter
            || shouldStartState._gameState.GameMode == GameModeType.Berek)
        {
            return false;
        }

        var bot = FindManagedBot();
        if (bot is null
            || bot.InternalId < 0
            || !bot.CanBeSeeker
            || shouldStartState._networkPlayerRegistry._components.All(
                player => player is null || player.Pointer != bot.Pointer))
        {
            return false;
        }

        result = bot.InternalId;
        _managedHunterConfirmationSent = false;
        _managedHunterConfirmationRetryAt = 0f;
        _logger?.LogInfo($"Prioritized managed test bot as hunter: internalId={result}");
        return true;
    }

    public static void ConfirmManagedBotHunter(
        Gameplay.Match.MatchState.SelectionState selectionState)
    {
        if (!Enabled
            || !BotPrefersHunter
            || _managedHunterConfirmationSent
            || Time.unscaledTime < _managedHunterConfirmationRetryAt
            || selectionState._gameState.GameMode == GameModeType.Berek)
        {
            return;
        }

        var bot = FindManagedBot();
        if (bot is null || selectionState._gameState.ChosenSeekerId != bot.InternalId)
        {
            return;
        }

        try
        {
            // A managed bot has no input authority and cannot operate the hunter-selection UI.
            // Publish the same authoritative confirmation event that SeekerComponent publishes
            // after a real player confirms. The stock state and start controllers still own the
            // transition, character replacement, positioning, and replication.
            GameEventsManager.Publish<ConfirmSeekerCharacterEvent>(
                null,
                new ConfirmSeekerCharacterEvent(bot.InternalId, CharacterType.murderer_ripper));
            _managedHunterConfirmationSent = true;
            ScheduleManagedHunterVisualRepair();
            _logger?.LogInfo(
                $"Confirmed managed test bot hunter through the stock event path: internalId={bot.InternalId}, character={CharacterType.murderer_ripper}");
        }
        catch (Exception exception)
        {
            _managedHunterConfirmationRetryAt = Time.unscaledTime + 0.5f;
            LogError("Managed bot hunter confirmation failed", exception);
        }
    }

    private static bool TryAddBot()
    {
        var sceneSpawner = ResolveAuthoritativeLobbySpawner();
        if (sceneSpawner is null)
        {
            _logger?.LogWarning("Lobby bot was not added: this client is not the active lobby state authority");
            return false;
        }

        return TryAddBot(sceneSpawner);
    }

    private static bool TryAddBot(SceneSpawner sceneSpawner)
    {

        if (FindManagedBot() is not null)
        {
            return false;
        }

        var playerRefId = FindAvailablePlayerRefId(sceneSpawner);
        if (playerRefId == 0)
        {
            _logger?.LogWarning("Lobby bot was not added: the lobby player registry has no free slot");
            return false;
        }

        _pendingOperation = PendingOperation.Add;
        _pendingStartedAt = Time.unscaledTime;
        _requestedPlayerRefId = playerRefId;
        _requestedSpawnerPointer = sceneSpawner.Pointer;

        try
        {
            var spawnEvent = new SpawnActorEvent(
                playerRefId,
                BotNickname,
                CharacterType.victim_penguin);
            var nativeInitializer = new SceneSpawner.__c__DisplayClass25_0
            {
                e = spawnEvent
            };
            var initializeBot = (NetworkRunner.OnBeforeSpawned)(Action<NetworkRunner, NetworkObject>)
                nativeInitializer._OnSpawnActorEvent_b__0;
            var spawnPosition = ResolveBotSpawnPosition(sceneSpawner);
            var spawnedObject = sceneSpawner.Runner.Spawn(
                sceneSpawner._playerPrefab,
                new Il2CppSystem.Nullable<Vector3>(spawnPosition),
                new Il2CppSystem.Nullable<Quaternion>(Quaternion.identity),
                new Il2CppSystem.Nullable<PlayerRef>(PlayerRef.None),
                initializeBot,
                default);
            if (spawnedObject is null || !spawnedObject.IsValid)
            {
                throw new InvalidOperationException("Fusion returned no valid NetworkObject for the lobby bot");
            }

            if (LoggingEnabled)
            {
                _logger?.LogInfo(
                    $"Spawned test bot through Fusion for playerRef={playerRefId}, networkObject={spawnedObject.Id}, "
                    + $"scene={sceneSpawner._gameState.CurrentScene}, position={spawnPosition}");
            }
        }
        catch
        {
            ClearPendingOperation();
            throw;
        }

        return true;
    }

    private static void TryRemoveBot(SpookedNetworkPlayer bot)
    {
        var sceneSpawner = ResolveAuthoritativeLobbySpawner();
        if (sceneSpawner is null
            || sceneSpawner.Pointer != _managedSpawnerPointer
            || bot.Object is null
            || !bot.Object.IsValid)
        {
            _logger?.LogWarning("Lobby bot was not removed: its authoritative network object is unavailable");
            return;
        }

        _pendingOperation = PendingOperation.Remove;
        _pendingStartedAt = Time.unscaledTime;
        _requestedPlayerRefId = bot.KinguinverseId;
        var networkObject = bot.Object;
        var networkObjectId = networkObject.Id;
        var botInternalId = bot.InternalId;
        var botPlayerRefId = bot.KinguinverseId;
        var botAnimator = FindNetworkAnimator(bot);
        if (botAnimator is not null)
        {
            ManagedAnimatorPointers.Add(botAnimator.Pointer);
        }

        // Let the stock SpookedNetworkPlayer.Despawned lifecycle publish its removal while the
        // registries still contain the bot. Clearing them first leaves a stale lobby UI card.
        sceneSpawner.Runner.Despawn(networkObject);

        // Current builds remove both entries themselves. Keep this id-only cleanup as a safe
        // fallback, without touching the component that Despawn may already have invalidated.
        if (botInternalId >= 0
            && botInternalId < sceneSpawner._networkPlayerRegistry._components.Length
            && sceneSpawner._networkPlayerRegistry._components[botInternalId] is not null)
        {
            sceneSpawner._networkPlayerRegistry.ClearPlayer(botInternalId);
        }
        if (sceneSpawner._players.Exists(botPlayerRefId, out _))
        {
            sceneSpawner._players.Remove(botPlayerRefId);
        }

        if (LoggingEnabled)
        {
            _logger?.LogInfo(
                $"Requested native bot despawn: playerRef={botPlayerRefId}, networkObject={networkObjectId}");
        }
    }

    private static void ObservePendingOperation()
    {
        if (_pendingOperation == PendingOperation.None)
        {
            return;
        }

        if (_pendingOperation == PendingOperation.Add)
        {
            var requestedBot = FindRequestedBot();
            if (requestedBot is not null)
            {
                NormalizeBotRegistration(requestedBot);
                TrackManagedBot(requestedBot);
                LogAuthoritativeSpawn(requestedBot);
                ClearPendingOperation();
                RefreshAllButtons();
                return;
            }
        }
        else if (FindManagedBot() is null)
        {
            if (LoggingEnabled)
            {
                _logger?.LogInfo($"Authoritative bot despawn completed: playerRef={_requestedPlayerRefId}");
            }

            ForgetManagedBot(false);
            ClearPendingOperation();
            RefreshAllButtons();
            return;
        }

        if (Time.unscaledTime - _pendingStartedAt <= PendingTimeout)
        {
            return;
        }

        _logger?.LogError(
            $"Lobby bot {_pendingOperation.ToString().ToLowerInvariant()} timed out for playerRef={_requestedPlayerRefId}");
        ClearPendingOperation();
        RefreshAllButtons();
    }

    private static void TryRunDiagnosticAutoAdd()
    {
        if (_configuration is null || !_configuration.AutoAddBotWhenLobbyReady.Value)
        {
            return;
        }

        var sceneSpawner = ResolveAuthoritativeLobbySpawner();
        if (sceneSpawner is null
            || sceneSpawner._networkPlayerRegistry.Count == 0
            || _diagnosticPublishedLobbySpawnerPointer == sceneSpawner.Pointer
            || _pendingOperation != PendingOperation.None)
        {
            return;
        }

        if (FindManagedBot() is not null)
        {
            _diagnosticPublishedLobbySpawnerPointer = sceneSpawner.Pointer;
            return;
        }

        if (sceneSpawner._gameState.GameMode != _managedMatchMode)
        {
            sceneSpawner._gameState.GameMode = _managedMatchMode;
        }

        _logger?.LogInfo(
            "Diagnostic lobby readiness reached: "
            + $"localPlayerRef={_diagnosticLocalPlayerRefId}, "
            + $"networkPlayerCount={sceneSpawner._networkPlayerRegistry.Count}");
        if (TryAddBot())
        {
            _diagnosticPublishedLobbySpawnerPointer = sceneSpawner.Pointer;
        }
    }

    private static void TryRunDiagnosticAutoOpen(PortalPlayView view)
    {
        if (_configuration is null || !_configuration.AutoOpenPortalWhenLobbyReady.Value)
        {
            return;
        }

        var sceneSpawner = ResolveAuthoritativeLobbySpawner();
        if (sceneSpawner is null
            || sceneSpawner._networkPlayerRegistry.Count == 0
            || (_configuration.AutoAddBotWhenLobbyReady.Value && FindManagedBot() is null)
            || Time.unscaledTime < _diagnosticPortalOpenNotBefore
            || _diagnosticOpenedPortalSpawnerPointer == sceneSpawner.Pointer)
        {
            return;
        }

        _diagnosticOpenedPortalSpawnerPointer = sceneSpawner.Pointer;
        var localPlayer = Resources.FindObjectsOfTypeAll<SpookedNetworkPlayer>()
            .FirstOrDefault(player => player is not null
                && player.Pointer != IntPtr.Zero
                && player.HasInputAuthority
                && !player.IsBot);
        var interactive = localPlayer?.GetComponent<EntityInteractiveComponent>();
        var portal = Resources.FindObjectsOfTypeAll<PortalPlay>()
            .FirstOrDefault(candidate => candidate is not null
                && candidate.Pointer != IntPtr.Zero
                && candidate.gameObject.activeInHierarchy);
        if (interactive is not null && portal is not null)
        {
            var localPosition = localPlayer!.transform.position;
            var portalPosition = portal.Position;
            _logger?.LogInfo(
                "Diagnostic UI automation invoking the stock PortalPlay interaction: "
                + $"distance={Vector3.Distance(localPosition, portalPosition):F2}");
            interactive.Interact(portal, InteractionType.StartInteraction, 0, false);
        }
        else
        {
            _logger?.LogWarning("PortalPlay interaction was unavailable; falling back to the stock UI callback");
            _gameUiManager!.OpenPortalPlayView();
        }
        _diagnosticPortalOpenedAt = Time.unscaledTime;
        _diagnosticPortalCaptured = false;
        _diagnosticPortalCaptureAt = Time.unscaledTime + 2f;
    }

    private static void TryCaptureDiagnosticPortal()
    {
        if (_configuration is null
            || !_configuration.CapturePortalScreenshot.Value
            || _diagnosticPortalCaptured
            || _diagnosticPortalCaptureAt <= 0f
            || Time.unscaledTime < _diagnosticPortalCaptureAt)
        {
            return;
        }

        if (_diagnosticHoveredButton is null)
        {
            var mapButton = Resources.FindObjectsOfTypeAll<SpookedOutlineButton>()
                .FirstOrDefault(button => button is not null
                    && button.Pointer != IntPtr.Zero
                    && button.gameObject.activeInHierarchy
                    && button.gameObject.name.StartsWith("CodexPortalMap_", StringComparison.Ordinal));
            if (mapButton is not null)
            {
                mapButton.OnPointerEnter(null!);
                _diagnosticHoveredButton = mapButton;
                _diagnosticHoverReleaseAt = Time.unscaledTime + 1f;
                _diagnosticPortalCaptureAt = Time.unscaledTime + 0.2f;
                _logger?.LogInfo("Diagnostic portal capture is hovering the first visible map button");
                return;
            }
        }

        var captureDirectory = Path.Combine(Paths.BepInExRootPath, "ui-captures");
        Directory.CreateDirectory(captureDirectory);
        var capturePath = Path.Combine(captureDirectory, "portal-layout.png");
        ScreenCapture.CaptureScreenshot(capturePath);
        _diagnosticPortalCaptured = true;
        _diagnosticUiActionNotBefore = Time.unscaledTime + 2f;
        _logger?.LogInfo($"Captured diagnostic portal framebuffer: {capturePath}");

        var portalView = _gameUiManager?._portalPlayView;
        if (portalView is not null)
        {
            if (portalView._playButton is SpookedOutlineButton playButton && playButton._isHiglighted)
            {
                _logger?.LogError("Diagnostic map hover incorrectly highlighted the stock PLAY button");
            }
            else
            {
                _logger?.LogInfo("Diagnostic map hover activated without highlighting PLAY");
            }
        }
    }

    private static void TryReleaseDiagnosticPortalHover()
    {
        if (_diagnosticHoveredButton is null || Time.unscaledTime < _diagnosticHoverReleaseAt)
        {
            return;
        }

        if (_diagnosticHoveredButton.Pointer != IntPtr.Zero)
        {
            _diagnosticHoveredButton.OnPointerExit(null!);
        }
        _diagnosticHoveredButton = null;
        _diagnosticHoverReleaseAt = 0f;
    }

    private static void TryCaptureDiagnosticFlow()
    {
        if (_configuration is null || !_configuration.CaptureFlowSequence.Value)
        {
            return;
        }

        var now = Time.realtimeSinceStartup;
        if (_diagnosticFlowCaptureDirectory is null)
        {
            var captureRoot = Path.Combine(Paths.BepInExRootPath, "ui-captures");
            _diagnosticFlowCaptureDirectory = Path.Combine(
                captureRoot,
                $"flow-{DateTime.Now:yyyyMMdd-HHmmss}");
            Directory.CreateDirectory(_diagnosticFlowCaptureDirectory);
            _diagnosticFlowCaptureStartedAt = now;
            _diagnosticFlowCaptureNextAt = now;
            _diagnosticFlowCaptureIndex = 0;
            _logger?.LogInfo(
                $"Capturing diagnostic flow every 0.5 seconds: {_diagnosticFlowCaptureDirectory}");
        }

        if (now < _diagnosticFlowCaptureNextAt)
        {
            return;
        }

        var elapsedMilliseconds = Mathf.Max(
            0,
            Mathf.RoundToInt((now - _diagnosticFlowCaptureStartedAt) * 1000f));
        var capturePath = Path.Combine(
            _diagnosticFlowCaptureDirectory,
            $"{_diagnosticFlowCaptureIndex:D5}-{elapsedMilliseconds:D7}ms.png");
        ScreenCapture.CaptureScreenshot(capturePath);
        _diagnosticFlowCaptureIndex++;
        _diagnosticFlowCaptureNextAt = now + 0.5f;
    }

    private static void TryCaptureDiagnosticMatch()
    {
        if (_configuration is null
            || !_configuration.CapturePortalScreenshot.Value
            || _diagnosticMatchCaptured
            || _diagnosticMatchCaptureAt <= 0f
            || Time.unscaledTime < _diagnosticMatchCaptureAt)
        {
            return;
        }

        var captureDirectory = Path.Combine(Paths.BepInExRootPath, "ui-captures");
        Directory.CreateDirectory(captureDirectory);
        var capturePath = Path.Combine(captureDirectory, "match-bot-outfit.png");
        ScreenCapture.CaptureScreenshot(capturePath);
        _diagnosticMatchCaptured = true;
        _logger?.LogInfo($"Captured diagnostic match framebuffer: {capturePath}");
    }

    private static void TryCaptureDiagnosticBotIdentity()
    {
        if (_configuration?.CaptureFlowSequence.Value != true)
        {
            return;
        }

        var now = Time.unscaledTime;
        string? fileName = null;
        if (_diagnosticBotPresentCaptureAt > 0f && now >= _diagnosticBotPresentCaptureAt)
        {
            fileName = "bot-present.png";
            _diagnosticBotPresentCaptureAt = -1f;
        }
        else if (_diagnosticBotRemovedCaptureAt > 0f && now >= _diagnosticBotRemovedCaptureAt)
        {
            fileName = "bot-removed.png";
            _diagnosticBotRemovedCaptureAt = -1f;
        }

        if (fileName is null)
        {
            return;
        }

        var captureDirectory = Path.Combine(Paths.BepInExRootPath, "ui-captures");
        Directory.CreateDirectory(captureDirectory);
        var capturePath = Path.Combine(captureDirectory, fileName);
        ScreenCapture.CaptureScreenshot(capturePath);
        _diagnosticUiActionNotBefore = Time.unscaledTime + 2f;
        _logger?.LogInfo($"Captured diagnostic bot identity framebuffer: {capturePath}");
    }

    private static void MaintainMatchBot()
    {
        if (!Enabled || !_carryBotIntoMatch || _pendingOperation != PendingOperation.None)
        {
            return;
        }

        var sceneSpawner = ResolveAuthoritativeGameSpawner();
        if (sceneSpawner is null || sceneSpawner._networkPlayerRegistry.Count == 0)
        {
            return;
        }

        if (FindManagedBot() is not null)
        {
            _carryBotIntoMatch = false;
            _managedMatchJoinStarted = false;
            return;
        }

        _logger?.LogInfo(
            $"Respawning managed test bot in match session: scene={sceneSpawner._gameState.CurrentScene}, mode={sceneSpawner._gameState.GameMode}");
        TryAddBot(sceneSpawner);
    }

    private static void LogAuthoritativeSpawn(SpookedNetworkPlayer bot)
    {
        var sceneSpawner = ResolveAuthoritativeSpawner();
        var registeredInternalId = -1;
        var playersRegistryContainsBot = sceneSpawner is not null
            && sceneSpawner._players.Exists(bot.KinguinverseId, out registeredInternalId);
        var registryCount = sceneSpawner?._networkPlayerRegistry.Count ?? 0;
        var networkObject = bot.Object;

        _logger?.LogInfo(
            "Authoritative test bot spawned: "
            + $"playerRef={bot.KinguinverseId}, internalId={bot.InternalId}, "
            + $"registeredInternalId={(playersRegistryContainsBot ? registeredInternalId : -1)}, "
            + $"networkObject={networkObject.Id}, valid={networkObject.IsValid}, "
            + $"inSimulation={networkObject.IsInSimulation}, stateAuthority={networkObject.HasStateAuthority}, "
            + $"networkPlayerCount={registryCount}, scene={sceneSpawner?._gameState.CurrentScene}, "
            + $"outfit={bot.CharacterData.HeadType}/{bot.CharacterData.TorsoType}/{bot.CharacterData.ArmsType}/"
            + $"{bot.CharacterData.LegsType}/{bot.CharacterData.BackType}/{bot.CharacterData.WholeType}");
        if (LoggingEnabled)
        {
            var dataRegistry = bot._spookedNetworkPlayerDataRegistry;
            var hasPlayerData = dataRegistry is not null
                && dataRegistry._dict is not null
                && dataRegistry._dict.ContainsKey(bot.KinguinverseId);
            _logger?.LogInfo(
                "Managed bot component readiness: "
                + $"playerData={hasPlayerData}, underlyingPrefab={bot.UnderlyingPrefabComponent is not null}, "
                + $"victim={bot.VictimComponent is not null}, seeker={bot.SeekerComponent is not null}, "
                + $"transform={bot.EntityTransformComponent is not null}, animator={bot.EntityNetworkAnimatorComponent is not null}, "
                + $"unityAnimator={bot.EntityNetworkAnimatorComponent?._animator is not null}, "
                + $"animationTable={bot.EntityNetworkAnimatorComponent?._animations is not null}, "
                + $"locomotion={bot.EntityLocomotionComponent is not null}, canvas={bot.EntityCanvasComponent is not null}");
        }
    }

    private static void NormalizeBotRegistration(SpookedNetworkPlayer bot)
    {
        var sceneSpawner = ResolveAuthoritativeSpawner();
        if (sceneSpawner is null || sceneSpawner.Pointer != _requestedSpawnerPointer)
        {
            return;
        }

        var registry = sceneSpawner._networkPlayerRegistry;
        var nativeInternalId = -1;
        for (var internalId = 0; internalId < registry._components.Length; internalId++)
        {
            var player = registry._components[internalId];
            if (player is not null && player.Pointer == bot.Pointer)
            {
                nativeInternalId = internalId;
                break;
            }
        }

        if (!sceneSpawner._players.Exists(bot.KinguinverseId, out var registeredInternalId))
        {
            if (nativeInternalId >= 0 && nativeInternalId < sceneSpawner._players._registry.Length)
            {
                sceneSpawner._players._registry[nativeInternalId] = bot.KinguinverseId;
                registeredInternalId = nativeInternalId;
            }
            else
            {
                registeredInternalId = sceneSpawner._players.Add(bot.KinguinverseId);
            }
        }
        else if (nativeInternalId >= 0 && registeredInternalId != nativeInternalId)
        {
            sceneSpawner._players._registry[registeredInternalId] = 0;
            sceneSpawner._players._registry[nativeInternalId] = bot.KinguinverseId;
            registeredInternalId = nativeInternalId;
        }

        bot.InternalId = registeredInternalId;
        bot.CanBeSeeker = BotPrefersHunter;
        if (bot.EntityNetworkAnimatorComponent is null)
        {
            var animator = FindNetworkAnimator(bot);
            if (animator is not null)
            {
                bot.EntityNetworkAnimatorComponent = animator;
                animator._spookedNetworkPlayer = bot;
            }
        }
        bot.IsReady = true;
        bot.SpawnedReady = true;
        var alreadyRegistered = registeredInternalId >= 0
            && registeredInternalId < registry._components.Length
            && registry._components[registeredInternalId] is { } registeredBot
            && registeredBot.Pointer == bot.Pointer;
        if (!alreadyRegistered)
        {
            registry[registeredInternalId] = bot;
        }
    }

    private static int FindAvailablePlayerRefId(SceneSpawner sceneSpawner)
    {
        var registry = sceneSpawner._players._registry;
        for (var playerRefId = 1; playerRefId < registry.Length; playerRefId++)
        {
            if (!sceneSpawner._players.Exists(playerRefId, out _))
            {
                return playerRefId;
            }
        }

        return 0;
    }

    private static Vector3 ResolveBotSpawnPosition(SceneSpawner sceneSpawner)
    {
        var spawnPosition = sceneSpawner.GetSpawnPosition();
        if (sceneSpawner._gameState.CurrentScene != SceneType.Lobby)
        {
            return spawnPosition;
        }

        foreach (var player in sceneSpawner._networkPlayerRegistry._components)
        {
            if (player is null
                || player.IsBot
                || !player.HasInputAuthority
                || player.Object is null
                || !player.Object.IsValid
                || !player.Object.IsInSimulation)
            {
                continue;
            }

            // The stock lobby spawn point is reused for every synthetic peer. Keep the bot
            // visibly separate so its model, label, and removal can be verified independently.
            return player.transform.position + Vector3.right * 1.75f;
        }

        return spawnPosition + Vector3.right * 1.75f;
    }

    private static SceneSpawner? ResolveAuthoritativeLobbySpawner()
    {
        var sceneSpawner = ResolveAuthoritativeSpawner();
        if (sceneSpawner is null
            || sceneSpawner._gameState is null
            || sceneSpawner._gameState.CurrentScene != SceneType.Lobby
            || sceneSpawner._gameState.GameStateType != GameStateType.Lobby)
        {
            return null;
        }

        return sceneSpawner;
    }

    private static SceneSpawner? ResolveAuthoritativeGameSpawner()
    {
        var sceneSpawner = ResolveAuthoritativeSpawner();
        if (sceneSpawner is null
            || sceneSpawner._gameState is null
            || sceneSpawner._gameState.CurrentScene == SceneType.Lobby
            || sceneSpawner._gameState.GameStateType != GameStateType.Game)
        {
            return null;
        }

        return sceneSpawner;
    }

    private static SceneSpawner? ResolveAuthoritativeSpawner()
    {
        var sceneSpawner = _sceneSpawner;
        try
        {
            if (sceneSpawner is null
                || sceneSpawner.Pointer == IntPtr.Zero
                || sceneSpawner.gameObject is null)
            {
                if (Time.unscaledTime < _nextSceneSpawnerRecoveryAt)
                {
                    return null;
                }

                // SceneSpawner.Spawn normally populates the cache. The global lookup is only a
                // recovery path for a plugin loaded after that lifecycle event. Back it off so a
                // scene transition cannot repeat Unity's sorted all-scene search every frame.
                _nextSceneSpawnerRecoveryAt = Time.unscaledTime + 0.5f;
                sceneSpawner = UnityEngine.Object.FindObjectOfType<SceneSpawner>();
                _sceneSpawner = sceneSpawner;
            }
        }
        catch
        {
            _sceneSpawner = null;
            _nextSceneSpawnerRecoveryAt = Time.unscaledTime + 0.5f;
            return null;
        }
        if (sceneSpawner is null
            || sceneSpawner._gameState is null
            || sceneSpawner.Object is null
            || !sceneSpawner.Object.IsValid
            || !sceneSpawner.HasStateAuthority
            || sceneSpawner.Runner is null
            || !sceneSpawner.Runner.IsRunning)
        {
            if (sceneSpawner is not null && sceneSpawner.Pointer != IntPtr.Zero)
            {
                _sceneSpawner = null;
                _nextSceneSpawnerRecoveryAt = Time.unscaledTime + 0.5f;
            }
            return null;
        }

        return sceneSpawner;
    }

    private static SpookedNetworkPlayer? FindManagedBot()
    {
        if (_managedPlayerPointer == IntPtr.Zero || _managedPlayerRefId == 0)
        {
            return null;
        }

        var sceneSpawner = ResolveAuthoritativeSpawner();
        if (sceneSpawner is null || sceneSpawner.Pointer != _managedSpawnerPointer)
        {
            return null;
        }

        foreach (var player in sceneSpawner._networkPlayerRegistry._components)
        {
            if (IsManagedBot(player)
                && player.Object is not null
                && player.Object.IsValid
                && player.Object.IsInSimulation)
            {
                return player;
            }
        }

        return null;
    }

    private static SpookedNetworkPlayer? FindRequestedBot()
    {
        var sceneSpawner = ResolveAuthoritativeSpawner();
        if (sceneSpawner is null || sceneSpawner.Pointer != _requestedSpawnerPointer)
        {
            return null;
        }

        foreach (var player in sceneSpawner._networkPlayerRegistry._components)
        {
            if (player is not null
                && player.IsBot
                && player.KinguinverseId == _requestedPlayerRefId
                && player.Object is not null
                && player.Object.IsValid
                && player.Object.IsInSimulation)
            {
                return player;
            }
        }

        return null;
    }

    private static bool IsManagedBot(SpookedNetworkPlayer? player)
    {
        return player is not null
            && player.IsBot
            && player.Pointer == _managedPlayerPointer
            && player.KinguinverseId == _managedPlayerRefId;
    }

    private static void TrackManagedBot(SpookedNetworkPlayer player)
    {
        _managedSpawnerPointer = _requestedSpawnerPointer;
        _managedPlayerPointer = player.Pointer;
        _managedNetworkObjectPointer = player.Object?.Pointer ?? IntPtr.Zero;
        _managedPlayerRefId = player.KinguinverseId;
        _managedFusionPlayerRefRaw = player.PlayerRef.RawEncoded;
        _managedPrefabRefreshRequested = false;
        _managedPrefabRefreshRequestedAt = -1f;
        _managedDirectPrefabSpawnRequested = false;
        _managedHunterVisualRepairAt = -1f;
        _managedHunterVisualRepairDeadline = -1f;
        _managedHunterConfirmationSent = false;
        _managedHunterConfirmationRetryAt = 0f;
        if (!RefreshManagedBotOutfit(player))
        {
            _nextManagedOutfitRefreshAt = Time.unscaledTime + 0.25f;
            _managedOutfitRefreshDeadline = Time.unscaledTime + 10f;
        }
        var networkAnimator = FindNetworkAnimator(player);
        if (networkAnimator is not null)
        {
            ManagedAnimatorPointers.Add(networkAnimator.Pointer);
        }
        _managedMatchJoinStarted = false;
        var sceneSpawner = ResolveAuthoritativeSpawner();
        if (sceneSpawner is not null && sceneSpawner._gameState.GameStateType == GameStateType.Game)
        {
            _carryBotIntoMatch = false;
            _diagnosticMatchCaptured = false;
            _diagnosticMatchCaptureAt = Time.unscaledTime + 3f;
            _logger?.LogInfo(
                $"Managed test bot registered in match: scene={sceneSpawner._gameState.CurrentScene}, mode={sceneSpawner._gameState.GameMode}");
        }
        else if (sceneSpawner is not null)
        {
            if (_configuration?.CaptureFlowSequence.Value == true)
            {
                _diagnosticBotPresentCaptureAt = Time.unscaledTime + 3f;
            }
            _diagnosticAutoStartSpawnerPointer = sceneSpawner.Pointer;
            _diagnosticBotReadyAt = Time.unscaledTime;
            _diagnosticModeRequested = false;
            _diagnosticModeSelectionFailureLogged = false;
            _diagnosticPrivateToggleRequested = false;
            _diagnosticPlayRequested = false;
            _diagnosticRemoveRequested = false;
        }
    }

    private static bool RefreshManagedBotOutfit(SpookedNetworkPlayer player)
    {
        SynchronizeManagedBotCharacterData(player);
        var refreshedControllerPointers = new HashSet<IntPtr>();
        var refreshedControllers = RefreshCharacterControllers(
            player,
            player.GetComponentsInChildren<PlayerCharacterPrefabController>(true),
            refreshedControllerPointers);

        var networkAnimator = FindNetworkAnimator(player);
        if (networkAnimator is not null)
        {
            ManagedAnimatorPointers.Add(networkAnimator.Pointer);
            refreshedControllers += RefreshCharacterControllers(
                player,
                networkAnimator.GetComponentsInChildren<PlayerCharacterPrefabController>(true),
                refreshedControllerPointers);
            refreshedControllers += RefreshCharacterControllers(
                player,
                networkAnimator.GetComponentsInParent<PlayerCharacterPrefabController>(true),
                refreshedControllerPointers);
        }

        if (LoggingEnabled && refreshedControllers > 0)
        {
            _logger?.LogInfo($"Refreshed managed bot outfit on {refreshedControllers} character controller(s)");
        }

        return refreshedControllers > 0;
    }

    private static void SynchronizeManagedBotCharacterData(SpookedNetworkPlayer player)
    {
        var characterDataRegistry = player._spookedPlayerCharacterData;
        if (characterDataRegistry is not null
            && player.InternalId >= 0
            && player.InternalId < characterDataRegistry._characterDatas.Length)
        {
            characterDataRegistry[player.InternalId] = player.CharacterData;
        }
    }

    private static int RefreshCharacterControllers(
        SpookedNetworkPlayer player,
        Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<PlayerCharacterPrefabController> controllers,
        HashSet<IntPtr>? refreshedControllerPointers = null)
    {
        var refreshedControllers = 0;
        foreach (var controller in controllers)
        {
            if (controller is null
                || controller.Pointer == IntPtr.Zero
                || (refreshedControllerPointers is not null
                    && !refreshedControllerPointers.Add(controller.Pointer)))
            {
                continue;
            }

            RefreshCharacterController(player, controller);
            refreshedControllers++;
        }
        return refreshedControllers;
    }

    private static void RefreshCharacterController(
        SpookedNetworkPlayer player,
        PlayerCharacterPrefabController controller)
    {
        // Prefabs ship with authored costume renderers. When the bot has no equipped pieces,
        // Refresh alone has no previous "current" piece to turn off and those authored renderers
        // can remain visible. Clear the visual state first, then apply the authoritative data.
        controller.TurnOff();
        controller.SetPlayer(player.InternalId);
        controller.RefreshCharacter(player.InternalId, false, false);
        NormalizeCostumeRenderers(controller, player.CharacterData);
    }

    public static void NormalizeCurrentCostumeRenderers(PlayerCharacterPrefabController controller)
    {
        if (Enabled && controller is not null && controller.Pointer != IntPtr.Zero)
        {
            NormalizeCostumeRenderers(controller, controller._currentCharacterData);
        }
    }

    private static void NormalizeCostumeRenderers(
        PlayerCharacterPrefabController controller,
        Types.Structs.CharacterData characterData)
    {
        foreach (var part in controller._headSettings)
        {
            SetCostumeRenderer(
                part?.CustomizablePartSettings,
                characterData.HeadType != Kinguinverse.WebServiceProvider.Types_v2.SkinPartType.None
                    && part?.HeadPartType == characterData.HeadType);
        }
        foreach (var part in controller._torsoSettings)
        {
            SetCostumeRenderer(
                part?.CustomizablePartSettings,
                characterData.TorsoType != Kinguinverse.WebServiceProvider.Types_v2.SkinPartType.None
                    && part?.TorsoPartType == characterData.TorsoType);
        }
        foreach (var part in controller._armsSettings)
        {
            SetCostumeRenderer(
                part?.CustomizablePartSettings,
                characterData.ArmsType != Kinguinverse.WebServiceProvider.Types_v2.SkinPartType.None
                    && part?.ArmsPartType == characterData.ArmsType);
        }
        foreach (var part in controller._legsSettings)
        {
            SetCostumeRenderer(
                part?.CustomizablePartSettings,
                characterData.LegsType != Kinguinverse.WebServiceProvider.Types_v2.SkinPartType.None
                    && part?.LegsPartType == characterData.LegsType);
        }
        foreach (var part in controller._backSettings)
        {
            SetCostumeRenderer(
                part?.CustomizablePartSettings,
                characterData.BackType != Kinguinverse.WebServiceProvider.Types_v2.SkinPartType.None
                    && part?.BackPartType == characterData.BackType);
        }
    }

    private static void SetCostumeRenderer(CustomizablePartSettings? settings, bool active)
    {
        var renderer = settings?.SkinnedMeshRenderer;
        if (renderer is not null && renderer.Pointer != IntPtr.Zero)
        {
            renderer.gameObject.SetActive(active);
        }
    }

    private static void TryRefreshPendingManagedBotOutfit()
    {
        if (_nextManagedOutfitRefreshAt < 0f || Time.unscaledTime < _nextManagedOutfitRefreshAt)
        {
            return;
        }

        var bot = FindManagedBot();
        if (bot is not null && TryScheduleManagedBotPrefabRepair(bot))
        {
            _nextManagedOutfitRefreshAt = Time.unscaledTime + 0.25f;
            return;
        }

        if (bot is not null && TrySpawnManagedBotCharacterPrefab(bot))
        {
            _nextManagedOutfitRefreshAt = Time.unscaledTime + 0.25f;
            return;
        }

        if (bot is not null && RefreshManagedBotOutfit(bot))
        {
            _nextManagedOutfitRefreshAt = -1f;
            _managedOutfitRefreshDeadline = -1f;
            return;
        }

        if (Time.unscaledTime >= _managedOutfitRefreshDeadline)
        {
            _nextManagedOutfitRefreshAt = -1f;
            _managedOutfitRefreshDeadline = -1f;
            _logger?.LogWarning("Managed bot outfit controller did not appear before the refresh deadline");
            return;
        }

        _nextManagedOutfitRefreshAt = Time.unscaledTime + 0.25f;
    }

    private static bool TryScheduleManagedBotPrefabRepair(SpookedNetworkPlayer bot)
    {
        if (_managedPrefabRefreshRequested)
        {
            return false;
        }

        var sceneSpawner = ResolveAuthoritativeSpawner();
        var underlyingPrefab = bot.UnderlyingPrefabComponent;
        if (sceneSpawner is null
            || sceneSpawner._gameState.GameStateType != GameStateType.Game
            || underlyingPrefab is null
            || !underlyingPrefab._isInitialized)
        {
            return false;
        }

        _managedPrefabRefreshRequested = true;
        _managedPrefabRefreshRequestedAt = Time.unscaledTime;
        if (LoggingEnabled)
        {
            _logger?.LogInfo("Scheduled managed bot character prefab orphan cleanup and repair");
        }
        return true;
    }

    private static bool TrySpawnManagedBotCharacterPrefab(SpookedNetworkPlayer bot)
    {
        var hasMatchingVisual = TryGetManagedBotVisual(bot, out var animator, out _)
            && ManagedBotVisualMatchesCharacter(bot, animator);
        if (_managedDirectPrefabSpawnRequested
            || !_managedPrefabRefreshRequested
            || Time.unscaledTime - _managedPrefabRefreshRequestedAt < 1f
            || hasMatchingVisual)
        {
            return false;
        }

        var underlyingPrefab = bot.UnderlyingPrefabComponent;
        var prefabCollection = underlyingPrefab?._spookedCharacterPrefabs;
        var runner = bot.Runner;
        if (underlyingPrefab is null || prefabCollection is null || runner is null)
        {
            return false;
        }

        _managedDirectPrefabSpawnRequested = true;
        var prefab = prefabCollection.GetPrefab(bot.CharacterType, bot.SubCharacterType);
        if (prefab is null)
        {
            _logger?.LogWarning(
                $"Managed bot character prefab was unavailable for {bot.CharacterType}/{bot.SubCharacterType}");
            return false;
        }

        NetworkObject? spawnedPrefab = null;
        var replacedAnimator = bot.EntityNetworkAnimatorComponent;
        _managedCharacterPrefabInitializer ??=
            (NetworkRunner.OnBeforeSpawned)(Action<NetworkRunner, NetworkObject>)InitializeManagedBotCharacterPrefab;
        _managedCharacterPrefabSpawnTarget = underlyingPrefab;
        _managedCharacterPrefabSpawnScope = true;
        try
        {
            spawnedPrefab = runner.Spawn(
                prefab,
                new Il2CppSystem.Nullable<Vector3>(underlyingPrefab.transform.position),
                new Il2CppSystem.Nullable<Quaternion>(underlyingPrefab.transform.rotation),
                new Il2CppSystem.Nullable<PlayerRef>(bot.PlayerRef),
                _managedCharacterPrefabInitializer,
                default);
        }
        finally
        {
            _managedCharacterPrefabSpawnScope = false;
            _managedCharacterPrefabSpawnTarget = null;
        }

        if (spawnedPrefab is null || !spawnedPrefab.IsValid)
        {
            _logger?.LogWarning(
                $"Direct stock character prefab spawn failed for {bot.CharacterType}/{bot.SubCharacterType}");
            return false;
        }

        var currentAnimator = bot.EntityNetworkAnimatorComponent;
        if (currentAnimator is not null && currentAnimator.Pointer != IntPtr.Zero)
        {
            CleanupSupersededManagedBotVisuals(bot, currentAnimator, replacedAnimator);
        }

        _logger?.LogInfo(
            $"Spawned the missing managed bot character prefab through Fusion: object={spawnedPrefab.Id}, "
            + $"character={bot.CharacterType}/{bot.SubCharacterType}");
        return true;
    }

    private static void CleanupSupersededManagedBotVisuals(
        SpookedNetworkPlayer bot,
        EntityNetworkAnimatorComponent currentAnimator,
        EntityNetworkAnimatorComponent? explicitlyReplacedAnimator = null)
    {
        var runner = bot.Runner;
        var botObject = bot.Object;
        var currentObject = currentAnimator.Object;
        if (runner is null || currentObject is null || !currentObject.IsValid)
        {
            return;
        }

        var removed = 0;
        foreach (var candidate in Resources.FindObjectsOfTypeAll<EntityNetworkAnimatorComponent>())
        {
            if (candidate is null
                || candidate.Pointer == IntPtr.Zero
                || candidate.Pointer == currentAnimator.Pointer)
            {
                continue;
            }

            var linkedPlayer = candidate._spookedNetworkPlayer
                ?? candidate.GetComponentInParent<SpookedNetworkPlayer>();
            var wasPublishedBeforeReplacement = candidate.Pointer == explicitlyReplacedAnimator?.Pointer;
            if (!wasPublishedBeforeReplacement && !IsManagedBot(linkedPlayer))
            {
                continue;
            }

            var candidateObject = candidate.Object;
            if (wasPublishedBeforeReplacement
                && candidateObject?.Pointer == botObject?.Pointer
                && TryDisableReplacedManagedBotVisual(bot, currentAnimator, candidate))
            {
                ManagedAnimatorPointers.Remove(candidate.Pointer);
                removed++;
                continue;
            }

            if (candidateObject is null
                || !candidateObject.IsValid
                || !candidateObject.HasStateAuthority
                || candidateObject.Pointer == currentObject.Pointer
                || candidateObject.Pointer == botObject?.Pointer)
            {
                continue;
            }

            ManagedAnimatorPointers.Remove(candidate.Pointer);
            runner.Despawn(candidateObject);
            removed++;
        }

        if (removed > 0)
        {
            _logger?.LogInfo($"Retired {removed} superseded managed bot character visual(s)");
        }
    }

    private static bool TryDisableReplacedManagedBotVisual(
        SpookedNetworkPlayer bot,
        EntityNetworkAnimatorComponent currentAnimator,
        EntityNetworkAnimatorComponent replacedAnimator)
    {
        var staleObject = replacedAnimator._animator?.gameObject;
        var currentObject = currentAnimator._animator?.gameObject;
        if (staleObject is null
            || staleObject.Pointer == IntPtr.Zero
            || staleObject.Pointer == bot.gameObject.Pointer
            || staleObject.Pointer == currentObject?.Pointer)
        {
            return false;
        }

        staleObject.SetActive(false);
        return true;
    }

    private static void InitializeManagedBotCharacterPrefab(
        NetworkRunner runner,
        NetworkObject spawnedPrefab)
    {
        var underlyingPrefab = _managedCharacterPrefabSpawnTarget;
        if (underlyingPrefab is null)
        {
            throw new InvalidOperationException("Managed bot character prefab target was lost during Fusion spawn");
        }

        underlyingPrefab.PrefabSpawned(spawnedPrefab.gameObject);
    }

    private static void ForgetManagedBot(bool preserveMatchIntent)
    {
        StopManagedBotControl(null, "managed bot left the session");
        _managedSpawnerPointer = IntPtr.Zero;
        _managedPlayerPointer = IntPtr.Zero;
        _managedNetworkObjectPointer = IntPtr.Zero;
        _managedPlayerRefId = 0;
        _managedFusionPlayerRefRaw = 0;
        _nextManagedOutfitRefreshAt = -1f;
        _managedOutfitRefreshDeadline = -1f;
        _managedPrefabRefreshRequested = false;
        _managedPrefabRefreshRequestedAt = -1f;
        _managedDirectPrefabSpawnRequested = false;
        _managedHunterVisualRepairAt = -1f;
        _managedHunterVisualRepairDeadline = -1f;
        if (!preserveMatchIntent)
        {
            _managedMatchJoinStarted = false;
            _carryBotIntoMatch = false;
        }
        _diagnosticAutoStartSpawnerPointer = IntPtr.Zero;
        _diagnosticBotReadyAt = 0f;
        _diagnosticModeRequested = false;
        _diagnosticPrivateToggleRequested = false;
        _diagnosticPlayRequested = false;
        _diagnosticRemoveRequested = false;
        _managedMatchStartGuardScope = false;
    }

    private static EntityNetworkAnimatorComponent? FindNetworkAnimator(SpookedNetworkPlayer player)
    {
        if (player.EntityNetworkAnimatorComponent is not null)
        {
            return player.EntityNetworkAnimatorComponent;
        }

        var playerObject = player.Object;
        if (playerObject is null || !playerObject.IsValid)
        {
            return null;
        }

        foreach (var animator in Resources.FindObjectsOfTypeAll<EntityNetworkAnimatorComponent>())
        {
            if (animator is null || animator.Pointer == IntPtr.Zero)
            {
                continue;
            }

            var animatorObject = animator.Object;
            if (animatorObject is not null
                && animatorObject.IsValid
                && animatorObject.Pointer == playerObject.Pointer)
            {
                return animator;
            }
        }

        return null;
    }

    private static void TryRunDiagnosticAutoStart(PortalPlayView view)
    {
        if (_configuration is null
            || !_configuration.AutoAddBotWhenLobbyReady.Value
            || _configuration.AutoRemoveBotWhenReady.Value
            || !_configuration.AutoStartPrivateMatchWhenBotReady.Value
            || _diagnosticPlayRequested
            || _diagnosticPortalOpenedAt <= 0f
            || Time.unscaledTime < _diagnosticUiActionNotBefore
            || FindManagedBot() is null)
        {
            return;
        }

        var sceneSpawner = ResolveAuthoritativeLobbySpawner();
        if (sceneSpawner is null
            || sceneSpawner.Pointer != _diagnosticAutoStartSpawnerPointer
            || Time.unscaledTime - Mathf.Max(_diagnosticBotReadyAt, _diagnosticPortalOpenedAt)
                < Mathf.Max(0f, _configuration.AutoStartDelaySeconds.Value))
        {
            return;
        }

        if (!_diagnosticModeRequested
            && _configuration.AutoStartGameMode.Value != DiagnosticGameMode.Preserve)
        {
            var requestedMode = _configuration.AutoStartGameMode.Value;
            // Resolve the native Mode switch by its unique scene name.
            var modeButton = Resources.FindObjectsOfTypeAll<Button>()
                .FirstOrDefault(button => button is not null
                    && button.Pointer != IntPtr.Zero
                    && button.gameObject.name == "Checkbox"
                    && button.transform.parent?.gameObject.name == "CodexPortalModeSwitch");
            if (modeButton is null)
            {
                if (!_diagnosticModeSelectionFailureLogged)
                {
                    _diagnosticModeSelectionFailureLogged = true;
                    _logger?.LogError("Diagnostic mode selection could not find the live portal Mode switch");
                }
                return;
            }

            // Every diagnostic process starts from the selector's stock Classic default. Drive
            // the real button once for Crown; Classic intentionally leaves it untouched.
            if (requestedMode == DiagnosticGameMode.Crown)
            {
                modeButton.onClick.Invoke();
            }

            _diagnosticModeRequested = true;
            _logger?.LogInfo($"Diagnostic match automation selected portal mode {requestedMode}");
            return;
        }

        if (!sceneSpawner._gameState.PrivateGameCheckbox)
        {
            if (_diagnosticPrivateToggleRequested)
            {
                return;
            }

            _diagnosticPrivateToggleRequested = true;
            _logger?.LogInfo("Diagnostic match automation invoking the stock private-game portal callback");
            view._privateGameButton?.onClick.Invoke();
            return;
        }

        _diagnosticPlayRequested = true;
        _logger?.LogInfo("Diagnostic match automation invoking the stock PLAY callback");
        view._playButton.onClick.Invoke();
        _logger?.LogInfo(
            $"Diagnostic stock PLAY callback completed: mode={sceneSpawner._gameState.GameMode}");
    }

    private static void TryRunDiagnosticAutoRemove(PortalPlayView view)
    {
        if (_configuration is null
            || !_configuration.AutoAddBotWhenLobbyReady.Value
            || !_configuration.AutoRemoveBotWhenReady.Value
            || _diagnosticRemoveRequested
            || FindManagedBot() is null)
        {
            return;
        }

        var sceneSpawner = ResolveAuthoritativeLobbySpawner();
        if (sceneSpawner is null
            || sceneSpawner.Pointer != _diagnosticAutoStartSpawnerPointer
            || Time.unscaledTime - _diagnosticBotReadyAt < Mathf.Max(0f, _configuration.AutoStartDelaySeconds.Value))
        {
            return;
        }

        if (!UiStateByView.TryGetValue(view.Pointer, out var state) || !state.IsAlive)
        {
            return;
        }

        _diagnosticRemoveRequested = true;
        _logger?.LogInfo("Diagnostic bot removal invoking the native Dummy bot switch");
        state.DummySwitch.Button.onClick.Invoke();
    }

    private static void RefreshAllButtons()
    {
        foreach (var state in UiStateByView.Values)
        {
            if (state.IsAlive)
            {
                RefreshButton(state);
            }
        }
    }

    private static void RefreshButton(LobbyTestBotUiState state)
    {
        var canManageBot = ResolveAuthoritativeLobbySpawner() is not null;
        var hasBot = FindManagedBot() is not null;
        var pending = _pendingOperation != PendingOperation.None;
        var interactable = canManageBot && !pending;
        var roleVisible = hasBot || _pendingOperation == PendingOperation.Add;
        state.Section.SetActive(true);
        state.Title.text = pending ? "Dummy bot  ·  Please wait" : "Dummy bot";
        LayoutButton(state.View, state, roleVisible);
        PortalSettingsLayout.SetNativeSwitchPresentation(
            state.DummySwitch,
            leftSelected: !hasBot,
            selectedColor: SelectedSegmentColor,
            deselectedColor: DeselectedSegmentColor,
            interactable: interactable,
            dimmed: pending);
        PortalSettingsLayout.SetNativeSwitchPresentation(
            state.RoleSwitch,
            leftSelected: !BotPrefersHunter,
            selectedColor: BotPrefersHunter ? state.View._seekerColor : state.View._victimColor,
            deselectedColor: DeselectedSegmentColor,
            interactable: interactable,
            dimmed: pending);
    }

    private static void ClearPendingOperation()
    {
        _pendingOperation = PendingOperation.None;
        _pendingStartedAt = 0f;
        _requestedPlayerRefId = 0;
        _requestedSpawnerPointer = IntPtr.Zero;
    }

    private static void LogError(string context, Exception exception)
    {
        _logger?.LogError($"{context}: {exception}");
    }

    private sealed class LobbyBotLifecycleWatcher : MonoBehaviour
    {
        public LobbyBotLifecycleWatcher(IntPtr pointer) : base(pointer)
        {
        }

        public LobbyBotLifecycleWatcher() : base(ClassInjector.DerivedConstructorPointer<LobbyBotLifecycleWatcher>())
        {
            ClassInjector.DerivedConstructorBody(this);
        }

        private void Update()
        {
            WatcherTick();
        }

        private void LateUpdate()
        {
            WatcherLateTick();
        }
    }
}

internal sealed record ManagedBotResolverState(
    NetworkPlayerRegistry Registry,
    SpookedNetworkPlayer Bot,
    int InternalId);

internal sealed class ManagedBotInputScope
{
    public ManagedBotInputScope(SpookedNetworkPlayer originalPlayer)
    {
        OriginalPlayer = originalPlayer;
    }

    public SpookedNetworkPlayer OriginalPlayer { get; }

    public bool Restored { get; set; }
}
