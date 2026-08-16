using BepInEx.Configuration;

namespace SneakOut.NetworkHostSelector;

internal sealed class NetworkHostSelectorConfig
{
    private NetworkHostSelectorConfig(ConfigEntry<bool> enableMod, ConfigEntry<bool> enableLogging)
    {
        EnableMod = enableMod;
        EnableLogging = enableLogging;
    }

    public ConfigEntry<bool> EnableMod { get; }

    public ConfigEntry<bool> EnableLogging { get; }

    public static NetworkHostSelectorConfig Bind(ConfigFile configFile)
    {
        var enableMod = configFile.Bind(
            "general",
            "EnableMod",
            true,
            "Always use the party creator as the Fusion match host when every real participant has this mod.");
        var enableLogging = configFile.Bind(
            "diagnostics",
            "EnableLogging",
            false,
            "Log participant snapshots, handshake traffic and validation, quorum state, and final match host decisions.");
        return new NetworkHostSelectorConfig(enableMod, enableLogging);
    }
}
