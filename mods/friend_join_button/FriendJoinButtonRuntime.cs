using BepInEx.Logging;
using HarmonyLib;
using Networking.Friends;
using Networking.PGOS;
using Steamworks;
using System.Collections;
using System.Reflection;
using System;
using TMPro;
using UI.Buttons;
using UI.Views.Lobby.People;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SneakOut.FriendJoinButton;

internal static class FriendJoinButtonRuntime
{
    private static readonly Dictionary<int, PopupJoinState> PopupStatesByInstanceId = new();
    private static readonly List<FriendSnapshot> FriendSnapshots = new();
    private static readonly object SnapshotLock = new();
    private static readonly Dictionary<string, string> PresenceByPlayerId = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, object> PresenceCallbackTargetsByPlayerId = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Delegate> PresenceCallbacksByPlayerId = new(StringComparer.Ordinal);
    private static readonly Dictionary<ulong, Dictionary<string, string>> SteamRichPresenceBySteamId = new();
    private static readonly Type? NakamaControllerPartyInvitationType = AccessTools.TypeByName("Networking.Nakama.NakamaController+PartyInvitation");
    private static readonly Type? PgosClientSDKType = AccessTools.TypeByName("Pgos.PgosClientSDK");
    private static readonly Type? PlayerProfileType = AccessTools.TypeByName("Pgos.Client.PlayerProfile");
    private static readonly Type? PlayerProfileOnGetPlayerPresenceType = AccessTools.TypeByName("Pgos.Client.PlayerProfile+OnGetPlayerPresence");
    private static readonly MethodInfo? PgosLobbyOnReceivePartyInvitationCallbackMethod =
        AccessTools.Method(typeof(PgosLobby), "OnReceivePartyInvitationCallback");
    private static readonly MethodInfo? PgosClientSDKGetMethod = PgosClientSDKType is null ? null : AccessTools.Method(PgosClientSDKType, "Get");
    private static readonly MethodInfo? PgosClientSDKGetPlayerProfileApiMethod = PgosClientSDKType is null ? null : AccessTools.Method(PgosClientSDKType, "GetPlayerProfileAPI");
    private static readonly MethodInfo? PlayerProfileGetPlayerPresenceMethod = PlayerProfileType is null ? null : AccessTools.Method(PlayerProfileType, "GetPlayerPresence");
    private static ManualLogSource? _logger;
    private static Harmony? _harmony;
    private static FriendJoinButtonConfig? _configuration;
    private static Callback<FriendRichPresenceUpdate_t>? _friendRichPresenceUpdateCallback;

    private sealed class FriendSnapshot
    {
        public string PgosId { get; init; } = string.Empty;

        public string SteamId { get; init; } = string.Empty;

        public string Nickname { get; init; } = string.Empty;

        public string PartyId { get; init; } = string.Empty;

        public string ServerName { get; init; } = string.Empty;

        public int CurrentMatchId { get; init; }

        public IReadOnlyDictionary<string, string> Properties { get; init; } = new Dictionary<string, string>();
    }

    private sealed class PopupJoinState
    {
        public FriendOnHoverPopupView PopupView { get; init; } = null!;

        public SpookedOutlineButton JoinButton { get; init; } = null!;

        public TMP_Text JoinButtonLabel { get; init; } = null!;

        public RectTransform InviteRectTransform { get; init; } = null!;

        public RectTransform JoinRectTransform { get; init; } = null!;

        public Vector2 OriginalInviteAnchoredPosition { get; init; }

        public float OriginalInviteWidth { get; init; }
    }

    private sealed class PresenceCallbackTarget
    {
        public PresenceCallbackTarget(string playerId)
        {
            PlayerId = playerId;
        }

        private string PlayerId { get; }

        public void Handle(object result, object playerPresence)
        {
            var resultType = result.GetType();
            var errCode = resultType.GetField("err_code")?.GetValue(result)?.ToString() ?? string.Empty;
            var message = resultType.GetField("msg")?.GetValue(result) as string ?? string.Empty;
            var presenceType = playerPresence.GetType();
            var status = presenceType.GetField("status")?.GetValue(playerPresence)?.ToString() ?? string.Empty;
            var rawPresence = presenceType.GetField("presence")?.GetValue(playerPresence) as string ?? string.Empty;
            MarkBackendPresenceRequestFinished(PlayerId, status, rawPresence, errCode == "kSuccess" ? string.Empty : $"{errCode}: {message}");
        }
    }

