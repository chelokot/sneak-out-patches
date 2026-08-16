using Fusion;
using Gameplay.Player.Components;
using HarmonyLib;
using Networking.Lobby;
using UI.Views;
using UI.Views.Lobby;

namespace SneakOut.NetworkHostSelector;

[HarmonyPatch(typeof(PingView), "Refresh")]
internal static class NetworkHostSelectorPingViewPatch
{
    [HarmonyPostfix]
    private static void Postfix(PingView __instance)
    {
        NetworkHostSelectorRuntime.RefreshHostLabel(__instance);
    }
}

[HarmonyPatch(typeof(PortalPlayView), "OnPlay")]
internal static class NetworkHostSelectorPortalPlayPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First + 50)]
    private static bool Prefix()
    {
        return NetworkHostSelectorRuntime.AllowPortalPlay();
    }
}

[HarmonyPatch(typeof(PhotonLobby), nameof(PhotonLobby.JoinMatchSession))]
internal static class NetworkHostSelectorJoinMatchSessionPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static void Prefix(ref string hostId)
    {
        NetworkHostSelectorRuntime.OverrideMatchHost(ref hostId);
    }
}

[HarmonyPatch(typeof(NetworkRunner), nameof(NetworkRunner.StartGame), new[] { typeof(StartGameArgs) })]
internal static class NetworkHostSelectorStartGamePatch
{
    [HarmonyPrefix]
    private static void Prefix(ref StartGameArgs args)
    {
        NetworkHostSelectorRuntime.InitializeSessionProperties(args);
    }
}

[HarmonyPatch(typeof(SpookedNetworkPlayer), nameof(SpookedNetworkPlayer.Spawned))]
internal static class NetworkHostSelectorPlayerSpawnedPatch
{
    [HarmonyPostfix]
    private static void Postfix(SpookedNetworkPlayer __instance)
    {
        NetworkHostSelectorRuntime.ObservePlayer(__instance);
    }
}

[HarmonyPatch(typeof(SpookedNetworkPlayer), nameof(SpookedNetworkPlayer.Despawned))]
internal static class NetworkHostSelectorPlayerDespawnedPatch
{
    [HarmonyPrefix]
    private static void Prefix(SpookedNetworkPlayer __instance)
    {
        NetworkHostSelectorRuntime.ForgetPlayer(__instance);
    }
}
