using System.Runtime.InteropServices;
using BepInEx.Logging;
using Events;
using Il2CppInterop.Runtime.Injection;
using Kinguinverse.DataUtils.Events;
using UI.InputBinding;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace SneakOut.KeyboardLayoutFix;

internal static class KeyboardLayoutFixRuntime
{
    private static readonly (Key Key, int VirtualKey)[] PhysicalLetterKeys =
    {
        (Key.A, 0x41), (Key.B, 0x42), (Key.C, 0x43), (Key.D, 0x44),
        (Key.E, 0x45), (Key.F, 0x46), (Key.G, 0x47), (Key.H, 0x48),
        (Key.I, 0x49), (Key.J, 0x4A), (Key.K, 0x4B), (Key.L, 0x4C),
        (Key.M, 0x4D), (Key.N, 0x4E), (Key.O, 0x4F), (Key.P, 0x50),
        (Key.Q, 0x51), (Key.R, 0x52), (Key.S, 0x53), (Key.T, 0x54),
        (Key.U, 0x55), (Key.V, 0x56), (Key.W, 0x57), (Key.X, 0x58),
        (Key.Y, 0x59), (Key.Z, 0x5A)
    };
    private static readonly HashSet<Key> ManagedPhysicalKeys =
        PhysicalLetterKeys.Select(entry => entry.Key).ToHashSet();
    private static readonly IReadOnlyDictionary<char, string> RussianPhysicalKeyLabels =
        new Dictionary<char, string>
        {
            ['Q'] = "Й", ['W'] = "Ц", ['E'] = "У", ['R'] = "К", ['T'] = "Е",
            ['Y'] = "Н", ['U'] = "Г", ['I'] = "Ш", ['O'] = "Щ", ['P'] = "З",
            ['A'] = "Ф", ['S'] = "Ы", ['D'] = "В", ['F'] = "А", ['G'] = "П",
            ['H'] = "Р", ['J'] = "О", ['K'] = "Л", ['L'] = "Д",
            ['Z'] = "Я", ['X'] = "Ч", ['C'] = "С", ['V'] = "М", ['B'] = "И",
            ['N'] = "Т", ['M'] = "Ь"
        };
    private const float LayoutPollInterval = 0.25f;
    private const float BindingRefreshDelay = 0.12f;
    private const float DiagnosticPromptProbeDelay = 45f;
    private const float DiagnosticPromptProbeInterval = 0.5f;
    private const float DiagnosticRussianDuration = 6f;
    private const uint KlfActivate = 0x00000001;
    private const uint KlfSetForProcess = 0x00000100;

    private static ManualLogSource? _logger;
    private static KeyboardLayoutFixConfig? _configuration;
    private static bool _watcherInstalled;
    private static long _lastKeyboardLayout;
    private static float _nextLayoutPollAt;
    private static float _bindingRefreshAt = -1f;
    private static float _diagnosticCycleStartedAt;
    private static float _nextDiagnosticPromptProbeAt;
    private static int _diagnosticCycleState;
    private static uint _lastPhysicalLetterMask;

    public static void Initialize(ManualLogSource logger, KeyboardLayoutFixConfig configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _diagnosticCycleStartedAt = Time.unscaledTime;
        if (configuration.EnableMod.Value)
        {
            EnsureWatcher();
        }
    }

    private static void EnsureWatcher()
    {
        if (_watcherInstalled)
        {
            return;
        }

        ClassInjector.RegisterTypeInIl2Cpp<KeyboardLayoutWatcher>();
        var watcherObject = new GameObject("KeyboardLayoutFixWatcher");
        UnityEngine.Object.DontDestroyOnLoad(watcherObject);
        watcherObject.hideFlags = HideFlags.HideAndDontSave;
        watcherObject.AddComponent<KeyboardLayoutWatcher>();
        _watcherInstalled = true;
        DetectLayoutChange(force: true, source: "startup");
    }

