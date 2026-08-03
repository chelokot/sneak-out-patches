using Gameplay.Player.Components;
using Gameplay.Skills;
using HarmonyLib;

namespace SneakOut.MagicWardrobeHookFix;

[HarmonyPatch(typeof(ButcherHook), "OnTriggerEnter")]
internal static class ButcherHookTriggerPatch
{
    private static void Postfix(ButcherHook __instance)
    {
        MagicWardrobeHookFixRuntime.MarkHookedWardrobeUser(__instance);
    }
}

[HarmonyPatch(
    typeof(EntityInteractiveComponent._InteractWithMagicWardrobe_d__76),
    nameof(EntityInteractiveComponent._InteractWithMagicWardrobe_d__76.MoveNext))]
internal static class InteractWithMagicWardrobeMoveNextPatch
{
    private static bool Prefix(
        EntityInteractiveComponent._InteractWithMagicWardrobe_d__76 __instance,
        ref bool __result)
    {
        if (!MagicWardrobeHookFixRuntime.BeginWardrobeStep(__instance))
        {
            return true;
        }

        __result = false;
        return false;
    }

    private static void Postfix(
        EntityInteractiveComponent._InteractWithMagicWardrobe_d__76 __instance,
        bool __result)
    {
        MagicWardrobeHookFixRuntime.EndWardrobeStep(__instance, __result);
    }
}
