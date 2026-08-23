using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using Fusion;
using Gameplay.Interactions;
using Gameplay.Player.Components;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UI.Buttons;
using UI.Views;

namespace SneakOut.RuntimeProfiler;

internal static class RuntimeProfilerRuntime
{
    private static readonly Stopwatch SessionClock = Stopwatch.StartNew();
    private static readonly Dictionary<MethodBase, EventHookDefinition> EventHooks = new();
    private static readonly object EventStateGate = new();

    [ThreadStatic]
    private static Stack<RuntimeEventScope>? _threadEventScopes;

    private static ManualLogSource? _logger;
    private static RuntimeProfilerConfig? _configuration;
    private static RuntimeEventLogWriter? _writer;
    private static Harmony? _harmony;
    private static Timer? _watchdogTimer;
    private static string? _logPath;
    private static string _activeMainThreadEvents = "none";
    private static string _lastStartedEvent = "none";
    private static string _lastCompletedEvent = "none";
    private static long _lastHeartbeatTimestamp;
    private static long _freezeStartTimestamp;
    private static long _sequence;
    private static long _eventId;
    private static int _mainThreadId;
    private static int _initialized;
    private static int _shutdown;
    private static int _watchdogCheckActive;
    private static int _freezeActive;
    private static int _applicationFocused = 1;
    private static int _applicationPaused;

    public static void Initialize(ManualLogSource logger, RuntimeProfilerConfig configuration)
    {
        _logger = logger;
        _configuration = configuration;

        if (!configuration.EnableMod.Value || Interlocked.Exchange(ref _initialized, 1) != 0)
        {
            return;
        }

        _mainThreadId = Environment.CurrentManagedThreadId;
        _lastHeartbeatTimestamp = Stopwatch.GetTimestamp();
        var logDirectory = Path.Combine(Paths.BepInExRootPath, "event-logs");
        Directory.CreateDirectory(logDirectory);
        _logPath = Path.Combine(
            logDirectory,
            $"runtime-events-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Environment.ProcessId}.tsv");
        _writer = new RuntimeEventLogWriter(_logPath);
        _writer.Enqueue(
            "utc\telapsed_ms\tsequence\tthread\tkind\tcategory\taction\tevent_id\tduration_ms\tdetails\tstate\terror");
        WriteRecord(
            "SESSION_START",
            "SYSTEM",
            RuntimeProfilerPlugin.PluginName,
            0,
            null,
            $"version={RuntimeProfilerPlugin.PluginVersion}; process={Environment.ProcessId}",
            $"freezeThresholdMs={FreezeThresholdMilliseconds}; hitchThresholdMs={HitchThresholdMilliseconds}; watchdogPollMs={WatchdogPollMilliseconds}",
            null);

        try
        {
            _harmony = new Harmony(RuntimeProfilerPlugin.PluginGuid);
            _harmony.PatchAll(typeof(RuntimeProfilerPlugin).Assembly);
            AttachHeartbeatWatcher();
            Interlocked.Exchange(ref _lastHeartbeatTimestamp, Stopwatch.GetTimestamp());
            _watchdogTimer = new Timer(
                _ => CheckMainThreadHeartbeat(),
                null,
                WatchdogPollMilliseconds,
                WatchdogPollMilliseconds);
            Application.add_quitting(new Action(Shutdown));
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            _logger.LogInfo($"Runtime event log: {_logPath}");
        }
        catch (Exception exception)
        {
            WriteRecord("ERROR", "SYSTEM", "initialize", 0, null, string.Empty, string.Empty, exception);
            Shutdown();
            throw;
        }
    }

