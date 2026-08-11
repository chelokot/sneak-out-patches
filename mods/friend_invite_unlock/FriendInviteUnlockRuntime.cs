using BepInEx.Logging;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Networking.Friends;
using Networking.Party;
using Steamworks;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Types;
using UnityEngine;
using UI.Views.Lobby.People;

namespace SneakOut.FriendInviteUnlock;

internal static class FriendInviteUnlockRuntime
{
    private const float SteamTickIntervalSeconds = 0.5f;
    private const float PendingJoinTimeoutSeconds = 180f;
    private const float StartedJoinTimeoutSeconds = 45f;

    private static ManualLogSource? _logger;
    private static Harmony? _harmony;
    private static FriendInviteUnlockConfig? _configuration;
    private static SteamJoinRequestedCallback? _steamJoinRequestedCallback;
    private static PendingSteamJoin? _pendingSteamJoin;
    private static string _publishedConnectString = string.Empty;
    private static float _nextSteamTick;
    private static float _nextCallbackRegistrationAttempt;
    private static bool _watcherInstalled;
    private static bool _steamCallbackTypeRegistered;
    private static bool _launchCommandChecked;
    private static bool _callbackRegistrationFailureLogged;
    private static bool _shutdown;
    private static readonly HashSet<ulong> InviteOverrideSteamIds = new();
    private static readonly MethodInfo? PgosLobbyInviteToPartyMethod =
        AccessTools.Method(typeof(PgosLobby), "InviteToParty", new[] { typeof(string) });

    public static void Initialize(ManualLogSource logger, FriendInviteUnlockConfig configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _shutdown = false;
        _harmony ??= new Harmony(FriendInviteUnlockPlugin.PluginGuid);
        _harmony.PatchAll();
        EnsureWatcher();
    }

    private static bool Enabled => _configuration is not null && _configuration.EnableMod.Value;

    private static bool RequiresLeader => _configuration is not null && _configuration.RequireTeamLeader.Value;

    private static bool SteamInvitesEnabled => Enabled
        && _configuration is not null
        && _configuration.EnableSteamInvites.Value;

    private static bool SteamAutoJoinEnabled => Enabled
        && _configuration is not null
        && _configuration.AutoJoinSteamInvites.Value;

    private static void EnsureWatcher()
    {
        if (_watcherInstalled)
        {
            return;
        }

        try
        {
            ClassInjector.RegisterTypeInIl2Cpp<SteamJoinRequestedCallback>();
            _steamCallbackTypeRegistered = true;
        }
        catch (Exception exception)
        {
            _logger?.LogError($"Steam Join Game callback adapter registration failed: {exception}");
        }

        ClassInjector.RegisterTypeInIl2Cpp<SteamInviteWatcher>();
        var watcherObject = new GameObject("FriendInviteSteamWatcher");
        watcherObject.hideFlags = HideFlags.HideAndDontSave;
        UnityEngine.Object.DontDestroyOnLoad(watcherObject);
        watcherObject.AddComponent<SteamInviteWatcher>();
        _watcherInstalled = true;
    }

    private static void WatcherTick()
    {
        if (_shutdown)
        {
            return;
        }

        var now = Time.realtimeSinceStartup;
        if (now < _nextSteamTick)
        {
            return;
        }
        _nextSteamTick = now + SteamTickIntervalSeconds;

        if (!Enabled)
        {
            ClearSteamJoinPresence();
            _pendingSteamJoin = null;
            return;
        }

        if (!SteamAPI.IsSteamRunning())
        {
            _publishedConnectString = string.Empty;
            return;
        }

        if (SteamAutoJoinEnabled)
        {
            TryEnsureSteamJoinCallback(now);
            CheckLaunchCommandLine();
        }

        RefreshSteamJoinPresence();
        ProcessPendingSteamJoin(now);
    }

    private static void TryEnsureSteamJoinCallback(float now)
    {
        if (!_steamCallbackTypeRegistered
            || _steamJoinRequestedCallback?.IsRegistered == true
            || now < _nextCallbackRegistrationAttempt)
        {
            return;
        }

        if (!CallbackDispatcher.IsInitialized)
        {
            return;
        }

        _nextCallbackRegistrationAttempt = now + 30f;
        try
        {
            var callback = _steamJoinRequestedCallback ?? new SteamJoinRequestedCallback();
            CallbackDispatcher.Register(callback);
            callback.IsRegistered = true;
            _steamJoinRequestedCallback = callback;
            _callbackRegistrationFailureLogged = false;
            _logger?.LogInfo("Registered raw-pointer Steam Join Game callback");
        }
        catch (Exception exception)
        {
            if (!_callbackRegistrationFailureLogged)
            {
                _logger?.LogError($"Steam Join Game callback registration failed: {exception}");
                _callbackRegistrationFailureLogged = true;
            }
        }
    }

