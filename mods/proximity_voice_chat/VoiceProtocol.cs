using System.Buffers.Binary;

namespace SneakOut.ProximityVoiceChat;

internal enum VoicePacketKind : byte
{
    Hello = 1,
    Audio = 2,
    Goodbye = 3,
}

internal readonly record struct VoicePacket(
    VoicePacketKind Kind,
    ulong SessionHash,
    ulong SenderSteamId,
    ulong SenderInstanceId,
    int SenderInternalId,
    uint Sequence,
    uint CaptureTimestampMilliseconds,
    ushort FragmentIndex,
    ushort FragmentCount,
    byte[] Payload);

internal static class VoiceProtocol
{
    private const uint Magic = 0x43565053; // SPVC in little-endian packet order.
    private const byte Version = 2;
    private const byte OpusCodecProfile = 1;
    public const int HeaderLength = 48;
    public const int MaximumDatagramLength = 1200;
    public const int MaximumFragmentPayloadLength = MaximumDatagramLength - HeaderLength;
    public const int MaximumPayloadLength = 32 * 1024;

    public static byte[] Encode(in VoicePacket packet)
    {
        if (packet.Payload.Length > MaximumFragmentPayloadLength
            || packet.FragmentCount == 0
            || packet.FragmentIndex >= packet.FragmentCount)
        {
            throw new ArgumentOutOfRangeException(nameof(packet), "Invalid voice datagram fragmentation.");
        }

        var result = new byte[HeaderLength + packet.Payload.Length];
        var span = result.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span, Magic);
        span[4] = Version;
        span[5] = (byte)packet.Kind;
        span[6] = OpusCodecProfile;
        span[7] = 0;
        BinaryPrimitives.WriteUInt64LittleEndian(span[8..], packet.SessionHash);
        BinaryPrimitives.WriteUInt64LittleEndian(span[16..], packet.SenderSteamId);
        BinaryPrimitives.WriteUInt64LittleEndian(span[24..], packet.SenderInstanceId);
        BinaryPrimitives.WriteInt32LittleEndian(span[32..], packet.SenderInternalId);
        BinaryPrimitives.WriteUInt32LittleEndian(span[36..], packet.Sequence);
        BinaryPrimitives.WriteUInt32LittleEndian(span[40..], packet.CaptureTimestampMilliseconds);
        BinaryPrimitives.WriteUInt16LittleEndian(span[44..], packet.FragmentIndex);
        BinaryPrimitives.WriteUInt16LittleEndian(span[46..], packet.FragmentCount);
        packet.Payload.CopyTo(result, HeaderLength);
        return result;
    }

    public static bool TryDecode(ReadOnlySpan<byte> data, out VoicePacket packet)
    {
        packet = default;
        if (data.Length < HeaderLength
            || BinaryPrimitives.ReadUInt32LittleEndian(data) != Magic
            || data[4] != Version)
        {
            return false;
        }

        var kind = (VoicePacketKind)data[5];
        if (kind is < VoicePacketKind.Hello or > VoicePacketKind.Goodbye)
        {
            return false;
        }

        if (data[6] != OpusCodecProfile || data[7] != 0)
        {
            return false;
        }

        var payloadLength = data.Length - HeaderLength;
        var fragmentIndex = BinaryPrimitives.ReadUInt16LittleEndian(data[44..]);
        var fragmentCount = BinaryPrimitives.ReadUInt16LittleEndian(data[46..]);
        var maximumFragments = (MaximumPayloadLength + MaximumFragmentPayloadLength - 1)
            / MaximumFragmentPayloadLength;
        if (payloadLength > MaximumFragmentPayloadLength
            || fragmentCount == 0
            || fragmentCount > maximumFragments
            || fragmentIndex >= fragmentCount
            || (kind == VoicePacketKind.Audio && payloadLength == 0)
            || (kind != VoicePacketKind.Audio && (payloadLength != 0 || fragmentIndex != 0 || fragmentCount != 1)))
        {
            return false;
        }

        packet = new VoicePacket(
            kind,
            BinaryPrimitives.ReadUInt64LittleEndian(data[8..]),
            BinaryPrimitives.ReadUInt64LittleEndian(data[16..]),
            BinaryPrimitives.ReadUInt64LittleEndian(data[24..]),
            BinaryPrimitives.ReadInt32LittleEndian(data[32..]),
            BinaryPrimitives.ReadUInt32LittleEndian(data[36..]),
            BinaryPrimitives.ReadUInt32LittleEndian(data[40..]),
            fragmentIndex,
            fragmentCount,
            data[HeaderLength..].ToArray());
        return true;
    }

    public static ulong HashSessionName(string sessionName)
    {
        // Stable FNV-1a is enough for room separation; this value is not an authentication token.
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var hash = offset;
        foreach (var value in System.Text.Encoding.UTF8.GetBytes(sessionName))
        {
            hash ^= value;
            hash *= prime;
        }
        return hash;
    }
}
