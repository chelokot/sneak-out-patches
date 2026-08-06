using BepInEx.Logging;
using Events;
using Gameplay.Player.Components;
using Gameplay.Match;
using Gameplay.Match.MatchState;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Kinguinverse.DataUtils.Events;
using Networking;
using Networking.Photon;
using SneakOut.PortalSettings;
using TMPro;
using Types;
using UI;
using UI.Buttons;
using UI.Views.Lobby;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SneakOut.PortalModeSelector;

internal static class PortalModeSelectorRuntime
{
    private const float ModeSwitchWidth = 315.3f;
    private const float ModeSwitchHeight = 37.4f;
    private const float ModeSwitchY = -11.4f;
    private const float MapButtonHeight = 26f;
    private const float MapButtonWidth = 78f;
    private const float MapButtonGap = 4f;

    private static readonly Dictionary<IntPtr, PortalModeUiState> UiStateByView = new();
    private static readonly PortalMapSelectionState PreferredMapSelection = new();
    private static readonly Color ClassicModeColor = new(0.08627451f, 0.5372549f, 0.654902f, 1f);
    private static readonly Color CrownModeColor = new(0.8117647f, 0.62352943f, 0f, 1f);
    private static readonly Color MapDisabledColor = new(0.16f, 0.18f, 0.22f, 0.95f);

    private static ManualLogSource? _logger;
    private static Harmony? _harmony;
    private static GameUIManager? _gameUiManager;
    private static bool _watcherInstalled;
    private static bool _controlsPrepared;
    private static GameModeType _preferredMode = GameModeType.Default;
    private static GameModeType? _activeMode;
    private static PortalMapSelectionState? _activeMapSelection;
    private static bool _activeMatchTickConfirmed;
    private static bool _activeModeCorrectionLogged;
    private static bool _berekSelectionRedirectLogged;
    private static bool _berekStartRedirectLogged;

    public static void Initialize(ManualLogSource logger)
    {
        _logger = logger;
        PreferredMapSelection.SynchronizeDefaults();
        _harmony ??= new Harmony(PortalModeSelectorPlugin.PluginGuid);
        _harmony.PatchAll();
        EnsureWatcher();
    }

    public static void BindPortalManager(GameUIManager gameUiManager)
    {
        _gameUiManager = gameUiManager;
        _controlsPrepared = false;
        _logger?.LogInfo("Portal selector captured GameUIManager");
    }

    private static void EnsureWatcher()
    {
        if (_watcherInstalled)
        {
            return;
        }

        ClassInjector.RegisterTypeInIl2Cpp<PortalLifecycleWatcher>();
        var watcherObject = new GameObject("PortalModeSelectorWatcher");
        UnityEngine.Object.DontDestroyOnLoad(watcherObject);
        watcherObject.hideFlags = HideFlags.HideAndDontSave;
        watcherObject.AddComponent<PortalLifecycleWatcher>();
        _watcherInstalled = true;
    }

