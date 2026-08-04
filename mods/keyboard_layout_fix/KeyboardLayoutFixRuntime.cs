using System.Runtime.InteropServices;
using BepInEx.Logging;
using Events;
using Gameplay.Player;
using Il2CppInterop.Runtime.Injection;
using Kinguinverse.DataUtils.Events;
using UI.InputBinding;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SneakOut.KeyboardLayoutFix;

internal static class KeyboardLayoutFixRuntime
{
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
    private static string _lastLayoutSignature = string.Empty;
    private static float _nextLayoutPollAt;
    private static float _bindingRefreshAt = -1f;
    private static float _diagnosticCycleStartedAt;
    private static float _nextDiagnosticPromptProbeAt;
    private static int _diagnosticCycleState;
    private static readonly HashSet<IntPtr> NativeMovementOwners = new();

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

    public static void ApplyNativePhysicalMovement(PlayerInputController inputController)
    {
        if (_configuration?.EnableMod.Value != true
            || inputController is null
            || inputController.Pointer == IntPtr.Zero)
        {
            return;
        }

        var pointer = inputController.Pointer;
        var decision = NativeMovementPolicy.Resolve(
            IsRussianLayout(),
            IsNativeKeyDown(0x57),
            IsNativeKeyDown(0x41),
            IsNativeKeyDown(0x53),
            IsNativeKeyDown(0x44),
            NativeMovementOwners.Contains(pointer));
        if (!decision.ShouldOverride)
        {
            // ResolveLocalInputs has already restored the stock value for this frame. Forget our
            // ownership without writing anything so English layout and gamepads remain untouched.
            NativeMovementOwners.Remove(pointer);
            return;
        }

        // Under Wine/XWayland, Unity's translated Input System state can lose the physical WASD
        // keys on a Cyrillic layout. Windows virtual letter keys remain physical-layout-neutral,
        // so read only those four keys and repair the already resolved movement vector directly.
        // Unlike the removed whole-keyboard QueueStateEvent implementation, this never injects
        // keyboard state and explicitly writes zero on release, preventing stuck movement.
        if (decision.OwnsMovement)
        {
            inputController._moveDirection = new Vector2(decision.Horizontal, decision.Vertical).normalized;
            NativeMovementOwners.Add(pointer);
            return;
        }

        NativeMovementOwners.Remove(pointer);
        inputController._moveDirection = Vector2.zero;
    }

    private static bool IsNativeKeyDown(int virtualKey)
    {
        return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    private static void DetectLayoutChange(bool force, string source)
    {
        var keyboardLayout = GetKeyboardLayout(0).ToInt64();
        var keyboard = Keyboard.current;
        var signature = BuildLayoutSignature(keyboardLayout, keyboard);
        if (!force && string.Equals(signature, _lastLayoutSignature, StringComparison.Ordinal))
        {
            return;
        }

        _lastKeyboardLayout = keyboardLayout;
        _lastLayoutSignature = signature;
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
        var refreshedPrompts = RefreshActivePromptLabels(IsRussianLayout());

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
        if (!IsRussianLayout())
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

    private static string BuildLayoutSignature(long keyboardLayout, Keyboard? keyboard)
    {
        return $"{keyboardLayout:X}:{keyboard?.keyboardLayout ?? string.Empty}:"
            + $"{keyboard?.wKey?.displayName ?? string.Empty}:"
            + $"{keyboard?.eKey?.displayName ?? string.Empty}:"
            + $"{keyboard?.aKey?.displayName ?? string.Empty}";
    }

    private static bool IsRussianLayout()
    {
        var lowWord = (int)(_lastKeyboardLayout & 0xFFFF);
        var highWord = (int)((_lastKeyboardLayout >> 16) & 0xFFFF);
        if (lowWord == 0x0419 || highWord == 0x0419)
        {
            return true;
        }

        // Wine can keep the Win32 HKL at US while Unity's Input System observes the host's
        // active XKB layout. Use both sources; otherwise the poller sees a constant HKL and
        // silently ignores every real GNOME/KDE layout transition.
        var keyboard = Keyboard.current;
        var unityLayout = keyboard?.keyboardLayout ?? string.Empty;
        return unityLayout.Contains("Russian", StringComparison.OrdinalIgnoreCase)
            || unityLayout.Contains("Cyrillic", StringComparison.OrdinalIgnoreCase)
            || string.Equals(keyboard?.wKey?.displayName, "Ц", StringComparison.OrdinalIgnoreCase)
            || string.Equals(keyboard?.eKey?.displayName, "У", StringComparison.OrdinalIgnoreCase)
            || string.Equals(keyboard?.aKey?.displayName, "Ф", StringComparison.OrdinalIgnoreCase);
    }

    private static int RefreshActivePromptLabels(bool useRussianLabels)
    {
        var refreshed = 0;
        foreach (var label in Resources.FindObjectsOfTypeAll<TMP_Text>())
        {
            try
            {
                if (label is null
                    || label.Pointer == IntPtr.Zero
                    || !label.isActiveAndEnabled)
                {
                    continue;
                }

                // IL2CPP can expose an active TMP wrapper while its native text value is null,
                // especially while EndMatchScene is replacing prompt objects. Cache the value
                // once and isolate stale wrappers so a label refresh cannot break the watcher.
                var original = label.text;
                if (string.IsNullOrEmpty(original)
                    || !original.Contains("Press", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var rewritten = original;
                foreach (var physicalKey in RussianPhysicalKeyLabels)
                {
                    var source = useRussianLabels ? physicalKey.Key.ToString() : physicalKey.Value;
                    var target = useRussianLabels ? physicalKey.Value : physicalKey.Key.ToString();
                    rewritten = rewritten.Replace($">{source}<", $">{target}<", StringComparison.Ordinal);
                    rewritten = rewritten.Replace($" {source} ", $" {target} ", StringComparison.Ordinal);
                }

                if (string.Equals(rewritten, original, StringComparison.Ordinal))
                {
                    continue;
                }

                label.text = rewritten;
                refreshed++;
            }
            catch (Exception exception)
            {
                Log($"Skipped stale TMP prompt during layout refresh: {exception.GetType().Name}");
            }
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
            try
            {
                if (label is null || label.Pointer == IntPtr.Zero || !label.isActiveAndEnabled)
                {
                    continue;
                }

                var text = label.text;
                if (!string.IsNullOrEmpty(text)
                    && text.Contains("Press", StringComparison.OrdinalIgnoreCase)
                    && text.Contains("to confirm", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch
            {
                // A diagnostic-only visual probe must never affect gameplay.
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
