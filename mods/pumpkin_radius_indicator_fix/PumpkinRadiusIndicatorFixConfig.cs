using BepInEx.Configuration;

namespace SneakOut.PumpkinRadiusIndicatorFix;

internal sealed class PumpkinRadiusIndicatorFixConfig
{
    private PumpkinRadiusIndicatorFixConfig(ConfigEntry<bool> enableMod, ConfigEntry<bool> enableLogging)
    {
        EnableMod = enableMod;
        EnableLogging = enableLogging;
    }

    public ConfigEntry<bool> EnableMod { get; }

    public ConfigEntry<bool> EnableLogging { get; }

    public static PumpkinRadiusIndicatorFixConfig Bind(ConfigFile configFile)
    {
        var enableMod = configFile.Bind(
            "general",
            "EnableMod",
            true,
            "Align the hunter trigger ring and the triggered kill/stun effects to their authoritative radii.");
        var enableLogging = configFile.Bind(
            "general",
            "EnableLogging",
            false,
            "Log corrected pumpkin indicator scales.");
        return new PumpkinRadiusIndicatorFixConfig(enableMod, enableLogging);
    }
}
