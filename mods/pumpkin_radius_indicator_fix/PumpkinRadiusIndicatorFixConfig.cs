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
            "Scale the pumpkin's visible danger ring to the authoritative instant-kill radius.");
        var enableLogging = configFile.Bind(
            "general",
            "EnableLogging",
            false,
            "Log corrected pumpkin indicator scales.");
        return new PumpkinRadiusIndicatorFixConfig(enableMod, enableLogging);
    }
}