    public static IEnumerable<MethodBase> GetEventTargets()
    {
        EventHooks.Clear();

        if (_configuration?.LogInteractions.Value == true)
        {
            AddHook(
                typeof(EntityInteractiveComponent),
                nameof(EntityInteractiveComponent.Interact),
                new[] { typeof(Interactable), typeof(Types.InteractionType), typeof(int), typeof(bool) },
                "INTERACTION",
                "resolve");
            AddHook(typeof(Door), "Open", new[] { typeof(int) }, "DOOR", "open");
            AddHook(typeof(Door), "Close", new[] { typeof(int) }, "DOOR", "close");
        }

        if (_configuration?.LogItemActions.Value == true)
        {
            AddHook(typeof(Chair), nameof(Chair.PickUp), new[] { typeof(int) }, "ITEM", "chair.pickup");
            AddHook(typeof(Chair), nameof(Chair.Throw), new[] { typeof(int) }, "ITEM", "chair.throw");
            AddHook(typeof(Barrel), nameof(Barrel.PickUp), new[] { typeof(int) }, "ITEM", "barrel.pickup");
            AddHook(typeof(Barrel), nameof(Barrel.Throw), new[] { typeof(int) }, "ITEM", "barrel.throw");
            AddHook(
                typeof(Gameplay.Interactions.Tasks.PotTask.Ingredient),
                nameof(Gameplay.Interactions.Tasks.PotTask.Ingredient.PickUp),
                new[] { typeof(int) },
                "ITEM",
                "ingredient.pickup");
            AddHook(
                typeof(Gameplay.Interactions.Tasks.PotTask.Ingredient),
                nameof(Gameplay.Interactions.Tasks.PotTask.Ingredient.Throw),
                new[] { typeof(int) },
                "ITEM",
                "ingredient.throw");
            AddHook(typeof(Jug), nameof(Jug.PickUp), new[] { typeof(int) }, "ITEM", "jug.pickup");
            AddHook(
                typeof(Gameplay.ThrowableBanana),
                nameof(Gameplay.ThrowableBanana.Throw),
                new[] { typeof(Vector3) },
                "ITEM",
                "banana.throw");
            AddHook(
                typeof(Gameplay.ThrowableSnare),
                nameof(Gameplay.ThrowableSnare.Throw),
                new[] { typeof(Vector3) },
                "ITEM",
                "snare.throw");

            var pickupMethods = new[]
            {
                "RPC_OnBananaPickUp",
                "RPC_OnBananaStrikePickUp",
                "RPC_OnBroomstickPickUp",
                "RPC_OnGhostEctoplasmPickUp",
                "RPC_OnGhostSlowPickUp",
                "RPC_OnShovelPickUp",
                "RPC_OnSkateboardPickUp",
                "RPC_OnSnarePickUp"
            };
            foreach (var methodName in pickupMethods)
            {
                AddHook(
                    typeof(Networking.Photon.SpookedNetworkCollisionManager),
                    methodName,
                    new[] { typeof(int) },
                    "ITEM",
                    ToActionName(methodName));
            }

            AddHook(
                typeof(Networking.Photon.SpookedNetworkCollisionManager),
                "RPC_OnWandPickUp",
                new[] { typeof(int), typeof(Types.WandSpellType) },
                "ITEM",
                "wand.pickup");
            AddHook(
                typeof(EntityItemsComponent),
                "OnQuickcastItemUsage",
                new[] { typeof(Inputs.SpookedInputEvent) },
                "ITEM",
                "quickcast.request");
            AddHook(
                typeof(EntityItemsComponent),
                nameof(EntityItemsComponent.OnCardItemUsage),
                new[] { typeof(Types.ItemType) },
                "ITEM",
                "card.request");
            AddHook(
                typeof(EntityItemsComponent),
                "Handle",
                new[] { typeof(Types.ItemType) },
                "ITEM",
                "use.applied");
            AddHook(
                typeof(EntityItemsComponent),
                "DropItem",
                new[] { typeof(Types.ItemType) },
                "ITEM",
                "drop");
        }

        if (_configuration?.LogSkillsAndPerks.Value == true)
        {
            AddHook(
                typeof(EntitySkillsComponent),
                nameof(EntitySkillsComponent.OnFirstSkillStartButton),
                Type.EmptyTypes,
                "SKILL",
                "first.request");
            AddHook(
                typeof(EntitySkillsComponent),
                nameof(EntitySkillsComponent.OnSecondSkillStartButton),
                Type.EmptyTypes,
                "SKILL",
                "second.request");
            AddHook(
                typeof(EntitySkillsComponent),
                nameof(EntitySkillsComponent.OnSkillCardUsage),
                new[] { typeof(Types.SpookedSkillType) },
                "SKILL",
                "card.request");
            AddHook(
                typeof(EntitySkillsComponent),
                nameof(EntitySkillsComponent.UseBooSkill),
                Type.EmptyTypes,
                "SKILL",
                "boo.request");
            AddHook(
                typeof(EntitySkillsComponent),
                "HostValidateAndUseSkill",
                new[] { typeof(bool) },
                "SKILL",
                "validate-and-use");
            AddHook(
                typeof(EntitySkillsComponent),
                "RPC_AfterSkill",
                new[] { typeof(Types.SpookedSkillType) },
                "SKILL",
                "use.applied");
            AddHook(
                typeof(MainBoostersViewModel),
                nameof(MainBoostersViewModel.OnEquipActionButton),
                Type.EmptyTypes,
                "PERK",
                "equip.request");
            AddHook(
                typeof(MainBoostersViewModel),
                "TreeSkillSelected",
                new[] { typeof(Il2CppSystem.Object), typeof(Il2CppSystem.EventArgs) },
                "PERK",
                "selection.changed");
            AddHook(
                typeof(MainBoostersViewModel),
                "TreeSkillEquipped",
                new[] { typeof(Il2CppSystem.Object), typeof(Il2CppSystem.EventArgs) },
                "PERK",
                "equip.applied");
            AddHook(
                typeof(SeekerSelectionViewModel),
                nameof(SeekerSelectionViewModel.OnLeftArrowClick),
                Type.EmptyTypes,
                "SELECTION",
                "seeker.left");
            AddHook(
                typeof(SeekerSelectionViewModel),
                nameof(SeekerSelectionViewModel.OnRightArrowClick),
                Type.EmptyTypes,
                "SELECTION",
                "seeker.right");
            AddHook(
                typeof(SeekerSelectionViewModel),
                "OnConfirm",
                new[] { typeof(Inputs.SpookedInputEvent) },
                "SELECTION",
                "seeker.confirm");
        }

        if (_configuration?.LogBuffsAndStuns.Value == true)
        {
            AddHook(
                typeof(EntityBuffsComponent),
                nameof(EntityBuffsComponent.HostApplyBuff),
                new[] { typeof(int), typeof(Types.SpookedBuffType), typeof(float) },
                "BUFF",
                "apply");
            AddHook(
                typeof(EntityBuffsComponent),
                nameof(EntityBuffsComponent.RemoveBuff),
                new[] { typeof(Types.SpookedBuffType) },
                "BUFF",
                "remove");
        }

        if (_configuration?.LogButtonPresses.Value == true)
        {
            AddHook(typeof(Button), "Press", Type.EmptyTypes, "UI", "button.press");
        }

        return EventHooks.Keys.ToArray();
    }

