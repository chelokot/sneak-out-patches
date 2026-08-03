namespace SneakOut.NetworkHostSelector;

internal readonly record struct HostSelectionRequest(
    string Membership,
    int Sequence,
    int TargetPlayerRaw,
    string TargetUserId);

internal static class HostSelectionProtocol
{
    public const int Version = 1;

    public static string CreateHello(string membership, string userId)
    {
        return $"{Version}|{membership}|{userId}";
    }

    public static string CreateAck(int revision, string membership, int targetPlayerRaw, string targetUserId)
    {
        return $"{Version}|{revision}|{membership}|{targetPlayerRaw}|{targetUserId}";
    }

    public static string CreateRequest(
        string membership,
        int sequence,
        int targetPlayerRaw,
        string targetUserId)
    {
        return $"{Version}|{membership}|{sequence}|{targetPlayerRaw}|{targetUserId}";
    }

    public static bool TryParseRequest(string value, out HostSelectionRequest request)
    {
        request = default;
        var fields = value.Split('|');
        if (fields.Length != 5
            || !int.TryParse(fields[0], out var version)
            || version != Version
            || string.IsNullOrWhiteSpace(fields[1])
            || !int.TryParse(fields[2], out var sequence)
            || sequence < 0
            || !int.TryParse(fields[3], out var targetPlayerRaw)
            || targetPlayerRaw < 0
            || (targetPlayerRaw == 0 && fields[4].Length != 0)
            || (targetPlayerRaw != 0 && string.IsNullOrWhiteSpace(fields[4])))
        {
            return false;
        }

        request = new HostSelectionRequest(fields[1], sequence, targetPlayerRaw, fields[4]);
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

}
