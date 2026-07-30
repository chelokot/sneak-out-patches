using BepInEx.Configuration;

namespace SneakOut.BackgroundLoadingGuard;

internal sealed class BackgroundLoadingGuardConfig
{
    private BackgroundLoadingGuardConfig(
        ConfigEntry<bool> enableMod,
        ConfigEntry<bool> forceRunInBackground,
        ConfigEntry<bool> enableLogging)
    {
        EnableMod = enableMod;
        ForceRunInBackground = forceRunInBackground;
        EnableLogging = enableLogging;
    }

    public ConfigEntry<bool> EnableMod { get; }

    public ConfigEntry<bool> ForceRunInBackground { get; }

    public ConfigEntry<bool> EnableLogging { get; }

    public static BackgroundLoadingGuardConfig Bind(ConfigFile configFile)
    {
        var enableMod = configFile.Bind(
            "general",
            "EnableMod",
            true,
            "Keep scene loading active while the game is temporarily unfocused.");
        var forceRunInBackground = configFile.Bind(
            "general",
            "ForceRunInBackground",
            true,
            "Keep the game running while unfocused during early loading.");
        var enableLogging = configFile.Bind(
            "general",
            "EnableLogging",
            false,
            "Log loading-screen start, scene activation, and focus changes during loading.");

        return new BackgroundLoadingGuardConfig(enableMod, forceRunInBackground, enableLogging);
    }
}
