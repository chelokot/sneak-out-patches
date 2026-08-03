using BepInEx.Configuration;

namespace SneakOut.LockerStunFix;

internal sealed class LockerStunFixConfig
{
    private LockerStunFixConfig(
        ConfigEntry<bool> enableMod,
        ConfigEntry<bool> fixHookExitSnap,
        ConfigEntry<bool> enableLogging)
    {
        EnableMod = enableMod;
        FixHookExitSnap = fixHookExitSnap;
        EnableLogging = enableLogging;
    }

    public ConfigEntry<bool> EnableMod { get; }

    public ConfigEntry<bool> FixHookExitSnap { get; }

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
            false,
            "Log seeker-opened locker tracking, suppressed locker stuns, and cancelled stale exit movement.");

        var fixHookExitSnap = configFile.Bind(
            "general",
            "FixHookExitSnap",
            true,
            "Stop an unfinished locker-exit lerp from teleporting a player back after a Butcher hook pulls them away.");

        return new LockerStunFixConfig(enableMod, fixHookExitSnap, enableLogging);
    }
}
