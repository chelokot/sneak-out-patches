using BepInEx.Logging;
using Gameplay.Player.Components;
using Steamworks;

namespace SneakOut.ProximityVoiceChat;

internal sealed class VoicePeerDirectory : IDisposable
{
    private const string ProtocolPresenceKey = "spvc_protocol";
    private const string SessionPresenceKey = "spvc_session";
    private const string InternalIdPresenceKey = "spvc_internal";
    private const float RefreshIntervalSeconds = 1.5f;

    private readonly ManualLogSource _logger;
    private readonly ProximityVoiceChatConfig _configuration;
    private readonly Dictionary<int, SpookedNetworkPlayer> _players = new();
    private readonly Dictionary<int, ulong> _steamIdByInternalId = new();
    private readonly Dictionary<ulong, int> _internalIdBySteamId = new();
    private readonly HashSet<ulong> _allowedPeers = new();
    private readonly HashSet<ulong> _authoritativePeers = new();
    private readonly HashSet<ulong> _confirmedPeers = new();
    private readonly HashSet<ulong> _handshakeConfirmedPeers = new();
    private readonly HashSet<ulong> _mutedPeers = new();
    private readonly HashSet<ulong> _presenceRequestedPeers = new();
    private ulong _localSteamId;
    private ulong _sessionHash;
    private int _localInternalId = -1;
    private float _nextRefreshTime;
    private bool _presencePublished;

    public VoicePeerDirectory(ManualLogSource logger, ProximityVoiceChatConfig configuration)
    {
        _logger = logger;
        _configuration = configuration;
        ReloadMutedIds();
        RebuildAllowedPeers();
    }

    public IReadOnlyCollection<ulong> AllowedPeers => _allowedPeers;

    public IReadOnlyCollection<ulong> ConfirmedPeers => _confirmedPeers;

    public void BeginSession(ulong sessionHash, int localInternalId, ulong localSteamId)
    {
        if (_sessionHash == sessionHash
            && _localInternalId == localInternalId
            && _localSteamId == localSteamId)
        {
            return;
        }

        EndSession();
        _sessionHash = sessionHash;
        _localInternalId = localInternalId;
        _localSteamId = localSteamId;
        ReloadMutedIds();
        RebuildAllowedPeers();
        PublishPresence();
        _nextRefreshTime = 0f;
    }

    public void RegisterPlayer(SpookedNetworkPlayer player)
    {
        if (player is null || player.IsBot)
        {
            return;
        }
        _players[player.InternalId] = player;
        if (!player.HasInputAuthority
            && VoiceIdentityResolver.TryResolveSteamId(player, out var steamId)
            && steamId != _localSteamId)
        {
            BindIdentity(steamId, player.InternalId, authoritative: true);
        }
    }

    public void UnregisterPlayer(SpookedNetworkPlayer player)
    {
        if (player is null || !_players.TryGetValue(player.InternalId, out var known) || known.Pointer != player.Pointer)
        {
            return;
        }
        _players.Remove(player.InternalId);
        if (_steamIdByInternalId.Remove(player.InternalId, out var steamId))
        {
            _internalIdBySteamId.Remove(steamId);
            _authoritativePeers.Remove(steamId);
            _handshakeConfirmedPeers.Remove(steamId);
            _confirmedPeers.Remove(steamId);
            _allowedPeers.Remove(steamId);
        }
    }

    public void Refresh(float nowSeconds)
    {
        if (_sessionHash == 0 || nowSeconds < _nextRefreshTime)
        {
            return;
        }
        _nextRefreshTime = nowSeconds + RefreshIntervalSeconds;
        ReloadMutedIds();

        foreach (var pair in _players.ToArray())
        {
            var player = pair.Value;
            if (player is null || player.Pointer == IntPtr.Zero)
            {
                _players.Remove(pair.Key);
                if (_steamIdByInternalId.Remove(pair.Key, out var staleSteamId))
                {
                    _internalIdBySteamId.Remove(staleSteamId);
                    _authoritativePeers.Remove(staleSteamId);
                    _handshakeConfirmedPeers.Remove(staleSteamId);
                }
                continue;
            }
            if (!player.HasInputAuthority
                && !_steamIdByInternalId.ContainsKey(player.InternalId)
                && VoiceIdentityResolver.TryResolveSteamId(player, out var steamId)
                && steamId != _localSteamId)
            {
                BindIdentity(steamId, player.InternalId, authoritative: true);
            }
        }
        RebuildAllowedPeers();

        var sessionText = _sessionHash.ToString("X16");
        var friendCount = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
        for (var index = 0; index < friendCount; index++)
        {
            var friend = SteamFriends.GetFriendByIndex(index, EFriendFlags.k_EFriendFlagImmediate);
            if (friend.m_SteamID == 0 || friend.m_SteamID == _localSteamId)
            {
                continue;
            }
            if (_presenceRequestedPeers.Add(friend.m_SteamID))
            {
                SteamFriends.RequestFriendRichPresence(friend);
            }
            if (SteamFriends.GetFriendRichPresence(friend, ProtocolPresenceKey) == "1"
                && string.Equals(
                    SteamFriends.GetFriendRichPresence(friend, SessionPresenceKey),
                    sessionText,
                    StringComparison.OrdinalIgnoreCase))
            {
                _allowedPeers.Add(friend.m_SteamID);
                _confirmedPeers.Add(friend.m_SteamID);
                if (int.TryParse(
                        SteamFriends.GetFriendRichPresence(friend, InternalIdPresenceKey),
                        out var internalId))
                {
                    BindIdentity(friend.m_SteamID, internalId, authoritative: false);
                }
            }
        }
    }

