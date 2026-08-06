using BepInEx;
using BepInEx.Logging;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using TMPro;
using UI.Buttons;
using UI.InputBinding;
using UI.VideoSettings;
using UI.Views;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SneakOut.Minimap;

internal static class MinimapSettingsUi
{
    private static MinimapConfig? _configuration;
    private static ManualLogSource? _logger;
    private static GameMenuView? _view;
    private static GameObject? _buttonContainer;
    private static Button? _mapButton;
    private static UnityAction? _mapClickAction;
    private static GameObject? _mapPanel;
    private static ToggleSettingsRow? _enabledRow;
    private static ToggleSettingsRow? _startVisibleRow;
    private static ToggleSettingsRow? _showWhileHoldingRow;
    private static DropdownSettingsRow? _shapeRow;
    private static SliderSettingsRow? _sizeRow;
    private static SliderSettingsRow? _zoomRow;
    private static SliderSettingsRow? _topMarginRow;
    private static SliderSettingsRow? _rightMarginRow;
    private static BindingSettingsRow? _bindingRow;
    private static UnityAction? _bindingClickAction;
    private static UnityAction? _bindingResetAction;
    private static TMP_Text? _header;
    private static Sprite? _mapIconSprite;
    private static bool _updating;
    private static bool _recordingBinding;
    private static float _recordingReadyAt;
    private static bool _registeredInCategoryList;
    private static float _diagnosticOpenAt = -1f;
    private static float _diagnosticDropdownOpenAt = -1f;
    private static float _diagnosticCaptureAt = -1f;