    public static RuntimeEventScope BeginPatchedEvent(
        MethodBase originalMethod,
        object? instance,
        object[] arguments)
    {
        if (_writer is null || !EventHooks.TryGetValue(originalMethod, out var hook))
        {
            return default;
        }

        var category = ResolveCategory(hook.Category, arguments);
        var id = Interlocked.Increment(ref _eventId);
        var startedTimestamp = Stopwatch.GetTimestamp();
        var details = DescribeArguments(originalMethod, arguments);
        var state = DescribeState(instance);
        var summary = $"{id}:{category}:{hook.Action}";
        var scope = new RuntimeEventScope(
            id,
            startedTimestamp,
            category,
            hook.Action,
            summary,
            instance);

        var scopes = _threadEventScopes ??= new Stack<RuntimeEventScope>(8);
        scopes.Push(scope);
        if (Environment.CurrentManagedThreadId == _mainThreadId)
        {
            lock (EventStateGate)
            {
                _lastStartedEvent = summary;
                _activeMainThreadEvents = string.Join(" > ", scopes.Reverse().Select(item => item.Summary));
            }
        }

        WriteRecord("BEGIN", category, hook.Action, id, null, details, state, null);
        return scope;
    }

    public static void EndPatchedEvent(RuntimeEventScope scope, Exception? exception)
    {
        if (scope.EventId == 0)
        {
            return;
        }

        var elapsedMilliseconds = ToMilliseconds(Stopwatch.GetTimestamp() - scope.StartTimestamp);
        var state = DescribeState(scope.Instance);
        WriteRecord(
            exception is null ? "END" : "FAIL",
            scope.Category,
            scope.Action,
            scope.EventId,
            elapsedMilliseconds,
            string.Empty,
            state,
            exception);

        var scopes = _threadEventScopes;
        if (scopes is not null && scopes.Count > 0)
        {
            if (scopes.Peek().EventId == scope.EventId)
            {
                scopes.Pop();
            }
            else
            {
                var remaining = scopes.Where(item => item.EventId != scope.EventId).Reverse().ToArray();
                scopes.Clear();
                foreach (var item in remaining)
                {
                    scopes.Push(item);
                }
            }
        }

        if (Environment.CurrentManagedThreadId == _mainThreadId)
        {
            lock (EventStateGate)
            {
                _lastCompletedEvent = scope.Summary;
                _activeMainThreadEvents = scopes is { Count: > 0 }
                    ? string.Join(" > ", scopes.Reverse().Select(item => item.Summary))
                    : "none";
            }
        }
    }

