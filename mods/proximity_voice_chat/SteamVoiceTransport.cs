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
    private readonly VoicePeerAdmission _admission = new();
    private readonly HashSet<ulong> _loggedSendFailures = new();
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

    public void AllowPeer(ulong steamId)
    {
        if (steamId == 0 || steamId == SteamUser.GetSteamID().m_SteamID)
        {
            return;
        }
        // Do not call AcceptP2PSessionWithUser here. Steam can only accept a session after the
        // remote endpoint has generated P2PSessionRequest_t. Marking a peer accepted before that
        // callback used to suppress the real accept and left both clients in an endless hello
        // loop with no voice packets delivered.
        _admission.Allow(steamId);
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
        var sent = SteamNetworking.SendP2PPacket(
            new CSteamID(steamId),
            _sendBuffer,
            (uint)packet.Length,
            sendType,
            VoiceChannel);
        if (sent)
        {
            _loggedSendFailures.Remove(steamId);
        }
        else if (_loggingEnabled && _loggedSendFailures.Add(steamId))
        {
            _logger.LogWarning($"Proximity voice Steam P2P send is waiting for peer {steamId}");
        }
        return sent;
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
        var wasKnown = _admission.Forget(steamId);
        _loggedSendFailures.Remove(steamId);
        if (!wasKnown)
        {
            return;
        }
        SteamNetworking.CloseP2PChannelWithUser(new CSteamID(steamId), VoiceChannel);
    }

    public void CloseAll()
    {
        foreach (var steamId in _admission.KnownPeers.ToArray())
        {
            SteamNetworking.CloseP2PChannelWithUser(new CSteamID(steamId), VoiceChannel);
        }
        _admission.Clear();
        _loggedSendFailures.Clear();
    }

    private void OnSessionRequest(P2PSessionRequest_t request)
    {
        // Admission is finalized by the authenticated Steam sender id and room hash in the first
        // decoded protocol packet. Accepting here only allows Steam's relay handshake to complete.
        var steamId = request.m_steamIDRemote.m_SteamID;
        if (_admission.CanAcceptRequest(steamId, _isCandidatePeer(steamId)))
        {
            var accepted = SteamNetworking.AcceptP2PSessionWithUser(request.m_steamIDRemote);
            if (accepted)
            {
                _admission.MarkAccepted(steamId);
                _loggedSendFailures.Remove(steamId);
                if (_loggingEnabled)
                {
                    _logger.LogInfo($"Accepted proximity voice Steam P2P session for {steamId}");
                }
            }
            else if (_loggingEnabled)
            {
                _logger.LogWarning($"Steam rejected proximity voice P2P session request from {steamId}");
            }
        }
        else
        {
            SteamNetworking.CloseP2PChannelWithUser(request.m_steamIDRemote, VoiceChannel);
        }
    }

    private void OnSessionFailure(P2PSessionConnectFail_t failure)
    {
        _admission.MarkDisconnected(failure.m_steamIDRemote.m_SteamID);
        _loggedSendFailures.Remove(failure.m_steamIDRemote.m_SteamID);
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
