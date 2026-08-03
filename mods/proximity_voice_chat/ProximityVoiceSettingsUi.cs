using BepInEx.Logging;
using BepInEx;
using TMPro;
using UI.Views;
using UnityEngine;
using UnityEngine.UI;

namespace SneakOut.ProximityVoiceChat;

internal static class ProximityVoiceSettingsUi
{
    private const float MinimumUiVoiceDistance = 8f;
    private const float MaximumUiVoiceDistance = 30f;
    private const float LeastSensitiveThreshold = 0.08f;
    private const float MostSensitiveThreshold = 0.002f;

    private static ProximityVoiceChatConfig? _configuration;
    private static ManualLogSource? _logger;
    private static GameMenuView? _view;
    private static VoiceSettingsRow? _modeRow;
    private static VoiceSettingsRow? _volumeRow;
    private static VoiceSettingsRow? _sensitivityRow;
    private static VoiceSettingsRow? _distanceRow;
    private static bool _updating;
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
            _view = view;
            var template = view._musicSlider.transform.parent?.gameObject;
            if (template is null)
            {
                return;
            }

            DestroyExistingRows(view._audioPanel.transform);
            _modeRow = CreateRow(template, view._audioPanel.transform, "ProximityVoiceModePanel");
            _volumeRow = CreateRow(template, view._audioPanel.transform, "ProximityVoiceVolumePanel");
            _sensitivityRow = CreateRow(template, view._audioPanel.transform, "ProximityVoiceSensitivityPanel");
            _distanceRow = CreateRow(template, view._audioPanel.transform, "ProximityVoiceDistancePanel");

