using Gameplay.Interactions;
using HarmonyLib;

namespace SneakOut.LockerStunFix;

[HarmonyPatch(typeof(Locker), nameof(Locker.TryToOpen))]
internal static class LockerTryToOpenPatch
{
    private static void Prefix(Locker __instance, int playerId)
    {
        LockerStunFixRuntime.ObserveOpen(__instance, playerId, nameof(Locker.TryToOpen));
    }
}

[HarmonyPatch(typeof(Locker), nameof(Locker.Open))]
internal static class LockerOpenPatch
{
    private static void Prefix(Locker __instance, int playerId)
    {
        LockerStunFixRuntime.ObserveOpen(__instance, playerId, nameof(Locker.Open));
    }
}

[HarmonyPatch(typeof(Locker), nameof(Locker.Close))]
internal static class LockerClosePatch
{
    private static void Prefix(Locker __instance)
    {
        LockerStunFixRuntime.ClearCycle(__instance, nameof(Locker.Close));
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

[HarmonyPatch(typeof(Locker), nameof(Locker.Hide))]
internal static class LockerHidePatch
{
    private static void Prefix(Locker __instance)
    {
        LockerStunFixRuntime.ClearCycle(__instance, nameof(Locker.Hide));
    }
}

[HarmonyPatch(typeof(Locker), nameof(Locker.HideFast))]
internal static class LockerHideFastPatch
{
    private static void Prefix(Locker __instance)
    {
        LockerStunFixRuntime.ClearCycle(__instance, nameof(Locker.HideFast));
    }
}
