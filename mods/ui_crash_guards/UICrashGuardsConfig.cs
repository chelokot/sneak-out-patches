using BepInEx.Configuration;

namespace SneakOut.UICrashGuards;

internal sealed class UICrashGuardsConfig
{
    private UICrashGuardsConfig(ConfigEntry<bool> enableMod, ConfigEntry<bool> enableLogging)
    {
        EnableMod = enableMod;
        EnableLogging = enableLogging;
    }

    public ConfigEntry<bool> EnableMod { get; }

    public ConfigEntry<bool> EnableLogging { get; }

    public static UICrashGuardsConfig Bind(ConfigFile configFile)
    {
        var enableMod = configFile.Bind(
            "general",
            "EnableMod",
            true,
            "Turn known crash-prone UI refresh handlers into no-ops.");
        var enableLogging = configFile.Bind(
            "general",
            "EnableLogging",
            false,
            "Log suppressed crash-prone UI refresh handlers.");

        return new UICrashGuardsConfig(enableMod, enableLogging);
    }
}
