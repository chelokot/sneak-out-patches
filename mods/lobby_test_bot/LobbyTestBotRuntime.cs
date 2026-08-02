using BepInEx;
using BepInEx.Logging;
using Events;
using Fusion;
using Gameplay.Player.Components;
using Gameplay.Spawn;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using Networking;
using Networking.Matchmaking;
using Networking.Matchmaking.Match;
using Types;
using UI.Views.Lobby;
using UnityEngine;
using UnityEngine.Events;
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

    private const float ButtonWidth = 108f;
    private const float ButtonHeight = 38f;
    private const float PendingTimeout = 8f;
    private const float RefreshInterval = 0.4f;

    private static readonly Dictionary<IntPtr, LobbyTestBotUiState> UiStateByView = new();

    private static ManualLogSource? _logger;
    private static LobbyTestBotConfig? _configuration;
    private static Harmony? _harmony;
    private static Font? _legacyFont;
    private static UI.GameUIManager? _gameUiManager;
    private static bool _watcherInstalled;
    private static bool _controlsPrepared;
    private static PendingOperation _pendingOperation;
    private static float _pendingStartedAt;
    private static int _requestedPlayerRefId;
    private static IntPtr _requestedSpawnerPointer;
    private static IntPtr _diagnosticPublishedLobbySpawnerPointer;
    private static IntPtr _diagnosticOpenedPortalSpawnerPointer;
    private static float _diagnosticPortalCaptureAt;
    private static bool _diagnosticPortalCaptured;
    private static bool _diagnosticMapPanelOpenedForCapture;
    private static IntPtr _diagnosticAutoStartSpawnerPointer;
    private static float _diagnosticBotReadyAt;
    private static bool _diagnosticModeRequested;
    private static bool _diagnosticPrivateToggleRequested;
    private static bool _diagnosticPlayRequested;
    private static int _diagnosticLocalPlayerRefId;
    private static IntPtr _managedSpawnerPointer;
    private static IntPtr _managedPlayerPointer;
    private static IntPtr _managedNetworkObjectPointer;
    private static int _managedPlayerRefId;
    private static readonly HashSet<IntPtr> ManagedAnimatorPointers = new();
    private static bool _managedMatchJoinStarted;
    private static bool _carryBotIntoMatch;
    private static GameModeType _managedMatchMode;
    [ThreadStatic]
    private static bool _managedMatchStartGuardScope;

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
            ObservePendingOperation();
            MaintainMatchBot();
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
                TryCaptureDiagnosticPortal();
                TickPortal(view);
                TryRunDiagnosticAutoStart(view);
            }
        }
        catch (Exception exception)
        {
            LogError("Lobby bot lifecycle watcher failed", exception);
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

    private static string BotNickname => _configuration!.BotNickname.Value.Trim();

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
            LayoutButton(view, state);
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
            state.Button.onClick.RemoveListener(state.ClickAction);
            UnityEngine.Object.Destroy(state.RootObject);
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
        ForgetManagedBot(preserveMatchIntent);
        RefreshAllButtons();
    }

    public static void IncludeManagedBotInPartyCount(ref int teamCount)
    {
        if (Enabled && _managedPlayerPointer != IntPtr.Zero && teamCount < 2)
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

            TryRunDiagnosticAutoAdd();
        }
        catch (Exception exception)
        {
            LogError("Diagnostic lobby bot auto-add failed", exception);
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

            RegisterBotInPlayerRegistries(sceneSpawner, bot);
        }
        catch (Exception exception)
        {
            LogError("Authoritative lobby bot completion observation failed", exception);
        }
    }

    public static bool ShouldRunNetworkAnimator(EntityNetworkAnimatorComponent animator)
    {
        if (!Enabled)
        {
            return true;
        }

        if (ManagedAnimatorPointers.Contains(animator.Pointer))
        {
            return false;
        }

        var player = animator._spookedNetworkPlayer;
        if (player is null)
        {
            player = animator.GetComponentInParent<SpookedNetworkPlayer>();
        }

        if (IsManagedBot(player))
        {
            ManagedAnimatorPointers.Add(animator.Pointer);
            return false;
        }

        var networkObject = animator.Object;
        if (_managedNetworkObjectPointer != IntPtr.Zero
            && networkObject is not null
            && networkObject.Pointer == _managedNetworkObjectPointer)
        {
            ManagedAnimatorPointers.Add(animator.Pointer);
            return false;
        }

        return player is not null || (_managedPlayerPointer == IntPtr.Zero && !_carryBotIntoMatch);
    }

    public static bool ShouldSkipVictimPlacement()
    {
        if (!Enabled || _managedPlayerRefId == 0 || _managedPlayerPointer == IntPtr.Zero)
        {
            return false;
        }

        if (LoggingEnabled)
        {
            _logger?.LogInfo("Skipped stock victim staging for the animation-less test bot");
        }

        return true;
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

        sceneType = _configuration.AutoStartMap.Value switch
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
            LayoutButton(view, existingState);
            return existingState;
        }

        var playSection = view._playButton.transform.parent?.GetComponent<RectTransform>();
        if (playSection is null || playSection.parent is null)
        {
            _logger?.LogWarning("Lobby bot button setup skipped: play-button panel was not found");
            return null;
        }

        var buttonObject = new GameObject("LobbyTestBotButton");
        buttonObject.transform.SetParent(view._playButton.transform, false);
        var buttonRect = buttonObject.AddComponent<RectTransform>();
        var background = buttonObject.AddComponent<Image>();
        var button = buttonObject.AddComponent<Button>();

        CopyButtonStyle(view._playButton, button, background);

        var labelObject = new GameObject("Label");
        labelObject.transform.SetParent(buttonObject.transform, false);
        var labelRect = labelObject.AddComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(4f, 2f);
        labelRect.offsetMax = new Vector2(-4f, -2f);
        var label = labelObject.AddComponent<Text>();
        label.font = GetLegacyFont();
        label.fontSize = 14;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.raycastTarget = false;

        var clickAction = (UnityAction)ToggleBot;
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(clickAction);
        buttonRect.SetAsLastSibling();

        var state = new LobbyTestBotUiState(
            buttonObject,
            button,
            clickAction,
            label);
        UiStateByView[view.Pointer] = state;

        LayoutButton(view, state);
        return state;
    }

    private static void CopyButtonStyle(Button source, Button target, Image targetImage)
    {
        target.transition = source.transition;
        target.colors = source.colors;
        target.spriteState = source.spriteState;
        target.navigation = source.navigation;
        target.targetGraphic = targetImage;

        if (source.targetGraphic is not Image sourceImage)
        {
            targetImage.color = new Color(0.08627451f, 0.5372549f, 0.654902f, 1f);
            return;
        }

        targetImage.sprite = sourceImage.sprite;
        targetImage.overrideSprite = sourceImage.overrideSprite;
        targetImage.type = sourceImage.type;
        targetImage.preserveAspect = sourceImage.preserveAspect;
        targetImage.fillCenter = sourceImage.fillCenter;
        targetImage.fillMethod = sourceImage.fillMethod;
        targetImage.fillAmount = sourceImage.fillAmount;
        targetImage.fillClockwise = sourceImage.fillClockwise;
        targetImage.fillOrigin = sourceImage.fillOrigin;
        targetImage.pixelsPerUnitMultiplier = sourceImage.pixelsPerUnitMultiplier;
        targetImage.material = sourceImage.material;
        targetImage.color = sourceImage.color;
    }

    private static Font GetLegacyFont()
    {
        if (_legacyFont is not null && _legacyFont.Pointer != IntPtr.Zero)
        {
            return _legacyFont;
        }

        _legacyFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return _legacyFont;
    }

    private static void LayoutButton(PortalPlayView view, LobbyTestBotUiState state)
    {
        var playButton = view._playButton;
        var playSection = playButton?.transform.parent?.GetComponent<RectTransform>();
        var playRect = playButton?.GetComponent<RectTransform>();
        var buttonRect = state.RootObject.GetComponent<RectTransform>();
        if (playButton is null || playSection is null || playRect is null || buttonRect is null)
        {
            return;
        }

        if (state.RootObject.transform.parent != playButton.transform)
        {
            state.RootObject.transform.SetParent(playButton.transform, false);
        }

        buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
        buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
        buttonRect.pivot = new Vector2(0.5f, 0.5f);
        buttonRect.localScale = Vector3.one;
        buttonRect.localRotation = Quaternion.identity;
        buttonRect.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);
        var playHeight = playRect.rect.height;
        buttonRect.anchoredPosition = new Vector2(
            114f,
            playHeight * 0.5f + ButtonHeight * 0.5f + 12f);
        buttonRect.SetAsLastSibling();
    }

    private static void ToggleBot()
    {
        try
        {
            if (!Enabled || _pendingOperation != PendingOperation.None)
            {
                return;
            }

            var bot = FindManagedBot();
            if (bot is null)
            {
                TryAddBot();
            }
            else
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
            var spawnedObject = sceneSpawner.Runner.Spawn(
                sceneSpawner._playerPrefab,
                new Il2CppSystem.Nullable<Vector3>(sceneSpawner.GetSpawnPosition()),
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
                    $"Spawned test bot through Fusion for playerRef={playerRefId}, networkObject={spawnedObject.Id}, scene={sceneSpawner._gameState.CurrentScene}");
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
        var botInternalId = bot.InternalId;
        var botPlayerRefId = bot.KinguinverseId;
        sceneSpawner.Runner.Despawn(bot.Object);
        sceneSpawner._networkPlayerRegistry.ClearPlayer(botInternalId);
        if (sceneSpawner._players.Exists(botPlayerRefId, out _))
        {
            sceneSpawner._players.Remove(botPlayerRefId);
        }

        if (LoggingEnabled)
        {
            _logger?.LogInfo(
                $"Requested native bot despawn: playerRef={bot.KinguinverseId}, networkObject={bot.Object.Id}");
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
            || _diagnosticOpenedPortalSpawnerPointer == sceneSpawner.Pointer)
        {
            return;
        }

        _diagnosticOpenedPortalSpawnerPointer = sceneSpawner.Pointer;
        _logger?.LogInfo("Diagnostic UI automation invoking the stock portal Open callback");
        view.Open();
        _diagnosticPortalCaptured = false;
        _diagnosticMapPanelOpenedForCapture = false;
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

        if (!_diagnosticMapPanelOpenedForCapture)
        {
            var mapsButtonObject = GameObject.Find("CodexPortalMapsButton");
            var mapsButton = mapsButtonObject?.GetComponent<Button>();
            if (mapsButton is not null)
            {
                mapsButton.onClick.Invoke();
                _diagnosticMapPanelOpenedForCapture = true;
                _diagnosticPortalCaptureAt = Time.unscaledTime + 1f;
                return;
            }
        }

        var captureDirectory = Path.Combine(Paths.BepInExRootPath, "ui-captures");
        Directory.CreateDirectory(captureDirectory);
        var capturePath = Path.Combine(captureDirectory, "portal-layout.png");
        ScreenCapture.CaptureScreenshot(capturePath);
        _diagnosticPortalCaptured = true;
        _logger?.LogInfo($"Captured diagnostic portal framebuffer: {capturePath}");
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
            + $"networkPlayerCount={registryCount}, scene={sceneSpawner?._gameState.CurrentScene}");
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

    private static void RegisterBotInPlayerRegistries(SceneSpawner sceneSpawner, SpookedNetworkPlayer bot)
    {
        if (!sceneSpawner._players.Exists(bot.KinguinverseId, out var internalId))
        {
            internalId = sceneSpawner._players.Add(bot.KinguinverseId);
        }

        if (internalId < 0 || internalId >= sceneSpawner._networkPlayerRegistry._components.Length)
        {
            throw new InvalidOperationException(
                $"The player registries returned invalid internalId={internalId} for bot playerRef={bot.KinguinverseId}");
        }

        bot.InternalId = internalId;
        sceneSpawner._networkPlayerRegistry[internalId] = bot;
    }

    private static void NormalizeBotRegistration(SpookedNetworkPlayer bot)
    {
        var sceneSpawner = ResolveAuthoritativeSpawner();
        if (sceneSpawner is null || sceneSpawner.Pointer != _requestedSpawnerPointer)
        {
            return;
        }

        var registry = sceneSpawner._networkPlayerRegistry;
        if (!sceneSpawner._players.Exists(bot.KinguinverseId, out var registeredInternalId))
        {
            registeredInternalId = sceneSpawner._players.Add(bot.KinguinverseId);
        }

        bot.InternalId = registeredInternalId;
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
        for (var internalId = 0; internalId < registry._components.Length; internalId++)
        {
            var registeredPlayer = registry._components[internalId];
            if (internalId != registeredInternalId
                && registeredPlayer is not null
                && registeredPlayer.Pointer == bot.Pointer)
            {
                registry.ClearPlayer(internalId);
            }
        }

        registry[registeredInternalId] = bot;
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
        var sceneSpawner = UnityEngine.Object.FindObjectOfType<SceneSpawner>();
        if (sceneSpawner is null
            || sceneSpawner._gameState is null
            || sceneSpawner.Object is null
            || !sceneSpawner.Object.IsValid
            || !sceneSpawner.HasStateAuthority
            || sceneSpawner.Runner is null
            || !sceneSpawner.Runner.IsRunning)
        {
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

        foreach (var player in Resources.FindObjectsOfTypeAll<SpookedNetworkPlayer>())
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

        foreach (var player in Resources.FindObjectsOfTypeAll<SpookedNetworkPlayer>())
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
            _logger?.LogInfo(
                $"Managed test bot registered in match: scene={sceneSpawner._gameState.CurrentScene}, mode={sceneSpawner._gameState.GameMode}");
        }
        else if (sceneSpawner is not null)
        {
            _diagnosticAutoStartSpawnerPointer = sceneSpawner.Pointer;
            _diagnosticBotReadyAt = Time.unscaledTime;
            _diagnosticModeRequested = false;
            _diagnosticPrivateToggleRequested = false;
            _diagnosticPlayRequested = false;
        }
    }

    private static void ForgetManagedBot(bool preserveMatchIntent)
    {
        _managedSpawnerPointer = IntPtr.Zero;
        _managedPlayerPointer = IntPtr.Zero;
        _managedNetworkObjectPointer = IntPtr.Zero;
        _managedPlayerRefId = 0;
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
            || !_configuration.AutoStartPrivateMatchWhenBotReady.Value
            || _diagnosticPlayRequested
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

        if (!_diagnosticModeRequested
            && _configuration.AutoStartGameMode.Value != DiagnosticGameMode.Preserve)
        {
            var requestedMode = _configuration.AutoStartGameMode.Value;
            var requestedLabel = requestedMode == DiagnosticGameMode.Crown ? "CROWN" : "CLASSIC";
            var modeButtonObject = view._playButton.transform
                .Find("CodexPortalControls/CodexPortalModeButton")?.gameObject;
            var modeButton = modeButtonObject?.GetComponent<Button>();
            var modeLabel = modeButtonObject?.GetComponentInChildren<Text>();
            if (modeButton is not null
                && !string.Equals(modeLabel?.text, requestedLabel, StringComparison.OrdinalIgnoreCase))
            {
                modeButton.onClick.Invoke();
            }
            else if (modeButton is null)
            {
                sceneSpawner._gameState.GameMode = requestedMode == DiagnosticGameMode.Crown
                    ? GameModeType.Berek
                    : GameModeType.Default;
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
        _logger?.LogInfo(
            $"Diagnostic match automation invoking the stock PLAY callback: mode={sceneSpawner._gameState.GameMode}");
        view._playButton.onClick.Invoke();
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
        state.RootObject.SetActive(canManageBot);
        if (!canManageBot)
        {
            return;
        }

        var hasBot = FindManagedBot() is not null;
        var pending = _pendingOperation != PendingOperation.None;
        state.Button.interactable = !pending;
        state.Label.text = pending ? "PLEASE WAIT" : hasBot ? "REMOVE BOT" : "ADD BOT";
        var labelColor = state.Label.color;
        labelColor.a = pending ? 0.45f : 1f;
        state.Label.color = labelColor;
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
    }
}

internal sealed record ManagedBotResolverState(
    NetworkPlayerRegistry Registry,
    SpookedNetworkPlayer Bot,
    int InternalId);