    public static void Initialize(MinimapConfig configuration, ManualLogSource logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public static void Attach(GameMenuView view)
    {
        if (_configuration is null
            || view is null
            || view.Pointer == IntPtr.Zero
            || view._categoryButtonPanel is null
            || view._interfaceButton is null
            || view._interfacePanel is null
            || view._audioPanel is null
            || view._musicSlider is null)
        {
            return;
        }

        try
        {
            ClearReferences();
            _view = view;
            DestroyExistingInjection(view);
            CreateCategoryButton(view);
            CreateMapPanel(view);
            Refresh(forceSliderValues: true);
            if (_configuration.CaptureSettingsScreenshot.Value)
            {
                _diagnosticOpenAt = Time.unscaledTime + 15f;
            }
            _logger?.LogInfo("Added minimap controls to the in-game Map settings tab");
        }
        catch (Exception exception)
        {
            _logger?.LogWarning($"Could not add minimap settings controls: {exception}");
            ClearReferences();
        }
    }

    public static void Tick()
    {
        if (_configuration is null || !UiIsAvailable())
        {
            return;
        }

        try
        {
            EnsureCategoryRegistration();
            RefreshCategoryHover();
            TickVisualDiagnostic();
            if (_mapPanel?.activeInHierarchy != true)
            {
                return;
            }

            if (!_updating)
            {
                TickBindingRecording();
                ApplyChangedValues();
            }
            Refresh(forceSliderValues: false);
        }
        catch
        {
            // GameMenuView and every injected child are scene-owned. A scene transition may
            // invalidate them between updates; the next OnAwake attaches a fresh tab.
            ClearReferences();
        }
    }

    public static void OnStockPanelsDeactivated(GameMenuView view)
    {
        if (_view is null || _view.Pointer != view.Pointer)
        {
            return;
        }

        if (_mapPanel is not null && _mapPanel.activeSelf)
        {
            _mapPanel.SetActive(false);
        }
        CancelBindingRecording();
        SetCategorySelected(selected: false);
    }

    public static GameObject? GetPanelForButton(GameMenuView view, Button button)
    {
        return _view is not null
            && _view.Pointer == view.Pointer
            && _mapButton is not null
            && _mapPanel is not null
            && button is not null
            && button.Pointer == _mapButton.Pointer
                ? _mapPanel
                : null;
    }

    private static void CreateCategoryButton(GameMenuView view)
    {
        var templateContainer = view._interfaceButton.transform.parent?.gameObject
            ?? throw new InvalidOperationException("Interface category container is unavailable");
        _buttonContainer = UnityEngine.Object.Instantiate(
            templateContainer,
            view._categoryButtonPanel,
            false);
        _buttonContainer.name = "Map";
        _buttonContainer.SetActive(true);
        // Keep Map in the first visible category group. The stock menu only exposes five
        // category slots at once, while Interface and Controls remain reachable to its right.
        _buttonContainer.transform.SetSiblingIndex(templateContainer.transform.GetSiblingIndex());

        _mapButton = _buttonContainer.GetComponentInChildren<Button>(true)
            ?? throw new InvalidOperationException("Cloned Map category has no button");
        _mapButton.name = "MapButton";
        _mapButton.onClick = new Button.ButtonClickedEvent();
        _mapClickAction = (UnityAction)OpenMapPanel;
        _mapButton.onClick.AddListener(_mapClickAction);

        var colorImage = _mapButton.transform.Find("Image")?.GetComponent<Image>();
        var outlineImage = _mapButton.transform.Find("Outline")?.GetComponent<Image>();
        var outlineButton = _mapButton.GetComponent<SpookedOutlineButton>();
        var icon = GetOrCreateMapIconSprite();
        if (colorImage is not null)
        {
            colorImage.sprite = icon;
            colorImage.preserveAspect = true;
        }
        if (outlineImage is not null)
        {
            outlineImage.enabled = false;
        }
        if (outlineButton is not null)
        {
            outlineButton._sizeUp = false;
            outlineButton._fadeOutline = false;
        }
        SetCategorySelected(selected: false);
    }

    private static void CreateMapPanel(GameMenuView view)
    {
        var panelsParent = view._audioPanel.transform.parent
            ?? throw new InvalidOperationException("Settings panels parent is unavailable");
        _mapPanel = UnityEngine.Object.Instantiate(view._audioPanel, panelsParent, false);
        _mapPanel.name = "Map";
        _mapPanel.transform.SetAsLastSibling();
        _mapPanel.SetActive(false);

        for (var childIndex = _mapPanel.transform.childCount - 1; childIndex >= 0; childIndex--)
        {
            var child = _mapPanel.transform.GetChild(childIndex);
            if (child.name == "TextBackground")
            {
                _header = child.GetComponentInChildren<TMP_Text>(true);
                continue;
            }
            UnityEngine.Object.Destroy(child.gameObject);
        }

        var toggleTemplate = view._videoPanel?.transform.Find("VsyncPanel")?.gameObject
            ?? throw new InvalidOperationException("Stock settings toggle row is unavailable");
        var sliderTemplate = view._musicSlider.transform.parent?.gameObject
            ?? throw new InvalidOperationException("Stock settings slider row is unavailable");
        var dropdownTemplate = view._videoPanel?.transform.Find("ScreenModePanel")?.gameObject
            ?? throw new InvalidOperationException("Stock settings dropdown row is unavailable");
        var stockBinding = view._controlsPanel?.GetComponentsInChildren<BindingUI>(true).FirstOrDefault()
            ?? throw new InvalidOperationException("Stock key-binding row is unavailable");
        var bindingTemplate = stockBinding.transform.parent?.gameObject
            ?? throw new InvalidOperationException("Stock key-binding panel is unavailable");

        _enabledRow = CreateToggleRow(toggleTemplate, _mapPanel.transform, "MinimapEnabledPanel");
        _startVisibleRow = CreateToggleRow(toggleTemplate, _mapPanel.transform, "MinimapStartVisiblePanel");
        _showWhileHoldingRow = CreateToggleRow(toggleTemplate, _mapPanel.transform, "MinimapShowWhileHoldingPanel");
        _shapeRow = CreateDropdownRow(dropdownTemplate, _mapPanel.transform, "MinimapShapePanel");
        _sizeRow = CreateSliderRow(sliderTemplate, _mapPanel.transform, "MinimapSizePanel");
        _zoomRow = CreateSliderRow(sliderTemplate, _mapPanel.transform, "MinimapZoomPanel");
        _topMarginRow = CreateSliderRow(sliderTemplate, _mapPanel.transform, "MinimapTopMarginPanel");
        _rightMarginRow = CreateSliderRow(sliderTemplate, _mapPanel.transform, "MinimapRightMarginPanel");
        _bindingRow = CreateBindingRow(bindingTemplate, _mapPanel.transform);

        ConfigureSlider(_sizeRow.Slider, 140f, 500f);
        ConfigureSlider(_zoomRow.Slider, 0f, 100f);
        ConfigureSlider(_topMarginRow.Slider, 0f, 300f);
        ConfigureSlider(_rightMarginRow.Slider, 0f, 300f);
    }

    private static ToggleSettingsRow CreateToggleRow(GameObject template, Transform parent, string name)
    {
        var root = UnityEngine.Object.Instantiate(template, parent, false);
        root.name = name;
        root.transform.SetAsLastSibling();
        var toggle = root.GetComponentInChildren<Toggle>(true)
            ?? throw new InvalidOperationException($"{name} has no stock toggle");
        toggle.onValueChanged = new Toggle.ToggleEvent();
        var title = root.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault()
            ?? throw new InvalidOperationException($"{name} has no title label");
        return new ToggleSettingsRow(root, toggle, title);
    }

    private static SliderSettingsRow CreateSliderRow(GameObject template, Transform parent, string name)
    {
        var root = UnityEngine.Object.Instantiate(template, parent, false);
        root.name = name;
        root.transform.SetAsLastSibling();
        var slider = root.GetComponentInChildren<Slider>(true)
            ?? throw new InvalidOperationException($"{name} has no stock slider");
        slider.onValueChanged = new Slider.SliderEvent();
        var labels = root.GetComponentsInChildren<TMP_Text>(true);
        var title = labels.FirstOrDefault(label =>
                label.name.Contains("MusicText", StringComparison.OrdinalIgnoreCase))
            ?? labels.FirstOrDefault()
            ?? throw new InvalidOperationException($"{name} has no title label");
        var value = labels.FirstOrDefault(label => label.Pointer != title.Pointer)
            ?? throw new InvalidOperationException($"{name} has no value label");
        return new SliderSettingsRow(root, slider, title, value);
    }

    private static DropdownSettingsRow CreateDropdownRow(GameObject template, Transform parent, string name)
    {
        var root = UnityEngine.Object.Instantiate(template, parent, false);
        root.name = name;
        root.transform.SetAsLastSibling();
        var dropdown = root.GetComponentInChildren<TMP_Dropdown>(true)
            ?? throw new InvalidOperationException($"{name} has no stock dropdown");
        var screenModeSelector = root.GetComponentInChildren<ScreenModeSelector>(true);
        if (screenModeSelector is not null)
        {
            screenModeSelector.enabled = false;
            UnityEngine.Object.Destroy(screenModeSelector);
        }
        var screenModeView = root.GetComponentInChildren<ScreenModeDropdownView>(true);
        if (screenModeView is not null)
        {
            screenModeView.enabled = false;
            UnityEngine.Object.Destroy(screenModeView);
        }
        dropdown.onValueChanged = new TMP_Dropdown.DropdownEvent();
        EnsureShapeOptions(dropdown);
        dropdown.interactable = true;
        dropdown.RefreshShownValue();

        var labels = root.GetComponentsInChildren<TMP_Text>(true);
        var title = labels.FirstOrDefault(label =>
                dropdown.captionText is null || label.Pointer != dropdown.captionText.Pointer)
            ?? throw new InvalidOperationException($"{name} has no title label");
        return new DropdownSettingsRow(root, dropdown, title);
    }

    private static BindingSettingsRow CreateBindingRow(GameObject template, Transform parent)
    {
        var root = UnityEngine.Object.Instantiate(template, parent, false);
        root.name = "MinimapToggleBindingPanel";
        root.transform.SetAsLastSibling();

        var bindingUi = root.GetComponentInChildren<BindingUI>(true)
            ?? throw new InvalidOperationException("Cloned key-binding panel has no BindingUI");
        bindingUi.enabled = false;
        var title = root.transform.Find("RebindUIPrefab/ActionNameText")?.GetComponent<TMP_Text>()
            ?? throw new InvalidOperationException("Cloned key-binding panel has no action label");
        var trigger = root.transform.Find("RebindUIPrefab/TriggerRebindButton")?.GetComponent<Button>()
            ?? throw new InvalidOperationException("Cloned key-binding panel has no record button");
        var value = trigger.GetComponentInChildren<TMP_Text>(true)
            ?? throw new InvalidOperationException("Cloned key-binding panel has no binding label");
        var reset = root.transform.Find("RebindUIPrefab/ResetToDefaultButton")?.GetComponent<Button>()
            ?? throw new InvalidOperationException("Cloned key-binding panel has no reset button");
        var resetLabel = reset.GetComponentInChildren<TMP_Text>(true)
            ?? throw new InvalidOperationException("Cloned key-binding panel has no reset label");

        trigger.onClick = new Button.ButtonClickedEvent();
        reset.onClick = new Button.ButtonClickedEvent();
        _bindingClickAction = (UnityAction)BeginBindingRecording;
        _bindingResetAction = (UnityAction)ResetBinding;
        trigger.onClick.AddListener(_bindingClickAction);
        reset.onClick.AddListener(_bindingResetAction);
        return new BindingSettingsRow(root, trigger, reset, title, value, resetLabel);
    }

    private static void ConfigureSlider(Slider slider, float minimum, float maximum)
    {
        slider.minValue = minimum;
        slider.maxValue = maximum;
        slider.wholeNumbers = true;
        slider.interactable = true;
    }

    private static void OpenMapPanel()
    {
        if (_view is null || _mapPanel is null || _mapButton is null)
        {
            return;
        }

        // Let the stock method update scrolling, gamepad state, and the previous category, then
        // replace only the selected panel/button with the injected Map category.
        _view.OnInterfaceButton();
        _view._interfacePanel?.SetActive(false);
        _mapPanel.SetActive(true);
        _view._currentSelectedButton = _mapButton;
        _view._currentPanelRectTransform = _mapPanel.GetComponent<RectTransform>();
        SetButtonSelected(_view._interfaceButton, selected: false);
        SetCategorySelected(selected: true);
        Refresh(forceSliderValues: true);
    }

    private static void ApplyChangedValues()
    {
        var configuration = _configuration!;
        var enabled = _enabledRow!.Toggle.isOn;
        var startVisible = _startVisibleRow!.Toggle.isOn;
        var showWhileHolding = _showWhileHoldingRow!.Toggle.isOn;
        var shape = _shapeRow!.Dropdown.value == 1
            ? MinimapShape.Rectangle
            : MinimapShape.Circle;
        var mapSize = Mathf.Clamp(Mathf.RoundToInt(_sizeRow!.Slider.value), 140, 500);
        var zoom = Mathf.Clamp(Mathf.RoundToInt(_zoomRow!.Slider.value), 0, 100);
        var topMargin = Mathf.Clamp(Mathf.RoundToInt(_topMarginRow!.Slider.value), 0, 300);
        var rightMargin = Mathf.Clamp(Mathf.RoundToInt(_rightMarginRow!.Slider.value), 0, 300);

        var visibilityChanged = configuration.StartVisible.Value != startVisible;
        var inputModeChanged = configuration.ShowWhileHolding.Value != showWhileHolding;
        var presentationChanged = configuration.MapShape.Value != shape
            || configuration.MapSize.Value != mapSize
            || configuration.Zoom.Value != zoom
            || configuration.TopMargin.Value != topMargin
            || configuration.RightMargin.Value != rightMargin;
        if (configuration.EnableMod.Value != enabled)
        {
            configuration.EnableMod.Value = enabled;
        }
        if (visibilityChanged)
        {
            configuration.StartVisible.Value = startVisible;
        }
        if (inputModeChanged)
        {
            configuration.ShowWhileHolding.Value = showWhileHolding;
        }
        if (configuration.MapShape.Value != shape)
        {
            configuration.MapShape.Value = shape;
        }
        if (configuration.MapSize.Value != mapSize)
        {
            configuration.MapSize.Value = mapSize;
        }
        if (configuration.Zoom.Value != zoom)
        {
            configuration.Zoom.Value = zoom;
        }
        if (configuration.TopMargin.Value != topMargin)
        {
            configuration.TopMargin.Value = topMargin;
        }
        if (configuration.RightMargin.Value != rightMargin)
        {
            configuration.RightMargin.Value = rightMargin;
        }
        if (visibilityChanged || presentationChanged || inputModeChanged)
        {
            MinimapRuntime.ApplyConfiguration(
                toggleBindingChanged: false,
                visibilityChanged,
                inputModeChanged);
        }
    }

    private static void Refresh(bool forceSliderValues)
    {
        if (_configuration is null || !RowsAreAvailable())
        {
            return;
        }

        _updating = true;
        try
        {
            SetTextIfChanged(_header, "MAP");
            RefreshToggle(_enabledRow!, _configuration.EnableMod.Value);
            RefreshToggle(_startVisibleRow!, _configuration.StartVisible.Value);
            RefreshToggle(_showWhileHoldingRow!, _configuration.ShowWhileHolding.Value);
            RefreshDropdown(
                _shapeRow!,
                _configuration.MapShape.Value == MinimapShape.Rectangle ? 1 : 0);
            RefreshSlider(_sizeRow!, Mathf.Clamp(_configuration.MapSize.Value, 140, 500), forceSliderValues);
            RefreshSlider(_zoomRow!, Mathf.Clamp(_configuration.Zoom.Value, 0, 100), forceSliderValues);
            RefreshSlider(_topMarginRow!, Mathf.Clamp(_configuration.TopMargin.Value, 0, 300), forceSliderValues);
            RefreshSlider(_rightMarginRow!, Mathf.Clamp(_configuration.RightMargin.Value, 0, 300), forceSliderValues);

            SetTextIfChanged(_enabledRow!.Title, "Minimap");
            SetTextIfChanged(_startVisibleRow!.Title, "Start visible");
            SetTextIfChanged(_showWhileHoldingRow!.Title, "Show while holding");
            SetTextIfChanged(_shapeRow!.Title, "Map shape");
            SetTextIfChanged(_sizeRow!.Title, "Map size");
            SetTextIfChanged(_sizeRow.Value, $"{Mathf.Clamp(_configuration.MapSize.Value, 140, 500)} px");
            SetTextIfChanged(_zoomRow!.Title, "Zoom");
            SetTextIfChanged(
                _zoomRow.Value,
                _configuration.Zoom.Value <= 0
                    ? "Full map"
                    : $"{Mathf.Clamp(_configuration.Zoom.Value, 0, 100)}%");
            SetTextIfChanged(_topMarginRow!.Title, "Top margin");
            SetTextIfChanged(_topMarginRow.Value, $"{Mathf.Clamp(_configuration.TopMargin.Value, 0, 300)} px");
            SetTextIfChanged(_rightMarginRow!.Title, "Right margin");
            SetTextIfChanged(_rightMarginRow.Value, $"{Mathf.Clamp(_configuration.RightMargin.Value, 0, 300)} px");
            SetTextIfChanged(_bindingRow!.Title, "Minimap key");
            SetTextIfChanged(_bindingRow.Value, _recordingBinding
                ? "Press a key (Esc cancels)"
                : GetBindingDisplayName(_configuration.ToggleBinding.Value));
            SetTextIfChanged(_bindingRow.ResetLabel, "Reset");
        }
        finally
        {
            _updating = false;
        }
    }

    private static void RefreshToggle(ToggleSettingsRow row, bool value)
    {
        if (row.Toggle.isOn != value)
        {
            row.Toggle.SetIsOnWithoutNotify(value);
        }
    }

    private static void RefreshSlider(SliderSettingsRow row, float value, bool force)
    {
        if (force || !Mathf.Approximately(row.Slider.value, value))
        {
            row.Slider.SetValueWithoutNotify(value);
        }
    }

    private static void RefreshDropdown(DropdownSettingsRow row, int value)
    {
        EnsureShapeOptions(row.Dropdown);
        if (row.Dropdown.value != value)
        {
            row.Dropdown.SetValueWithoutNotify(value);
            row.Dropdown.RefreshShownValue();
        }
        SetTextIfChanged(row.Dropdown.captionText, value == 1 ? "Rectangle" : "Circle");
    }

    private static void EnsureShapeOptions(TMP_Dropdown dropdown)
    {
        if (dropdown.options.Count == 2
            && string.Equals(dropdown.options[0].text, "Circle", StringComparison.Ordinal)
            && string.Equals(dropdown.options[1].text, "Rectangle", StringComparison.Ordinal))
        {
            return;
        }

        dropdown.ClearOptions();
        var options = new Il2CppSystem.Collections.Generic.List<TMP_Dropdown.OptionData>();
        options.Add(new TMP_Dropdown.OptionData("Circle"));
        options.Add(new TMP_Dropdown.OptionData("Rectangle"));
        dropdown.AddOptions(options);
        dropdown.RefreshShownValue();
    }

    private static void BeginBindingRecording()
    {
        if (_bindingRow is null || _mapPanel?.activeInHierarchy != true)
        {
            return;
        }

        _recordingBinding = true;
        // Do not record the keyboard/gamepad submit event that activated the button.
        _recordingReadyAt = Time.unscaledTime + 0.2f;
        Refresh(forceSliderValues: false);
    }

    private static void TickBindingRecording()
    {
        if (!_recordingBinding)
        {
            return;
        }
        if (_mapPanel?.activeInHierarchy != true)
        {
            CancelBindingRecording();
            return;
        }
        if (Time.unscaledTime < _recordingReadyAt)
        {
            return;
        }

        var keyboard = Keyboard.current;
        if (keyboard is null)
        {
            return;
        }
        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            CancelBindingRecording();
            return;
        }

        var keys = keyboard.allKeys;
        for (var index = 0; index < keys.Count; index++)
        {
            var key = keys[index];
            if (key is null || !key.wasPressedThisFrame || key.Pointer == keyboard.escapeKey.Pointer)
            {
                continue;
            }

            SetBinding($"<Keyboard>/{key.name}");
            return;
        }
    }

