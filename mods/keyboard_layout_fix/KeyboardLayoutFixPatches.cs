using Gameplay.Player;
using HarmonyLib;

namespace SneakOut.KeyboardLayoutFix;

[HarmonyPatch(typeof(PlayerInputController), "ResolveLocalInputs")]
internal static class PlayerInputControllerResolveLocalInputsPatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerInputController __instance)
    {
        KeyboardLayoutFixRuntime.ApplyPhysicalMovement(__instance);
    }
}
