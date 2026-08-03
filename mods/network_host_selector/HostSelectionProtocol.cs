namespace SneakOut.NetworkHostSelector;

internal enum HostSelectionMessageType : byte
{
    Hello = 1,
    SelectRequest = 2,
    ProposalAck = 3,
}

internal readonly record struct HostSelectionMessage(
    HostSelectionMessageType Type,
    int Revision,
    int TargetPlayerRaw);

internal static class HostSelectionProtocol
{
    public const int Version = 1;
    public const int PayloadLength = 16;
    private const uint Magic = 0x53484F53; // SOHS in little-endian byte order.

    public static byte[] Encode(HostSelectionMessageType type, int revision = 0, int targetPlayerRaw = 0)
    {
        var payload = new byte[PayloadLength];
        WriteUInt32(payload, 0, Magic);
        payload[4] = Version;
        payload[5] = (byte)type;
        WriteInt32(payload, 8, revision);
        WriteInt32(payload, 12, targetPlayerRaw);
        return payload;
    }

    public static bool TryDecode(IReadOnlyList<byte> payload, out HostSelectionMessage message)
    {
        message = default;
        if (payload.Count != PayloadLength
            || ReadUInt32(payload, 0) != Magic
            || payload[4] != Version
            || !Enum.IsDefined(typeof(HostSelectionMessageType), payload[5]))
        {
            return false;
        }

        message = new HostSelectionMessage(
            (HostSelectionMessageType)payload[5],
            ReadInt32(payload, 8),
            ReadInt32(payload, 12));
        return true;
    }

    public static string ComputeMembershipSignature(IEnumerable<(int PlayerRaw, string UserId)> participants)
    {
        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var hash = offsetBasis;
        foreach (var participant in participants
                     .OrderBy(entry => entry.PlayerRaw)
                     .ThenBy(entry => entry.UserId, StringComparer.Ordinal))
        {
            foreach (var value in BitConverter.GetBytes(participant.PlayerRaw))
            {
                hash = (hash ^ value) * prime;
            }
            foreach (var value in System.Text.Encoding.UTF8.GetBytes(participant.UserId))
            {
                hash = (hash ^ value) * prime;
            }
            hash = (hash ^ 0xff) * prime;
        }
        return hash.ToString("X16");
    }

    private static void WriteInt32(IList<byte> target, int offset, int value)
    {
        WriteUInt32(target, offset, unchecked((uint)value));
    }

    private static void WriteUInt32(IList<byte> target, int offset, uint value)
    {
        target[offset] = (byte)value;
        target[offset + 1] = (byte)(value >> 8);
        target[offset + 2] = (byte)(value >> 16);
        target[offset + 3] = (byte)(value >> 24);
    }

    private static int ReadInt32(IReadOnlyList<byte> source, int offset)
    {
        return unchecked((int)ReadUInt32(source, offset));
    }

    private static uint ReadUInt32(IReadOnlyList<byte> source, int offset)
    {
        return source[offset]
            | ((uint)source[offset + 1] << 8)
            | ((uint)source[offset + 2] << 16)
            | ((uint)source[offset + 3] << 24);
    }
}