    public static void ObserveFrame()
    {
        var now = Stopwatch.GetTimestamp();
        var previous = Interlocked.Exchange(ref _lastHeartbeatTimestamp, now);
        if (previous == 0)
        {
            return;
        }

        var elapsedMilliseconds = ToMilliseconds(now - previous);
        if (Interlocked.Exchange(ref _freezeActive, 0) != 0)
        {
            var freezeDuration = ToMilliseconds(now - Interlocked.Read(ref _freezeStartTimestamp));
            WriteRecord(
                "FREEZE_END",
                "FREEZE",
                "main-thread-recovered",
                0,
                freezeDuration,
                BuildWatchdogContext($"heartbeatGapMs={elapsedMilliseconds:F1}"),
                string.Empty,
                null);
            return;
        }

        if (!ApplicationMonitoringEnabled)
        {
            return;
        }

        if (elapsedMilliseconds >= FreezeThresholdMilliseconds)
        {
            WriteRecord(
                "FREEZE_RECOVERED",
                "FREEZE",
                "detected-on-recovery",
                0,
                elapsedMilliseconds,
                BuildWatchdogContext("watchdogThreadWasAlsoDelayed=true"),
                string.Empty,
                null);
        }
        else if (elapsedMilliseconds >= HitchThresholdMilliseconds)
        {
            WriteRecord(
                "HITCH",
                "FREEZE",
                "frame-delay",
                0,
                elapsedMilliseconds,
                BuildWatchdogContext(string.Empty),
                string.Empty,
                null);
        }
    }

    public static void ObserveApplicationFocus(bool focused)
    {
        Interlocked.Exchange(ref _applicationFocused, focused ? 1 : 0);
        Interlocked.Exchange(ref _lastHeartbeatTimestamp, Stopwatch.GetTimestamp());
        if (!focused)
        {
            Interlocked.Exchange(ref _freezeActive, 0);
        }

        WriteRecord(
            "STATE",
            "APPLICATION",
            "focus",
            0,
            null,
            $"focused={focused.ToString().ToLowerInvariant()}",
            string.Empty,
            null);
    }

    public static void ObserveApplicationPause(bool paused)
    {
        Interlocked.Exchange(ref _applicationPaused, paused ? 1 : 0);
        Interlocked.Exchange(ref _lastHeartbeatTimestamp, Stopwatch.GetTimestamp());
        if (paused)
        {
            Interlocked.Exchange(ref _freezeActive, 0);
        }

        WriteRecord(
            "STATE",
            "APPLICATION",
            "pause",
            0,
            null,
            $"paused={paused.ToString().ToLowerInvariant()}",
            string.Empty,
            null);
    }

    public static void Shutdown()
    {
        if (Interlocked.Exchange(ref _shutdown, 1) != 0)
        {
            return;
        }

        _watchdogTimer?.Dispose();
        _watchdogTimer = null;
        WriteRecord(
            "SESSION_END",
            "SYSTEM",
            RuntimeProfilerPlugin.PluginName,
            0,
            null,
            string.Empty,
            string.Empty,
            null);
        _writer?.Dispose();
        _writer = null;
    }

