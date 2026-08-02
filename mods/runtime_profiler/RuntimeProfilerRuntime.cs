using System.Diagnostics;
using System.Reflection;
using System.Text;
using HarmonyLib;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace SneakOut.RuntimeProfiler;

internal static class RuntimeProfilerRuntime
{
    private static readonly ThreadLocal<Stack<ActiveFrame>> ThreadFrames = new(() => new Stack<ActiveFrame>(32));
    private static readonly Dictionary<MethodBase, int> MethodIds = new();
    private static MethodDescriptor[] _methods = Array.Empty<MethodDescriptor>();
    private static MethodStatistics[] _methodStats = Array.Empty<MethodStatistics>();
    private static long[] _edgeCalls = Array.Empty<long>();
    private static long[] _edgeTotalTicks = Array.Empty<long>();

    private static ManualLogSource? _logger;
    private static RuntimeProfilerConfig? _configuration;
    private static Harmony? _harmony;
    private static string? _reportPath;
    private static Timer? _reportTimer;
    private static int _initialized;
    private static int _reportWritten;
    private static int _patchedMethodCount;
    private static long _profileStartTimestamp;

    public static void Initialize(ManualLogSource logger, RuntimeProfilerConfig configuration)
    {
        _logger = logger;
        _configuration = configuration;

        if (!configuration.EnableMod.Value)
        {
            return;
        }

        if (Interlocked.Exchange(ref _initialized, 1) != 0)
        {
            return;
        }

        _harmony = new Harmony(RuntimeProfilerPlugin.PluginGuid);
        PatchConfiguredMethods();
        _profileStartTimestamp = Stopwatch.GetTimestamp();
        _reportTimer = new Timer(
            _ => WriteReport(),
            null,
            TimeSpan.FromSeconds(
                Math.Max(0, configuration.WarmupSeconds.Value)
                + Math.Max(10, configuration.ReportAfterSeconds.Value)),
            Timeout.InfiniteTimeSpan);
        Application.add_quitting(new Action(WriteReport));
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        LogInfo($"Patched {_patchedMethodCount} methods");
    }

    private static void OnProcessExit(object? sender, EventArgs args)
    {
        WriteReport();
    }

    private static void PatchConfiguredMethods()
    {
        var prefix = AccessTools.Method(typeof(RuntimeProfilerRuntime), nameof(ProfilePrefix));
        var finalizer = AccessTools.Method(typeof(RuntimeProfilerRuntime), nameof(ProfileFinalizer));
        var targetAssemblies = new HashSet<string>(
            SplitConfigList(_configuration!.TargetAssemblies.Value),
            StringComparer.Ordinal);
        foreach (var targetAssembly in targetAssemblies)
        {
            if (AppDomain.CurrentDomain.GetAssemblies().Any(assembly =>
                    string.Equals(assembly.GetName().Name, targetAssembly, StringComparison.Ordinal)))
            {
                continue;
            }

            try
            {
                Assembly.Load(new AssemblyName(targetAssembly));
            }
            catch (Exception exception)
            {
                LogInfo($"Target assembly {targetAssembly} could not be loaded: {exception.Message}");
            }
        }

        var includeNamespacePrefixes = SplitConfigList(_configuration.IncludeNamespacePrefixes.Value);
        var targetMethodPatterns = SplitConfigList(_configuration.TargetMethodPatterns.Value);
        var excludeNamespacePrefixes = SplitConfigList(_configuration.ExcludeNamespacePrefixes.Value);
        var candidateMethods = new List<MethodBase>();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!targetAssemblies.Contains(assembly.GetName().Name ?? string.Empty))
            {
                continue;
            }