    private static void ResetBinding()
    {
        SetBinding("<Keyboard>/tab");
    }

    private static void SetBinding(string binding)
    {
        CancelBindingRecording();
        if (_configuration is null
            || string.Equals(_configuration.ToggleBinding.Value, binding, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _configuration.ToggleBinding.Value = binding;
        MinimapRuntime.ApplyConfiguration(
            toggleBindingChanged: true,
            visibilityChanged: false,
            inputModeChanged: false);
        _logger?.LogInfo($"Minimap key binding changed to {binding}");
    }

    private static void CancelBindingRecording()
    {
        _recordingBinding = false;
        _recordingReadyAt = 0f;
    }

    private static string GetBindingDisplayName(string binding)
    {
        var slashIndex = binding.LastIndexOf('/');
        var controlName = slashIndex >= 0 && slashIndex + 1 < binding.Length
            ? binding[(slashIndex + 1)..]
            : binding;
        var keyboard = Keyboard.current;
        if (keyboard is not null)
        {
            var keys = keyboard.allKeys;
            for (var index = 0; index < keys.Count; index++)
            {
                var key = keys[index];
                if (key is not null && string.Equals(key.name, controlName, StringComparison.OrdinalIgnoreCase))
                {
                    return key.displayName;
                }
            }
        }

        return controlName switch
        {
            "capsLock" => "Caps Lock",
            "leftShift" => "Left Shift",
            "rightShift" => "Right Shift",
            "leftCtrl" => "Left Ctrl",
            "rightCtrl" => "Right Ctrl",
            "leftAlt" => "Left Alt",
            "rightAlt" => "Right Alt",
            _ when controlName.Length == 1 => controlName.ToUpperInvariant(),
            _ => controlName,
        };
    }

    private static void EnsureCategoryRegistration()
    {
        if (_registeredInCategoryList || _view?._activeCategoryButtons is null || _mapButton is null)
        {
            return;
        }

        var buttons = _view._activeCategoryButtons;
        for (var index = 0; index < buttons.Count; index++)
        {
            if (buttons[index]?.Pointer == _mapButton.Pointer)
            {
                _registeredInCategoryList = true;
                return;
            }
        }

        var insertAt = buttons.Count;
        for (var index = 0; index < buttons.Count; index++)
        {
            if (buttons[index]?.Pointer == _view._interfaceButton?.Pointer)
            {
                insertAt = index;
                break;
            }
            if (buttons[index]?.Pointer == _view._gameplayButton?.Pointer)
            {
                insertAt = index + 1;
            }
        }
        buttons.Insert(insertAt, _mapButton);
        _registeredInCategoryList = true;
    }

    private static void SetCategorySelected(bool selected)
    {
        if (_view is not null && _mapButton is not null)
        {
            SetButtonSelected(_mapButton, selected);
        }
    }

    private static void RefreshCategoryHover()
    {
        if (_view is null || _mapButton is null)
        {
            return;
        }

        var outlineButton = _mapButton.GetComponent<SpookedOutlineButton>();
        var selected = _mapPanel?.activeSelf == true;
        var highlighted = outlineButton?._isHiglighted == true;
        var background = _mapButton.GetComponent<Image>();
        if (background is not null)
        {
            background.sprite = selected || highlighted
                ? _view._selectedButtonSprite
                : _view._deselectedButtonSprite;
        }
    }

    private static void SetButtonSelected(Button? button, bool selected)
    {
        if (_view is null || button is null)
        {
            return;
        }
        var background = button.GetComponent<Image>();
        if (background is not null)
        {
            background.sprite = selected ? _view._selectedButtonSprite : _view._deselectedButtonSprite;
        }
    }

    private static Sprite GetOrCreateMapIconSprite()
    {
        if (_mapIconSprite is not null)
        {
            return _mapIconSprite;
        }

        const int size = 48;
        var pixels = new Color32[size * size];
        var color = new Color32(255, 255, 255, 255);
        DrawIconLine(pixels, size, 8, 10, 18, 7, color);
        DrawIconLine(pixels, size, 18, 7, 30, 11, color);
        DrawIconLine(pixels, size, 30, 11, 40, 8, color);
        DrawIconLine(pixels, size, 8, 10, 8, 38, color);
        DrawIconLine(pixels, size, 18, 7, 18, 35, color);
        DrawIconLine(pixels, size, 30, 11, 30, 39, color);
        DrawIconLine(pixels, size, 40, 8, 40, 36, color);
        DrawIconLine(pixels, size, 8, 38, 18, 35, color);
        DrawIconLine(pixels, size, 18, 35, 30, 39, color);
        DrawIconLine(pixels, size, 30, 39, 40, 36, color);
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "MinimapSettingsMapIcon",
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        texture.SetPixels32(ToIl2CppArray(pixels));
        texture.Apply(false, true);
        _mapIconSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            100f);
        _mapIconSprite.name = "MinimapSettingsMapIcon";
        return _mapIconSprite;
    }

    private static void DrawIconLine(
        Color32[] pixels,
        int size,
        int startX,
        int startY,
        int endX,
        int endY,
        Color32 color)
    {
        var deltaX = Mathf.Abs(endX - startX);
        var stepX = startX < endX ? 1 : -1;
        var deltaY = -Mathf.Abs(endY - startY);
        var stepY = startY < endY ? 1 : -1;
        var error = deltaX + deltaY;
        while (true)
        {
            for (var offsetY = -1; offsetY <= 1; offsetY++)
            {
                for (var offsetX = -1; offsetX <= 1; offsetX++)
                {
                    var x = startX + offsetX;
                    var y = startY + offsetY;
                    if (x >= 0 && x < size && y >= 0 && y < size)
                    {
                        pixels[y * size + x] = color;
                    }
                }
            }
            if (startX == endX && startY == endY)
            {
                break;
            }
            var twiceError = error * 2;
            if (twiceError >= deltaY)
            {
                error += deltaY;
                startX += stepX;
            }
            if (twiceError <= deltaX)
            {
                error += deltaX;
                startY += stepY;
            }
        }
    }

    private static Il2CppStructArray<Color32> ToIl2CppArray(IReadOnlyList<Color32> values)
    {
        var result = new Il2CppStructArray<Color32>(values.Count);
        for (var index = 0; index < values.Count; index++)
        {
            result[index] = values[index];
        }
        return result;
    }

    private static void TickVisualDiagnostic()
    {
        if (_configuration?.CaptureSettingsScreenshot.Value != true || _view is null)
        {
            return;
        }

        var now = Time.unscaledTime;
        if (_diagnosticOpenAt >= 0f && now >= _diagnosticOpenAt)
        {
            _diagnosticOpenAt = -1f;
            _view.Open();
            if (_mapButton is not null)
            {
                var mapButton = _mapButton;
                var resolvedPanel = _view.GetPanel(ref mapButton);
                if (resolvedPanel?.Pointer == _mapPanel?.Pointer)
                {
                    _logger?.LogInfo("Stock category navigation resolved the minimap Map panel");
                }
                else
                {
                    _logger?.LogWarning("Stock category navigation did not resolve the minimap Map panel");
                }
            }
            OpenMapPanel();
            _bindingRow?.Trigger.onClick.Invoke();
            if (_recordingBinding)
            {
                _logger?.LogInfo("Minimap key-recording control entered capture mode");
            }
            else
            {
                _logger?.LogWarning("Minimap key-recording control did not enter capture mode");
            }
            CancelBindingRecording();
            Refresh(forceSliderValues: false);
            _diagnosticDropdownOpenAt = now + 0.5f;
            return;
        }
        if (_diagnosticDropdownOpenAt >= 0f && now >= _diagnosticDropdownOpenAt)
        {
            _diagnosticDropdownOpenAt = -1f;
            if (_shapeRow is not null)
            {
                EnsureShapeOptions(_shapeRow.Dropdown);
                _logger?.LogInfo(
                    "Minimap shape dropdown options: "
                    + $"{_shapeRow.Dropdown.options[0].text}, "
                    + _shapeRow.Dropdown.options[1].text);
                _shapeRow.Dropdown.Show();
            }
            _diagnosticCaptureAt = now + 1.5f;
            return;
        }
        if (_diagnosticCaptureAt < 0f || now < _diagnosticCaptureAt)
        {
            return;
        }

        _diagnosticCaptureAt = -1f;
        var captureDirectory = Path.Combine(Paths.BepInExRootPath, "ui-captures");
        Directory.CreateDirectory(captureDirectory);
        var capturePath = Path.Combine(captureDirectory, "minimap-settings.png");
        ScreenCapture.CaptureScreenshot(capturePath);
        _configuration.CaptureSettingsScreenshot.Value = false;
        _logger?.LogInfo($"Captured minimap settings: {capturePath}");
    }

    private static void DestroyExistingInjection(GameMenuView view)
    {
        for (var childIndex = view._categoryButtonPanel.childCount - 1; childIndex >= 0; childIndex--)
        {
            var child = view._categoryButtonPanel.GetChild(childIndex);
            if (child is not null && child.name == "Map")
            {
                UnityEngine.Object.Destroy(child.gameObject);
            }
        }

        var panelsParent = view._audioPanel?.transform.parent;
        if (panelsParent is null)
        {
            return;
        }
        for (var childIndex = panelsParent.childCount - 1; childIndex >= 0; childIndex--)
        {
            var child = panelsParent.GetChild(childIndex);
            if (child is not null && child.name == "Map")
            {
                UnityEngine.Object.Destroy(child.gameObject);
            }
        }
    }

    private static void SetTextIfChanged(TMP_Text? label, string value)
    {
        if (label is not null && !string.Equals(label.text, value, StringComparison.Ordinal))
        {
            label.text = value;
        }
    }

    private static bool UiIsAvailable()
    {
        return _view is not null
            && _view.Pointer != IntPtr.Zero
            && _buttonContainer is not null
            && _buttonContainer.Pointer != IntPtr.Zero
            && _mapButton is not null
            && _mapButton.Pointer != IntPtr.Zero
            && _mapPanel is not null
            && _mapPanel.Pointer != IntPtr.Zero
            && RowsAreAvailable();
    }

    private static bool RowsAreAvailable()
    {
        return ToggleRowIsAvailable(_enabledRow)
            && ToggleRowIsAvailable(_startVisibleRow)
            && ToggleRowIsAvailable(_showWhileHoldingRow)
            && DropdownRowIsAvailable(_shapeRow)
            && SliderRowIsAvailable(_sizeRow)
            && SliderRowIsAvailable(_zoomRow)
            && SliderRowIsAvailable(_topMarginRow)
            && SliderRowIsAvailable(_rightMarginRow)
            && BindingRowIsAvailable(_bindingRow);
    }

    private static bool ToggleRowIsAvailable(ToggleSettingsRow? row)
    {
        return row is not null
            && row.Root is not null
            && row.Root.Pointer != IntPtr.Zero
            && row.Toggle is not null
            && row.Toggle.Pointer != IntPtr.Zero;
    }

    private static bool SliderRowIsAvailable(SliderSettingsRow? row)
    {
        return row is not null
            && row.Root is not null
            && row.Root.Pointer != IntPtr.Zero
            && row.Slider is not null
            && row.Slider.Pointer != IntPtr.Zero;
    }

    private static bool DropdownRowIsAvailable(DropdownSettingsRow? row)
    {
        return row is not null
            && row.Root is not null
            && row.Root.Pointer != IntPtr.Zero
            && row.Dropdown is not null
            && row.Dropdown.Pointer != IntPtr.Zero;
    }

    private static bool BindingRowIsAvailable(BindingSettingsRow? row)
    {
        return row is not null
            && row.Root is not null
            && row.Root.Pointer != IntPtr.Zero
            && row.Trigger is not null
            && row.Trigger.Pointer != IntPtr.Zero
            && row.Reset is not null
            && row.Reset.Pointer != IntPtr.Zero;
    }

    private static void ClearReferences()
    {
        if (_mapButton is not null && _mapClickAction is not null && _mapButton.Pointer != IntPtr.Zero)
        {
            _mapButton.onClick.RemoveListener(_mapClickAction);
        }
        if (_bindingRow is not null && _bindingRow.PointerIsValid())
        {
            if (_bindingClickAction is not null)
            {
                _bindingRow.Trigger.onClick.RemoveListener(_bindingClickAction);
            }
            if (_bindingResetAction is not null)
            {
                _bindingRow.Reset.onClick.RemoveListener(_bindingResetAction);
            }
        }
        CancelBindingRecording();
        _view = null;
        _buttonContainer = null;
        _mapButton = null;
        _mapClickAction = null;
        _mapPanel = null;
        _enabledRow = null;
        _startVisibleRow = null;
        _showWhileHoldingRow = null;
        _shapeRow = null;
        _sizeRow = null;
        _zoomRow = null;
        _topMarginRow = null;
        _rightMarginRow = null;
        _bindingRow = null;
        _bindingClickAction = null;
        _bindingResetAction = null;
        _header = null;
        _updating = false;
        _registeredInCategoryList = false;
        _diagnosticOpenAt = -1f;
        _diagnosticDropdownOpenAt = -1f;
        _diagnosticCaptureAt = -1f;
    }

    private sealed record ToggleSettingsRow(GameObject Root, Toggle Toggle, TMP_Text Title);

    private sealed record SliderSettingsRow(GameObject Root, Slider Slider, TMP_Text Title, TMP_Text Value);

    private sealed record DropdownSettingsRow(GameObject Root, TMP_Dropdown Dropdown, TMP_Text Title);

    private sealed record BindingSettingsRow(
        GameObject Root,
        Button Trigger,
        Button Reset,
        TMP_Text Title,
        TMP_Text Value,
        TMP_Text ResetLabel)
    {
        public bool PointerIsValid()
        {
            return Root is not null
                && Root.Pointer != IntPtr.Zero
                && Trigger is not null
                && Trigger.Pointer != IntPtr.Zero
                && Reset is not null
                && Reset.Pointer != IntPtr.Zero;
        }
    }
}
