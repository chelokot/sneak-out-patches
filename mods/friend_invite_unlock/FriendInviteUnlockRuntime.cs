using BepInEx.Logging;
using HarmonyLib;
using Networking.PGOS;
using Networking.Friends;
using UI.Views.Lobby.People;

namespace SneakOut.FriendInviteUnlock;

internal static class FriendInviteUnlockRuntime
{
    private static ManualLogSource? _logger;
    private static Harmony? _harmony;
    private static FriendInviteUnlockConfig? _configuration;

    public static void Initialize(ManualLogSource logger, FriendInviteUnlockConfig configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _harmony ??= new Harmony(FriendInviteUnlockPlugin.PluginGuid);
        _harmony.PatchAll();
    }

    private static bool Enabled => _configuration is not null && _configuration.EnableMod.Value;

    private static bool RequiresLeader => _configuration is not null && _configuration.RequireTeamLeader.Value;

    private static bool ShouldForceInvite(SpookedFriend? friend)
    {
        return Enabled
            && friend is not null
            && !string.IsNullOrEmpty(friend.PgosId)
            && !friend.Online;
    }

    private static bool CanUseInviteOverride(PgosLobby? pgosLobby)
    {
        if (!Enabled || pgosLobby is null)
        {
            return false;
        }

        if (!RequiresLeader)
        {
            return true;
        }

        return pgosLobby.AmITeamLeader;
    }

    private static bool ShouldPromoteStatus(FriendPlayerRecord record, SpookedFriend? friend, bool amITeamLeader, bool itsMyPlayer, bool partOfTheTeam)
    {
        if (!ShouldForceInvite(friend))
        {
            return false;
        }

        if (itsMyPlayer || partOfTheTeam)
        {
            return false;
        }

        if (RequiresLeader && !amITeamLeader)
        {
            return false;
        }

        return record._status == PlayerRecordStatus.Offline;
    }

    private static void PromoteInviteStatus(FriendPlayerRecord record, SpookedFriend? friend, bool amITeamLeader, bool itsMyPlayer, bool partOfTheTeam)
    {
        if (!ShouldPromoteStatus(record, friend, amITeamLeader, itsMyPlayer, partOfTheTeam))
        {
            return;
        }

        record._status = PlayerRecordStatus.OnlineActionOn;
        record.RefreshRecord();

        if (_configuration!.EnableLogging.Value)
        {
            _logger?.LogInfo($"Forced invite-enabled friend record for '{friend!.Nickname}' ({friend.PgosId})");
        }
    }

    private static bool ShouldForcePopupInvite(FriendOnHoverPopupView popupView)
    {
        if (!ShouldForceInvite(popupView._data))
        {
            return false;
        }

        return CanUseInviteOverride(popupView._pgosLobby);
    }

    private static void EnablePopupInviteButton(FriendOnHoverPopupView popupView)
    {
        var inviteButton = popupView._inviteButton;
        if (inviteButton is null)
        {
            return;
        }

        popupView._status = PlayerRecordStatus.OnlineActionOn;
        inviteButton.SetInteractable(true);
        if (popupView._inviteButtonColorImage is not null)
        {
            popupView._inviteButtonColorImage.color = popupView._inviteColor;
        }
    }

    [HarmonyPatch(typeof(FriendPlayerRecord), nameof(FriendPlayerRecord.InitPlayerRecord))]
    private static class FriendPlayerRecordInitPlayerRecordPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            FriendPlayerRecord __instance,
            SpookedFriend data,
            bool amITeamLeader,
            bool partOfTheTeam)
        {
            PromoteInviteStatus(__instance, data, amITeamLeader, false, partOfTheTeam);
        }
    }

    [HarmonyPatch(typeof(FriendPlayerRecord), nameof(FriendPlayerRecord.UpdateStatus))]
    private static class FriendPlayerRecordUpdateStatusPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            FriendPlayerRecord __instance,
            SpookedFriend data,
            bool amITeamLeader,
            bool itsMyPlayer,
            bool partOfTheTeam)
        {
            PromoteInviteStatus(__instance, data, amITeamLeader, itsMyPlayer, partOfTheTeam);
        }
    }

    [HarmonyPatch(typeof(FriendPlayerRecord), "get_IsOnline")]
    private static class FriendPlayerRecordIsOnlinePatch
    {
        [HarmonyPostfix]
        private static void Postfix(FriendPlayerRecord __instance, ref bool __result)
        {
            if (__result)
            {
                return;
            }

            if (__instance._status != PlayerRecordStatus.OnlineActionOn)
            {
                return;
            }

            if (!ShouldForceInvite(__instance._data))
            {
                return;
            }

            __result = true;
        }
    }

    [HarmonyPatch(typeof(FriendOnHoverPopupView), nameof(FriendOnHoverPopupView.Init))]
    private static class FriendOnHoverPopupViewInitPatch
    {
        [HarmonyPostfix]
        private static void Postfix(FriendOnHoverPopupView __instance)
        {
            if (!ShouldForcePopupInvite(__instance))
            {
                return;
            }

            EnablePopupInviteButton(__instance);
        }
    }

    [HarmonyPatch(typeof(FriendOnHoverPopupView), nameof(FriendOnHoverPopupView.ShowOptions))]
    private static class FriendOnHoverPopupViewShowOptionsPatch
    {
        [HarmonyPostfix]
        private static void Postfix(FriendOnHoverPopupView __instance)
        {
            if (!ShouldForcePopupInvite(__instance))
            {
                return;
            }

            EnablePopupInviteButton(__instance);
        }
    }

    [HarmonyPatch(typeof(FriendOnHoverPopupView), "OnInviteClick")]
    private static class FriendOnHoverPopupViewOnInviteClickPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(FriendOnHoverPopupView __instance)
        {
            if (!ShouldForcePopupInvite(__instance))
            {
                return true;
            }

            var friend = __instance._data;
            if (friend is null || string.IsNullOrEmpty(friend.PgosId) || __instance._pgosLobby is null)
            {
                return true;
            }

            __instance._buttonClicked = true;
            __instance._pgosLobby.InviteToParty(friend.PgosId);
            __instance.HideOptions();

            if (_configuration!.EnableLogging.Value)
            {
                _logger?.LogInfo($"Forced offline invite send for '{friend.Nickname}' ({friend.PgosId})");
            }

            return false;
        }
    }
}
