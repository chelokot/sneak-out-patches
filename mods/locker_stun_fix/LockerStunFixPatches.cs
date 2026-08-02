using Gameplay.Interactions;
using HarmonyLib;

namespace SneakOut.LockerStunFix;

[HarmonyPatch(typeof(Locker), nameof(Locker.TryToOpen))]
internal static class LockerTryToOpenPatch
{
    private static void Prefix(Locker __instance, int playerId)
    {
        LockerStunFixRuntime.MarkSeekerOpen(__instance, playerId);
    }
}

[HarmonyPatch(typeof(Locker), nameof(Locker.HandleBooSkill))]
internal static class LockerHandleBooSkillPatch
{
    private static bool Prefix(Locker __instance, int playerId)
    {
        return LockerStunFixRuntime.ShouldApplyLockerStun(__instance, playerId);
    }
}

[HarmonyPatch(typeof(Locker), nameof(Locker.Close))]
internal static class LockerClosePatch
{
    private static void Prefix(Locker __instance)
    {
        LockerStunFixRuntime.ClearCycle(__instance);
    }
}

[HarmonyPatch(typeof(Locker), nameof(Locker.Hide))]
internal static class LockerHidePatch
{
    private static void Prefix(Locker __instance)
    {
        LockerStunFixRuntime.ClearCycle(__instance);
    }
}

[HarmonyPatch(typeof(Locker), nameof(Locker.HideFast))]
internal static class LockerHideFastPatch
{
    private static void Prefix(Locker __instance)
    {
        LockerStunFixRuntime.ClearCycle(__instance);
    }
}
