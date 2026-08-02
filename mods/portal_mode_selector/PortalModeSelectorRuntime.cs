using BepInEx.Logging;
using Events;
using Gameplay.Player.Components;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Kinguinverse.DataUtils.Events;
using Networking;
using Networking.Photon;
using Types;
using UI;
using UI.Views.Lobby;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SneakOut.PortalModeSelector;

internal static class PortalModeSelectorRuntime
{
    private const float ButtonHeight = 38f;
    private const float ButtonGap = 6f;
    private const float ToolbarButtonWidth = 108f;
    private const float MapButtonWidth = 82f;

    private static readonly Dictionary<IntPtr, PortalModeUiState> UiStateByView = new();
    private static readonly PortalMapSelectionState PreferredMapSelection = new();
    private static readonly Color ClassicModeColor = new(0.08627451f, 0.5372549f, 0.654902f, 1f);
    private static readonly Color CrownModeColor = new(0.8117647f, 0.62352943f, 0f, 1f);
    private static readonly Color MapDisabledColor = new(0.16f, 0.18f, 0.22f, 0.95f);

    private static ManualLogSource? _logger;
    private static Harmony? _harmony;
    private static Font? _legacyFont;
    private static GameUIManager? _gameUiManager;
    private static bool _watcherInstalled;
    private static bool _controlsPrepared;
    private static GameModeType _preferredMode = GameModeType.Default;
    private static bool _mapPanelExpanded;
    private static GameModeType? _activeMode;
    private static PortalMapSelectionState? _activeMapSelection;
    private static bool _activeMatchTickConfirmed;

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

        var playSection = view._playButton.transform.parent?.GetComponent<RectTransform>();
        if (playSection is null || playSection.parent is null)
        {
            _logger?.LogWarning("Portal controls skipped: play-button panel was not found");
            return false;
        }

        var rootObject = new GameObject("CodexPortalControls");
        rootObject.transform.SetParent(view._playButton.transform, false);
        var rootRect = rootObject.AddComponent<RectTransform>();
        rootRect.localScale = Vector3.one;

        var modeButton = CreateTextButton(rootObject.transform, "Mode", view._playButton, ToolbarButtonWidth);
        if (modeButton is null)
        {
            UnityEngine.Object.Destroy(rootObject);
            return false;
        }

        var modeClickAction = (UnityAction)(() => ToggleMode(view.Pointer));
        modeButton.Value.Button.onClick.AddListener(modeClickAction);
        var mapsButton = CreateTextButton(rootObject.transform, "Maps", view._playButton, ToolbarButtonWidth);
        if (mapsButton is null)
        {
            modeButton.Value.Button.onClick.RemoveListener(modeClickAction);
            UnityEngine.Object.Destroy(rootObject);
            return false;
        }

        var mapsClickAction = (UnityAction)(() => ToggleMapPanel(view.Pointer));
        mapsButton.Value.Button.onClick.AddListener(mapsClickAction);
        var mapOptions = new List<PortalMapOptionUiState>();
        foreach (var map in PreferredMapSelection.GetAvailableMaps(GameModeType.Default).OrderBy(GetMapDisplayOrder))
        {
            var option = CreateMapOption(view.Pointer, rootObject.transform, view._playButton, map, GameModeType.Default);
            if (option is not null)
            {
                mapOptions.Add(option);
            }
        }

        foreach (var map in PreferredMapSelection.GetAvailableMaps(GameModeType.Berek).OrderBy(GetMapDisplayOrder))
        {
            var option = CreateMapOption(view.Pointer, rootObject.transform, view._playButton, map, GameModeType.Berek);
            if (option is not null)
            {
                mapOptions.Add(option);
            }
        }

        var state = new PortalModeUiState(
            view,
            rootObject,
            modeButton.Value.Button,
            modeButton.Value.Background,
            modeButton.Value.Label,
            modeClickAction,
            mapsButton.Value.Button,
            mapsButton.Value.Background,
            mapsButton.Value.Label,
            mapsClickAction,
            mapOptions.ToArray());
        UiStateByView[view.Pointer] = state;