    private readonly record struct FriendJoinAvailability(string PartyId, FriendSnapshot Snapshot, ulong SteamLobbyId);

    public static void Initialize(ManualLogSource logger, FriendJoinButtonConfig configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _friendRichPresenceUpdateCallback ??= Callback<FriendRichPresenceUpdate_t>.Create(
            (Callback<FriendRichPresenceUpdate_t>.DispatchDelegate)OnFriendRichPresenceUpdated);
        _harmony ??= new Harmony(FriendJoinButtonPlugin.PluginGuid);
        _harmony.PatchAll();
    }

    private static bool Enabled => _configuration is not null && _configuration.EnableMod.Value;

    private static bool LoggingEnabled => _configuration is not null && _configuration.EnableLogging.Value;

    private static void RefreshOpenPopupsForPlayer(string playerId)
    {
        foreach (var popupState in PopupStatesByInstanceId.Values.ToArray())
        {
            var popupFriend = popupState.PopupView._data;
            if (popupFriend is null || !string.Equals(popupFriend.PgosId, playerId, StringComparison.Ordinal))
            {
                continue;
            }

            ConfigureJoinPopup(popupState.PopupView);
        }
    }

    private static bool TryGetJoinAvailability(SpookedFriend? friend, out FriendJoinAvailability availability)
    {
        availability = default;
        if (!Enabled || friend is null || string.IsNullOrWhiteSpace(friend.PgosId))
        {
            return false;
        }

        EnsureBackendPresenceRequested(friend);

        if (!TryGetFriendSnapshot(friend, out var snapshot))
        {
            if (TryGetBackendPresenceSnapshot(friend, out snapshot))
            {
                availability = new FriendJoinAvailability(snapshot.PartyId, snapshot, 0);
                return true;
            }

            if (TryGetSteamLobbyJoinAvailability(friend, out availability))
            {
                return true;
            }

            return false;
        }

        if (string.IsNullOrWhiteSpace(snapshot.PartyId))
        {
            if (LoggingEnabled)
            {
                _logger?.LogInfo(
                    $"Friend '{friend.Nickname}' has no joinable party snapshot. currentMatchId={snapshot.CurrentMatchId}, serverName='{snapshot.ServerName}', propertyKeys=[{string.Join(", ", snapshot.Properties.Keys.OrderBy(static key => key, StringComparer.OrdinalIgnoreCase))}]");
            }

            return false;
        }

        availability = new FriendJoinAvailability(snapshot.PartyId, snapshot, 0);
        return true;
    }

    private static bool TryGetSteamLobbyJoinAvailability(SpookedFriend friend, out FriendJoinAvailability availability)
    {
        availability = default;
        if (friend.SteamId.m_SteamID == 0)
        {
            if (LoggingEnabled)
            {
                _logger?.LogInfo($"Steam lobby unavailable for '{friend.Nickname}': missing SteamId");
            }
            return false;
        }

        if (!SteamFriends.GetFriendGamePlayed(friend.SteamId, out var friendGameInfo))
        {
            if (LoggingEnabled)
            {
                _logger?.LogInfo($"Steam lobby unavailable for '{friend.Nickname}' ({friend.SteamId.m_SteamID}): GetFriendGamePlayed=false");
            }
            return false;
        }

        if (friendGameInfo.m_steamIDLobby.m_SteamID == 0)
        {
            if (TryGetSteamRichPresenceJoinAvailability(friend, out availability))
            {
                return true;
            }

            if (LoggingEnabled)
            {
                _logger?.LogInfo($"Steam lobby unavailable for '{friend.Nickname}' ({friend.SteamId.m_SteamID}): lobbyId=0");
            }
            return false;
        }

        var snapshot = new FriendSnapshot
        {
            PgosId = friend.PgosId,
            SteamId = friend.SteamId.m_SteamID.ToString(),
            Nickname = friend.Nickname,
            Properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };

        availability = new FriendJoinAvailability(string.Empty, snapshot, friendGameInfo.m_steamIDLobby.m_SteamID);
        if (LoggingEnabled)
        {
            _logger?.LogInfo($"Steam lobby available for '{friend.Nickname}' ({friend.SteamId.m_SteamID}): lobbyId={friendGameInfo.m_steamIDLobby.m_SteamID}");
        }
        return true;
    }

