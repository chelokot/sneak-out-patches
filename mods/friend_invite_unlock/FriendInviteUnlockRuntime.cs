using BepInEx.Logging;
using Events;
using HarmonyLib;
using Networking.Friends;
using Networking.Party;
using Steamworks;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UI.Views.Lobby.People;
using Il2CppFriends = Il2CppSystem.Collections.Generic;

namespace SneakOut.FriendInviteUnlock;

internal static class FriendInviteUnlockRuntime
{
    private static ManualLogSource? _logger;
    private static Harmony? _harmony;
    private static FriendInviteUnlockConfig? _configuration;
    private static readonly HashSet<ulong> InviteOverrideSteamIds = new();
    private static readonly MethodInfo? PgosLobbyInviteToPartyMethod =
        AccessTools.Method(typeof(PgosLobby), "InviteToParty", new[] { typeof(string) });

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
        return Enabled && HasActionableInviteTarget(friend);
    }

    private static bool HasInviteOverride(SpookedFriend? friend)
    {
        return friend is not null && InviteOverrideSteamIds.Contains(friend.SteamId.m_SteamID);
    }

    private static bool HasActionableInviteTarget(SpookedFriend? friend)
    {
        return friend is not null
            && friend.SteamId.m_SteamID != 0
            && PgosLobbyInviteToPartyMethod is not null;
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
            if (friend is not null)
            {
                InviteOverrideSteamIds.Remove(friend.SteamId.m_SteamID);
            }

            return;
        }

        InviteOverrideSteamIds.Add(friend!.SteamId.m_SteamID);
        record._status = PlayerRecordStatus.OnlineActionOn;
        record.RefreshRecord();
        EnforceActiveRecordVisualState(record);

        if (_configuration!.EnableLogging.Value)
        {
            _logger?.LogInfo($"Forced active friend record for '{friend!.Nickname}' ({friend.SteamId.m_SteamID})");
        }
    }

    private static bool ShouldForcePopupInvite(FriendOnHoverPopupView popupView)
    {
        if (popupView._status != PlayerRecordStatus.OnlineActionOn
            || !ShouldForceInvite(popupView._data)
            || !HasInviteOverride(popupView._data))
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

    private static void EnforceActiveRecordVisualState(FriendPlayerRecord record)
    {
        record._status = PlayerRecordStatus.OnlineActionOn;
        record._recordButton?.SetInteractable(true);

        if (record._backgroundImage is not null)
        {
            record._backgroundImage.sprite = record._onlineBackgroundSprite;
            record._backgroundImage.color = Color.white;
        }

        if (record._statusFlagImage is not null)
        {
            record._statusFlagImage.color = record._onlineColor;
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

            if (!HasInviteOverride(__instance._data))
            {
                return;
            }

            __result = true;
        }
    }

    private static void MergeAllSteamFriends(Il2CppFriends.List<SpookedFriend> friends)
    {
        if (!Enabled)
        {
            return;
        }

        var mergedFriends = new List<SpookedFriend>();
        var existingSteamIds = new HashSet<ulong>();
        for (var index = 0; index < friends.Count; index++)
        {
            var friend = friends[index];
            mergedFriends.Add(friend);
            if (friend.SteamId.m_SteamID != 0)
            {
                existingSteamIds.Add(friend.SteamId.m_SteamID);
            }
        }

        var totalSteamFriends = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
        for (var index = 0; index < totalSteamFriends; index++)
        {
            var steamId = SteamFriends.GetFriendByIndex(index, EFriendFlags.k_EFriendFlagImmediate);
            if (steamId.m_SteamID == 0 || !existingSteamIds.Add(steamId.m_SteamID))
            {
                continue;
            }

            var personaState = SteamFriends.GetFriendPersonaState(steamId);
            var hasGame = SteamFriends.GetFriendGamePlayed(steamId, out _);
            var syntheticFriend = new SpookedFriend(
                steamId,
                SteamFriends.GetFriendPersonaName(steamId),
                personaState != EPersonaState.k_EPersonaStateOffline && personaState != EPersonaState.k_EPersonaStateInvisible,
                hasGame,
                false,
                0,
                0,
                string.Empty);

            mergedFriends.Add(syntheticFriend);
        }

        var orderedFriends = mergedFriends
            .OrderByDescending(HasClassicActiveState)
            .ThenByDescending(friend => friend.Online)
            .ThenBy(friend => friend.Nickname, StringComparer.OrdinalIgnoreCase)
            .ToList();

        friends.Clear();
        foreach (var friend in orderedFriends)
        {
            friends.Add(friend);
        }

        if (_configuration!.EnableLogging.Value)
        {
            _logger?.LogInfo($"Merged Steam friends into list: {orderedFriends.Count} visible friends");
        }
    }

    private static bool HasClassicActiveState(SpookedFriend friend)
    {
        return friend.Online && !string.IsNullOrWhiteSpace(friend.PgosId);
    }

    [HarmonyPatch(typeof(SpookedFriendsRefreshedFullEvent), "get_Friends")]
    private static class SpookedFriendsRefreshedFullEventGetFriendsPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Il2CppFriends.List<SpookedFriend> __result)
        {
            MergeAllSteamFriends(__result);
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
            __instance._buttonClicked = true;
            PgosLobbyInviteToPartyMethod!.Invoke(
                __instance._pgosLobby,
                new object[] { friend.SteamId.m_SteamID.ToString() });
            __instance.HideOptions();

            if (_configuration!.EnableLogging.Value)
            {
                _logger?.LogInfo($"Forced invite send for '{friend.Nickname}' ({friend.SteamId.m_SteamID})");
            }

            return false;
        }
    }
}
