using System.Reflection;
using HarmonyLib;

namespace SneakOut.RuntimeProfiler;

[HarmonyPatch]
internal static class RuntimeEventPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        return RuntimeProfilerRuntime.GetEventTargets();
    }

    [HarmonyPrefix]
    private static void Prefix(
        MethodBase __originalMethod,
        object? __instance,
        object[] __args,
        out RuntimeEventScope __state)
    {
        __state = RuntimeProfilerRuntime.BeginPatchedEvent(__originalMethod, __instance, __args);
    }

    [HarmonyFinalizer]
    private static Exception? Finalizer(Exception? __exception, RuntimeEventScope __state)
    {
        RuntimeProfilerRuntime.EndPatchedEvent(__state, __exception);
        return __exception;
    }
}
