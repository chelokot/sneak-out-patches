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

[HarmonyPatch(typeof(Globe), "Use")]
internal static class GlobeLaunchUsePatch
{
    [HarmonyPostfix]
    private static void Postfix(Globe __instance)
    {
        GlobeLaunchRuntime.ObserveVanillaInteractionState(__instance);
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
