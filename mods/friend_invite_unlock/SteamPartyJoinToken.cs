using System.Text;

namespace SneakOut.FriendInviteUnlock;

internal readonly record struct SteamPartyJoinToken(
    ulong HostSteamId,
    string PartyId,
    string Region)
{
    public const string ArgumentPrefix = "+sneakout_join=";
    public const int MaximumConnectStringBytes = 255;

    private const string ProtocolVersion = "so1";
    private const int MaximumPartyIdCharacters = 160;
    private const int MaximumRegionCharacters = 64;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public bool TryEncode(out string connectString)
    {
        connectString = string.Empty;
        if (HostSteamId == 0
            || !IsValidValue(PartyId, MaximumPartyIdCharacters)
            || !IsValidValue(Region, MaximumRegionCharacters))
        {
            return false;
        }

        var payload = string.Join(
            '.',
            ProtocolVersion,
            HostSteamId.ToString(),
            EncodeValue(PartyId),
            EncodeValue(Region));
        var candidate = ArgumentPrefix + payload;
        if (Encoding.UTF8.GetByteCount(candidate) > MaximumConnectStringBytes)
        {
            return false;
        }

        connectString = candidate;
        return true;
    }

    public static bool TryParse(string? text, out SteamPartyJoinToken token)
    {
        token = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var start = text.IndexOf(ArgumentPrefix, StringComparison.Ordinal);
        if (start < 0)
        {
            return false;
        }

        var valueStart = start + ArgumentPrefix.Length;
        var valueEnd = valueStart;
        while (valueEnd < text.Length
               && !char.IsWhiteSpace(text[valueEnd])
               && text[valueEnd] != '"'
               && text[valueEnd] != '\'')
        {
            valueEnd++;
        }

        var connectString = text[start..valueEnd];
        if (Encoding.UTF8.GetByteCount(connectString) > MaximumConnectStringBytes)
        {
            return false;
        }

        var parts = text[valueStart..valueEnd].Split('.');
        if (parts.Length != 4
            || !string.Equals(parts[0], ProtocolVersion, StringComparison.Ordinal)
            || !ulong.TryParse(parts[1], out var hostSteamId)
            || hostSteamId == 0
            || !TryDecodeValue(parts[2], out var partyId)
            || !TryDecodeValue(parts[3], out var region)
            || !IsValidValue(partyId, MaximumPartyIdCharacters)
            || !IsValidValue(region, MaximumRegionCharacters))
        {
            return false;
        }

        token = new SteamPartyJoinToken(hostSteamId, partyId, region);
        return true;
    }

    public static bool TryExtract(IEnumerable<string> arguments, out SteamPartyJoinToken token)
    {
        foreach (var argument in arguments)
        {
            if (TryParse(argument, out token))
            {
                return true;
            }
        }

        token = default;
        return false;
    }

    private static bool IsValidValue(string? value, int maximumCharacters)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Length <= maximumCharacters
            && !value.Any(char.IsControl);
    }

    private static string EncodeValue(string value)
    {
        return Convert.ToBase64String(StrictUtf8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static bool TryDecodeValue(string encoded, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrEmpty(encoded)
            || encoded.Any(character => !IsBase64UrlCharacter(character)))
        {
            return false;
        }

        var padded = encoded.Replace('-', '+').Replace('_', '/');
        var remainder = padded.Length % 4;
        if (remainder == 1)
        {
            return false;
        }
        padded += remainder switch
        {
            0 => string.Empty,
            2 => "==",
            3 => "=",
            _ => string.Empty,
        };

        try
        {
            value = StrictUtf8.GetString(Convert.FromBase64String(padded));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static bool IsBase64UrlCharacter(char character)
    {
        return character is >= 'A' and <= 'Z'
            or >= 'a' and <= 'z'
            or >= '0' and <= '9'
            or '-'
            or '_';
    }
}
