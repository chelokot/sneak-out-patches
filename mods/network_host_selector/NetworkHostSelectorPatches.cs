using HarmonyLib;
using Networking.Lobby;
using UI;
using UI.Views.Lobby;

namespace SneakOut.NetworkHostSelector;

[HarmonyPatch(typeof(GameUIManager), "OnAwake")]
internal static class NetworkHostSelectorGameUiManagerPatch
{
    [HarmonyPostfix]
    private static void Postfix(GameUIManager __instance)
    {
        NetworkHostSelectorRuntime.BindPortalManager(__instance);
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