    private static void CheckLaunchCommandLine()
    {
        if (_launchCommandChecked)
        {
            return;
        }
        _launchCommandChecked = true;

        try
        {
            if (SteamApps.GetLaunchCommandLine(out var launchCommandLine, 1024) > 0
                && SteamPartyJoinToken.TryParse(launchCommandLine, out var launchToken))
            {
                QueueSteamJoin(launchToken, 0, "Steam launch command");
                return;
            }
        }
        catch (Exception exception)
        {
            if (_configuration?.EnableLogging.Value == true)
            {
                _logger?.LogWarning($"Could not inspect Steam launch command: {exception.Message}");
            }
        }

        if (SteamPartyJoinToken.TryExtract(Environment.GetCommandLineArgs(), out var processToken))
        {
            QueueSteamJoin(processToken, 0, "process launch command");
        }
    }

    private static void OnSteamJoinRequested(GameRichPresenceJoinRequested_t request)
    {
        var connectString = request.m_rgchConnect;
        if (!SteamPartyJoinToken.TryParse(connectString, out var token))
        {
            if (connectString?.Contains(SteamPartyJoinToken.ArgumentPrefix, StringComparison.Ordinal) == true)
            {
                _logger?.LogWarning("Ignored a Steam Join Game request with an invalid Sneak Out party token");
            }
            return;
        }

        QueueSteamJoin(token, request.m_steamIDFriend.m_SteamID, "Steam overlay");
    }

    private static void QueueSteamJoin(
        SteamPartyJoinToken token,
        ulong expectedInviterSteamId,
        string source)
    {
        if (!SteamAutoJoinEnabled)
        {
            return;
        }

        if (expectedInviterSteamId != 0 && expectedInviterSteamId != token.HostSteamId)
        {
            _logger?.LogWarning(
                $"Ignored a Steam party token whose host {token.HostSteamId} did not match inviter {expectedInviterSteamId}");
            return;
        }

        if (!IsImmediateSteamFriend(token.HostSteamId))
        {
            _logger?.LogWarning($"Ignored a Steam party token from non-friend {token.HostSteamId}");
            return;
        }

        if (_pendingSteamJoin is not null
            && !_pendingSteamJoin.Attempted
            && _pendingSteamJoin.Token == token)
        {
            return;
        }

        _pendingSteamJoin = new PendingSteamJoin(token, source, Time.realtimeSinceStartup);
        _logger?.LogInfo(
            $"Accepted {source} party request from Steam friend {token.HostSteamId}; waiting for the lobby service");
    }

    private static bool IsImmediateSteamFriend(ulong steamId)
    {
        if (steamId == 0 || !SteamAPI.IsSteamRunning())
        {
            return false;
        }

        try
        {
            return SteamFriends.GetFriendRelationship(new CSteamID(steamId))
                == EFriendRelationship.k_EFriendRelationshipFriend;
        }
        catch
        {
            return false;
        }
    }