    private static int FreezeThresholdMilliseconds =>
        Math.Max(250, _configuration?.FreezeThresholdMilliseconds.Value ?? 1000);

    private static int HitchThresholdMilliseconds =>
        Math.Max(50, _configuration?.HitchThresholdMilliseconds.Value ?? 250);

    private static int WatchdogPollMilliseconds =>
        Math.Clamp(_configuration?.WatchdogPollMilliseconds.Value ?? 100, 50, 1000);

    private static bool ApplicationMonitoringEnabled =>
        (Volatile.Read(ref _applicationFocused) != 0 && Volatile.Read(ref _applicationPaused) == 0)
        || _configuration?.DetectWhileUnfocused.Value == true;

    private static void AttachHeartbeatWatcher()
    {
        ClassInjector.RegisterTypeInIl2Cpp<RuntimeProfilerWatcher>();
        var watcherObject = new GameObject("RuntimeEventLoggerHeartbeat");
        UnityEngine.Object.DontDestroyOnLoad(watcherObject);
        watcherObject.hideFlags = HideFlags.HideAndDontSave;
        watcherObject.AddComponent<RuntimeProfilerWatcher>();
    }

    private static void CheckMainThreadHeartbeat()
    {
        if (Interlocked.Exchange(ref _watchdogCheckActive, 1) != 0)
        {
            return;
        }

        try
        {
            if (Volatile.Read(ref _shutdown) != 0 || !ApplicationMonitoringEnabled)
            {
                return;
            }

            var now = Stopwatch.GetTimestamp();
            var lastHeartbeat = Interlocked.Read(ref _lastHeartbeatTimestamp);
            var stalledMilliseconds = ToMilliseconds(now - lastHeartbeat);
            if (stalledMilliseconds < FreezeThresholdMilliseconds
                || Interlocked.CompareExchange(ref _freezeActive, 1, 0) != 0)
            {
                return;
            }

            Interlocked.Exchange(ref _freezeStartTimestamp, lastHeartbeat);
            WriteRecord(
                "FREEZE_START",
                "FREEZE",
                "main-thread-unresponsive",
                0,
                stalledMilliseconds,
                BuildWatchdogContext($"detectedAfterMs={stalledMilliseconds:F1}"),
                string.Empty,
                null);
        }
        catch (Exception exception)
        {
            WriteRecord("ERROR", "FREEZE", "watchdog", 0, null, string.Empty, string.Empty, exception);
        }
        finally
        {
            Volatile.Write(ref _watchdogCheckActive, 0);
        }
    }

    private static void AddHook(
        Type declaringType,
        string methodName,
        Type[] parameterTypes,
        string category,
        string action)
    {
        var method = AccessTools.DeclaredMethod(declaringType, methodName, parameterTypes);
        if (method is null)
        {
            WriteRecord(
                "WARNING",
                "SYSTEM",
                "hook-missing",
                0,
                null,
                $"method={declaringType.FullName}.{methodName}",
                string.Empty,
                null);
            return;
        }

        EventHooks[method] = new EventHookDefinition(category, action);
    }

    private static string ToActionName(string rpcMethodName)
    {
        var itemName = rpcMethodName
            .Replace("RPC_On", string.Empty, StringComparison.Ordinal)
            .Replace("PickUp", string.Empty, StringComparison.Ordinal);
        return $"{itemName.ToLowerInvariant()}.pickup";
    }

    private static string ResolveCategory(string category, IReadOnlyList<object> arguments)
    {
        if (!string.Equals(category, "BUFF", StringComparison.Ordinal))
        {
            return category;
        }

        foreach (var argument in arguments)
        {
            if (argument is Types.SpookedBuffType buffType && IsStun(buffType))
            {
                return "STUN";
            }
        }

        return category;
    }

    private static bool IsStun(Types.SpookedBuffType buffType)
    {
        var name = buffType.ToString();
        return name.Contains("Stun", StringComparison.OrdinalIgnoreCase)
               || buffType == Types.SpookedBuffType.BananaFail
               || buffType == Types.SpookedBuffType.Slip;
    }