    private static void WatcherTick()
    {
        if (_controlsPrepared)
        {
            return;
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
            PreparePortalControls();
            _controlsPrepared = UiStateByView.TryGetValue(view.Pointer, out var state) && state.IsAlive;
        }
        catch (Exception exception)
        {
            LogError("Portal lifecycle watcher failed", exception);
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

    public static void OpenPortal(PortalPlayView view)
    {
        // Portal views are recreated more than once during lobby-to-match transition. The next
        // PLAY call replaces the active snapshot, so opening a view must not erase an in-flight one.
        if (TryEnsureControls(view))
        {
            _logger?.LogInfo("Portal controls opened with safe UI controls");
        }
    }

    private static bool TryEnsureControls(PortalPlayView view)
    {
        if (view.Pointer == IntPtr.Zero || view._playButton is null)
        {
            return false;
        }

        if (UiStateByView.TryGetValue(view.Pointer, out var existingState) && existingState.IsAlive)
        {
            LayoutControls(view, existingState);
            RefreshControls(existingState);
            return true;
        }

        var modeSection = PortalSettingsLayout.CreateNativeSection(
            view,
            PortalSettingsLayout.ModeSectionName,
            "Mode");
        if (modeSection is null)
        {
            _logger?.LogWarning("Portal controls skipped: native Mode row was not found");
            return false;
        }

        var modeSwitch = PortalSettingsLayout.CreateNativeSwitch(
            view,
            modeSection.Root.transform,
            "CodexPortalModeSwitch",
            "CLASSIC",
            "CROWN",
            fontSize: 12f);
        if (modeSwitch is null)
        {
            UnityEngine.Object.Destroy(modeSection.Root);
            _logger?.LogWarning("Portal controls skipped: native Mode switch was incomplete");
            return false;
        }

        PortalSettingsLayout.UseNativeSwitchIcons(
            modeSwitch,
            "PortalInteractionIcon",
            "crown_icon");

        var modeClickAction = (UnityAction)(() => ToggleMode(view.Pointer));
        modeSwitch.Button.onClick.AddListener(modeClickAction);

        var mapsSection = PortalSettingsLayout.CreateNativeSection(
            view,
            PortalSettingsLayout.MapsSectionName,
            "Maps");
        if (mapsSection is null)
        {
            modeSwitch.Button.onClick.RemoveListener(modeClickAction);
            UnityEngine.Object.Destroy(modeSection.Root);
            return false;
        }

        var mapOptions = new List<PortalMapOptionUiState>();
        foreach (var map in PreferredMapSelection.GetAvailableMaps(GameModeType.Default).OrderBy(GetMapDisplayOrder))
        {
            var option = CreateMapOption(
                view,
                mapsSection.Root.transform,
                view._playButton,
                map,
                GameModeType.Default);
            if (option is not null)
            {
                mapOptions.Add(option);
            }
        }

        foreach (var map in PreferredMapSelection.GetAvailableMaps(GameModeType.Berek).OrderBy(GetMapDisplayOrder))
        {
            var option = CreateMapOption(
                view,
                mapsSection.Root.transform,
                view._playButton,
                map,
                GameModeType.Berek);
            if (option is not null)
            {
                mapOptions.Add(option);
            }
        }

        var state = new PortalModeUiState(
            view,
            modeSection.Root,
            modeSection.Title,
            modeSwitch,
            modeClickAction,
            mapsSection.Root,
            mapsSection.Title,
            mapOptions.ToArray());
        UiStateByView[view.Pointer] = state;

        LayoutControls(view, state);
        RefreshControls(state);
        return true;
    }

    private static PortalMapOptionUiState? CreateMapOption(
        PortalPlayView view,
        Transform parent,
        SpookedOutlineButton styleSource,
        SceneType sceneType,
        GameModeType gameModeType)
    {
        var segment = PortalSettingsLayout.CreateSegmentButton(
            view,
            parent,
            $"CodexPortalMap_{gameModeType}_{sceneType}",
            styleSource,
            fontSize: 11f);
        if (segment is null)
        {
            return null;
        }

        var clickAction = (UnityAction)(() => ToggleMap(view.Pointer, sceneType, gameModeType));
        segment.Button.onClick.AddListener(clickAction);
        segment.Label.text = FormatMapName(sceneType).ToUpperInvariant();
        return new PortalMapOptionUiState(
            sceneType,
            gameModeType,
            segment.Root,
            segment.Background,
            segment.Label,
            segment.Button,
            clickAction);
    }

    private static void LayoutControls(PortalPlayView view, PortalModeUiState state)
    {
        PortalSettingsLayout.Apply(view);
        PortalSettingsLayout.LayoutNativeSwitch(
            state.ModeSwitch,
            0f,
            ModeSwitchY,
            ModeSwitchWidth,
            ModeSwitchHeight);

        var visibleIndex = 0;
        var visibleCount = PreferredMapSelection.GetAvailableMaps(_preferredMode).Count;
        foreach (var option in state.MapOptions)
        {
            if (option.GameModeType != _preferredMode)
            {
                continue;
            }

            var rect = option.RootObject.GetComponent<RectTransform>();
            if (rect is null)
            {
                continue;
            }

            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(MapButtonWidth, MapButtonHeight);
            var column = visibleIndex % 4;
            var row = visibleIndex / 4;
            var rowCount = Mathf.CeilToInt(visibleCount / 4f);
            var visualRow = rowCount - row - 1;
            var itemsInRow = Mathf.Min(4, visibleCount - row * 4);
            var rowY = rowCount == 1
                ? -7f
                : -33f + visualRow * (MapButtonHeight + MapButtonGap);
            rect.anchoredPosition = new Vector2(
                -(itemsInRow - 1) * (MapButtonWidth + MapButtonGap) * 0.5f
                    + column * (MapButtonWidth + MapButtonGap),
                rowY);
            visibleIndex++;
        }
    }

    private static void RefreshControls(PortalModeUiState state)
    {
        var classic = _preferredMode == GameModeType.Default;
        state.ModeTitle.text = "Mode";
        PortalSettingsLayout.SetNativeSwitchPresentation(
            state.ModeSwitch,
            leftSelected: classic,
            selectedColor: classic ? ClassicModeColor : CrownModeColor,
            deselectedColor: MapDisabledColor,
            interactable: true);

        var selectedMaps = PreferredMapSelection.GetSelectedMaps(_preferredMode);
        var availableMaps = PreferredMapSelection.GetAvailableMaps(_preferredMode);
        state.MapsTitle.text = $"Maps  {selectedMaps.Count}/{availableMaps.Count}";
        foreach (var option in state.MapOptions)
        {
            var visible = option.GameModeType == _preferredMode;
            option.RootObject.SetActive(visible);
            if (!visible)
            {
                continue;
            }

            option.Label.text = FormatMapName(option.SceneType).ToUpperInvariant();
            option.Label.enabled = true;
            option.Background.color = selectedMaps.Contains(option.SceneType)
                ? classic ? ClassicModeColor : CrownModeColor
                : MapDisabledColor;
        }

        LayoutControls(state.View, state);
    }

    private static void ToggleMode(IntPtr viewPointer)
    {
        _preferredMode = _preferredMode == GameModeType.Berek ? GameModeType.Default : GameModeType.Berek;
        PublishRequestedGameMode(_preferredMode);
        if (UiStateByView.TryGetValue(viewPointer, out var state) && state.IsAlive)
        {
            RefreshControls(state);
        }
    }

    private static void ToggleMap(IntPtr viewPointer, SceneType sceneType, GameModeType gameModeType)
    {
        var selectedMaps = PreferredMapSelection.GetSelectedMaps(gameModeType);
        if (selectedMaps.Contains(sceneType))
        {
            if (selectedMaps.Count == 1)
            {
                return;
            }

            selectedMaps.Remove(sceneType);
        }
        else
        {
            selectedMaps.Add(sceneType);
        }

        if (UiStateByView.TryGetValue(viewPointer, out var state) && state.IsAlive)
        {
            RefreshControls(state);
        }
    }

    public static void ActivateSelection(PortalPlayView view)
    {
        _activeMode = _preferredMode;
        _activeMapSelection = PreferredMapSelection.Snapshot();
        _activeMatchTickConfirmed = false;
        _activeModeCorrectionLogged = false;
        _berekSelectionRedirectLogged = false;
        _berekStartRedirectLogged = false;
        var photonLobby = UnityEngine.Object.FindObjectOfType<PhotonPlayFabLobbyController>();
        if (photonLobby is not null && photonLobby.HasStateAuthority)
        {
            photonLobby.HostChosenGameMode = _activeMode.Value;
        }
        view._gameState.GameMode = _activeMode.Value;
        PublishRequestedGameMode(_activeMode.Value);
        _logger?.LogInfo($"Portal play requested {_activeMode.Value}");
    }

    public static void ReleasePortalView(PortalPlayView view)
    {
        if (!UiStateByView.Remove(view.Pointer, out var state))
        {
            return;
        }

        state.ModeSwitch.Button.onClick.RemoveListener(state.ModeClickAction);
        foreach (var option in state.MapOptions)
        {
            option.Button.onClick.RemoveListener(option.ClickAction);
        }

        if (state.ModeSection is not null && state.ModeSection.Pointer != IntPtr.Zero)
        {
            UnityEngine.Object.Destroy(state.ModeSection);
        }

        if (state.MapsSection is not null && state.MapsSection.Pointer != IntPtr.Zero)
        {
            UnityEngine.Object.Destroy(state.MapsSection);
        }
    }

    public static bool TryOverrideMatchMode(ref GameModeType gameModeType)
    {
        if (!_activeMode.HasValue)
        {
            return false;
        }

        gameModeType = _activeMode.Value;
        return true;
    }

    public static void ApplyActiveMode(GameState gameState)
    {
        if (_activeMode.HasValue && gameState.GameMode != _activeMode.Value)
        {
            var staleMode = gameState.GameMode;
            gameState.GameMode = _activeMode.Value;
            if (!_activeModeCorrectionLogged)
            {
                _activeModeCorrectionLogged = true;
                _logger?.LogInfo(
                    $"Corrected match-scene GameState mode {staleMode} -> {_activeMode.Value}");
            }
        }
    }

    public static void ApplyActiveModeFromPlayer(SpookedNetworkPlayer player)
    {
        var gameState = player._gameState;
        if (gameState is not null && gameState.Pointer != IntPtr.Zero)
        {
            ApplyActiveMode(gameState);
        }
    }

    public static bool TryRedirectBerekSelection(MatchStateMachine stateMachine)
    {
        if (_activeMode != GameModeType.Berek
            || stateMachine.BerekSelectionState is null
            || stateMachine.BerekSelectionState.Pointer == IntPtr.Zero)
        {
            return false;
        }

        ApplyActiveMode(stateMachine._gameState);
        WireAllBerekComponents();
        stateMachine.EnqueueSwitchState(stateMachine.BerekSelectionState);
        if (!_berekSelectionRedirectLogged)
        {
            _berekSelectionRedirectLogged = true;
            _logger?.LogInfo("Redirected BeforeSelectionState to BerekSelectionState");
        }

        return true;
    }

    public static bool TryStartBerekMode(
        GameStartController gameStartController,
        CharacterType seekerCharacterType)
    {
        if (_activeMode != GameModeType.Berek)
        {
            return false;
        }

        ApplyActiveMode(gameStartController._gameState);
        WireAllBerekComponents();
        gameStartController.StartCoroutine(
            gameStartController.HandleBerekModeStart(seekerCharacterType));
        if (!_berekStartRedirectLogged)
        {
            _berekStartRedirectLogged = true;
            _logger?.LogInfo("Redirected PrepareVictims to HandleBerekModeStart");
        }

        return true;
    }

    public static void ApplyActiveModeToSessionProperties(Fusion.StartGameArgs args)
    {
        if (!_activeMode.HasValue || args.SessionProperties is null)
        {
            return;
        }

        // This is the final authoritative value consumed by Photon. The client recreates its
        // GameState during the lobby-to-match transition, so changing only that earlier object is
        // insufficient for Crown sessions.
        const string key = "game_mode";
        args.SessionProperties[key] = _activeMode.Value.ToString();
    }

    public static void ApplyActiveModeForMatchTick(GameState gameState)
    {
        ApplyActiveMode(gameState);
        if (!_activeMatchTickConfirmed
            && _activeMode.HasValue
            && gameState.GameMode == _activeMode.Value)
        {
            _activeMatchTickConfirmed = true;
            _logger?.LogInfo($"Match state machine confirmed active mode {_activeMode.Value}");
        }
    }

    public static void WireAllBerekComponents()
    {
        if (_activeMode != GameModeType.Berek)
        {
            return;
        }

        foreach (var player in Resources.FindObjectsOfTypeAll<SpookedNetworkPlayer>())
        {
            WirePlayerBerekComponent(player);
        }
    }

    public static void WirePlayerBerekComponent(SpookedNetworkPlayer player)
    {
        var berekComponent = player.EntityBerekComponent ?? player.GetComponent<EntityBerekComponent>();
        if (berekComponent is null)
        {
            return;
        }

        player.EntityBerekComponent = berekComponent;
        if (berekComponent._spookedNetworkPlayer is null)
        {
            berekComponent._spookedNetworkPlayer = player;
        }
    }

    public static bool TryOverrideRandomScene(
        Il2CppStructArray<SceneType> mapsToPlayOn,
        GameModeType gameModeType,
        ref SceneType sceneType)
    {
        if (!_activeMode.HasValue || _activeMapSelection is null)
        {
            return false;
        }

        var selectedPool = _activeMapSelection
            .GetSelectedMaps(_activeMode.Value)
            .ToArray();
        if (selectedPool.Length == 0)
        {
            return false;
        }

        sceneType = selectedPool[UnityEngine.Random.Range(0, selectedPool.Length)];
        _logger?.LogInfo($"Portal selected map {sceneType} for {_activeMode.Value}");
        return true;
    }

    private static void PublishRequestedGameMode(GameModeType selectedMode)
    {
        try
        {
            GameEventsManager.Publish<RequestChangeGameModeEvent>(null, new RequestChangeGameModeEvent(selectedMode));
        }
        catch (Exception exception)
        {
            LogError("Portal selector failed to publish RequestChangeGameModeEvent", exception);
        }
    }

    private static string FormatMapName(SceneType sceneType)
    {
        return sceneType switch
        {
            SceneType.Map01 => "Map 1",
            SceneType.Map02 => "Map 2",
            SceneType.Map03 => "Map 3",
            SceneType.Map04 => "Map 4",
            SceneType.Map_East01 => "East 1",
            SceneType.Map_East02 => "East 2",
            SceneType.Map_School01 => "School 1",
            SceneType.Map_School02 => "School 2",
            SceneType.Map05_TagGame => "Crown",
            _ => sceneType.ToString().Replace('_', ' ')
        };
    }

    private static int GetMapDisplayOrder(SceneType sceneType)
    {
        return sceneType switch
        {
            SceneType.Map01 => 0,
            SceneType.Map02 => 1,
            SceneType.Map03 => 2,
            SceneType.Map04 => 3,
            SceneType.Map_East01 => 4,
            SceneType.Map_East02 => 5,
            SceneType.Map_School01 => 6,
            SceneType.Map_School02 => 7,
            SceneType.Map05_TagGame => 0,
            _ => 1000 + (int)sceneType
        };
    }

    public static void LogError(string message, Exception exception)
    {
        _logger?.LogError($"{message}: {exception}");
    }

    private sealed class PortalLifecycleWatcher : MonoBehaviour
    {
        public PortalLifecycleWatcher(IntPtr pointer) : base(pointer)
        {
        }

        public PortalLifecycleWatcher() : base(ClassInjector.DerivedConstructorPointer<PortalLifecycleWatcher>())
        {
            ClassInjector.DerivedConstructorBody(this);
        }

        private void Update()
        {
            WatcherTick();
        }
    }
}
