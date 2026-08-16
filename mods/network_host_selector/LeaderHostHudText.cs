namespace SneakOut.NetworkHostSelector;

internal static class LeaderHostHudText
{
    private const string Separator = "   ";
    private const string HostPrefix = "Host: ";

    public static string Compose(string? mapText, string? hostName)
    {
        var stockText = RemoveHostSuffix(mapText ?? string.Empty);
        var normalizedName = NormalizeName(hostName);
        if (normalizedName.Length == 0)
        {
            return stockText;
        }

        return stockText.Length == 0
            ? HostPrefix + normalizedName
            : stockText + Separator + HostPrefix + normalizedName;
    }

    private static string RemoveHostSuffix(string text)
    {
        var markerIndex = text.IndexOf(Separator + HostPrefix, StringComparison.Ordinal);
        if (markerIndex >= 0)
        {
            return text[..markerIndex];
        }

        return text.StartsWith(HostPrefix, StringComparison.Ordinal)
            ? string.Empty
            : text;
    }

    private static string NormalizeName(string? hostName)
    {
        return string.IsNullOrWhiteSpace(hostName)
            ? string.Empty
            : string.Join(
                " ",
                hostName.Split(
                    new[] { '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
