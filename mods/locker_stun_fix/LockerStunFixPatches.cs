using Gameplay.Interactions;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace SneakOut.LockerStunFix;

[HarmonyPatch(typeof(Locker), nameof(Locker.TryToOpen), new[] { typeof(int) })]
internal static class LockerTryToOpenPatch
{
    [HarmonyPrefix]
    private static void Prefix(Locker __instance, int playerId)
    {
        LockerStunFixRuntime.ObserveOpen(__instance, playerId, nameof(Locker.TryToOpen));
    }
}

[HarmonyPatch(typeof(Locker), nameof(Locker.Open), new[] { typeof(int), typeof(bool) })]
internal static class LockerOpenPatch
{
    [HarmonyPrefix]
    private static void Prefix(Locker __instance, int playerId)
    {
        LockerStunFixRuntime.ObserveOpen(__instance, playerId, nameof(Locker.Open));
    }
}

[HarmonyPatch(typeof(Locker), nameof(Locker.Close), new[] { typeof(int) })]
internal static class LockerClosePatch
{
    [HarmonyPrefix]
    private static void Prefix(Locker __instance)
    {
        LockerStunFixRuntime.ClearCycle(__instance, nameof(Locker.Close));
    }
}

[HarmonyPatch(typeof(Locker), nameof(Locker.Hide), new[] { typeof(int) })]
internal static class LockerHidePatch
{
    [HarmonyPrefix]
    private static void Prefix(Locker __instance)
    {
        LockerStunFixRuntime.ClearCycle(__instance, nameof(Locker.Hide));
    }
}

[HarmonyPatch(typeof(Locker), nameof(Locker.HideFast), new[] { typeof(int) })]
internal static class LockerHideFastPatch
{
    [HarmonyPrefix]
    private static void Prefix(Locker __instance)
    {
        LockerStunFixRuntime.ClearCycle(__instance, nameof(Locker.HideFast));
    }
}

[HarmonyPatch(typeof(Locker), nameof(Locker.HandleBooSkill), new[] { typeof(int) })]
internal static class LockerHandleBooSkillPatch
{
    [HarmonyPrefix]
    private static bool Prefix(Locker __instance, int playerId, out bool __state)
    {
        __state = false;
        if (!LockerStunFixRuntime.ShouldApplyLockerStun(__instance, playerId))
        {
            return false;
        }

        __state = LockerStunFixRuntime.TryBeginBalancedBooQuery(__instance, playerId);
        return true;
    }

    [HarmonyFinalizer]
    private static Exception? Finalizer(Exception? __exception, bool __state)
    {
        LockerStunFixRuntime.EndBalancedBooQuery(__state);
        return __exception;
    }
}

[HarmonyPatch(
    typeof(Physics),
    nameof(Physics.OverlapSphere),
    new[]
    {
        typeof(Vector3),
        typeof(float),
        typeof(int)
    })]
internal static class LockerBooOverlapSpherePatch
{
    [HarmonyPrefix]
    private static void Prefix(ref Vector3 __0, ref float __1, out bool __state)
    {
        __state = LockerStunFixRuntime.TryPrepareBalancedBooOverlap(ref __0, ref __1);
    }

    [HarmonyPostfix]
    private static void Postfix(
        bool __state,
        ref Il2CppReferenceArray<Collider> __result)
    {
        LockerStunFixRuntime.FilterBalancedBooOverlap(__state, ref __result);
    }
}
