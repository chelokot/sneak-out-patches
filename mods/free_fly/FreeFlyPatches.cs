using HarmonyLib;
using UnityEngine;

namespace SneakOut.FreeFly;

[HarmonyPatch]
internal static class PlayerInputControllerUpdatePatch
{
    private static System.Reflection.MethodBase? TargetMethod()
    {
        var type = AccessTools.TypeByName("Gameplay.Player.PlayerInputController");
        return type is null ? null : AccessTools.Method(type, "Update");
    }

    private static void Postfix(Component __instance)
    {
        FreeFlyRuntime.TryApplyFreeFly(__instance);
    }
}
