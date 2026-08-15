using HarmonyLib;
using Networking.Lobby;

namespace SneakOut.QuickReconnect;

[HarmonyPatch(typeof(PhotonLobby), "InitRunner")]
internal static class PhotonLobbyInitRunnerPatch
{
    private static void Prefix(PhotonLobby __instance)
    {
        QuickReconnectRuntime.RegisterCloudConnectionLostHandler(__instance);
    }
}
