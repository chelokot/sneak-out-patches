using Gameplay.Interactions;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace SneakOut.LockerStunFix;

[HarmonyPatch(typeof(Locker), nameof(Locker.HandleBooSkill))]
internal static class LockerHandleBooSkillPatch
{
    [HarmonyPrefix]
    private static void Prefix(Locker __instance, int playerId, out bool __state)
    {
        __state = LockerStunFixRuntime.TryBeginBalancedBooQuery(__instance, playerId);
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
