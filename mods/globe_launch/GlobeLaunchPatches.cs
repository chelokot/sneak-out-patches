using Gameplay.Interactions;
using HarmonyLib;

namespace SneakOut.GlobeLaunch;

[HarmonyPatch(typeof(Globe), "OnAwake")]
internal static class GlobeLaunchOnAwakePatch
{
    [HarmonyPostfix]
    private static void Postfix(Globe __instance)
    {
        GlobeLaunchRuntime.Reset(__instance);
    }
}

[HarmonyPatch(typeof(Globe), "StartInteraction")]
internal static class GlobeLaunchStartInteractionPatch
{
    [HarmonyPostfix]
    private static void Postfix(Globe __instance, int internalId)
    {
        GlobeLaunchRuntime.ObserveSuccessfulHit(__instance, internalId);
    }
}

[HarmonyPatch(typeof(Globe), "Update")]
internal static class GlobeLaunchUpdatePatch
{
    [HarmonyPostfix]
    private static void Postfix(Globe __instance)
    {
        GlobeLaunchRuntime.TickFlight(__instance);
    }
}