            foreach (var type in GetLoadableTypes(assembly))
            {
                if (!ShouldIncludeType(type, includeNamespacePrefixes, excludeNamespacePrefixes))
                {
                    continue;
                }

                foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (!ShouldIncludeMethod(method))
                    {
                        continue;
                    }

                    if (!ShouldIncludeMethodByPattern(method, targetMethodPatterns))
                    {
                        continue;
                    }

                    candidateMethods.Add(method);
                }
            }
        }

        var selectedMethods = candidateMethods
            .OrderBy(method => method.DeclaringType?.FullName, StringComparer.Ordinal)
            .ThenBy(method => method.Name, StringComparer.Ordinal)
            .Take(_configuration.MaxPatchedMethods.Value)
            .ToArray();
        _methods = selectedMethods
            .Select((method, methodId) => new MethodDescriptor(methodId, GetSignature(method), false))
            .ToArray();
        _methodStats = selectedMethods.Select(_ => new MethodStatistics()).ToArray();
        _edgeCalls = new long[selectedMethods.Length * selectedMethods.Length];
        _edgeTotalTicks = new long[selectedMethods.Length * selectedMethods.Length];
        for (var methodId = 0; methodId < selectedMethods.Length; methodId++)
        {
            MethodIds[selectedMethods[methodId]] = methodId;
        }

        for (var methodId = 0; methodId < selectedMethods.Length; methodId++)
        {
            var method = selectedMethods[methodId];
            try
            {
                _harmony!.Patch(method, prefix: new HarmonyMethod(prefix), finalizer: new HarmonyMethod(finalizer));
                _methods[methodId] = _methods[methodId] with { Patched = true };
                _patchedMethodCount++;
                LogInfo($"Patched [{methodId}] {_methods[methodId].Signature}");
            }
            catch (Exception exception)
            {
                LogInfo($"Failed to patch {GetSignature(method)}: {exception.Message}");
            }
        }
    }

    private static bool ShouldIncludeType(Type type, IReadOnlyList<string> includeNamespacePrefixes, IReadOnlyList<string> excludeNamespacePrefixes)
    {
        var fullName = type.FullName ?? string.Empty;

        if (fullName.Length == 0)
        {
            return false;
        }

        if (excludeNamespacePrefixes.Any(prefix => fullName.StartsWith(prefix, StringComparison.Ordinal)))
        {
            return false;
        }

        return includeNamespacePrefixes.Count == 0 ||
               includeNamespacePrefixes.Any(prefix => fullName.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static bool ShouldIncludeMethod(MethodInfo method)
    {
        if (method.IsAbstract)
        {
            return false;
        }

        if (method.ContainsGenericParameters || method.IsGenericMethodDefinition)
        {
            return false;
        }

        if (!_configuration!.IncludeConstructors.Value && (method.IsConstructor || method.IsSpecialName && method.Name == ".cctor"))
        {
            return false;
        }

        if (!_configuration.IncludePropertyAccessors.Value && method.IsSpecialName &&
            (method.Name.StartsWith("get_", StringComparison.Ordinal) ||
             method.Name.StartsWith("set_", StringComparison.Ordinal) ||
             method.Name.StartsWith("add_", StringComparison.Ordinal) ||
             method.Name.StartsWith("remove_", StringComparison.Ordinal)))
        {
            return false;
        }

        if (!_configuration.IncludeCompilerGenerated.Value &&
            (method.Name.Contains('<') || (method.DeclaringType?.FullName?.Contains('<') ?? false)))
        {
            return false;
        }

        return true;
    }

    private static bool ShouldIncludeMethodByPattern(MethodInfo method, IReadOnlyList<string> targetMethodPatterns)
    {
        if (targetMethodPatterns.Count == 0)
        {
            return true;
        }

        var signature = GetSignature(method);
        return targetMethodPatterns.Any(pattern => signature.Contains(pattern, StringComparison.Ordinal));
    }

    private static IReadOnlyList<string> SplitConfigList(string value)
    {
        return value
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => item.Length > 0)
            .ToArray();
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.Where(type => type is not null)!;
        }
    }

    private static void ProfilePrefix(MethodBase __originalMethod, out bool __state)
    {
        __state = false;
        var warmupSeconds = Math.Max(0, _configuration?.WarmupSeconds.Value ?? 0);
        if (warmupSeconds > 0
            && (Stopwatch.GetTimestamp() - _profileStartTimestamp) / (double)Stopwatch.Frequency < warmupSeconds)
        {
            return;
        }

        if (!MethodIds.TryGetValue(__originalMethod, out var methodId))
        {
            return;
        }

        var stack = ThreadFrames.Value!;
        var parentMethodId = stack.Count > 0 ? stack.Peek().MethodId : -1;
        stack.Push(new ActiveFrame(methodId, parentMethodId, Stopwatch.GetTimestamp()));
        __state = true;
    }

    private static Exception? ProfileFinalizer(Exception? __exception, bool __state)
    {
        if (!__state)
        {
            return __exception;
        }

        var stack = ThreadFrames.Value!;
        if (stack.Count == 0)
        {
            return __exception;
        }

        var frame = stack.Pop();
        var elapsedTicks = Stopwatch.GetTimestamp() - frame.StartTimestamp;
        var selfTicks = elapsedTicks - frame.ChildTicks;
        if (selfTicks < 0)
        {
            selfTicks = 0;
        }

        if (stack.Count > 0)
        {
            var parent = stack.Pop();
            parent.ChildTicks += elapsedTicks;
            stack.Push(parent);
        }

        _methodStats[frame.MethodId].Record(elapsedTicks, selfTicks, __exception is not null);

        if (frame.ParentMethodId >= 0)
        {
            var edgeIndex = frame.ParentMethodId * _methods.Length + frame.MethodId;
            Interlocked.Increment(ref _edgeCalls[edgeIndex]);
            Interlocked.Add(ref _edgeTotalTicks[edgeIndex], elapsedTicks);
        }

        return __exception;
    }

    private static string GetSignature(MethodBase method)
    {
        var parameters = string.Join(
            ", ",
            method.GetParameters().Select(parameter => $"{GetFriendlyTypeName(parameter.ParameterType)} {parameter.Name}"));
        var returnType = method is MethodInfo info ? GetFriendlyTypeName(info.ReturnType) : "void";
        var declaringType = method.DeclaringType?.FullName ?? "<global>";
        return $"{returnType} {declaringType}.{method.Name}({parameters})";
    }

    private static string GetFriendlyTypeName(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.FullName ?? type.Name;
        }

        var genericDefinitionName = type.GetGenericTypeDefinition().FullName ?? type.Name;
        var tickIndex = genericDefinitionName.IndexOf('`');
        if (tickIndex >= 0)
        {
            genericDefinitionName = genericDefinitionName[..tickIndex];
        }

        var genericArguments = string.Join(", ", type.GetGenericArguments().Select(GetFriendlyTypeName));
        return $"{genericDefinitionName}<{genericArguments}>";
    }

    private static void WriteReport()
    {
        if (Interlocked.Exchange(ref _reportWritten, 1) != 0)
        {
            return;
        }

        if (_patchedMethodCount == 0)
        {
            return;
        }

        try
        {
            _reportTimer?.Dispose();
            _reportTimer = null;
            var reportDirectory = Path.Combine(Paths.BepInExRootPath, "profile-reports");
            Directory.CreateDirectory(reportDirectory);
            _reportPath = Path.Combine(
                reportDirectory,
                $"runtime-profiler-{DateTime.UtcNow:yyyyMMdd-HHmmss}.txt");

            var builder = new StringBuilder();
            builder.AppendLine("SneakOut Runtime Profiler Report");
            builder.AppendLine($"GeneratedAtUtc: {DateTimeOffset.UtcNow:O}");
            builder.AppendLine($"PatchedMethods: {_patchedMethodCount}");
            builder.AppendLine($"WarmupSeconds: {Math.Max(0, _configuration?.WarmupSeconds.Value ?? 0)}");
            builder.AppendLine($"ProfileSeconds: {Math.Max(10, _configuration?.ReportAfterSeconds.Value ?? 60)}");
            builder.AppendLine();

            AppendMethodTable(builder);
            builder.AppendLine();
            AppendEdgeTable(builder);

            File.WriteAllText(_reportPath, builder.ToString(), Encoding.UTF8);
            LogInfo($"Wrote profiler report to {_reportPath}");
        }
        catch (Exception exception)
        {
            _logger?.LogError($"Runtime profiler failed to write report: {exception}");
        }
    }

    private static void AppendMethodTable(StringBuilder builder)
    {
        builder.AppendLine("Top Methods");
        builder.AppendLine("SelfMs\tTotalMs\tAvgMs\tMaxMs\tCalls\tExceptions\tMethod");

        foreach (var item in _methods
                     .Where(method => method.Patched)
                     .Select(method => new MethodReportRow(method.Signature, _methodStats[method.MethodId].Snapshot()))
                     .OrderByDescending(row => row.Snapshot.SelfTicks)
                     .ThenByDescending(row => row.Snapshot.TotalTicks)
                     .Take(_configuration!.TopMethodCount.Value))
        {
            builder.AppendLine(
                $"{TicksToMilliseconds(item.Snapshot.SelfTicks):F3}\t" +
                $"{TicksToMilliseconds(item.Snapshot.TotalTicks):F3}\t" +
                $"{TicksToMilliseconds(item.Snapshot.AverageTicks):F3}\t" +
                $"{TicksToMilliseconds(item.Snapshot.MaxTicks):F3}\t" +
                $"{item.Snapshot.Calls}\t" +
                $"{item.Snapshot.Exceptions}\t" +
                item.Signature);
        }
    }

    private static void AppendEdgeTable(StringBuilder builder)
    {
        builder.AppendLine("Top Caller -> Callee Edges");
        builder.AppendLine("TotalMs\tCalls\tAvgMs\tEdge");

        foreach (var item in EnumerateEdgeSnapshots()
                     .OrderByDescending(snapshot => snapshot.TotalTicks)
                     .Take(_configuration!.TopEdgeCount.Value))
        {
            builder.AppendLine(
                $"{TicksToMilliseconds(item.TotalTicks):F3}\t" +
                $"{item.Calls}\t" +
                $"{TicksToMilliseconds(item.AverageTicks):F3}\t" +
                $"{item.ParentSignature} -> {item.ChildSignature}");
        }
    }

    private static double TicksToMilliseconds(long ticks)
    {
        return ticks * 1000d / Stopwatch.Frequency;
    }

    private static IEnumerable<EdgeSnapshot> EnumerateEdgeSnapshots()
    {
        for (var parentMethodId = 0; parentMethodId < _methods.Length; parentMethodId++)
        {
            for (var childMethodId = 0; childMethodId < _methods.Length; childMethodId++)
            {
                var edgeIndex = parentMethodId * _methods.Length + childMethodId;
                var calls = Interlocked.Read(ref _edgeCalls[edgeIndex]);
                if (calls == 0)
                {
                    continue;
                }

                var totalTicks = Interlocked.Read(ref _edgeTotalTicks[edgeIndex]);
                yield return new EdgeSnapshot(
                    _methods[parentMethodId].Signature,
                    _methods[childMethodId].Signature,
                    calls,
                    totalTicks,
                    totalTicks / calls);
            }
        }
    }

    private static void LogInfo(string message)
    {
        if (_configuration is null || !_configuration.EnableLogging.Value)
        {
            return;
        }

        _logger?.LogInfo(message);
    }

    private struct ActiveFrame
    {
        public ActiveFrame(int methodId, int parentMethodId, long startTimestamp)
        {
            MethodId = methodId;
            ParentMethodId = parentMethodId;
            StartTimestamp = startTimestamp;
            ChildTicks = 0;
        }

        public int MethodId { get; }

        public int ParentMethodId { get; }

        public long StartTimestamp { get; }

        public long ChildTicks { get; set; }
    }

    private sealed class MethodStatistics
    {
        private long _calls;
        private long _exceptions;
        private long _totalTicks;
        private long _selfTicks;
        private long _maxTicks;

        public void Record(long totalTicks, long selfTicks, bool threw)
        {
            Interlocked.Increment(ref _calls);
            Interlocked.Add(ref _totalTicks, totalTicks);
            Interlocked.Add(ref _selfTicks, selfTicks);
            if (threw)
            {
                Interlocked.Increment(ref _exceptions);
            }

            var observedMax = Interlocked.Read(ref _maxTicks);
            while (totalTicks > observedMax)
            {
                var previous = Interlocked.CompareExchange(ref _maxTicks, totalTicks, observedMax);
                if (previous == observedMax)
                {
                    break;
                }

                observedMax = previous;
            }
        }

        public MethodSnapshot Snapshot()
        {
            var calls = Interlocked.Read(ref _calls);
            var totalTicks = Interlocked.Read(ref _totalTicks);
            return new MethodSnapshot(
                calls,
                Interlocked.Read(ref _exceptions),
                totalTicks,
                Interlocked.Read(ref _selfTicks),
                Interlocked.Read(ref _maxTicks),
                calls == 0 ? 0 : totalTicks / calls);
        }
    }

    private readonly record struct MethodDescriptor(int MethodId, string Signature, bool Patched);
    private readonly record struct MethodReportRow(string Signature, MethodSnapshot Snapshot);
    private readonly record struct MethodSnapshot(long Calls, long Exceptions, long TotalTicks, long SelfTicks, long MaxTicks, long AverageTicks);
    private readonly record struct EdgeSnapshot(string ParentSignature, string ChildSignature, long Calls, long TotalTicks, long AverageTicks);
}
