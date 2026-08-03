using BepInEx.Logging;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Steamworks;

namespace SneakOut.ProximityVoiceChat;

internal sealed class SteamVoiceTransport : IDisposable
{
    private const int VoiceChannel = 91;
    private const int MaximumPacketsPerTick = 96;
    private const int MaximumDrainBytes = 1024 * 1024;
    private const uint MaximumPacketBytes = VoiceProtocol.MaximumDatagramLength;

    private readonly ManualLogSource _logger;
    private readonly bool _loggingEnabled;
    private readonly Func<ulong, bool> _isCandidatePeer;
    private readonly HashSet<ulong> _acceptedPeers = new();
    private readonly Callback<P2PSessionRequest_t> _sessionRequestCallback;
    private readonly Callback<P2PSessionConnectFail_t> _sessionFailureCallback;
    private Il2CppStructArray<byte> _receiveBuffer = new((long)MaximumPacketBytes);
    private Il2CppStructArray<byte> _sendBuffer = new((long)MaximumPacketBytes);
    private Il2CppStructArray<byte>? _drainBuffer;
    private bool _disposed;

    public SteamVoiceTransport(
        ManualLogSource logger,
        bool loggingEnabled,
        Func<ulong, bool> isCandidatePeer)
    {
        _logger = logger;
        _loggingEnabled = loggingEnabled;
        _isCandidatePeer = isCandidatePeer;
        _sessionRequestCallback = Callback<P2PSessionRequest_t>.Create(
            (Action<P2PSessionRequest_t>)OnSessionRequest);
        _sessionFailureCallback = Callback<P2PSessionConnectFail_t>.Create(
            (Action<P2PSessionConnectFail_t>)OnSessionFailure);
    }

    public void AcceptPeer(ulong steamId)
    {
        if (steamId == 0 || steamId == SteamUser.GetSteamID().m_SteamID || !_acceptedPeers.Add(steamId))
        {
            return;
        }
        SteamNetworking.AcceptP2PSessionWithUser(new CSteamID(steamId));
        if (_loggingEnabled)
        {
            _logger.LogInfo($"Accepted proximity voice peer {steamId}");
        }
    }

    public bool Send(ulong steamId, byte[] packet, bool reliable)
    {
        if (_disposed || steamId == 0 || packet.Length == 0 || packet.Length > MaximumPacketBytes)
        {
            return false;
        }
        EnsureSendCapacity(packet.Length);
        for (var index = 0; index < packet.Length; index++)
        {
            _sendBuffer[index] = packet[index];
        }
        var sendType = reliable
            ? EP2PSend.k_EP2PSendReliable
            : EP2PSend.k_EP2PSendUnreliableNoDelay;
        return SteamNetworking.SendP2PPacket(
            new CSteamID(steamId),
            _sendBuffer,
            (uint)packet.Length,
            sendType,
            VoiceChannel);
    }

    public void Poll(Action<CSteamID, byte[]> onPacket)
    {
        if (_disposed)
        {
            return;
        }

        for (var packetIndex = 0; packetIndex < MaximumPacketsPerTick; packetIndex++)
        {
            if (!SteamNetworking.IsP2PPacketAvailable(out var availableBytes, VoiceChannel))
            {
                return;
            }
            if (availableBytes == 0 || availableBytes > MaximumPacketBytes)
            {
                DrainInvalidPacket(availableBytes);
                continue;
            }

            EnsureReceiveCapacity((int)availableBytes);
            if (!SteamNetworking.ReadP2PPacket(
                    _receiveBuffer,
                    (uint)_receiveBuffer.Length,
                    out var receivedBytes,
                    out var remoteSteamId,
                    VoiceChannel)
                || receivedBytes == 0
                || receivedBytes > MaximumPacketBytes)
            {
                continue;
            }

            var packet = new byte[checked((int)receivedBytes)];
            for (var index = 0; index < packet.Length; index++)
            {
                packet[index] = _receiveBuffer[index];
            }
            onPacket(remoteSteamId, packet);
        }
    }

    public void ClosePeer(ulong steamId)
    {
        if (!_acceptedPeers.Remove(steamId))
        {
            return;
        }
        SteamNetworking.CloseP2PChannelWithUser(new CSteamID(steamId), VoiceChannel);
    }

    public void CloseAll()
    {
        foreach (var steamId in _acceptedPeers.ToArray())
        {
            SteamNetworking.CloseP2PChannelWithUser(new CSteamID(steamId), VoiceChannel);
        }
        _acceptedPeers.Clear();
    }

    private void OnSessionRequest(P2PSessionRequest_t request)
    {
        // Admission is finalized by the authenticated Steam sender id and room hash in the first
        // decoded protocol packet. Accepting here only allows Steam's relay handshake to complete.
        var steamId = request.m_steamIDRemote.m_SteamID;
        if (_isCandidatePeer(steamId))
        {
            AcceptPeer(steamId);
        }
        else
        {
            SteamNetworking.CloseP2PChannelWithUser(request.m_steamIDRemote, VoiceChannel);
        }
    }

    private void OnSessionFailure(P2PSessionConnectFail_t failure)
    {
        _acceptedPeers.Remove(failure.m_steamIDRemote.m_SteamID);
        if (_loggingEnabled)
        {
            _logger.LogWarning(
                $"Proximity voice P2P session failed for {failure.m_steamIDRemote.m_SteamID}: {failure.m_eP2PSessionError}");
        }
    }

    private void DrainInvalidPacket(uint availableBytes)
    {
        var capacity = checked((int)Math.Min(Math.Max(availableBytes, 1), MaximumDrainBytes));
        if (_drainBuffer is null || _drainBuffer.Length < capacity)
        {
            _drainBuffer = new Il2CppStructArray<byte>(capacity);
        }
        SteamNetworking.ReadP2PPacket(
            _drainBuffer,
            (uint)_drainBuffer.Length,
            out _,
            out _,
            VoiceChannel);
    }

    private void EnsureReceiveCapacity(int required)
    {
        if (_receiveBuffer.Length < required)
        {
            _receiveBuffer = new Il2CppStructArray<byte>(required);
        }
    }

    private void EnsureSendCapacity(int required)
    {
        if (_sendBuffer.Length < required)
        {
            _sendBuffer = new Il2CppStructArray<byte>(required);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        CloseAll();
        _sessionRequestCallback.Dispose();
        _sessionFailureCallback.Dispose();
    }
}
