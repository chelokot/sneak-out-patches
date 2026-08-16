using BepInEx;
using BepInEx.Logging;
using TMPro;
using UI.InputBinding;
using UI.VideoSettings;
using UI.Views;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SneakOut.ProximityVoiceChat;

internal static class ProximityVoiceSettingsUi
{
    private const float LeastSensitiveThreshold = 0.08f;
    private const float MostSensitiveThreshold = 0.002f;
    private const float PartyRowRefreshIntervalSeconds = 0.5f;
    private const float MicrophoneDeviceRefreshIntervalSeconds = 1f;

    private static readonly Dictionary<ulong, PlayerVolumeSettingsRow> PlayerVolumeRows = new();
    private static ProximityVoiceChatConfig? _configuration;
    private static ManualLogSource? _logger;
    private static GameMenuView? _view;
    private static GameObject? _sliderTemplate;
    private static ToggleSettingsRow? _enabledRow;
    private static DropdownSettingsRow? _modeRow;
    private static DropdownSettingsRow? _microphoneRow;
    private static BindingSettingsRow? _bindingRow;
    private static ToggleSettingsRow? _stopWhenUnfocusedRow;
    private static ToggleSettingsRow? _directionalVoiceRow;
    private static ActionSettingsRow? _microphoneTestRow;
    private static SliderSettingsRow? _ownVoiceVolumeRow;
    private static SliderSettingsRow? _sensitivityRow;
    private static UnityAction? _bindingClickAction;
    private static UnityAction? _bindingResetAction;
    private static UnityAction? _microphoneTestClickAction;
    private static bool _updating;
    private static bool _recordingBinding;
    private static float _recordingReadyAt;
    private static float _nextPartyRowRefreshAt;
    private static float _nextMicrophoneDeviceRefreshAt;
    private static string[] _microphoneDevices = Array.Empty<string>();
    private static float _diagnosticOpenAt = -1f;
    private static float _diagnosticCaptureAt = -1f;

