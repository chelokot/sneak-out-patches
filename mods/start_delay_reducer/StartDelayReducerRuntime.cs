using BepInEx.Logging;
using Fusion;
using Gameplay.Match.MatchState;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using TMPro;
using UI.Buttons;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SneakOut.StartDelayReducer;

internal static class StartDelayReducerRuntime
{
    private const float ButtonWidth = 230f;
    private const float ButtonHeight = 58f;
    private const float ButtonBottomMargin = 74f;
    private const float WatchInterval = 0.2f;

    private static ManualLogSource? _logger;
    private static Harmony? _harmony;
    private static StartDelayReducerConfig? _configuration;
    private static MatchStateMachine? _cachedStateMachine;
    private static StartNowUiState? _uiState;
    private static IntPtr _skipRequestedForMachine;
    private static bool _loggedMissingStyleSource;
    private static bool _loggedWatcherUpdate;
    private static bool _loggedStateMachineCapture;
    private static bool _loggedCanvasDiscovery;
    private static string _lastStateDiagnostic = string.Empty;

    public static void Initialize(ManualLogSource logger, StartDelayReducerConfig configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _harmony ??= new Harmony(StartDelayReducerPlugin.PluginGuid);
        _harmony.PatchAll();
        ClassInjector.RegisterTypeInIl2Cpp<StartNowWatcher>();
        var watcherObject = new GameObject("StartNowWatcher");
        UnityEngine.Object.DontDestroyOnLoad(watcherObject);
        watcherObject.hideFlags = HideFlags.HideAndDontSave;
        watcherObject.AddComponent<StartNowWatcher>();
    }

    public static void UpdateAvailableViews()
    {
        if (_configuration is null)
        {
            return;
        }

        MatchStateMachine? stateMachine;
        try
        {
            stateMachine = ResolveStateMachine();
            LogStateTransition(stateMachine);
        }
        catch (Exception exception)
        {
            _cachedStateMachine = null;
            _logger?.LogError($"Start Now state lookup failed: {exception}");
            return;
        }

        var canStartNow = _configuration.EnableMod.Value
            && stateMachine is not null
            && IsHostWaitingToStart(stateMachine);
        if (!canStartNow && (stateMachine is null || !IsWaitingState(stateMachine)))
        {
            _skipRequestedForMachine = IntPtr.Zero;
        }

        UpdateButton(canStartNow);
    }

    private static void UpdateButton(bool canStartNow)
    {
        if (_configuration is null)
        {
            return;
        }

        try
        {
            if (!canStartNow)
            {
                SetButtonVisible(false);
                return;
            }

            var state = EnsureButton();
            if (state is null)
            {
                return;
            }

            LayoutButton(state);
            state.RootObject.SetActive(true);
            state.Button.interactable = _skipRequestedForMachine == IntPtr.Zero;
            state.Label.text = _skipRequestedForMachine == IntPtr.Zero ? "START NOW" : "STARTING...";
        }
        catch (Exception exception)
        {
            _logger?.LogError($"Start Now UI update failed: {exception}");
            _uiState = null;
        }
    }

    public static void ApplyRequestedSkip(MatchStateMachine stateMachine, ref int stateEndTick)
    {
        if (!HasPendingSkip(stateMachine) || !stateMachine.HasStateAuthority)
        {
            return;
        }

        var runner = stateMachine.Runner;
        if (runner is null)
        {
            return;
        }

        var stockEndTick = stateEndTick;
        stateEndTick = runner.Tick.Raw;
        LogInfo($"Skipped queued pre-match phase: stockEndTick={stockEndTick}, startNowTick={stateEndTick}");
    }

    public static void CaptureStateMachine(MatchStateMachine stateMachine)
    {
        if (stateMachine.Pointer != IntPtr.Zero)
        {
            _cachedStateMachine = stateMachine;
            if (!_loggedStateMachineCapture)
            {
                _loggedStateMachineCapture = true;
                LogInfo($"Captured MatchStateMachine from FixedUpdateNetwork: 0x{stateMachine.Pointer:X}");
            }
        }
    }

    private static MatchStateMachine? ResolveStateMachine()
    {
        if (IsAlive(_cachedStateMachine))
        {
            return _cachedStateMachine;
        }

        return null;
    }

    private static bool IsHostWaitingToStart(MatchStateMachine stateMachine)
    {
        return stateMachine.HasStateAuthority
            && stateMachine.Runner is not null
            && IsWaitingState(stateMachine);
    }

