using HarmonyLib;
using Gameplay.Player.Components;

namespace SneakOut.FreeFly;

[HarmonyPatch]
internal static class PlayerInputControllerUpdatePatch
{
    private static System.Reflection.MethodBase? TargetMethod()
    {
        var type = AccessTools.TypeByName("Gameplay.Player.PlayerInputController");
        return type is null ? null : AccessTools.Method(type, "Update");
    }

    private static void Postfix()
    {
        FreeFlyRuntime.TryApplyFreeFly();
    }
}

[HarmonyPatch(typeof(SpookedNetworkPlayer), nameof(SpookedNetworkPlayer.Spawned))]
internal static class SpookedNetworkPlayerSpawnedPatch
{
    private static void Postfix(SpookedNetworkPlayer __instance)
    {
        FreeFlyRuntime.RememberLocalNetworkPlayer(__instance);
    }
}

[HarmonyPatch]
internal static class SpookedNetworkPlayerSpawnedReadyPatch
{
    private static System.Reflection.MethodBase? TargetMethod()
    {
        return AccessTools.Method(typeof(SpookedNetworkPlayer), "RPC_SpawnedReady");
    }

    private static void Postfix(SpookedNetworkPlayer __instance)
    {
        FreeFlyRuntime.RememberLocalNetworkPlayer(__instance);
    }
}