    private static string DescribeArguments(MethodBase method, IReadOnlyList<object> arguments)
    {
        var parameters = method.GetParameters();
        var items = new List<string>(arguments.Count);
        for (var index = 0; index < arguments.Count; index++)
        {
            var name = index < parameters.Length ? parameters[index].Name : $"arg{index}";
            items.Add($"{name}={FormatValue(arguments[index])}");
        }

        return string.Join("; ", items);
    }

    private static string DescribeState(object? instance)
    {
        try
        {
            return instance switch
            {
                Button button => DescribeButton(button),
                Door door =>
                    $"{DescribeInteractable(door)}; isOpen={door.IsOpen.ToString().ToLowerInvariant()}",
                Interactable interactable => DescribeInteractable(interactable),
                EntitySkillsComponent skills =>
                    $"{DescribeNetworkBehaviour(skills)}; first={skills.FirstSkillType}; second={skills.SecondSkillType}; "
                    + $"firstCooldown={skills.FirstSkillCooldown:F3}; secondCooldown={skills.SecondSkillCooldown:F3}; "
                    + $"duringPropChange={skills.DuringPropChange.ToString().ToLowerInvariant()}",
                EntityBuffsComponent buffs =>
                    $"{DescribeNetworkBehaviour(buffs)}; stunned={buffs.IsStuned.ToString().ToLowerInvariant()}; "
                    + $"canMove={buffs.CanMove.ToString().ToLowerInvariant()}; "
                    + $"blocked={buffs.BlockInputs.ToString().ToLowerInvariant()}",
                MainBoostersViewModel boosters =>
                    $"skill={boosters.CurrentSkillSelected}; buttonState={boosters.CurrentSelectedButtonState}; "
                    + $"blocked={boosters.IsCurrentSkillSelectedBlocked.ToString().ToLowerInvariant()}; "
                    + $"booster={boosters.CurrentBoosterSelected}",
                SeekerSelectionViewModel selection =>
                    $"seeker={selection._chosenSeeker}; confirmed={selection._confirm.ToString().ToLowerInvariant()}; "
                    + $"selectionIndex={selection._currentSelectionIndex}; shift={selection._shift}; "
                    + $"open={selection._isOpen.ToString().ToLowerInvariant()}",
                NetworkBehaviour networkBehaviour => DescribeNetworkBehaviour(networkBehaviour),
                Component component =>
                    $"object={GetHierarchyPath(component.transform)}; "
                    + $"active={component.gameObject.activeInHierarchy.ToString().ToLowerInvariant()}",
                Il2CppObjectBase il2CppObject =>
                    $"type={il2CppObject.GetType().FullName}; pointer=0x{il2CppObject.Pointer:X}",
                null => "instance=null",
                _ => $"type={instance.GetType().FullName}"
            };
        }
        catch (Exception exception)
        {
            return $"state-unavailable={exception.GetType().Name}";
        }
    }

    private static string DescribeButton(Button button)
    {
        var selectedObject = EventSystem.current?.currentSelectedGameObject;
        var customState = button is SpookedOutlineButton outlineButton
            ? $"; customSelected={outlineButton._isSelected.ToString().ToLowerInvariant()}"
              + $"; highlighted={outlineButton._isHiglighted.ToString().ToLowerInvariant()}"
            : string.Empty;
        return $"object={GetHierarchyPath(button.transform)}; scene={button.gameObject.scene.name}; "
               + $"interactable={button.interactable.ToString().ToLowerInvariant()}; "
               + $"effectiveInteractable={button.IsInteractable().ToString().ToLowerInvariant()}; "
               + $"enabled={button.enabled.ToString().ToLowerInvariant()}; "
               + $"active={button.gameObject.activeInHierarchy.ToString().ToLowerInvariant()}; "
               + $"selected={(selectedObject == button.gameObject).ToString().ToLowerInvariant()}"
               + customState;
    }

    private static string DescribeInteractable(Interactable interactable)
    {
        return $"object={GetHierarchyPath(interactable.transform)}; type={interactable.InteractableType}; "
               + $"networkId={interactable.NetworkObjectId}; "
               + $"playerCurrentlyUsing={interactable.PlayerCurrentlyUsing}; "
               + $"position={FormatVector(interactable.Position)}";
    }