    private static void Tick()
    {
        if (_configuration is null || !_configuration.EnableMod.Value)
        {
            return;
        }

        var now = Time.unscaledTime;
        SynchronizePhysicalLetterState();
        RunDiagnosticLayoutCycle(now);
        if (now >= _nextLayoutPollAt)
        {
            _nextLayoutPollAt = now + LayoutPollInterval;
            DetectLayoutChange(force: false, source: "poll");
        }

        if (_bindingRefreshAt >= 0f && now >= _bindingRefreshAt)
        {
            _bindingRefreshAt = -1f;
            RefreshBindingLabels();
        }
    }

    private static void HandleFocus(bool hasFocus)
    {
        if (hasFocus)
        {
            DetectLayoutChange(force: true, source: "focus");
        }
    }

    private static void SynchronizePhysicalLetterState()
    {
        var keyboard = Keyboard.current;
        if (keyboard is null)
        {
            return;
        }

        uint physicalMask = 0;
        for (var index = 0; index < PhysicalLetterKeys.Length; index++)
        {
            if ((GetAsyncKeyState(PhysicalLetterKeys[index].VirtualKey) & 0x8000) != 0)
            {
                physicalMask |= 1u << index;
            }
        }

        if (physicalMask == _lastPhysicalLetterMask)
        {
            return;
        }

        _lastPhysicalLetterMask = physicalMask;
        var pressedKeys = new List<Key>();
        var allKeys = keyboard.allKeys;
        for (var index = 0; index < allKeys.Count; index++)
        {
            var control = allKeys[index];
            if (control.isPressed && !ManagedPhysicalKeys.Contains(control.keyCode))
            {
                pressedKeys.Add(control.keyCode);
            }
        }
        for (var index = 0; index < PhysicalLetterKeys.Length; index++)
        {
            if ((physicalMask & (1u << index)) != 0)
            {
                pressedKeys.Add(PhysicalLetterKeys[index].Key);
            }
        }

        InputSystem.QueueStateEvent(
            keyboard,
            new KeyboardState(pressedKeys.ToArray()),
            InputState.currentTime);
    }

    private static void DetectLayoutChange(bool force, string source)
    {
        var keyboardLayout = GetKeyboardLayout(0).ToInt64();
        if (!force && keyboardLayout == _lastKeyboardLayout)
        {
            return;
        }

        _lastKeyboardLayout = keyboardLayout;
        var keyboard = Keyboard.current;
        if (keyboard is null)
        {
            _bindingRefreshAt = Time.unscaledTime + BindingRefreshDelay;
            Log($"Keyboard layout refresh deferred ({source}): no Input System keyboard; hkl=0x{keyboardLayout:X}");
            return;
        }

        _bindingRefreshAt = Time.unscaledTime + BindingRefreshDelay;
        Log(
            $"Keyboard layout change detected ({source}): hkl=0x{keyboardLayout:X}, "
            + $"unity={keyboard.keyboardLayout}, W={keyboard.wKey.displayName}, E={keyboard.eKey.displayName}");
    }

