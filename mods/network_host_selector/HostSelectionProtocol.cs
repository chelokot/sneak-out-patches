namespace SneakOut.NetworkHostSelector;

internal readonly record struct HostSelectionState(
    int Revision,
    int TargetPlayerRaw,
    string TargetUserId,
    string Membership,
    int CommonCapabilities,
    bool PrivateGame,
    bool Compatible,
    bool Ready);

internal readonly record struct HostSelectionPeer(
    int PlayerRaw,
    string Membership,
    int Capabilities,
    int AcknowledgedRevision);

internal readonly record struct HostSelectionAdvertisement(
    string Membership,
    int Capabilities,
    int AcknowledgedRevision,
    int TargetPlayerRaw);

internal static class HostSelectionProtocol
{
    public const int Version = 7;
    public const string PropertyState = "sohs_s";
    public const string PropertyPeers = "sohs_e";
    public const int UniformSeekerRandomCapability = 1 << 0;

    public static string CreateHello(string membership, int capabilities)
    {
        return $"{Version}|{membership}|{capabilities}";
    }

    public static string CreateAck(
        int revision,
        string membership,
        int targetPlayerRaw,
        int capabilities)
    {
        return $"{Version}|{revision}|{membership}|{targetPlayerRaw}|{capabilities}";
    }

    public static bool TryParseAdvertisement(
        string value,
        out HostSelectionAdvertisement advertisement)
    {
        advertisement = default;
        var fields = value.Split('|');
        if (fields.Length == 3
            && TryParseVersion(fields[0])
            && !string.IsNullOrWhiteSpace(fields[1])
            && int.TryParse(fields[2], out var helloCapabilities)
            && helloCapabilities >= 0)
        {
            advertisement = new HostSelectionAdvertisement(
                fields[1],
                helloCapabilities,
                AcknowledgedRevision: -1,
                TargetPlayerRaw: 0);
            return true;
        }

        if (fields.Length == 5
            && TryParseVersion(fields[0])
            && int.TryParse(fields[1], out var revision)
            && revision >= 0
            && !string.IsNullOrWhiteSpace(fields[2])
            && int.TryParse(fields[3], out var targetPlayerRaw)
            && targetPlayerRaw > 0
            && int.TryParse(fields[4], out var ackCapabilities)
            && ackCapabilities >= 0)
        {
            advertisement = new HostSelectionAdvertisement(
                fields[2],
                ackCapabilities,
                revision,
                targetPlayerRaw);
            return true;
        }

        return false;
    }

    public static string CreateState(
        int revision,
        int targetPlayerRaw,
        string targetUserId,
        string membership,
        int commonCapabilities,
        bool privateGame,
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
            privateGame ? 1 : 0,
            compatible ? 1 : 0,
            ready ? 1 : 0);
    }

    public static bool TryParseState(string value, out HostSelectionState state)
    {
        state = default;
        var fields = value.Split('|');
        if (fields.Length != 9
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
            || !TryParseFlag(fields[6], out var privateGame)
            || !TryParseFlag(fields[7], out var compatible)
            || !TryParseFlag(fields[8], out var ready)
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
            privateGame,
            compatible,
            ready);
        return true;
    }

    public static string UpsertPeer(
        string registry,
        int playerRaw,
        string membership,
        int capabilities,
        int acknowledgedRevision)
    {
        var peers = ParsePeers(registry);
        peers[playerRaw] = new HostSelectionPeer(
            playerRaw,
            membership,
            capabilities,
            acknowledgedRevision);
        return EncodePeers(peers.Values);
    }

    public static bool TryGetPeer(string registry, int playerRaw, out HostSelectionPeer peer)
    {
        return ParsePeers(registry).TryGetValue(playerRaw, out peer);
    }

    public static string RetainCurrentPeers(string registry, IEnumerable<int> playerRefs)
    {
        var currentPlayerRefs = playerRefs
            .Where(playerRaw => playerRaw > 0)
            .ToHashSet();
        return EncodePeers(ParsePeers(registry).Values.Where(peer =>
            currentPlayerRefs.Contains(peer.PlayerRaw)));
    }

    public static bool HasExactPeerSet(
        string registry,
        IEnumerable<int> playerRefs,
        string membership)
    {
        if (string.IsNullOrWhiteSpace(membership))
        {
            return false;
        }

        var expectedPlayerRefs = playerRefs
            .Where(playerRaw => playerRaw > 0)
            .Distinct()
            .ToArray();
        var peers = ParsePeers(registry);
        return expectedPlayerRefs.Length > 0
            && peers.Count == expectedPlayerRefs.Length
            && expectedPlayerRefs.All(playerRaw =>
                peers.TryGetValue(playerRaw, out var peer)
                && string.Equals(peer.Membership, membership, StringComparison.Ordinal));
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
            if (fields.Length != 5
                || !int.TryParse(fields[0], out var version)
                || version != Version
                || !int.TryParse(fields[1], out var playerRaw)
                || playerRaw <= 0
                || string.IsNullOrWhiteSpace(fields[2])
                || !int.TryParse(fields[3], out var capabilities)
                || capabilities < 0
                || !int.TryParse(fields[4], out var acknowledgedRevision)
                || acknowledgedRevision < -1)
            {
                continue;
            }
            result[playerRaw] = new HostSelectionPeer(
                playerRaw,
                fields[2],
                capabilities,
                acknowledgedRevision);
        }
        return result;
    }

    private static string EncodePeers(IEnumerable<HostSelectionPeer> peers)
    {
        return string.Join(
            ";",
            peers
                .OrderBy(peer => peer.PlayerRaw)
                .Select(peer => string.Join(
                    ",",
                    Version,
                    peer.PlayerRaw,
                    peer.Membership,
                    peer.Capabilities,
                    peer.AcknowledgedRevision)));
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

    private static bool TryParseVersion(string value)
    {
        return int.TryParse(value, out var version) && version == Version;
    }

}
