using Gameplay.Interactions;
using HarmonyLib;

namespace SneakOut.GlobeLaunch;

[HarmonyPatch(typeof(Globe._AddDelayedForce_d__21), "MoveNext")]
internal static class GlobeLaunchDelayedForcePatch
{
    [HarmonyPrefix]
    private static void Prefix(Globe._AddDelayedForce_d__21 __instance)
    {
        GlobeLaunchRuntime.ObserveVanillaHit(__instance);
    }
}

[HarmonyPatch(typeof(Globe), "OnAwake")]
internal static class GlobeLaunchOnAwakePatch
{
    [HarmonyPostfix]
    private static void Postfix(Globe __instance)
    {
        GlobeLaunchRuntime.Reset(__instance);
    }
}

[HarmonyPatch(typeof(Globe), "Update")]
internal static class GlobeLaunchUpdatePatch
{
    [HarmonyPostfix]
    private static void Postfix(Globe __instance)
    {
        GlobeLaunchRuntime.ObserveVanillaInteractionState(__instance);
        GlobeLaunchRuntime.TickFlight(__instance);
    }
}
