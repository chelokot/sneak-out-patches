namespace SneakOut.ProximityVoiceChat;

internal enum VoiceTransportSendMode
{
    Bootstrap,
    Realtime,
    Reliable,
}

internal static class VoiceTransportSendPolicy
{
    public static VoiceTransportSendMode ForControlPacket(VoicePacketKind kind)
    {
        return kind switch
        {
            VoicePacketKind.Hello => VoiceTransportSendMode.Bootstrap,
            VoicePacketKind.Goodbye => VoiceTransportSendMode.Reliable,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    public static bool CanInitiateConnection(VoiceTransportSendMode mode)
    {
        return mode is VoiceTransportSendMode.Bootstrap or VoiceTransportSendMode.Reliable;
    }
}
