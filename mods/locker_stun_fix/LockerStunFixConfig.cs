using BepInEx.Configuration;

namespace SneakOut.LockerStunFix;

internal sealed class LockerStunFixConfig
{
    private LockerStunFixConfig(
        ConfigEntry<bool> enableMod,
        ConfigEntry<bool> highlightStunZone,
        ConfigEntry<bool> enableLogging)
    {
        EnableMod = enableMod;
        HighlightStunZone = highlightStunZone;
        EnableLogging = enableLogging;
    }

    public ConfigEntry<bool> EnableMod { get; }

    public ConfigEntry<bool> HighlightStunZone { get; }

    public ConfigEntry<bool> EnableLogging { get; }

    public static LockerStunFixConfig Bind(ConfigFile configFile)
    {
        var enableMod = configFile.Bind(
            "general",
            "EnableMod",
            true,
            "Prevent a penguin from stunning nearby seekers when a seeker already opened its locker.");
        var highlightStunZone = configFile.Bind(
            "visuals",
            "HighlightStunZone",
            true,
            "Show the exact forward-offset Boo overlap boundary and facing arrow when an eligible penguin exits a locker.");
        var enableLogging = configFile.Bind(
            "general",
            "EnableLogging",
            true,
            "Log locker opener attribution and every Boo allow/suppress decision without player audio or other payload data.");

        return new LockerStunFixConfig(enableMod, highlightStunZone, enableLogging);
    }
}