        LayoutControls(view, state);
        RefreshControls(state);
        return true;
    }

    private static (Button Button, Image Background, Text Label)? CreateTextButton(
        Transform parent,
        string name,
        Button styleSource,
        float width)
    {
        try
        {
            var buttonObject = new GameObject($"CodexPortal{name}Button");
            buttonObject.transform.SetParent(parent, false);
            var rect = buttonObject.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(width, ButtonHeight);

            var background = buttonObject.AddComponent<Image>();
            var button = buttonObject.AddComponent<Button>();
            CopyButtonStyle(styleSource, button, background);

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

            return (button, background, label);
        }
        catch (Exception exception)
        {
            LogError($"Portal control creation failed for {name}", exception);
            return null;
        }
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

    private static void CopyButtonStyle(Button source, Button target, Image targetImage)
    {
        target.transition = source.transition;
        target.colors = source.colors;
        target.spriteState = source.spriteState;
        target.navigation = source.navigation;
        target.targetGraphic = targetImage;

        if (source.targetGraphic is not Image sourceImage)
        {
            targetImage.color = ClassicModeColor;
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

    private static PortalMapOptionUiState? CreateMapOption(
        IntPtr viewPointer,
        Transform parent,
        Button styleSource,
        SceneType sceneType,
        GameModeType gameModeType)
    {
        var buttonParts = CreateTextButton(parent, $"Map_{gameModeType}_{sceneType}", styleSource, MapButtonWidth);
        if (buttonParts is null)
        {
            return null;
        }

        var clickAction = (UnityAction)(() => ToggleMap(viewPointer, sceneType, gameModeType));
        buttonParts.Value.Button.onClick.AddListener(clickAction);
        buttonParts.Value.Label.text = FormatMapName(sceneType);
        buttonParts.Value.Label.fontSize = 13;
        return new PortalMapOptionUiState(
            sceneType,
            gameModeType,
            buttonParts.Value.Button.gameObject,
            buttonParts.Value.Background,
            buttonParts.Value.Label,
            buttonParts.Value.Button,
            clickAction);
    }

    private static void LayoutControls(PortalPlayView view, PortalModeUiState state)
    {
        var playButton = view._playButton;
        var playSection = playButton?.transform.parent?.GetComponent<RectTransform>();
        var playRect = playButton?.GetComponent<RectTransform>();
        var rootRect = state.RootObject.GetComponent<RectTransform>();
        if (playButton is null || playSection is null || playRect is null || rootRect is null)
        {
            return;
        }

        if (state.RootObject.transform.parent != playButton.transform)
        {
            state.RootObject.transform.SetParent(playButton.transform, false);
        }

        // Use PLAY itself as the coordinate system. Its anchors stretch inside an animated parent,
        // so copying those anchors onto siblings stretches the custom buttons across the modal.
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.pivot = new Vector2(0.5f, 0.5f);
        rootRect.localScale = Vector3.one;
        rootRect.localRotation = Quaternion.identity;
        rootRect.anchoredPosition = Vector2.zero;
        rootRect.sizeDelta = Vector2.zero;
        rootRect.SetAsLastSibling();

        var playHeight = playRect.rect.height;
        var toolbarY = playHeight * 0.5f + ButtonHeight * 0.5f + 12f
            + (_mapPanelExpanded ? -8f : 0f);
        var modeRect = state.ModeButton.GetComponent<RectTransform>();
        if (modeRect is not null)
        {
            modeRect.anchorMin = new Vector2(0.5f, 0.5f);
            modeRect.anchorMax = new Vector2(0.5f, 0.5f);
            modeRect.pivot = new Vector2(0.5f, 0.5f);
            modeRect.sizeDelta = new Vector2(ToolbarButtonWidth, ButtonHeight);
            modeRect.anchoredPosition = new Vector2(-114f, toolbarY);
        }

        var mapsRect = state.MapsButton.GetComponent<RectTransform>();
        if (mapsRect is not null)
        {
            mapsRect.anchorMin = new Vector2(0.5f, 0.5f);
            mapsRect.anchorMax = new Vector2(0.5f, 0.5f);
            mapsRect.pivot = new Vector2(0.5f, 0.5f);
            mapsRect.sizeDelta = new Vector2(ToolbarButtonWidth, ButtonHeight);
            mapsRect.anchoredPosition = new Vector2(0f, toolbarY);
        }

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
            rect.sizeDelta = new Vector2(MapButtonWidth, ButtonHeight);
            var column = visibleIndex % 4;
            var row = visibleIndex / 4;
            var rowCount = Mathf.CeilToInt(visibleCount / 4f);
            var visualRow = rowCount - row - 1;
            var itemsInRow = Mathf.Min(4, visibleCount - row * 4);
            rect.anchoredPosition = new Vector2(
                -(itemsInRow - 1) * (MapButtonWidth + ButtonGap) * 0.5f
                    + column * (MapButtonWidth + ButtonGap),
                toolbarY + ButtonHeight + ButtonGap + visualRow * (ButtonHeight + ButtonGap));
            visibleIndex++;
        }
    }

    private static void RefreshControls(PortalModeUiState state)
    {
        var classic = _preferredMode == GameModeType.Default;
        state.ModeLabel.text = classic ? "CLASSIC" : "CROWN";
        state.ModeBackground.color = classic ? ClassicModeColor : CrownModeColor;

        var selectedMaps = PreferredMapSelection.GetSelectedMaps(_preferredMode);
        var availableMaps = PreferredMapSelection.GetAvailableMaps(_preferredMode);
        state.MapsLabel.text = _mapPanelExpanded
            ? "DONE"
            : $"MAPS  {selectedMaps.Count}/{availableMaps.Count}";
        state.MapsBackground.color = _mapPanelExpanded
            ? classic ? ClassicModeColor : CrownModeColor
            : MapDisabledColor;
        var publicGamePanel = state.View is { } portalView && portalView.Pointer != IntPtr.Zero
            ? portalView._privateGameButton?.transform.parent?.gameObject
            : null;
        if (publicGamePanel is not null)
        {
            publicGamePanel.SetActive(!_mapPanelExpanded);
        }
        foreach (var option in state.MapOptions)
        {
            var visible = _mapPanelExpanded && option.GameModeType == _preferredMode;
            option.RootObject.SetActive(visible);
            if (!visible)
            {
                continue;
            }

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

    private static void ToggleMapPanel(IntPtr viewPointer)
    {
        _mapPanelExpanded = !_mapPanelExpanded;
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

        state.ModeButton.onClick.RemoveListener(state.ModeClickAction);
        state.MapsButton.onClick.RemoveListener(state.MapsClickAction);
        foreach (var option in state.MapOptions)
        {
            option.Button.onClick.RemoveListener(option.ClickAction);
        }

        var publicGamePanel = state.View is { } portalView && portalView.Pointer != IntPtr.Zero
            ? portalView._privateGameButton?.transform.parent?.gameObject
            : null;
        if (publicGamePanel is not null)
        {
            publicGamePanel.SetActive(true);
        }

        if (state.RootObject is not null && state.RootObject.Pointer != IntPtr.Zero)
        {
            UnityEngine.Object.Destroy(state.RootObject);
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
        if (_activeMode.HasValue)
        {
            gameState.GameMode = _activeMode.Value;
        }
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
        if (args.SessionProperties.ContainsKey(key))
        {
            args.SessionProperties[key] = _activeMode.Value.ToString();
        }
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

        var availableMaps = mapsToPlayOn.ToHashSet();
        var selectedPool = _activeMapSelection
            .GetSelectedMaps(_activeMode.Value)
            .Where(availableMaps.Contains)
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