    private static bool IsWaitingState(MatchStateMachine stateMachine)
    {
        var current = stateMachine.CurrentState;
        if (current is null || current.Pointer == IntPtr.Zero)
        {
            return false;
        }

        return current.Pointer == stateMachine.BeforeStartState?.Pointer
            || current.Pointer == stateMachine.CountingToStartState?.Pointer;
    }

    private static bool HasPendingSkip(MatchStateMachine stateMachine)
    {
        return _configuration is not null
            && _configuration.EnableMod.Value
            && _skipRequestedForMachine != IntPtr.Zero
            && _skipRequestedForMachine == stateMachine.Pointer;
    }

    private static StartNowUiState? EnsureButton()
    {
        if (_uiState is not null && _uiState.IsAlive)
        {
            return _uiState;
        }

        _uiState = null;

        var canvas = ResolveHudCanvas();
        if (canvas is null)
        {
            return null;
        }

        var styleSource = Resources.FindObjectsOfTypeAll<SpookedOutlineButton>()
            .FirstOrDefault(button =>
                button is not null
                && button.Pointer != IntPtr.Zero
                && button.gameObject.name != "StartNowButton"
                && button._targetColorImage is not null
                && button._targetOutlineImage is not null
                && button.GetComponentInChildren<TMP_Text>(true) is not null);
        if (styleSource is null)
        {
            if (!_loggedMissingStyleSource)
            {
                _loggedMissingStyleSource = true;
                LogInfo("No complete stock outline-button style is loaded yet");
            }
            return null;
        }

        var buttonObject = UnityEngine.Object.Instantiate(styleSource.gameObject, canvas.transform, false);
        buttonObject.name = "StartNowButton";
        buttonObject.SetActive(false);

        var button = buttonObject.GetComponent<SpookedOutlineButton>();
        var buttonRect = buttonObject.GetComponent<RectTransform>();
        var label = buttonObject.GetComponentInChildren<TMP_Text>(true);
        if (button is null || buttonRect is null || label is null)
        {
            UnityEngine.Object.Destroy(buttonObject);
            return null;
        }

        button.onClick = new Button.ButtonClickedEvent();
        var clickAction = (UnityAction)StartNow;
        button.onClick.AddListener(clickAction);

        label.text = "START NOW";
        label.fontSize = 18f;
        label.fontSizeMin = 13f;
        label.fontSizeMax = 18f;
        label.enableAutoSizing = true;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.raycastTarget = false;
        FitStockButtonLayers(buttonRect, button, label);
        buttonRect.SetAsLastSibling();

        _uiState = new StartNowUiState(buttonObject, buttonRect, button, label, clickAction);
        LayoutButton(_uiState);
        LogInfo($"Created host-only Start Now button on HUD canvas '{canvas.gameObject.name}' from the stock outline-button style");
        return _uiState;
    }

    private static Canvas? ResolveHudCanvas()
    {
        var canvases = Resources.FindObjectsOfTypeAll<Canvas>()
            .Where(canvas =>
                canvas is not null
                && canvas.Pointer != IntPtr.Zero
                && canvas.gameObject.activeInHierarchy
                && canvas.renderMode != RenderMode.WorldSpace
                && canvas.GetComponent<RectTransform>() is not null)
            .OrderByDescending(canvas => canvas.sortingOrder)
            .ThenByDescending(canvas => canvas.transform.childCount)
            .ToArray();

        if (!_loggedCanvasDiscovery)
        {
            _loggedCanvasDiscovery = true;
            LogInfo($"Active screen-space canvases: {string.Join("; ", canvases.Select(canvas => $"{canvas.gameObject.name}/order={canvas.sortingOrder}/children={canvas.transform.childCount}"))}");
        }

        return canvases.FirstOrDefault();
    }