            ConfigureSlider(_modeRow.Slider, 0f, 2f, wholeNumbers: true);
            ConfigureSlider(_volumeRow.Slider, 0f, 2f, wholeNumbers: false);
            ConfigureSlider(_sensitivityRow.Slider, 0f, 100f, wholeNumbers: true);
            ConfigureSlider(_distanceRow.Slider, MinimumUiVoiceDistance, MaximumUiVoiceDistance, wholeNumbers: true);
            Refresh(forceSliderValues: true);
            if (_configuration.CaptureSettingsScreenshot.Value)
            {
                _diagnosticOpenAt = Time.unscaledTime + 15f;
            }
            _logger?.LogInfo("Added proximity voice controls to the stock audio settings panel");
        }
        catch (Exception exception)
        {
            _logger?.LogWarning($"Could not add proximity voice settings controls: {exception.Message}");
            ClearReferences();
        }
    }

    public static void Tick()
    {
        if (_configuration is null || !RowsAreAvailable())
        {
            return;
        }

        try
        {
            TickVisualDiagnostic();
            if (_modeRow?.Root.activeInHierarchy != true)
            {
                return;
            }
            if (!_updating)
            {
                ApplyChangedSliderValues();
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

    private static void ApplyChangedSliderValues()
    {
        var mode = (VoiceTransmissionMode)Mathf.Clamp(Mathf.RoundToInt(_modeRow!.Slider.value), 0, 2);
        if (_configuration!.TransmissionMode.Value != mode)
        {
            _configuration.TransmissionMode.Value = mode;
        }

        var volume = Mathf.Clamp(_volumeRow!.Slider.value, 0f, 2f);
        if (!Mathf.Approximately(_configuration.MasterVolume.Value, volume))
        {
            _configuration.MasterVolume.Value = volume;
        }

        var sensitivity = Mathf.Clamp01(_sensitivityRow!.Slider.value / 100f);
        var threshold = Mathf.Lerp(LeastSensitiveThreshold, MostSensitiveThreshold, sensitivity);
        if (!Mathf.Approximately(_configuration.VoiceActivationThreshold.Value, threshold))
        {
            _configuration.VoiceActivationThreshold.Value = threshold;
        }

        var distance = Mathf.Clamp(
            _distanceRow!.Slider.value,
            MinimumUiVoiceDistance,
            MaximumUiVoiceDistance);
        if (!Mathf.Approximately(_configuration.MaximumDistance.Value, distance))
        {
            _configuration.MaximumDistance.Value = distance;
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
            var modeRow = _modeRow!;
            var volumeRow = _volumeRow!;
            var sensitivityRow = _sensitivityRow!;
            var distanceRow = _distanceRow!;
            var mode = _configuration.TransmissionMode.Value;
            if (forceSliderValues || Mathf.RoundToInt(modeRow.Slider.value) != (int)mode)
            {
                modeRow.Slider.SetValueWithoutNotify((int)mode);
            }
            if (forceSliderValues || !Mathf.Approximately(volumeRow.Slider.value, _configuration.MasterVolume.Value))
            {
                volumeRow.Slider.SetValueWithoutNotify(_configuration.MasterVolume.Value);
            }
            var sensitivity = Mathf.InverseLerp(
                LeastSensitiveThreshold,
                MostSensitiveThreshold,
                _configuration.VoiceActivationThreshold.Value) * 100f;
            if (forceSliderValues || !Mathf.Approximately(sensitivityRow.Slider.value, sensitivity))
            {
                sensitivityRow.Slider.SetValueWithoutNotify(sensitivity);
            }
            if (forceSliderValues || !Mathf.Approximately(distanceRow.Slider.value, _configuration.MaximumDistance.Value))
            {
                distanceRow.Slider.SetValueWithoutNotify(_configuration.MaximumDistance.Value);
            }

            SetTextIfChanged(modeRow.Title, "Voice mode");
            SetTextIfChanged(modeRow.Value, mode switch
            {
                VoiceTransmissionMode.PushToTalk => $"Push to talk ({_configuration.PushToTalkKey.Value})",
                VoiceTransmissionMode.VoiceActivation => "Voice activation",
                VoiceTransmissionMode.AlwaysOn => "Always on",
                _ => mode.ToString(),
            });
            SetTextIfChanged(volumeRow.Title, "Voice volume");
            SetTextIfChanged(volumeRow.Value, $"{Mathf.RoundToInt(_configuration.MasterVolume.Value * 100f)}%");
            SetTextIfChanged(sensitivityRow.Title, "Microphone sensitivity");
            SetTextIfChanged(sensitivityRow.Value, $"{Mathf.RoundToInt(sensitivity)}%");
            SetTextIfChanged(distanceRow.Title, "Voice distance");
            SetTextIfChanged(distanceRow.Value, $"{Mathf.RoundToInt(_configuration.MaximumDistance.Value)} m");

            // Sensitivity is meaningful only for voice activation. Hiding the full stock panel
            // lets VerticalLayoutGroup close the gap automatically.
            var sensitivityActive = mode == VoiceTransmissionMode.VoiceActivation;
            if (sensitivityRow.Root.activeSelf != sensitivityActive)
            {
                sensitivityRow.Root.SetActive(sensitivityActive);
            }
        }
        finally
        {
            _updating = false;
        }
    }

    private static void SetTextIfChanged(TMP_Text label, string value)
    {
        if (!string.Equals(label.text, value, StringComparison.Ordinal))
        {
            label.text = value;
        }
    }

    private static VoiceSettingsRow CreateRow(GameObject template, Transform parent, string name)
    {
        var root = UnityEngine.Object.Instantiate(template, parent, false);
        root.name = name;
        root.transform.SetAsLastSibling();
        var slider = root.GetComponentInChildren<Slider>(true)
            ?? throw new InvalidOperationException($"{name} has no stock slider");
        // A cloned UnityEvent retains the music slider's persistent callback. A fresh event keeps
        // the stock visuals/navigation without changing music whenever a voice control is moved.
        slider.onValueChanged = new Slider.SliderEvent();

        var labels = root.GetComponentsInChildren<TMP_Text>(true);
        var title = labels.FirstOrDefault(label => label.name.Contains("MusicText", StringComparison.OrdinalIgnoreCase))
            ?? labels.FirstOrDefault()
            ?? throw new InvalidOperationException($"{name} has no title label");
        var value = labels.FirstOrDefault(label => label.Pointer != title.Pointer)
            ?? throw new InvalidOperationException($"{name} has no value label");
        return new VoiceSettingsRow(root, slider, title, value);
    }

    private static void ConfigureSlider(Slider slider, float minimum, float maximum, bool wholeNumbers)
    {
        slider.minValue = minimum;
        slider.maxValue = maximum;
        slider.wholeNumbers = wholeNumbers;
        slider.interactable = true;
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
            && RowIsAvailable(_modeRow)
            && RowIsAvailable(_volumeRow)
            && RowIsAvailable(_sensitivityRow)
            && RowIsAvailable(_distanceRow);
    }

    private static bool RowIsAvailable(VoiceSettingsRow? row)
    {
        return row is not null
            && row.Root is not null
            && row.Root.Pointer != IntPtr.Zero
            && row.Slider is not null
            && row.Slider.Pointer != IntPtr.Zero;
    }

    private static void ClearReferences()
    {
        _view = null;
        _modeRow = null;
        _volumeRow = null;
        _sensitivityRow = null;
        _distanceRow = null;
        _diagnosticOpenAt = -1f;
        _diagnosticCaptureAt = -1f;
    }

    private sealed record VoiceSettingsRow(GameObject Root, Slider Slider, TMP_Text Title, TMP_Text Value);
}