    private static void RefreshSteamJoinPresence()
    {
        var desiredConnectString = string.Empty;
        if (SteamInvitesEnabled)
        {
            TryBuildCurrentConnectString(out desiredConnectString);
        }

        if (string.Equals(desiredConnectString, _publishedConnectString, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            var published = SteamFriends.SetRichPresence(
                "connect",
                string.IsNullOrEmpty(desiredConnectString) ? null! : desiredConnectString);
            if (!published)
            {
                if (_configuration?.EnableLogging.Value == true)
                {
                    _logger?.LogWarning("Steam rejected the Friend Invite Unlock Join Game presence update");
                }
                return;
            }

            _publishedConnectString = desiredConnectString;
            if (_configuration?.EnableLogging.Value == true)
            {
                _logger?.LogInfo(string.IsNullOrEmpty(desiredConnectString)
                    ? "Cleared Steam Join Game presence"
                    : "Published Steam Join Game presence for the current lobby party");
            }
        }
        catch (Exception exception)
        {
            if (_configuration?.EnableLogging.Value == true)
            {
                _logger?.LogWarning($"Steam Join Game presence update failed: {exception.Message}");
            }
        }
    }

    private static void ClearSteamJoinPresence()
    {
        if (string.IsNullOrEmpty(_publishedConnectString))
        {
            return;
        }

        if (SteamAPI.IsSteamRunning())
        {
            try
            {
                SteamFriends.SetRichPresence("connect", null!);
            }
            catch
            {
                // Steam is allowed to disappear during process shutdown.
            }
        }
        _publishedConnectString = string.Empty;
    }

    private static bool TryBuildCurrentConnectString(out string connectString)
    {
        connectString = string.Empty;
        try
        {
            if (!SteamInvitesEnabled || !TryGetPgosLobby(out var pgosLobby) || !CanUseInviteOverride(pgosLobby))
            {
                return false;
            }

            var gameState = pgosLobby._gameState;
            if (gameState is null
                || gameState.Pointer == IntPtr.Zero
                || gameState.CurrentScene != SceneType.Lobby
                || !pgosLobby.TryGetCurrentParty(out var currentParty)
                || currentParty is null
                || !currentParty.Open)
            {
                return false;
            }

            var partyId = pgosLobby.PartyId;
            var region = pgosLobby.ResolveLobbyRegionForInvite();
            var localSteamId = SteamUser.GetSteamID().m_SteamID;
            return new SteamPartyJoinToken(localSteamId, partyId, region).TryEncode(out connectString);
        }
        catch (Exception exception)
        {
            if (_configuration?.EnableLogging.Value == true)
            {
                _logger?.LogWarning($"Could not build Steam lobby token: {exception.Message}");
            }
            return false;
        }
    }

    private static bool TryGetPgosLobby(out PgosLobby pgosLobby)
    {
        pgosLobby = null!;
        try
        {
            var instance = PgosLobby.Instance;
            if (instance is null || instance.Pointer == IntPtr.Zero)
            {
                return false;
            }

            pgosLobby = instance;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ProcessPendingSteamJoin(float now)
    {
        var pending = _pendingSteamJoin;
        if (pending is null || !SteamAutoJoinEnabled)
        {
            return;
        }

        if (now - pending.QueuedAt > PendingJoinTimeoutSeconds)
        {
            _logger?.LogWarning(
                $"Steam party join timed out before the lobby service became ready ({pending.Token.PartyId})");
            _pendingSteamJoin = null;
            return;
        }

        if (!TryGetPgosLobby(out var pgosLobby))
        {
            return;
        }

        if (string.Equals(pgosLobby.PartyId, pending.Token.PartyId, StringComparison.Ordinal))
        {
            _logger?.LogInfo($"Joined the Steam friend's party through the stock Sneak Out lobby flow ({pending.Token.PartyId})");
            _pendingSteamJoin = null;
            return;
        }

        if (pending.Attempted)
        {
            if (now - pending.AttemptedAt > StartedJoinTimeoutSeconds)
            {
                _logger?.LogWarning(
                    $"Steam party join did not complete; the party may be full or no longer exist ({pending.Token.PartyId})");
                _pendingSteamJoin = null;
            }
            return;
        }

        var gameState = pgosLobby._gameState;
        var nakamaService = pgosLobby._nakamaService;
        if (gameState is null
            || gameState.Pointer == IntPtr.Zero
            || gameState.CurrentScene != SceneType.Lobby
            || nakamaService is null
            || nakamaService.Pointer == IntPtr.Zero
            || string.IsNullOrWhiteSpace(nakamaService.UserId)
            || nakamaService.Socket is null
            || nakamaService.Socket.Pointer == IntPtr.Zero
            || !nakamaService.Socket.IsConnected)
        {
            return;
        }

        try
        {
            pgosLobby._pendingInvitationPartyId = pending.Token.PartyId;
            pgosLobby._pendingInvitationRegion = pending.Token.Region;
            pgosLobby.JoinLobbyFromInvitationAsync();
            pending.Attempted = true;
            pending.AttemptedAt = now;
            _logger?.LogInfo(
                $"Started stock Nakama/Photon join for {pending.Source} party {pending.Token.PartyId}");
        }
        catch (Exception exception)
        {
            _logger?.LogWarning($"Could not start the Steam party join: {exception.Message}");
        }
    }

    private static bool TrySendSteamGameInvite(SpookedFriend friend)
    {
        if (!SteamInvitesEnabled || !TryBuildCurrentConnectString(out var connectString))
        {
            return false;
        }

        try
        {
            return SteamFriends.InviteUserToGame(friend.SteamId, connectString);
        }
        catch (Exception exception)
        {
            _logger?.LogWarning($"Steam game invite failed for {friend.SteamId.m_SteamID}: {exception.Message}");
            return false;
        }
    }

    private static void Shutdown()
    {
        if (_shutdown)
        {
            return;
        }

        ClearSteamJoinPresence();
        if (_steamJoinRequestedCallback?.IsRegistered == true)
        {
            try
            {
                CallbackDispatcher.Unregister(_steamJoinRequestedCallback);
            }
            catch
            {
                // Steam may already have torn down its callback dispatcher.
            }
        }
        _steamJoinRequestedCallback = null;
        _pendingSteamJoin = null;
        _shutdown = true;
    }

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

    private static Il2CppReferenceArray<SpookedFriend> MergeAllSteamFriends(Il2CppReferenceArray<SpookedFriend> friends)
    {
        if (!Enabled || !SteamAPI.IsSteamRunning())
        {
            return friends;
        }

        var mergedFriends = new List<SpookedFriend>();
        var existingSteamIds = new HashSet<ulong>();
        for (var index = 0; index < friends.Length; index++)
        {
            var friend = friends[index];
            if (friend is null)
            {
                continue;
            }

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

        var result = new Il2CppReferenceArray<SpookedFriend>(orderedFriends.Count);
        for (var index = 0; index < orderedFriends.Count; index++)
        {
            result[index] = orderedFriends[index];
        }

        if (_configuration!.EnableLogging.Value)
        {
            _logger?.LogInfo($"Merged Steam friends into list: {orderedFriends.Count} visible friends");
        }

        return result;
    }

    private static bool HasClassicActiveState(SpookedFriend friend)
    {
        return friend.Online && !string.IsNullOrWhiteSpace(friend.PgosId);
    }

    [HarmonyPatch(typeof(FriendsView), nameof(FriendsView.RefreshFriends))]
    private static class FriendsViewRefreshFriendsPatch
    {
        [HarmonyPrefix]
        private static void Prefix(ref Il2CppReferenceArray<SpookedFriend> friends)
        {
            friends = MergeAllSteamFriends(friends);
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
            var steamInviteSent = TrySendSteamGameInvite(friend);
            PgosLobbyInviteToPartyMethod!.Invoke(
                __instance._pgosLobby,
                new object[] { friend.SteamId.m_SteamID.ToString() });
            __instance.HideOptions();

            if (_configuration!.EnableLogging.Value)
            {
                _logger?.LogInfo(
                    $"Forced invite send for '{friend.Nickname}' ({friend.SteamId.m_SteamID}); "
                    + $"steamGameInvite={steamInviteSent}");
            }

            return false;
        }
    }

    private sealed class PendingSteamJoin
    {
        public PendingSteamJoin(SteamPartyJoinToken token, string source, float queuedAt)
        {
            Token = token;
            Source = source;
            QueuedAt = queuedAt;
        }

        public SteamPartyJoinToken Token { get; }

        public string Source { get; }

        public float QueuedAt { get; }

        public bool Attempted { get; set; }

        public float AttemptedAt { get; set; }
    }

    private sealed class SteamInviteWatcher : MonoBehaviour
    {
        public SteamInviteWatcher(IntPtr pointer) : base(pointer)
        {
        }

        public SteamInviteWatcher() : base(ClassInjector.DerivedConstructorPointer<SteamInviteWatcher>())
        {
            ClassInjector.DerivedConstructorBody(this);
        }

        private void Update()
        {
            try
            {
                WatcherTick();
            }
            catch (Exception exception)
            {
                _logger?.LogError($"Steam invite watcher failed: {exception}");
            }
        }

        private void OnApplicationQuit()
        {
            Shutdown();
        }
    }

    private sealed class SteamJoinRequestedCallback : Steamworks.Callback
    {
        public SteamJoinRequestedCallback(IntPtr pointer) : base(pointer)
        {
        }

        public SteamJoinRequestedCallback()
            : base(ClassInjector.DerivedConstructorPointer<SteamJoinRequestedCallback>())
        {
            ClassInjector.DerivedConstructorBody(this);
        }

        public bool IsRegistered { get; set; }

        public override bool IsGameServer => false;

        public override Il2CppSystem.Type GetCallbackType()
        {
            return Il2CppType.Of<GameRichPresenceJoinRequested_t>();
        }

        public override void OnRunCallback(IntPtr parameterPointer)
        {
            if (parameterPointer == IntPtr.Zero)
            {
                return;
            }

            OnSteamJoinRequested(new GameRichPresenceJoinRequested_t(parameterPointer));
        }

        public override void SetUnregistered()
        {
            IsRegistered = false;
        }
    }
}
