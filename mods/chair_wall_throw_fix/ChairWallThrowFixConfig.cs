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
            "Let a held chair leave the player's hands when its release position slightly overlaps level geometry.");
        var maximumReleaseCorrection = configFile.Bind(
            "general",
            "MaximumReleaseCorrection",
            0.65f,
            new ConfigDescription(
                "Maximum distance in metres that an overlapping held chair may be moved back toward its thrower before the stock throw runs.",
                new AcceptableValueRange<float>(0.1f, 1f)));
        var enableLogging = configFile.Bind(
            "general",
            "EnableLogging",
            false,
            "Log front-obstacle UI suppression and corrected release positions. Actual blocked throw overrides are always logged once per input attempt.");

        return new ChairWallThrowFixConfig(enableMod, maximumReleaseCorrection, enableLogging);
    }
}
