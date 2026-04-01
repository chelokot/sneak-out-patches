using HarmonyLib;
using Networking.PGOS;
using UI.Views.Lobby.People;

namespace SneakOut.DoublePartyInviteFix;

[HarmonyPatch(typeof(PgosLobby), "OnTeamInvitationClickedEvent")]
internal static class PgosLobbyOnTeamInvitationClickedEventPatch
{
    private static bool Prefix(PgosLobby __instance, Il2CppSystem.Object? sender, Il2CppSystem.EventArgs? args)
    {
        return !DoublePartyInviteFixRuntime.TryHandleTeamInvitationClicked(__instance, args);
    }
}

[HarmonyPatch(typeof(PgosLobby), "OnJoinLobbyEvent")]
internal static class PgosLobbyOnJoinLobbyEventPatch
{
    private static bool Prefix(PgosLobby __instance, Il2CppSystem.Object? sender, Il2CppSystem.EventArgs? args)
    {
        return !DoublePartyInviteFixRuntime.TryHandleJoinLobbyEvent(__instance, args);
    }
}

[HarmonyPatch(typeof(PgosLobby), nameof(PgosLobby.JoinLobbyFromInvitation))]
internal static class PgosLobbyJoinLobbyFromInvitationPatch
{
    private static bool Prefix(PgosLobby __instance)
    {
        DoublePartyInviteFixRuntime.LogJoinLobbyFromInvitation(__instance);
        return !DoublePartyInviteFixRuntime.TryHandleJoinLobbyFromInvitation(__instance);
    }
}

[HarmonyPatch(typeof(InvitationRecord), nameof(InvitationRecord.OnAccept))]
internal static class InvitationRecordOnAcceptPatch
{
    private static void Prefix(InvitationRecord __instance)
    {
        DoublePartyInviteFixRuntime.LogInvitationAccept(__instance);
    }
}
