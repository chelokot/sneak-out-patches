using System.Collections.Concurrent;
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
    private static readonly ThreadLocal<Stack<ActiveFrame>> ThreadFrames = new(() => new Stack<ActiveFrame>());
    private static readonly ConcurrentDictionary<MethodBase, string> MethodSignatures = new();
    private static readonly ConcurrentDictionary<string, MethodStatistics> MethodStats = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, EdgeStatistics> EdgeStats = new(StringComparer.Ordinal);

    private static ManualLogSource? _logger;
    private static RuntimeProfilerConfig? _configuration;
    private static Harmony? _harmony;
    private static string? _reportPath;
    private static int _initialized;
    private static int _reportWritten;
    private static int _patchedMethodCount;

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
        var includeNamespacePrefixes = SplitConfigList(_configuration.IncludeNamespacePrefixes.Value);
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

                    candidateMethods.Add(method);
                }
            }
        }

        foreach (var method in candidateMethods
                     .OrderBy(method => method.DeclaringType?.FullName, StringComparer.Ordinal)
                     .ThenBy(method => method.Name, StringComparer.Ordinal)
                     .Take(_configuration.MaxPatchedMethods.Value))
        {
            try
            {
                _harmony!.Patch(method, prefix: new HarmonyMethod(prefix), finalizer: new HarmonyMethod(finalizer));
                _patchedMethodCount++;
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

    private static void ProfilePrefix(MethodBase __originalMethod)
    {
        var stack = ThreadFrames.Value!;
        var parentSignature = stack.Count > 0 ? stack.Peek().Signature : null;
        stack.Push(new ActiveFrame(GetSignature(__originalMethod), parentSignature, Stopwatch.GetTimestamp()));
    }

    private static Exception? ProfileFinalizer(Exception? __exception)
    {
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

        var methodStatistics = MethodStats.GetOrAdd(frame.Signature, _ => new MethodStatistics());
        methodStatistics.Record(elapsedTicks, selfTicks, __exception is not null);

        if (frame.ParentSignature is not null)
        {
            var edgeKey = $"{frame.ParentSignature} -> {frame.Signature}";
            var edgeStatistics = EdgeStats.GetOrAdd(edgeKey, _ => new EdgeStatistics(frame.ParentSignature, frame.Signature));
            edgeStatistics.Record(elapsedTicks);
        }

        return __exception;
    }

    private static string GetSignature(MethodBase method)
    {
        return MethodSignatures.GetOrAdd(method, static currentMethod =>
        {
            var parameters = string.Join(
                ", ",
                currentMethod.GetParameters().Select(parameter => $"{GetFriendlyTypeName(parameter.ParameterType)} {parameter.Name}"));
            var returnType = currentMethod is MethodInfo info ? GetFriendlyTypeName(info.ReturnType) : "void";
            var declaringType = currentMethod.DeclaringType?.FullName ?? "<global>";
            return $"{returnType} {declaringType}.{currentMethod.Name}({parameters})";
        });
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
            var reportDirectory = Path.Combine(Paths.BepInExRootPath, "profile-reports");
            Directory.CreateDirectory(reportDirectory);
            _reportPath = Path.Combine(
                reportDirectory,
                $"runtime-profiler-{DateTime.UtcNow:yyyyMMdd-HHmmss}.txt");

            var builder = new StringBuilder();
            builder.AppendLine("SneakOut Runtime Profiler Report");
            builder.AppendLine($"GeneratedAtUtc: {DateTimeOffset.UtcNow:O}");
            builder.AppendLine($"PatchedMethods: {_patchedMethodCount}");
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

        foreach (var item in MethodStats
                     .Select(pair => new MethodReportRow(pair.Key, pair.Value.Snapshot()))
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

        foreach (var item in EdgeStats
                     .Select(pair => pair.Value.Snapshot())
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
        public ActiveFrame(string signature, string? parentSignature, long startTimestamp)
        {
            Signature = signature;
            ParentSignature = parentSignature;
            StartTimestamp = startTimestamp;
            ChildTicks = 0;
        }

        public string Signature { get; }

        public string? ParentSignature { get; }

        public long StartTimestamp { get; }

        public long ChildTicks { get; set; }
    }

    private sealed class MethodStatistics
    {
        private readonly object _gate = new();
        private long _calls;
        private long _exceptions;
        private long _totalTicks;
        private long _selfTicks;
        private long _maxTicks;

        public void Record(long totalTicks, long selfTicks, bool threw)
        {
            lock (_gate)
            {
                _calls++;
                _totalTicks += totalTicks;
                _selfTicks += selfTicks;
                if (totalTicks > _maxTicks)
                {
                    _maxTicks = totalTicks;
                }

                if (threw)
                {
                    _exceptions++;
                }
            }
        }

        public MethodSnapshot Snapshot()
        {
            lock (_gate)
            {
                return new MethodSnapshot(
                    _calls,
                    _exceptions,
                    _totalTicks,
                    _selfTicks,
                    _maxTicks,
                    _calls == 0 ? 0 : _totalTicks / _calls);
            }
        }
    }

    private sealed class EdgeStatistics
    {
        private readonly object _gate = new();
        private long _calls;
        private long _totalTicks;

        public EdgeStatistics(string parentSignature, string childSignature)
        {
            ParentSignature = parentSignature;
            ChildSignature = childSignature;
        }

        public string ParentSignature { get; }

        public string ChildSignature { get; }

        public void Record(long totalTicks)
        {
            lock (_gate)
            {
                _calls++;
                _totalTicks += totalTicks;
            }
        }

        public EdgeSnapshot Snapshot()
        {
            lock (_gate)
            {
                return new EdgeSnapshot(
                    ParentSignature,
                    ChildSignature,
                    _calls,
                    _totalTicks,
                    _calls == 0 ? 0 : _totalTicks / _calls);
            }
        }
    }

    private readonly record struct MethodReportRow(string Signature, MethodSnapshot Snapshot);
    private readonly record struct MethodSnapshot(long Calls, long Exceptions, long TotalTicks, long SelfTicks, long MaxTicks, long AverageTicks);
    private readonly record struct EdgeSnapshot(string ParentSignature, string ChildSignature, long Calls, long TotalTicks, long AverageTicks);
}