    public static void Initialize(ProximityVoiceChatConfig configuration, ManualLogSource logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public static void Attach(GameMenuView view)
    {
        if (_configuration is null
            || view is null
            || view.Pointer == IntPtr.Zero
            || view._audioPanel is null
            || view._musicSlider is null)
        {
            return;
        }

        try
        {
            ClearReferences();
            _view = view;
            DestroyExistingRows(view._audioPanel.transform);

            var toggleTemplate = view._videoPanel?.transform.Find("VsyncPanel")?.gameObject
                ?? throw new InvalidOperationException("Stock settings toggle row is unavailable");
            var dropdownTemplate = view._videoPanel?.transform.Find("ScreenModePanel")?.gameObject
                ?? throw new InvalidOperationException("Stock settings dropdown row is unavailable");
            var sliderTemplate = view._musicSlider.transform.parent?.gameObject
                ?? throw new InvalidOperationException("Stock settings slider row is unavailable");
            _sliderTemplate = sliderTemplate;
            var stockBinding = view._controlsPanel?.GetComponentsInChildren<BindingUI>(true).FirstOrDefault()
                ?? throw new InvalidOperationException("Stock key-binding row is unavailable");
            var bindingTemplate = stockBinding.transform.parent?.gameObject
                ?? throw new InvalidOperationException("Stock key-binding panel is unavailable");

            _enabledRow = CreateToggleRow(toggleTemplate, view._audioPanel.transform, "ProximityVoiceEnabledPanel");
            _modeRow = CreateDropdownRow(
                dropdownTemplate,
                view._audioPanel.transform,
                "ProximityVoiceModePanel",
                configureModeOptions: true);
            _microphoneRow = CreateDropdownRow(
                dropdownTemplate,
                view._audioPanel.transform,
                "ProximityVoiceMicrophonePanel",
                configureModeOptions: false);
            _bindingRow = CreateBindingRow(bindingTemplate, view._audioPanel.transform);
            _stopWhenUnfocusedRow = CreateToggleRow(
                toggleTemplate,
                view._audioPanel.transform,
                "ProximityVoiceStopWhenUnfocusedPanel");
            _directionalVoiceRow = CreateToggleRow(
                toggleTemplate,
                view._audioPanel.transform,
                "ProximityVoiceDirectionalPanel");
            _ownVoiceVolumeRow = CreateSliderRow(
                sliderTemplate,
                view._audioPanel.transform,
                "ProximityVoiceOwnVolumePanel");
            _microphoneTestRow = CreateActionRow(
                bindingTemplate,
                view._audioPanel.transform,
                "ProximityVoiceMicrophoneTestPanel");
            _sensitivityRow = CreateSliderRow(
                sliderTemplate,
                view._audioPanel.transform,
                "ProximityVoiceSensitivityPanel");

            ConfigureSlider(
                _ownVoiceVolumeRow.Slider,
                VoicePlayerVolumePolicy.MinimumVolume,
                VoicePlayerVolumePolicy.MaximumVolume,
                wholeNumbers: false);
            ConfigureSlider(_sensitivityRow.Slider, 0f, 100f, wholeNumbers: true);
            RefreshMicrophoneDevices(force: true);
            SyncPlayerVolumeRows(force: true);
            Refresh(forceSliderValues: true);
            if (_configuration.CaptureSettingsScreenshot.Value)
            {
                _diagnosticOpenAt = Time.unscaledTime + 15f;
            }
            _logger?.LogInfo("Added proximity voice controls to the stock audio settings panel");
        }
        catch (Exception exception)
        {
            _logger?.LogWarning($"Could not add proximity voice settings controls: {exception}");
            ClearReferences();
        }
    }

    public static void Tick()
    {
        if (_configuration is null)
        {
            return;
        }

        try
        {
            var controlsAvailable = RowsAreAvailable();
            var audioTabActive = controlsAvailable && _modeRow?.Root.activeInHierarchy == true;
            if (VoiceMicrophoneTestPolicy.ShouldCancelForSettingsState(
                    controlsAvailable,
                    audioTabActive))
            {
                CancelBindingRecording();
                ProximityVoiceChatRuntime.CancelMicrophoneTest();
                return;
            }

            RefreshMicrophoneDevices(force: false);
            SyncPlayerVolumeRows(force: false);
            TickVisualDiagnostic();
            if (!_updating)
            {
                TickBindingRecording();
                ApplyChangedValues();
            }
            Refresh(forceSliderValues: false);
        }
        catch
        {
            // GameMenuView is scene-owned. A scene transition can invalidate all rows between two
            // Unity updates; the next GameMenuView.OnAwake will attach a fresh set.
            ClearReferences();
        }
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
            _view.OnAudioButton();
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
        var capturePath = Path.Combine(captureDirectory, "proximity-voice-settings.png");
        ScreenCapture.CaptureScreenshot(capturePath);
        _configuration.CaptureSettingsScreenshot.Value = false;
        _logger?.LogInfo($"Captured proximity voice settings: {capturePath}");
    }

    private static void ApplyChangedValues()
    {
        var configuration = _configuration!;
        var enabled = _enabledRow!.Toggle.isOn;
        var mode = (VoiceTransmissionMode)Mathf.Clamp(_modeRow!.Dropdown.value, 0, 2);
        var microphoneDevice = VoiceMicrophoneDevicePolicy.GetSelection(
            _microphoneRow!.Dropdown.value,
            _microphoneDevices);
        var stopWhenUnfocused = _stopWhenUnfocusedRow!.Toggle.isOn;
        var directionalVoice = _directionalVoiceRow!.Toggle.isOn;
        var ownVoiceVolume = Mathf.Clamp(
            _ownVoiceVolumeRow!.Slider.value,
            VoicePlayerVolumePolicy.MinimumVolume,
            VoicePlayerVolumePolicy.MaximumVolume);
        var sensitivity = Mathf.Clamp01(_sensitivityRow!.Slider.value / 100f);
        var threshold = Mathf.Lerp(LeastSensitiveThreshold, MostSensitiveThreshold, sensitivity);

        if (configuration.EnableMod.Value != enabled)
        {
            configuration.EnableMod.Value = enabled;
        }
        if (configuration.TransmissionMode.Value != mode)
        {
            configuration.TransmissionMode.Value = mode;
        }
        if (!string.Equals(
                configuration.MicrophoneDevice.Value,
                microphoneDevice,
                StringComparison.Ordinal))
        {
            configuration.MicrophoneDevice.Value = microphoneDevice;
            _logger?.LogInfo(
                $"Proximity voice microphone changed to "
                + VoiceMicrophoneDevicePolicy.GetDisplayName(microphoneDevice));
        }
        if (configuration.StopWhenGameIsUnfocused.Value != stopWhenUnfocused)
        {
            configuration.StopWhenGameIsUnfocused.Value = stopWhenUnfocused;
        }
        if (configuration.DirectionalVoice.Value != directionalVoice)
        {
            configuration.DirectionalVoice.Value = directionalVoice;
        }
        if (!Mathf.Approximately(configuration.MicrophoneVolume.Value, ownVoiceVolume))
        {
            configuration.MicrophoneVolume.Value = ownVoiceVolume;
        }
        foreach (var playerRow in PlayerVolumeRows.Values)
        {
            var volume = Mathf.Clamp(
                playerRow.Settings.Slider.value,
                VoicePlayerVolumePolicy.MinimumVolume,
                VoicePlayerVolumePolicy.MaximumVolume);
            if (!Mathf.Approximately(configuration.GetPlayerVolume(playerRow.SteamId), volume))
            {
                configuration.SetPlayerVolume(playerRow.SteamId, volume);
            }
        }
        if (!Mathf.Approximately(configuration.VoiceActivationThreshold.Value, threshold))
        {
            configuration.VoiceActivationThreshold.Value = threshold;
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
            var mode = _configuration.TransmissionMode.Value;
            var sensitivity = Mathf.InverseLerp(
                LeastSensitiveThreshold,
                MostSensitiveThreshold,
                _configuration.VoiceActivationThreshold.Value) * 100f;

            RefreshToggle(_enabledRow!, _configuration.EnableMod.Value);
            RefreshDropdown(_modeRow!, (int)mode);
            RefreshMicrophoneDropdown();
            RefreshToggle(_stopWhenUnfocusedRow!, _configuration.StopWhenGameIsUnfocused.Value);
            RefreshToggle(_directionalVoiceRow!, _configuration.DirectionalVoice.Value);
            RefreshSlider(
                _ownVoiceVolumeRow!,
                _configuration.MicrophoneVolume.Value,
                forceSliderValues);
            RefreshSlider(_sensitivityRow!, sensitivity, forceSliderValues);
            foreach (var playerRow in PlayerVolumeRows.Values)
            {
                var volume = _configuration.GetPlayerVolume(playerRow.SteamId);
                RefreshSlider(playerRow.Settings, volume, forceSliderValues);
                SetTextIfChanged(playerRow.Settings.Title, playerRow.DisplayName);
                SetTextIfChanged(playerRow.Settings.Value, $"{Mathf.RoundToInt(volume * 100f)}%");
            }

            SetTextIfChanged(_enabledRow!.Title, "Proximity voice chat");
            SetTextIfChanged(_modeRow!.Title, "Voice mode");
            SetTextIfChanged(_microphoneRow!.Title, "Microphone");
            SetTextIfChanged(_bindingRow!.Title, "Push-to-talk key");
            SetTextIfChanged(
                _bindingRow.Value,
                _recordingBinding
                    ? "Press a key (Esc cancels)"
                    : GetBindingDisplayName(_configuration.PushToTalkBinding.Value));
            SetTextIfChanged(_bindingRow.ResetLabel, "Reset");
            SetTextIfChanged(_stopWhenUnfocusedRow!.Title, "Stop when game is unfocused");
            SetTextIfChanged(_directionalVoiceRow!.Title, "Directional voice");
            SetTextIfChanged(_ownVoiceVolumeRow!.Title, "My voice volume");
            SetTextIfChanged(
                _ownVoiceVolumeRow.Value,
                $"{Mathf.RoundToInt(_configuration.MicrophoneVolume.Value * 100f)}%");
            SetTextIfChanged(_microphoneTestRow!.Title, "Microphone test (1 second delay)");
            var microphoneTestState = ProximityVoiceChatRuntime.MicrophoneTestState;
            SetTextIfChanged(
                _microphoneTestRow.Value,
                microphoneTestState == VoiceMicrophoneTestState.Draining
                    ? "Finishing..."
                    : ProximityVoiceChatRuntime.IsMicrophoneTestCapturing
                        ? "Stop test"
                        : "Start test");
            _microphoneTestRow.Button.interactable =
                _configuration.EnableMod.Value
                && microphoneTestState != VoiceMicrophoneTestState.Draining;
            SetTextIfChanged(_sensitivityRow!.Title, "Microphone sensitivity");
            SetTextIfChanged(_sensitivityRow.Value, $"{Mathf.RoundToInt(sensitivity)}%");

            var bindingActive = mode == VoiceTransmissionMode.PushToTalk;
            if (_bindingRow.Root.activeSelf != bindingActive)
            {
                _bindingRow.Root.SetActive(bindingActive);
            }
            if (!bindingActive)
            {
                CancelBindingRecording();
            }

            var sensitivityActive = mode == VoiceTransmissionMode.VoiceActivation;
            if (_sensitivityRow.Root.activeSelf != sensitivityActive)
            {
                _sensitivityRow.Root.SetActive(sensitivityActive);
            }
        }
        finally
        {
            _updating = false;
        }
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

    private static DropdownSettingsRow CreateDropdownRow(
        GameObject template,
        Transform parent,
        string name,
        bool configureModeOptions)
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
        if (configureModeOptions)
        {
            EnsureModeOptions(dropdown);
        }
        else
        {
            dropdown.ClearOptions();
            var options = new Il2CppSystem.Collections.Generic.List<TMP_Dropdown.OptionData>();
            options.Add(new TMP_Dropdown.OptionData(VoiceMicrophoneDevicePolicy.SystemDefaultLabel));
            dropdown.AddOptions(options);
        }
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
        root.name = "ProximityVoicePushToTalkBindingPanel";
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

    private static ActionSettingsRow CreateActionRow(GameObject template, Transform parent, string name)
    {
        var root = UnityEngine.Object.Instantiate(template, parent, false);
        root.name = name;
        root.transform.SetAsLastSibling();

        var bindingUi = root.GetComponentInChildren<BindingUI>(true)
            ?? throw new InvalidOperationException($"{name} has no stock action row");
        bindingUi.enabled = false;
        var title = root.transform.Find("RebindUIPrefab/ActionNameText")?.GetComponent<TMP_Text>()
            ?? throw new InvalidOperationException($"{name} has no action label");
        var button = root.transform.Find("RebindUIPrefab/TriggerRebindButton")?.GetComponent<Button>()
            ?? throw new InvalidOperationException($"{name} has no action button");
        var value = button.GetComponentInChildren<TMP_Text>(true)
            ?? throw new InvalidOperationException($"{name} has no action button label");
        var reset = root.transform.Find("RebindUIPrefab/ResetToDefaultButton")?.gameObject
            ?? throw new InvalidOperationException($"{name} has no secondary stock button");

        reset.SetActive(false);
        button.onClick = new Button.ButtonClickedEvent();
        _microphoneTestClickAction = (UnityAction)ProximityVoiceChatRuntime.ToggleMicrophoneTestCapture;
        button.onClick.AddListener(_microphoneTestClickAction);
        return new ActionSettingsRow(root, button, title, value);
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
        var title = labels.FirstOrDefault(label => label.name.Contains("MusicText", StringComparison.OrdinalIgnoreCase))
            ?? labels.FirstOrDefault()
            ?? throw new InvalidOperationException($"{name} has no title label");
        var value = labels.FirstOrDefault(label => label.Pointer != title.Pointer)
            ?? throw new InvalidOperationException($"{name} has no value label");
        return new SliderSettingsRow(root, slider, title, value);
    }

    private static void SyncPlayerVolumeRows(bool force)
    {
        if (_sliderTemplate is null
            || _sliderTemplate.Pointer == IntPtr.Zero
            || _view?._audioPanel is null
            || !SliderRowIsAvailable(_sensitivityRow))
        {
            return;
        }

        var now = Time.unscaledTime;
        if (!force && now < _nextPartyRowRefreshAt)
        {
            return;
        }
        _nextPartyRowRefreshAt = now + PartyRowRefreshIntervalSeconds;

        var members = ProximityVoiceChatRuntime.GetRemotePartyMembers();
        var memberIds = members.Select(member => member.SteamId).ToHashSet();
        foreach (var steamId in PlayerVolumeRows.Keys.Where(steamId => !memberIds.Contains(steamId)).ToArray())
        {
            var staleRow = PlayerVolumeRows[steamId];
            if (staleRow.Settings.Root is not null && staleRow.Settings.Root.Pointer != IntPtr.Zero)
            {
                UnityEngine.Object.Destroy(staleRow.Settings.Root);
            }
            PlayerVolumeRows.Remove(steamId);
        }

        var parent = _view._audioPanel.transform;
        foreach (var member in members)
        {
            if (!PlayerVolumeRows.TryGetValue(member.SteamId, out var playerRow)
                || !SliderRowIsAvailable(playerRow.Settings))
            {
                var settings = CreateSliderRow(
                    _sliderTemplate,
                    parent,
                    $"ProximityVoicePlayerVolumePanel_{member.SteamId}");
                ConfigureSlider(
                    settings.Slider,
                    VoicePlayerVolumePolicy.MinimumVolume,
                    VoicePlayerVolumePolicy.MaximumVolume,
                    wholeNumbers: false);
                var initialVolume = _configuration!.GetPlayerVolume(member.SteamId);
                RefreshSlider(settings, initialVolume, force: true);
                SetTextIfChanged(settings.Title, member.DisplayName);
                SetTextIfChanged(settings.Value, $"{Mathf.RoundToInt(initialVolume * 100f)}%");
                playerRow = new PlayerVolumeSettingsRow(member.SteamId, member.DisplayName, settings);
                PlayerVolumeRows[member.SteamId] = playerRow;
            }
            else if (!string.Equals(playerRow.DisplayName, member.DisplayName, StringComparison.Ordinal))
            {
                playerRow = playerRow with { DisplayName = member.DisplayName };
                PlayerVolumeRows[member.SteamId] = playerRow;
            }

            playerRow.Settings.Root.transform.SetSiblingIndex(
                _sensitivityRow!.Root.transform.GetSiblingIndex());
        }
    }

    private static void ConfigureSlider(Slider slider, float minimum, float maximum, bool wholeNumbers)
    {
        slider.minValue = minimum;
        slider.maxValue = maximum;
        slider.wholeNumbers = wholeNumbers;
        slider.interactable = true;
    }

    private static void RefreshToggle(ToggleSettingsRow row, bool value)
    {
        if (row.Toggle.isOn != value)
        {
            row.Toggle.SetIsOnWithoutNotify(value);
        }
    }

    private static void RefreshDropdown(DropdownSettingsRow row, int value)
    {
        EnsureModeOptions(row.Dropdown);
        var clamped = Mathf.Clamp(value, 0, 2);
        if (row.Dropdown.value != clamped)
        {
            row.Dropdown.SetValueWithoutNotify(clamped);
            row.Dropdown.RefreshShownValue();
        }
        if (row.Dropdown.captionText is not null)
        {
            SetTextIfChanged(row.Dropdown.captionText, GetModeLabel(clamped));
        }
    }

    private static void RefreshMicrophoneDevices(bool force)
    {
        if (_configuration is null || !DropdownRowIsAvailable(_microphoneRow))
        {
            return;
        }

        var now = Time.unscaledTime;
        if (!force && now < _nextMicrophoneDeviceRefreshAt)
        {
            return;
        }
        _nextMicrophoneDeviceRefreshAt = now + MicrophoneDeviceRefreshIntervalSeconds;

        var unityDevices = Microphone.devices;
        var discoveredDevices = new string[unityDevices?.Length ?? 0];
        for (var index = 0; index < discoveredDevices.Length; index++)
        {
            discoveredDevices[index] = unityDevices![index];
        }
        var normalizedDevices = VoiceMicrophoneDevicePolicy.NormalizeDevices(discoveredDevices);
        if (!force && _microphoneDevices.SequenceEqual(normalizedDevices, StringComparer.Ordinal))
        {
            return;
        }

        _microphoneDevices = normalizedDevices;
        var dropdown = _microphoneRow!.Dropdown;
        dropdown.ClearOptions();
        var options = new Il2CppSystem.Collections.Generic.List<TMP_Dropdown.OptionData>();
        options.Add(new TMP_Dropdown.OptionData(VoiceMicrophoneDevicePolicy.SystemDefaultLabel));
        foreach (var device in _microphoneDevices)
        {
            options.Add(new TMP_Dropdown.OptionData(device));
        }
        dropdown.AddOptions(options);
        dropdown.interactable = true;
        RefreshMicrophoneDropdown();
    }

    private static void RefreshMicrophoneDropdown()
    {
        if (_configuration is null || !DropdownRowIsAvailable(_microphoneRow))
        {
            return;
        }

        var selectionIndex = VoiceMicrophoneDevicePolicy.GetSelectionIndex(
            _configuration.MicrophoneDevice.Value,
            _microphoneDevices);
        var dropdown = _microphoneRow!.Dropdown;
        if (dropdown.value != selectionIndex)
        {
            dropdown.SetValueWithoutNotify(selectionIndex);
        }
        dropdown.RefreshShownValue();
        if (dropdown.captionText is not null)
        {
            var selection = VoiceMicrophoneDevicePolicy.GetSelection(
                selectionIndex,
                _microphoneDevices);
            SetTextIfChanged(
                dropdown.captionText,
                VoiceMicrophoneDevicePolicy.GetDisplayName(selection));
        }
    }

    private static void EnsureModeOptions(TMP_Dropdown dropdown)
    {
        if (dropdown.options.Count == 3
            && string.Equals(dropdown.options[0].text, "Push to talk", StringComparison.Ordinal)
            && string.Equals(dropdown.options[1].text, "Voice activation", StringComparison.Ordinal)
            && string.Equals(dropdown.options[2].text, "Always on", StringComparison.Ordinal))
        {
            return;
        }

        dropdown.ClearOptions();
        var options = new Il2CppSystem.Collections.Generic.List<TMP_Dropdown.OptionData>();
        options.Add(new TMP_Dropdown.OptionData("Push to talk"));
        options.Add(new TMP_Dropdown.OptionData("Voice activation"));
        options.Add(new TMP_Dropdown.OptionData("Always on"));
        dropdown.AddOptions(options);
        dropdown.RefreshShownValue();
    }

    private static string GetModeLabel(int value)
    {
        return value switch
        {
            1 => "Voice activation",
            2 => "Always on",
            _ => "Push to talk",
        };
    }

    private static void RefreshSlider(SliderSettingsRow row, float value, bool force)
    {
        if (force || !Mathf.Approximately(row.Slider.value, value))
        {
            row.Slider.SetValueWithoutNotify(value);
        }
    }

    private static void BeginBindingRecording()
    {
        if (_bindingRow?.Root.activeInHierarchy != true)
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
        if (_bindingRow?.Root.activeInHierarchy != true)
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
        SetBinding(ProximityVoiceChatConfig.DefaultPushToTalkBinding);
    }

    private static void SetBinding(string binding)
    {
        CancelBindingRecording();
        if (_configuration is null
            || string.Equals(_configuration.PushToTalkBinding.Value, binding, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _configuration.PushToTalkBinding.Value = binding;
        _logger?.LogInfo($"Proximity voice push-to-talk binding changed to {binding}");
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

    private static void SetTextIfChanged(TMP_Text label, string value)
    {
        if (!string.Equals(label.text, value, StringComparison.Ordinal))
        {
            label.text = value;
        }
    }

    private static void DestroyExistingRows(Transform parent)
    {
        for (var childIndex = parent.childCount - 1; childIndex >= 0; childIndex--)
        {
            var child = parent.GetChild(childIndex);
            if (child is not null && child.name.StartsWith("ProximityVoice", StringComparison.Ordinal))
            {
                UnityEngine.Object.Destroy(child.gameObject);
            }
        }
    }

    private static bool RowsAreAvailable()
    {
        return _view is not null
            && _view.Pointer != IntPtr.Zero
            && ToggleRowIsAvailable(_enabledRow)
            && DropdownRowIsAvailable(_modeRow)
            && DropdownRowIsAvailable(_microphoneRow)
            && BindingRowIsAvailable(_bindingRow)
            && ToggleRowIsAvailable(_stopWhenUnfocusedRow)
            && ToggleRowIsAvailable(_directionalVoiceRow)
            && ActionRowIsAvailable(_microphoneTestRow)
            && SliderRowIsAvailable(_ownVoiceVolumeRow)
            && SliderRowIsAvailable(_sensitivityRow);
    }

    private static bool ToggleRowIsAvailable(ToggleSettingsRow? row)
    {
        return row is not null
            && row.Root is not null
            && row.Root.Pointer != IntPtr.Zero
            && row.Toggle is not null
            && row.Toggle.Pointer != IntPtr.Zero;
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
        return row is not null && row.PointerIsValid();
    }

    private static bool ActionRowIsAvailable(ActionSettingsRow? row)
    {
        return row is not null
            && row.Root is not null
            && row.Root.Pointer != IntPtr.Zero
            && row.Button is not null
            && row.Button.Pointer != IntPtr.Zero;
    }

    private static bool SliderRowIsAvailable(SliderSettingsRow? row)
    {
        return row is not null
            && row.Root is not null
            && row.Root.Pointer != IntPtr.Zero
            && row.Slider is not null
            && row.Slider.Pointer != IntPtr.Zero;
    }

    private static void ClearReferences()
    {
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
        if (_microphoneTestRow is not null
            && ActionRowIsAvailable(_microphoneTestRow)
            && _microphoneTestClickAction is not null)
        {
            _microphoneTestRow.Button.onClick.RemoveListener(_microphoneTestClickAction);
        }
        CancelBindingRecording();
        ProximityVoiceChatRuntime.CancelMicrophoneTest();
        PlayerVolumeRows.Clear();
        _view = null;
        _sliderTemplate = null;
        _enabledRow = null;
        _modeRow = null;
        _microphoneRow = null;
        _bindingRow = null;
        _stopWhenUnfocusedRow = null;
        _directionalVoiceRow = null;
        _microphoneTestRow = null;
        _ownVoiceVolumeRow = null;
        _sensitivityRow = null;
        _bindingClickAction = null;
        _bindingResetAction = null;
        _microphoneTestClickAction = null;
        _updating = false;
        _nextPartyRowRefreshAt = 0f;
        _nextMicrophoneDeviceRefreshAt = 0f;
        _microphoneDevices = Array.Empty<string>();
        _diagnosticOpenAt = -1f;
        _diagnosticCaptureAt = -1f;
    }

    private sealed record ToggleSettingsRow(GameObject Root, Toggle Toggle, TMP_Text Title);

    private sealed record DropdownSettingsRow(GameObject Root, TMP_Dropdown Dropdown, TMP_Text Title);

    private sealed record ActionSettingsRow(GameObject Root, Button Button, TMP_Text Title, TMP_Text Value);

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

    private sealed record SliderSettingsRow(GameObject Root, Slider Slider, TMP_Text Title, TMP_Text Value);

    private sealed record PlayerVolumeSettingsRow(
        ulong SteamId,
        string DisplayName,
        SliderSettingsRow Settings);
}
