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
            "Log compatibility handshakes, synchronized leader identity, and match host overrides.");
        return new NetworkHostSelectorConfig(enableMod, enableLogging);
    }
}