    public bool IsAllowed(ulong steamId)
    {
        return steamId != 0 && steamId != _localSteamId && _allowedPeers.Contains(steamId);
    }

    public bool IsMuted(ulong steamId)
    {
        return _mutedPeers.Contains(steamId);
    }

    public void ConfirmHandshake(ulong steamId)
    {
        if (_allowedPeers.Contains(steamId))
        {
            _handshakeConfirmedPeers.Add(steamId);
            _confirmedPeers.Add(steamId);
        }
    }

    public bool TryBindPacketIdentity(ulong steamId, int internalId)
    {
        if (!IsAllowed(steamId) || internalId < 0 || internalId == _localInternalId)
        {
            return false;
        }

        if (_internalIdBySteamId.TryGetValue(steamId, out var existingInternalId))
        {
            return existingInternalId == internalId;
        }
        if (_steamIdByInternalId.TryGetValue(internalId, out var existingSteamId))
        {
            return existingSteamId == steamId;
        }

        return BindIdentity(steamId, internalId, authoritative: false);
    }

    public bool TryGetPlayer(ulong steamId, out SpookedNetworkPlayer player)
    {
        player = null!;
        try
        {
            if (!_internalIdBySteamId.TryGetValue(steamId, out var internalId)
                || !_players.TryGetValue(internalId, out var resolvedPlayer)
                || resolvedPlayer is null
                || resolvedPlayer.Pointer == IntPtr.Zero
                || resolvedPlayer.HasInputAuthority
                || resolvedPlayer.IsBot)
            {
                return false;
            }
            player = resolvedPlayer;
            return true;
        }
        catch
        {
            player = null!;
            return false;
        }
    }

    public void EndSession()
    {
        if (_presencePublished && SteamAPI.IsSteamRunning())
        {
            SteamFriends.SetRichPresence(ProtocolPresenceKey, null!);
            SteamFriends.SetRichPresence(SessionPresenceKey, null!);
            SteamFriends.SetRichPresence(InternalIdPresenceKey, null!);
        }
        _presencePublished = false;
        _sessionHash = 0;
        _localInternalId = -1;
        _players.Clear();
        _steamIdByInternalId.Clear();
        _internalIdBySteamId.Clear();
        _allowedPeers.Clear();
        _authoritativePeers.Clear();
        _confirmedPeers.Clear();
        _handshakeConfirmedPeers.Clear();
        _presenceRequestedPeers.Clear();
        ReloadMutedIds();
    }

    private bool BindIdentity(ulong steamId, int internalId, bool authoritative)
    {
        if (steamId == 0 || internalId < 0 || steamId == _localSteamId || internalId == _localInternalId)
        {
            return false;
        }
        if (_internalIdBySteamId.TryGetValue(steamId, out var oldInternalId) && oldInternalId != internalId)
        {
            if (!authoritative)
            {
                return false;
            }
            _steamIdByInternalId.Remove(oldInternalId);
        }
        if (_steamIdByInternalId.TryGetValue(internalId, out var oldSteamId) && oldSteamId != steamId)
        {
            if (!authoritative)
            {
                return false;
            }
            _internalIdBySteamId.Remove(oldSteamId);
        }

        _internalIdBySteamId[steamId] = internalId;
        _steamIdByInternalId[internalId] = steamId;
        if (authoritative)
        {
            _authoritativePeers.Add(steamId);
            _allowedPeers.Add(steamId);
        }
        return true;
    }

    private void PublishPresence()
    {
        if (_sessionHash == 0 || !SteamAPI.IsSteamRunning())
        {
            return;
        }
        SteamFriends.SetRichPresence(ProtocolPresenceKey, "1");
        SteamFriends.SetRichPresence(SessionPresenceKey, _sessionHash.ToString("X16"));
        SteamFriends.SetRichPresence(InternalIdPresenceKey, _localInternalId.ToString());
        _presencePublished = true;
        if (_configuration.EnableLogging.Value)
        {
            _logger.LogInfo($"Published proximity voice presence for room {_sessionHash:X16}");
        }
    }

    private void ReloadMutedIds()
    {
        _mutedPeers.Clear();
        ParseSteamIds(_configuration.MutedSteamIds.Value, _mutedPeers);
    }

    private void RebuildAllowedPeers()
    {
        _allowedPeers.Clear();
        _allowedPeers.UnionWith(_authoritativePeers);
        var explicitlyConfigured = new HashSet<ulong>();
        ParseSteamIds(_configuration.AdditionalPeerSteamIds.Value, explicitlyConfigured);
        _allowedPeers.UnionWith(explicitlyConfigured);
        _allowedPeers.Remove(_localSteamId);

        _confirmedPeers.Clear();
        _confirmedPeers.UnionWith(explicitlyConfigured);
        _handshakeConfirmedPeers.IntersectWith(_allowedPeers);
        _confirmedPeers.UnionWith(_handshakeConfirmedPeers);
        _confirmedPeers.Remove(_localSteamId);
    }

    private static void ParseSteamIds(string text, ISet<ulong> destination)
    {
        foreach (var part in text.Split(new[] { ',', ';', ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (ulong.TryParse(part, out var steamId) && steamId != 0)
            {
                destination.Add(steamId);
            }
        }
    }

    public void Dispose()
    {
        EndSession();
    }
}
