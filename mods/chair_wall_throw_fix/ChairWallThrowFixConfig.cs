using BepInEx.Configuration;

namespace SneakOut.ChairWallThrowFix;

internal sealed class ChairWallThrowFixConfig
{
    private ChairWallThrowFixConfig(
        ConfigEntry<bool> enableMod,
        ConfigEntry<float> maximumReleaseCorrection,
        ConfigEntry<bool> enableLogging)
    {
        EnableMod = enableMod;
        MaximumReleaseCorrection = maximumReleaseCorrection;
        EnableLogging = enableLogging;
    }

    public ConfigEntry<bool> EnableMod { get; }

    public ConfigEntry<float> MaximumReleaseCorrection { get; }

    public ConfigEntry<bool> EnableLogging { get; }

    public static ChairWallThrowFixConfig Bind(ConfigFile configFile)
    {
        var enableMod = configFile.Bind(
            "general",
            "EnableMod",
            true,
            "Let held throwables be released beside obstacles and clamp chairs to the player's side of intervening walls.");
        var maximumReleaseCorrection = configFile.Bind(
            "general",
            "MaximumReleaseCorrection",
            0.65f,
            new ConfigDescription(
                "Maximum fallback correction in metres for a chair that still overlaps geometry after its swept release clamp.",
                new AcceptableValueRange<float>(0.1f, 1f)));
        var enableLogging = configFile.Bind(
            "general",
            "EnableLogging",
            false,
            "Log front-obstacle UI suppression and corrected release positions. Actual blocked throw overrides are always logged once per input attempt.");

        return new ChairWallThrowFixConfig(enableMod, maximumReleaseCorrection, enableLogging);
    }
}
