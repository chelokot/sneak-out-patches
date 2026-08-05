using BepInEx.Configuration;

namespace SneakOut.LockerStunFix;

internal sealed class LockerStunFixConfig
{
    private LockerStunFixConfig(
        ConfigEntry<bool> enableMod,
        ConfigEntry<bool> enableLogging)
    {
        EnableMod = enableMod;
        EnableLogging = enableLogging;
    }

    public ConfigEntry<bool> EnableMod { get; }

    public ConfigEntry<bool> EnableLogging { get; }

    public static LockerStunFixConfig Bind(ConfigFile configFile)
    {
        var enableMod = configFile.Bind(
            "general",
            "EnableMod",
            true,
            "Prevent a penguin from stunning nearby seekers when a seeker already opened its locker.");
        var enableLogging = configFile.Bind(
            "general",
            "EnableLogging",
            true,
            "Log locker opener attribution and every Boo allow/suppress decision without player audio or other payload data.");

        return new LockerStunFixConfig(enableMod, enableLogging);
    }
}