    private static void LayoutButton(StartNowUiState state)
    {
        var buttonRect = state.RectTransform;
        if (!state.IsAlive)
        {
            return;
        }

        buttonRect.anchorMin = new Vector2(0.5f, 0f);
        buttonRect.anchorMax = new Vector2(0.5f, 0f);
        buttonRect.pivot = new Vector2(0.5f, 0f);
        buttonRect.localScale = Vector3.one;
        buttonRect.localRotation = Quaternion.identity;
        buttonRect.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);
        buttonRect.anchoredPosition = new Vector2(0f, ButtonBottomMargin);
        buttonRect.SetAsLastSibling();
    }

    private static void StartNow()
    {
        try
        {
            var stateMachine = ResolveStateMachine();
            if (stateMachine is null || !IsHostWaitingToStart(stateMachine))
            {
                _logger?.LogWarning("Start Now ignored: this client is not the authoritative host in a pre-match waiting phase");
                return;
            }

            var runner = stateMachine.Runner;
            if (runner is null)
            {
                return;
            }

            _skipRequestedForMachine = stateMachine.Pointer;
            var stockEndTick = stateMachine.StateEndTick;
            stateMachine.StateEndTick = runner.Tick.Raw;
            LogInfo($"Start Now pressed: stockEndTick={stockEndTick}, startNowTick={stateMachine.StateEndTick}");
        }
        catch (Exception exception)
        {
            _logger?.LogError($"Start Now click failed: {exception}");
            _skipRequestedForMachine = IntPtr.Zero;
        }
    }

    private static void SetButtonVisible(bool visible)
    {
        if (_uiState is null)
        {
            return;
        }

        if (!_uiState.IsAlive)
        {
            _uiState = null;
            return;
        }

        try
        {
            _uiState.RootObject.SetActive(visible);
        }
        catch
        {
            _uiState = null;
        }
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
            current.pivot = new Vector2(0.5f, 0.5f);
            current.offsetMin = Vector2.zero;
            current.offsetMax = Vector2.zero;
            current.anchoredPosition = Vector2.zero;
            current.localScale = Vector3.one;
            current = current.parent?.GetComponent<RectTransform>();
        }
    }

    private static bool IsAlive(MatchStateMachine? stateMachine)
    {
        return stateMachine is not null && stateMachine.Pointer != IntPtr.Zero;
    }

    private static void LogInfo(string message)
    {
        if (_configuration?.EnableLogging.Value == true)
        {
            _logger?.LogInfo(message);
        }
    }

    private static void LogStateTransition(MatchStateMachine? stateMachine)
    {
        if (_configuration?.EnableLogging.Value != true)
        {
            return;
        }

        if (stateMachine is null)
        {
            return;
        }

        var currentPointer = stateMachine.CurrentState?.Pointer ?? IntPtr.Zero;
        var runnerTick = stateMachine.Runner?.Tick.Raw ?? -1;
        var signature = $"{stateMachine.Pointer:X}:{currentPointer:X}:{stateMachine.HasStateAuthority}:{stateMachine.StateEndTick}";
        if (signature == _lastStateDiagnostic)
        {
            return;
        }

        _lastStateDiagnostic = signature;
        var diagnostic = $"machine=0x{stateMachine.Pointer:X}, current=0x{currentPointer:X}, "
            + $"beforeStart=0x{stateMachine.BeforeStartState?.Pointer ?? IntPtr.Zero:X}, "
            + $"counting=0x{stateMachine.CountingToStartState?.Pointer ?? IntPtr.Zero:X}, "
            + $"authority={stateMachine.HasStateAuthority}, tick={runnerTick}, endTick={stateMachine.StateEndTick}";
        _logger?.LogInfo($"Observed match state: {diagnostic}");
    }

    public sealed class StartNowWatcher : MonoBehaviour
    {
        private float _nextUpdateAt;

        public StartNowWatcher(IntPtr pointer) : base(pointer)
        {
        }

        public StartNowWatcher() : base(ClassInjector.DerivedConstructorPointer<StartNowWatcher>())
        {
            ClassInjector.DerivedConstructorBody(this);
        }

        private void Update()
        {
            if (!_loggedWatcherUpdate)
            {
                _loggedWatcherUpdate = true;
                LogInfo("Start Now watcher is updating");
            }

            if (Time.unscaledTime < _nextUpdateAt)
            {
                return;
            }

            _nextUpdateAt = Time.unscaledTime + WatchInterval;
            UpdateAvailableViews();
        }
    }

    private sealed class StartNowUiState
    {
        public StartNowUiState(
            GameObject rootObject,
            RectTransform rectTransform,
            SpookedOutlineButton button,
            TMP_Text label,
            UnityAction clickAction)
        {
            RootObject = rootObject;
            RectTransform = rectTransform;
            Button = button;
            Label = label;
            ClickAction = clickAction;
        }

        public GameObject RootObject { get; }

        public RectTransform RectTransform { get; }

        public SpookedOutlineButton Button { get; }

        public TMP_Text Label { get; }

        public UnityAction ClickAction { get; }

        public bool IsAlive
        {
            get
            {
                try
                {
                    return RootObject && RectTransform && Button;
                }
                catch
                {
                    return false;
                }
            }
        }
    }
}