    private static bool TryGetSteamRichPresenceJoinAvailability(SpookedFriend friend, out FriendJoinAvailability availability)
    {
        availability = default;
        if (!TryGetCachedSteamRichPresence(friend.SteamId.m_SteamID, out var properties))
        {
            SteamFriends.RequestFriendRichPresence(friend.SteamId);
            properties = ReadSteamRichPresence(friend.SteamId);
            if (properties.Count > 0)
            {
                CacheSteamRichPresence(friend.SteamId.m_SteamID, properties);
            }
        }

        if (properties.Count == 0)
        {
            if (LoggingEnabled)
            {
                _logger?.LogInfo($"Steam rich presence unavailable for '{friend.Nickname}' ({friend.SteamId.m_SteamID}): no keys");
            }

            return false;
        }

        if (!TryExtractSteamLobbyId(properties, out var steamLobbyId))
        {
            if (LoggingEnabled)
            {
                _logger?.LogInfo(
                    $"Steam rich presence has no joinable lobby for '{friend.Nickname}' ({friend.SteamId.m_SteamID}): keys=[{string.Join(", ", properties.Keys.OrderBy(static key => key, StringComparer.OrdinalIgnoreCase))}]");
            }

            return false;
        }

        var snapshot = new FriendSnapshot
        {
            PgosId = friend.PgosId,
            SteamId = friend.SteamId.m_SteamID.ToString(),
            Nickname = friend.Nickname,
            Properties = properties
        };

        availability = new FriendJoinAvailability(string.Empty, snapshot, steamLobbyId);
        if (LoggingEnabled)
        {
            _logger?.LogInfo($"Steam rich presence available for '{friend.Nickname}' ({friend.SteamId.m_SteamID}): lobbyId={steamLobbyId}");
        }

        return true;
    }

    private static Dictionary<string, string> ReadSteamRichPresence(CSteamID steamId)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var richPresenceKeyCount = SteamFriends.GetFriendRichPresenceKeyCount(steamId);
        for (var index = 0; index < richPresenceKeyCount; index++)
        {
            var key = SteamFriends.GetFriendRichPresenceKeyByIndex(steamId, index);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var value = SteamFriends.GetFriendRichPresence(steamId, key);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            properties[key] = value;
        }