    private static void RefreshBindingLabels()
    {
        var initialization = Resources.FindObjectsOfTypeAll<Initialization>()
            .FirstOrDefault(instance => instance is not null && instance.Pointer != IntPtr.Zero);
        var keyBindingController = initialization?._keyBindingController;
        if (keyBindingController is not null && keyBindingController.Pointer != IntPtr.Zero)
        {
            keyBindingController.SetAllBindings();
            ApplyLocalizedPhysicalKeyLabels(keyBindingController);
        }

        var bindingViews = Resources.FindObjectsOfTypeAll<BindingUI>();
        var refreshedViews = 0;
        foreach (var bindingView in bindingViews)
        {
            if (bindingView is null
                || bindingView.Pointer == IntPtr.Zero
                || !bindingView.isActiveAndEnabled
                || bindingView.m_Action is null
                || bindingView.m_BindingText is null)
            {
                continue;
            }

            bindingView.UpdateBindingDisplay();
            refreshedViews++;
        }

        // Gameplay interaction, equipment, skill, and settings views already listen to this
        // stock event. Publishing it avoids hard-coding every UI implementation in the mod.
        GameEventsManager.Publish<AfterControlsOverrideEvent>(null, new AfterControlsOverrideEvent());
        var refreshedPrompts = RefreshActivePromptLabels(IsRussianLayout(_lastKeyboardLayout));

        var keyboard = Keyboard.current;
        Log(
            "Keyboard bindings refreshed: "
            + $"unity={keyboard?.keyboardLayout ?? "unavailable"}, "
            + $"W={keyboard?.wKey?.displayName ?? "?"}, E={keyboard?.eKey?.displayName ?? "?"}, "
            + $"bindingViews={refreshedViews}, controllerReady={keyBindingController is not null}, "
            + $"activePrompts={refreshedPrompts}, "
            + $"move={keyBindingController?.MoveActionPcKey ?? "?"}, "
            + $"primary={keyBindingController?.PrimaryActionPcKey ?? "?"}, "
            + $"sprint={keyBindingController?.SprintActionPcKey ?? "?"}");
    }

    private static void ApplyLocalizedPhysicalKeyLabels(KeyBindingController controller)
    {
        if (!IsRussianLayout(_lastKeyboardLayout))
        {
            return;
        }

        // Wine updates Keyboard.keyboardLayout but leaves KeyControl.displayName in US English.
        // Keep the actual InputAction paths untouched so they remain physical keys, and localize
        // only the controller strings consumed by HUD/settings views.
        controller._MoveActionPcKey_k__BackingField = LocalizePhysicalKeys(controller.MoveActionPcKey);
        controller._PrimaryActionPcKey_k__BackingField = LocalizePhysicalKeys(controller.PrimaryActionPcKey);
        controller._SecondaryActionPcKey_k__BackingField = LocalizePhysicalKeys(controller.SecondaryActionPcKey);
        controller._KillActionPcKey_k__BackingField = LocalizePhysicalKeys(controller.KillActionPcKey);
        controller._CrouchActionPcKey_k__BackingField = LocalizePhysicalKeys(controller.CrouchActionPcKey);
        controller._SprintActionPcKey_k__BackingField = LocalizePhysicalKeys(controller.SprintActionPcKey);
        controller._EmoteMenuActionPcKey_k__BackingField = LocalizePhysicalKeys(controller.EmoteMenuActionPcKey);
        controller._PingActionPcKey_k__BackingField = LocalizePhysicalKeys(controller.PingActionPcKey);
        controller._FirstItemUsageActionPcKey_k__BackingField = LocalizePhysicalKeys(controller.FirstItemUsageActionPcKey);
        controller._SecondItemUsageActionPcKey_k__BackingField = LocalizePhysicalKeys(controller.SecondItemUsageActionPcKey);
        controller._FirstSkillActionPcKey_k__BackingField = LocalizePhysicalKeys(controller.FirstSkillActionPcKey);
        controller._SecondSkillActionPcKey_k__BackingField = LocalizePhysicalKeys(controller.SecondSkillActionPcKey);
        controller._VoiceChatHoldPcKey_k__BackingField = LocalizePhysicalKeys(controller.VoiceChatHoldPcKey);
    }

    private static bool IsRussianLayout(long keyboardLayout)
    {
        var lowWord = (int)(keyboardLayout & 0xFFFF);
        var highWord = (int)((keyboardLayout >> 16) & 0xFFFF);
        return lowWord == 0x0419 || highWord == 0x0419;
    }

