using System.Globalization;

namespace SneakOut.ProximityVoiceChat;

internal static class VoicePlayerVolumePolicy
{
    public const float MinimumVolume = 0f;
    public const float MaximumVolume = 2f;

    public static Dictionary<ulong, float> Parse(string? text)
    {
        var result = new Dictionary<ulong, float>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        foreach (var part in text.Split(
                     new[] { ',', ';', '\r', '\n', '\t' },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = part.IndexOf('=');
            if (separatorIndex <= 0)
            {
                separatorIndex = part.IndexOf(':');
            }
            if (separatorIndex <= 0
                || !ulong.TryParse(part[..separatorIndex].Trim(), out var steamId)
                || steamId == 0
                || !float.TryParse(
                    part[(separatorIndex + 1)..].Trim(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var volume)
                || !float.IsFinite(volume))
            {
                continue;
            }

            result[steamId] = Math.Clamp(volume, MinimumVolume, MaximumVolume);
        }
        return result;
    }

    public static string Serialize(IReadOnlyDictionary<ulong, float> volumes)
    {
        return string.Join(
            ",",
            volumes
                .Where(pair => pair.Key != 0 && float.IsFinite(pair.Value))
                .OrderBy(pair => pair.Key)
                .Select(pair => $"{pair.Key}={Math.Clamp(pair.Value, MinimumVolume, MaximumVolume).ToString("R", CultureInfo.InvariantCulture)}"));
    }
}
