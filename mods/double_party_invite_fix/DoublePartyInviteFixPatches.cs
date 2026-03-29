using HarmonyLib;
using Networking.PGOS;

namespace SneakOut.DoublePartyInviteFix;

[HarmonyPatch(typeof(PgosLobby), "OnJoinLobbyEvent")]
internal static class PgosLobbyOnJoinLobbyEventPatch
{
    private static bool Prefix(PgosLobby __instance, Il2CppSystem.Object? sender, Il2CppSystem.EventArgs? args)
    {
        return !DoublePartyInviteFixRuntime.TryHandleJoinLobbyEvent(__instance, args);
    }
}