    private static int RefreshActivePromptLabels(bool useRussianLabels)
    {
        var refreshed = 0;
        foreach (var label in Resources.FindObjectsOfTypeAll<TMP_Text>())
        {
            if (label is null
                || label.Pointer == IntPtr.Zero
                || !label.isActiveAndEnabled
                || !label.text.Contains("Press", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var rewritten = label.text;
            foreach (var physicalKey in RussianPhysicalKeyLabels)
            {
                var source = useRussianLabels ? physicalKey.Key.ToString() : physicalKey.Value;
                var target = useRussianLabels ? physicalKey.Value : physicalKey.Key.ToString();
                rewritten = rewritten.Replace($">{source}<", $">{target}<", StringComparison.Ordinal);
                rewritten = rewritten.Replace($" {source} ", $" {target} ", StringComparison.Ordinal);
            }

            if (string.Equals(rewritten, label.text, StringComparison.Ordinal))
            {
                continue;
            }

            label.text = rewritten;
            refreshed++;
        }

        return refreshed;
    }

    private static string LocalizePhysicalKeys(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return label;
        }

        var parts = label.Split('/');
        for (var index = 0; index < parts.Length; index++)
        {
            var part = parts[index].Trim();
            if (part.Length == 1
                && RussianPhysicalKeyLabels.TryGetValue(char.ToUpperInvariant(part[0]), out var localized))
            {
                parts[index] = localized;
            }
        }

        return string.Join("/", parts);
    }

    private static void RunDiagnosticLayoutCycle(float now)
    {
        if (_configuration is null || !_configuration.CycleLayoutsForFlow.Value)
        {
            return;
        }

        if (_diagnosticCycleState == 0)
        {
            if (now - _diagnosticCycleStartedAt < DiagnosticPromptProbeDelay
                || now < _nextDiagnosticPromptProbeAt)
            {
                return;
            }

            _nextDiagnosticPromptProbeAt = now + DiagnosticPromptProbeInterval;
            if (!IsEnglishSelectionPromptVisible())
            {
                return;
            }

            ActivateDiagnosticLayout("00000419", "Russian");
            _diagnosticCycleState = 1;
            _diagnosticCycleStartedAt = now;
            return;
        }

        if (_diagnosticCycleState == 1
            && now - _diagnosticCycleStartedAt >= DiagnosticRussianDuration)
        {
            ActivateDiagnosticLayout("00000409", "English");
            _diagnosticCycleState = 2;
        }
    }

    private static bool IsEnglishSelectionPromptVisible()
    {
        foreach (var label in Resources.FindObjectsOfTypeAll<TMP_Text>())
        {
            if (label is not null
                && label.Pointer != IntPtr.Zero
                && label.isActiveAndEnabled
                && label.text.Contains("Press", StringComparison.OrdinalIgnoreCase)
                && label.text.Contains("to confirm", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void ActivateDiagnosticLayout(string layoutIdentifier, string label)
    {
        var loadedLayout = LoadKeyboardLayout(
            layoutIdentifier,
            KlfActivate | KlfSetForProcess);
        if (loadedLayout == IntPtr.Zero)
        {
            _logger?.LogError($"Diagnostic {label} keyboard layout activation failed");
            return;
        }

        Log($"Diagnostic keyboard layout activated: {label}, hkl=0x{loadedLayout.ToInt64():X}");
        DetectLayoutChange(force: true, source: $"diagnostic-{label.ToLowerInvariant()}");
    }

    private static void Log(string message)
    {
        if (_configuration is not null && _configuration.EnableLogging.Value)
        {
            _logger?.LogInfo(message);
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint threadId);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadKeyboardLayout(string layoutIdentifier, uint flags);

    private sealed class KeyboardLayoutWatcher : MonoBehaviour
    {
        public KeyboardLayoutWatcher(IntPtr pointer) : base(pointer)
        {
        }

        public KeyboardLayoutWatcher() : base(ClassInjector.DerivedConstructorPointer<KeyboardLayoutWatcher>())
        {
            ClassInjector.DerivedConstructorBody(this);
        }

        private void Update()
        {
            Tick();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            HandleFocus(hasFocus);
        }
    }
}
