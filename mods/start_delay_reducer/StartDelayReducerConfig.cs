using BepInEx.Configuration;

namespace SneakOut.StartDelayReducer;

internal sealed class StartDelayReducerConfig
{
    private StartDelayReducerConfig(
        ConfigEntry<bool> enableMod,
        ConfigEntry<bool> enableLogging)
    {
        EnableMod = enableMod;
        EnableLogging = enableLogging;
    }

    public ConfigEntry<bool> EnableMod { get; }

    public ConfigEntry<bool> EnableLogging { get; }

    public static StartDelayReducerConfig Bind(ConfigFile configFile)
    {
        var enableMod = configFile.Bind(
            "general",
            "EnableMod",
            true,
            "Show a host-only Start Now button while the match waits for players.");
        var enableLogging = configFile.Bind(
            "general",
            "EnableLogging",
            false,
            "Log Start Now button creation and authoritative phase skips.");

        return new StartDelayReducerConfig(
            enableMod,
            enableLogging);
    }
}
