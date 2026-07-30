using System.Reflection;
using Fusion;
using Gameplay.Player.Components;
using Gameplay.Spawn;
using HarmonyLib;
using UI.Views.Lobby;

namespace SneakOut.LobbyTestBot;

[HarmonyPatch(typeof(PortalPlayView), nameof(PortalPlayView.Open))]
[HarmonyAfter("chelokot.sneakout.portal-mode-selector")]
internal static class PortalPlayViewOpenPatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(PortalPlayView __instance)
    {
        LobbyTestBotRuntime.OpenPortal(__instance);
    }
}

[HarmonyPatch(typeof(PortalPlayView), nameof(PortalPlayView.Update))]
internal static class PortalPlayViewUpdatePatch
{
    [HarmonyPostfix]
    private static void Postfix(PortalPlayView __instance)
    {
        LobbyTestBotRuntime.TickPortal(__instance);
    }
}

[HarmonyPatch(typeof(PortalPlayView), nameof(PortalPlayView.Close))]
internal static class PortalPlayViewClosePatch
{
    [HarmonyPostfix]
    private static void Postfix(PortalPlayView __instance)
    {
        LobbyTestBotRuntime.ReleasePortal(__instance);
    }
}

[HarmonyPatch(typeof(PortalPlayView), nameof(PortalPlayView.ManagerDispose))]
internal static class PortalPlayViewManagerDisposePatch
{
    [HarmonyPostfix]
    private static void Postfix(PortalPlayView __instance)
    {
        LobbyTestBotRuntime.ReleasePortal(__instance);
    }
}

[HarmonyPatch(typeof(SpookedNetworkPlayer), nameof(SpookedNetworkPlayer.Despawned))]
internal static class SpookedNetworkPlayerDespawnedPatch
{
    [HarmonyPostfix]
    private static void Postfix(SpookedNetworkPlayer __instance)
    {
        LobbyTestBotRuntime.ObservePlayerDespawned(__instance);
    }
}

[HarmonyPatch(typeof(SpookedNetworkPlayer), nameof(SpookedNetworkPlayer.Init))]
internal static class SpookedNetworkPlayerInitPatch
{
    [HarmonyPostfix]
    private static void Postfix(SpookedNetworkPlayer __instance)
    {
        LobbyTestBotRuntime.ObservePlayerInitialized(__instance);
    }
}

[HarmonyPatch]
internal static class SceneSpawnerBotSpawnCompletedPatch
{
    private static MethodBase TargetMethod()
    {
        var closureType = typeof(SceneSpawner).GetNestedType(
            "__c__DisplayClass25_0",
            BindingFlags.Public | BindingFlags.NonPublic);
        return AccessTools.DeclaredMethod(closureType, "_OnSpawnActorEvent_b__0");
    }

    [HarmonyPostfix]
    private static void Postfix(NetworkObject instance)
    {
        LobbyTestBotRuntime.ObserveBotSpawnCompleted(instance);
    }
}
