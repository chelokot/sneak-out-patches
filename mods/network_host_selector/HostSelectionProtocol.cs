namespace SneakOut.NetworkHostSelector;

internal readonly record struct HostSelectionState(
    int Revision,
    int TargetPlayerRaw,
    string TargetUserId,
    string Membership,
    int CommonCapabilities,
    bool Compatible,
    bool Ready);

internal readonly record struct HostSelectionPeer(
    int PlayerRaw,
    string UserId,
    string Membership,
    int Capabilities,
    int AcknowledgedRevision);

internal static class HostSelectionProtocol
{
    public const int Version = 5;
    public const string PropertyState = "sohs_s";
    public const string PropertyPeers = "sohs_e";
    public const int UniformSeekerRandomCapability = 1 << 0;

    public static string CreateHello(string membership, string userId, int capabilities)
    {
        return $"{Version}|{membership}|{userId}|{capabilities}";
    }

    public static string CreateAck(
        int revision,
        string membership,
        int targetPlayerRaw,
        string targetUserId,
        int capabilities)
    {
        return $"{Version}|{revision}|{membership}|{targetPlayerRaw}|{targetUserId}|{capabilities}";
    }

    public static string CreateState(
        int revision,
        int targetPlayerRaw,
        string targetUserId,
        string membership,
        int commonCapabilities,
        bool compatible,
        bool ready)
    {
        return string.Join(
            "|",
            Version,
            revision,
            targetPlayerRaw,
            EncodeToken(targetUserId),
            membership,
            commonCapabilities,
            compatible ? 1 : 0,
            ready ? 1 : 0);
    }

    public static bool TryParseState(string value, out HostSelectionState state)
    {
        state = default;
        var fields = value.Split('|');
        if (fields.Length != 8
            || !int.TryParse(fields[0], out var version)
            || version != Version
            || !int.TryParse(fields[1], out var revision)
            || revision < 0
            || !int.TryParse(fields[2], out var targetPlayerRaw)
            || targetPlayerRaw < 0
            || !TryDecodeToken(fields[3], out var targetUserId)
            || string.IsNullOrWhiteSpace(fields[4])
            || !int.TryParse(fields[5], out var commonCapabilities)
            || commonCapabilities < 0
            || !TryParseFlag(fields[6], out var compatible)
            || !TryParseFlag(fields[7], out var ready)
            || (targetPlayerRaw == 0 && targetUserId.Length != 0)
            || (targetPlayerRaw != 0 && string.IsNullOrWhiteSpace(targetUserId)))
        {
            return false;
        }

        state = new HostSelectionState(
            revision,
            targetPlayerRaw,
            targetUserId,
            fields[4],
            commonCapabilities,
            compatible,
            ready);
        return true;
    }

    public static string UpsertPeer(
        string registry,
        int playerRaw,
        string userId,
        string membership,
        int capabilities,
        int acknowledgedRevision)
    {
        var peers = ParsePeers(registry);
        peers[playerRaw] = new HostSelectionPeer(
            playerRaw,
            userId,
            membership,
            capabilities,
            acknowledgedRevision);
        return string.Join(
            ";",
            peers.Values
                .OrderBy(peer => peer.PlayerRaw)
                .Select(peer => string.Join(
                    ",",
                    Version,
                    peer.PlayerRaw,
                    EncodeToken(peer.UserId),
                    peer.Membership,
                    peer.Capabilities,
                    peer.AcknowledgedRevision)));
    }

    public static bool TryGetPeer(string registry, int playerRaw, out HostSelectionPeer peer)
    {
        return ParsePeers(registry).TryGetValue(playerRaw, out peer);
    }

    public static string ComputeMembershipSignature(IEnumerable<int> playerRefs)
    {
        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var hash = offsetBasis;
        foreach (var playerRaw in playerRefs.OrderBy(value => value))
        {
            foreach (var value in BitConverter.GetBytes(playerRaw))
            {
                hash = (hash ^ value) * prime;
            }
            hash = (hash ^ 0xff) * prime;
        }
        return hash.ToString("X16");
    }

    private static Dictionary<int, HostSelectionPeer> ParsePeers(string registry)
    {
        var result = new Dictionary<int, HostSelectionPeer>();
        foreach (var encodedPeer in registry.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = encodedPeer.Split(',');
            if (fields.Length != 6
                || !int.TryParse(fields[0], out var version)
                || version != Version
                || !int.TryParse(fields[1], out var playerRaw)
                || playerRaw <= 0
                || !TryDecodeToken(fields[2], out var userId)
                || string.IsNullOrWhiteSpace(userId)
                || string.IsNullOrWhiteSpace(fields[3])
                || !int.TryParse(fields[4], out var capabilities)
                || capabilities < 0
                || !int.TryParse(fields[5], out var acknowledgedRevision)
                || acknowledgedRevision < -1)
            {
                continue;
            }
            result[playerRaw] = new HostSelectionPeer(
                playerRaw,
                userId,
                fields[3],
                capabilities,
                acknowledgedRevision);
        }
        return result;
    }

    private static string EncodeToken(string value)
    {
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value));
    }

    private static bool TryDecodeToken(string value, out string decoded)
    {
        decoded = string.Empty;
        try
        {
            decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(value));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool TryParseFlag(string value, out bool flag)
    {
        flag = value == "1";
        return flag || value == "0";
    }

}