        return properties;
    }

    private static bool TryGetCachedSteamRichPresence(ulong steamId, out Dictionary<string, string> properties)
    {
        lock (SnapshotLock)
        {
            return SteamRichPresenceBySteamId.TryGetValue(steamId, out properties!);
        }
    }

    private static void CacheSteamRichPresence(ulong steamId, Dictionary<string, string> properties)
    {
        lock (SnapshotLock)
        {
            SteamRichPresenceBySteamId[steamId] = properties;
        }
    }

    private static void OnFriendRichPresenceUpdated(FriendRichPresenceUpdate_t callback)
    {
        var properties = ReadSteamRichPresence(callback.m_steamIDFriend);
        CacheSteamRichPresence(callback.m_steamIDFriend.m_SteamID, properties);

        if (LoggingEnabled)
        {
            _logger?.LogInfo(
                $"Steam rich presence update for {callback.m_steamIDFriend.m_SteamID}: keys=[{string.Join(", ", properties.Keys.OrderBy(static key => key, StringComparer.OrdinalIgnoreCase))}]");
        }

        RefreshAllOpenPopups();
    }

    private static bool TryExtractSteamLobbyId(IReadOnlyDictionary<string, string> properties, out ulong steamLobbyId)
    {
        foreach (var key in new[] { "connect", "steam_player_group", "steam_lobby", "lobby", "lobbyId", "steamidlobby" })
        {
            if (!properties.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (ulong.TryParse(value, out steamLobbyId) && steamLobbyId != 0)
            {
                return true;
            }

            var digits = new string(value.Where(char.IsDigit).ToArray());
            if (!string.IsNullOrWhiteSpace(digits) && ulong.TryParse(digits, out steamLobbyId) && steamLobbyId != 0)
            {
                return true;
            }
        }

        steamLobbyId = 0;
        return false;
    }

    private static void EnsureBackendPresenceRequested(SpookedFriend? friend)
    {
        if (!Enabled || friend is null || string.IsNullOrWhiteSpace(friend.PgosId))
        {
            return;
        }

        lock (SnapshotLock)
        {
            if (PresenceByPlayerId.ContainsKey(friend.PgosId) || PresenceCallbacksByPlayerId.ContainsKey(friend.PgosId))
            {
                if (LoggingEnabled)
                {
                    _logger?.LogInfo($"Backend presence already cached or pending for '{friend.Nickname}' ({friend.PgosId})");
                }
                return;
            }
        }

        var playerProfileApi = GetPlayerProfileApi();
        if (playerProfileApi is null || PlayerProfileOnGetPlayerPresenceType is null || PlayerProfileGetPlayerPresenceMethod is null)
        {
            if (LoggingEnabled)
            {
                _logger?.LogInfo(
                    $"Backend presence unavailable for '{friend.Nickname}' ({friend.PgosId}): apiNull={playerProfileApi is null}, callbackTypeNull={PlayerProfileOnGetPlayerPresenceType is null}, methodNull={PlayerProfileGetPlayerPresenceMethod is null}");
            }
            return;
        }

        var callbackTarget = new PresenceCallbackTarget(friend.PgosId);
        var callbackMethod = AccessTools.Method(typeof(PresenceCallbackTarget), nameof(PresenceCallbackTarget.Handle));
        var callback = callbackMethod is null
            ? null
            : Delegate.CreateDelegate(PlayerProfileOnGetPlayerPresenceType, callbackTarget, callbackMethod, false);
        if (callback is null)
        {
            if (LoggingEnabled)
            {
                _logger?.LogInfo($"Backend presence callback creation failed for '{friend.Nickname}' ({friend.PgosId})");
            }
            return;
        }

        lock (SnapshotLock)
        {
            PresenceCallbackTargetsByPlayerId[friend.PgosId] = callbackTarget;
            PresenceCallbacksByPlayerId[friend.PgosId] = callback;
        }

        if (LoggingEnabled)
        {
            _logger?.LogInfo($"Requesting backend presence for '{friend.Nickname}' ({friend.PgosId})");
        }

        PlayerProfileGetPlayerPresenceMethod.Invoke(playerProfileApi, new object[] { friend.PgosId, callback });
    }

    private static object? GetPlayerProfileApi()
    {
        if (PgosClientSDKGetMethod is null || PgosClientSDKGetPlayerProfileApiMethod is null)
        {
            return null;
        }

        var sdk = PgosClientSDKGetMethod.Invoke(null, Array.Empty<object>());
        return sdk is null
            ? null
            : PgosClientSDKGetPlayerProfileApiMethod.Invoke(sdk, Array.Empty<object>());
    }

    private static void MarkBackendPresenceRequestFinished(string playerId, string status, string rawPresence, string failureMessage)
    {
        lock (SnapshotLock)
        {
            PresenceCallbackTargetsByPlayerId.Remove(playerId);
            PresenceCallbacksByPlayerId.Remove(playerId);
            if (string.IsNullOrWhiteSpace(failureMessage))
            {
                PresenceByPlayerId[playerId] = rawPresence;
            }
            else
            {
                PresenceByPlayerId.Remove(playerId);
            }
        }

        if (LoggingEnabled)
        {
            _logger?.LogInfo(
                string.IsNullOrWhiteSpace(failureMessage)
                    ? $"Backend presence for {playerId}: status={status}, presence={rawPresence}"
                    : $"Backend presence lookup failed for {playerId}: {failureMessage}");
        }

        RefreshOpenPopupsForPlayer(playerId);
    }

    private static bool TryGetBackendPresenceSnapshot(SpookedFriend friend, out FriendSnapshot snapshot)
    {
        string rawPresence;
        lock (SnapshotLock)
        {
            if (!PresenceByPlayerId.TryGetValue(friend.PgosId, out rawPresence!))
            {
                snapshot = null!;
                return false;
            }
        }

        if (!TryExtractPartyId(rawPresence, out var partyId))
        {
            snapshot = null!;
            return false;
        }

        snapshot = new FriendSnapshot
        {
            PgosId = friend.PgosId,
            Nickname = friend.Nickname,
            PartyId = partyId,
            Properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };
        return true;
    }

    private static bool TryExtractPartyId(string rawStatus, out string partyId)
    {
        foreach (var segment in rawStatus.Split(new[] { ';', ',', '&', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separatorIndex = segment.IndexOf('=');
            if (separatorIndex < 0)
            {
                separatorIndex = segment.IndexOf(':');
            }

            if (separatorIndex <= 0 || separatorIndex >= segment.Length - 1)
            {
                continue;
            }

            var key = segment[..separatorIndex].Trim().Trim('"');
            var value = segment[(separatorIndex + 1)..].Trim().Trim('"');
            if ((string.Equals(key, "partyId", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(key, "party_id", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(key, "party", StringComparison.OrdinalIgnoreCase)) &&
                !string.IsNullOrWhiteSpace(value))
            {
                partyId = value;
                return true;
            }
        }

        partyId = string.Empty;
        return false;
    }

    private static bool TryGetFriendSnapshot(SpookedFriend friend, out FriendSnapshot snapshot)
    {
        lock (SnapshotLock)
        {
            var matchedByPgosId = FriendSnapshots.FirstOrDefault(candidate =>
                string.Equals(candidate.PgosId, friend.PgosId, StringComparison.Ordinal));
            if (matchedByPgosId is not null)
            {
                snapshot = matchedByPgosId;
                return true;
            }

            var matchedByNickname = FriendSnapshots.Where(candidate =>
                    !string.IsNullOrWhiteSpace(candidate.Nickname) &&
                    string.Equals(candidate.Nickname, friend.Nickname, StringComparison.Ordinal))
                .ToArray();
            if (matchedByNickname.Length == 1)
            {
                snapshot = matchedByNickname[0];
                return true;
            }
        }

        snapshot = null!;
        return false;
    }

    private static void PromoteJoinStatus(FriendPlayerRecord record, SpookedFriend? friend, bool itsMyPlayer, bool partOfTheTeam)
    {
        if (itsMyPlayer || partOfTheTeam || !TryGetJoinAvailability(friend, out _))
        {
            return;
        }

        record._status = PlayerRecordStatus.OnlineActionOn;
        record.RefreshRecord();
    }

    private static void ConfigureJoinPopup(FriendOnHoverPopupView popupView)
    {
        EnsurePopupState(popupView);

        if (!PopupStatesByInstanceId.TryGetValue(popupView.GetInstanceID(), out var popupState))
        {
            return;
        }

        if (!TryGetJoinAvailability(popupView._data, out var availability))
        {
            if (LoggingEnabled && popupView._data is not null)
            {
                _logger?.LogInfo($"Popup has no joinable snapshot for '{popupView._data.Nickname}' ({popupView._data.PgosId})");
            }
            HideJoinButton(popupState);
            return;
        }

        ShowJoinButton(popupState, availability);
    }

    private static void EnsurePopupState(FriendOnHoverPopupView popupView)
    {
        var popupInstanceId = popupView.GetInstanceID();
        if (PopupStatesByInstanceId.ContainsKey(popupInstanceId))
        {
            return;
        }

        var inviteButton = popupView._inviteButton;
        if (inviteButton is null)
        {
            return;
        }

        var inviteRectTransform = inviteButton.GetComponent<RectTransform>();
        if (inviteRectTransform is null)
        {
            return;
        }

        var joinButton = UnityEngine.Object.Instantiate(inviteButton, inviteButton.transform.parent);
        joinButton.name = "JoinButton";
        joinButton.onClick.RemoveAllListeners();
        joinButton.onClick.AddListener((UnityAction)(() => OnJoinButtonClicked(popupView)));

        var joinLabel = joinButton.GetComponentInChildren<TMP_Text>(true);
        if (joinLabel is null)
        {
            UnityEngine.Object.Destroy(joinButton.gameObject);
            return;
        }

        var joinRectTransform = joinButton.GetComponent<RectTransform>();
        if (joinRectTransform is null)
        {
            UnityEngine.Object.Destroy(joinButton.gameObject);
            return;
        }

        PopupStatesByInstanceId[popupInstanceId] = new PopupJoinState
        {
            PopupView = popupView,
            JoinButton = joinButton,
            JoinButtonLabel = joinLabel,
            InviteRectTransform = inviteRectTransform,
            JoinRectTransform = joinRectTransform,
            OriginalInviteAnchoredPosition = inviteRectTransform.anchoredPosition,
            OriginalInviteWidth = inviteRectTransform.rect.width,
        };

        HideJoinButton(PopupStatesByInstanceId[popupInstanceId]);
    }

    private static void HideJoinButton(PopupJoinState popupState)
    {
        popupState.JoinButton.gameObject.SetActive(false);
        popupState.InviteRectTransform.anchoredPosition = popupState.OriginalInviteAnchoredPosition;
        popupState.InviteRectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, popupState.OriginalInviteWidth);
    }

    private static void ShowJoinButton(PopupJoinState popupState, FriendJoinAvailability availability)
    {
        var gap = 12f;
        var buttonWidth = Mathf.Max(0f, (popupState.OriginalInviteWidth - gap) / 2f);
        var offset = (buttonWidth + gap) / 2f;

        popupState.JoinButton.gameObject.SetActive(true);
        popupState.JoinButtonLabel.SetText("JOIN");
        popupState.JoinButton.SetInteractable(true);

        popupState.InviteRectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, buttonWidth);
        popupState.JoinRectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, buttonWidth);

        popupState.InviteRectTransform.anchoredPosition = popupState.OriginalInviteAnchoredPosition + Vector2.left * offset;
        popupState.JoinRectTransform.anchoredPosition = popupState.OriginalInviteAnchoredPosition + Vector2.right * offset;

        if (LoggingEnabled)
        {
            _logger?.LogInfo($"Configured JOIN button for '{popupState.PopupView._data?.Nickname}' with party id {availability.PartyId}");
        }
    }

    private static void OnJoinButtonClicked(FriendOnHoverPopupView popupView)
    {
        if (!TryGetJoinAvailability(popupView._data, out var availability))
        {
            return;
        }

        if (!TryExecuteJoin(popupView, availability))
        {
            return;
        }

        popupView._buttonClicked = true;
        popupView.HideOptions();
    }

    private static bool TryExecuteJoin(FriendOnHoverPopupView popupView, FriendJoinAvailability availability)
    {
        if (availability.SteamLobbyId != 0)
        {
            SteamMatchmaking.JoinLobby(new CSteamID(availability.SteamLobbyId));

            if (LoggingEnabled)
            {
                _logger?.LogInfo($"Join button routed '{popupView._data?.Nickname}' through Steam lobby {availability.SteamLobbyId}");
            }

            return true;
        }

        var pgosLobby = popupView._pgosLobby;
        var friend = popupView._data;
        if (pgosLobby is null || friend is null || PgosLobbyOnReceivePartyInvitationCallbackMethod is null || NakamaControllerPartyInvitationType is null)
        {
            return false;
        }

        var invitation = Activator.CreateInstance(NakamaControllerPartyInvitationType);
        if (invitation is null)
        {
            return false;
        }

        NakamaControllerPartyInvitationType.GetField("PartyId")?.SetValue(invitation, availability.PartyId);
        NakamaControllerPartyInvitationType.GetField("InviterUserId")?.SetValue(invitation, friend.PgosId);
        NakamaControllerPartyInvitationType.GetField("InviterUsername")?.SetValue(invitation, friend.Nickname);

        PgosLobbyOnReceivePartyInvitationCallbackMethod.Invoke(null, new object[] { invitation });
        pgosLobby.JoinLobbyFromInvitation();

        if (LoggingEnabled)
        {
            _logger?.LogInfo(
                $"Join button routed '{friend.Nickname}' through invitation flow with party id {availability.PartyId}, currentMatchId={availability.Snapshot.CurrentMatchId}, serverName='{availability.Snapshot.ServerName}'");
        }

        return true;
    }

    private static void RefreshAllOpenPopups()
    {
        foreach (var popupState in PopupStatesByInstanceId.Values.ToArray())
        {
            ConfigureJoinPopup(popupState.PopupView);
        }
    }

    private static void CleanupPopupState(FriendOnHoverPopupView popupView)
    {
        var popupInstanceId = popupView.GetInstanceID();
        if (!PopupStatesByInstanceId.Remove(popupInstanceId, out var popupState))
        {
            return;
        }

        if (popupState.JoinButton is not null)
        {
            UnityEngine.Object.Destroy(popupState.JoinButton.gameObject);
        }
    }

    [HarmonyPatch(typeof(FriendPlayerRecord), nameof(FriendPlayerRecord.InitPlayerRecord))]
    private static class FriendPlayerRecordInitPlayerRecordPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            FriendPlayerRecord __instance,
            SpookedFriend data,
            bool partOfTheTeam)
        {
            PromoteJoinStatus(__instance, data, false, partOfTheTeam);
        }
    }

    [HarmonyPatch(typeof(FriendPlayerRecord), nameof(FriendPlayerRecord.UpdateStatus))]
    private static class FriendPlayerRecordUpdateStatusPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            FriendPlayerRecord __instance,
            SpookedFriend data,
            bool itsMyPlayer,
            bool partOfTheTeam)
        {
            PromoteJoinStatus(__instance, data, itsMyPlayer, partOfTheTeam);
        }
    }

    [HarmonyPatch(typeof(FriendOnHoverPopupView), nameof(FriendOnHoverPopupView.Init))]
    private static class FriendOnHoverPopupViewInitPatch
    {
        [HarmonyPostfix]
        private static void Postfix(FriendOnHoverPopupView __instance)
        {
            ConfigureJoinPopup(__instance);
        }
    }

    [HarmonyPatch(typeof(FriendOnHoverPopupView), nameof(FriendOnHoverPopupView.ShowOptions))]
    private static class FriendOnHoverPopupViewShowOptionsPatch
    {
        [HarmonyPostfix]
        private static void Postfix(FriendOnHoverPopupView __instance)
        {
            ConfigureJoinPopup(__instance);
        }
    }

    [HarmonyPatch(typeof(FriendOnHoverPopupView), nameof(FriendOnHoverPopupView.ManagerDispose))]
    private static class FriendOnHoverPopupViewManagerDisposePatch
    {
        [HarmonyPrefix]
        private static void Prefix(FriendOnHoverPopupView __instance)
        {
            CleanupPopupState(__instance);
        }
    }

    [HarmonyPatch]
    private static class KinguinverseSetFriendsPatch
    {
        private static MethodBase? TargetMethod()
        {
            var kinguinverseType = AccessTools.TypeByName("Kinguinverse");
            return kinguinverseType is null
                ? null
                : AccessTools.Method(kinguinverseType, "set_Friends");
        }

        [HarmonyPostfix]
        private static void Postfix()
        {
        }
    }

    [HarmonyPatch]
    private static class KinguinverseRefreshFriendsAndInvitationsPatch
    {
        private static MethodBase? TargetMethod()
        {
            var kinguinverseType = AccessTools.TypeByName("Kinguinverse");
            return kinguinverseType is null
                ? null
                : AccessTools.Method(kinguinverseType, "OnRefreshFriendsAndInvitationsRequestEvent");
        }

        [HarmonyPostfix]
        private static void Postfix()
        {
        }
    }

    [HarmonyPatch]
    private static class KinguinverseRefreshFriendPatch
    {
        private static MethodBase? TargetMethod()
        {
            var kinguinverseType = AccessTools.TypeByName("Kinguinverse");
            return kinguinverseType is null
                ? null
                : AccessTools.Method(kinguinverseType, "OnRefreshFriendRequestEvent");
        }

        [HarmonyPostfix]
        private static void Postfix()
        {
        }
    }
}