    private static string DescribeNetworkBehaviour(NetworkBehaviour behaviour)
    {
        return $"object={GetHierarchyPath(behaviour.transform)}; "
               + $"inputAuthority={behaviour.HasInputAuthority.ToString().ToLowerInvariant()}; "
               + $"stateAuthority={behaviour.HasStateAuthority.ToString().ToLowerInvariant()}; "
               + $"proxy={behaviour.IsProxy.ToString().ToLowerInvariant()}";
    }

    private static string GetHierarchyPath(Transform? transform)
    {
        if (transform is null)
        {
            return "<no-transform>";
        }

        var names = new Stack<string>();
        var current = transform;
        for (var depth = 0; current is not null && depth < 16; depth++)
        {
            names.Push(current.name);
            current = current.parent;
        }

        return string.Join("/", names);
    }

    private static string FormatValue(object? value)
    {
        try
        {
            return value switch
            {
                null => "null",
                string text => text,
                bool boolean => boolean.ToString().ToLowerInvariant(),
                float single => single.ToString("F3", CultureInfo.InvariantCulture),
                double number => number.ToString("F3", CultureInfo.InvariantCulture),
                Vector3 vector => FormatVector(vector),
                Interactable interactable => DescribeInteractable(interactable),
                UnityEngine.Object unityObject => $"{unityObject.GetType().Name}:{unityObject.name}",
                Enum enumValue => $"{enumValue.GetType().Name}.{enumValue}",
                byte or sbyte or short or ushort or int or uint or long or ulong or decimal =>
                    Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
                Il2CppObjectBase il2CppObject =>
                    $"{il2CppObject.GetType().Name}@0x{il2CppObject.Pointer:X}",
                _ => value.GetType().FullName ?? value.GetType().Name
            };
        }
        catch (Exception exception)
        {
            return $"<unavailable:{exception.GetType().Name}>";
        }
    }

    private static string FormatVector(Vector3 vector) =>
        FormattableString.Invariant($"({vector.x:F3},{vector.y:F3},{vector.z:F3})");

    private static string BuildWatchdogContext(string prefix)
    {
        lock (EventStateGate)
        {
            var suffix =
                $"active={_activeMainThreadEvents}; lastStarted={_lastStartedEvent}; lastCompleted={_lastCompletedEvent}";
            return string.IsNullOrEmpty(prefix) ? suffix : $"{prefix}; {suffix}";
        }
    }

    private static void WriteRecord(
        string kind,
        string category,
        string action,
        long eventId,
        double? durationMilliseconds,
        string details,
        string state,
        Exception? exception)
    {
        var writer = _writer;
        if (writer is null)
        {
            return;
        }

        var sequence = Interlocked.Increment(ref _sequence);
        var line = string.Join(
            '\t',
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            SessionClock.Elapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture),
            sequence.ToString(CultureInfo.InvariantCulture),
            Environment.CurrentManagedThreadId.ToString(CultureInfo.InvariantCulture),
            Sanitize(kind),
            Sanitize(category),
            Sanitize(action),
            eventId == 0 ? string.Empty : eventId.ToString(CultureInfo.InvariantCulture),
            durationMilliseconds?.ToString("F3", CultureInfo.InvariantCulture) ?? string.Empty,
            Sanitize(details),
            Sanitize(state),
            Sanitize(exception?.ToString() ?? string.Empty));
        writer.Enqueue(line);
    }

    private static string Sanitize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(Math.Min(value.Length, 512));
        foreach (var character in value)
        {
            builder.Append(character switch
            {
                '\t' => ' ',
                '\r' => ' ',
                '\n' => ' ',
                _ => character
            });
            if (builder.Length >= 2048)
            {
                builder.Append("...");
                break;
            }
        }

        return builder.ToString();
    }

    private static double ToMilliseconds(long stopwatchTicks) =>
        stopwatchTicks * 1000d / Stopwatch.Frequency;

    private static void OnProcessExit(object? sender, EventArgs args)
    {
        Shutdown();
    }

    private readonly record struct EventHookDefinition(string Category, string Action);
}

internal readonly record struct RuntimeEventScope(
    long EventId,
    long StartTimestamp,
    string Category,
    string Action,
    string Summary,
    object? Instance);
