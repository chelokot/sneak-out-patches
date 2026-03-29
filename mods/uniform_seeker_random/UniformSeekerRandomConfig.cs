using BepInEx.Configuration;

namespace SneakOut.UniformSeekerRandom;

internal sealed class UniformSeekerRandomConfig
{
    private UniformSeekerRandomConfig(ConfigEntry<bool> enableMod, ConfigEntry<bool> enableLogging)
    {
        EnableMod = enableMod;
        EnableLogging = enableLogging;
    }

    public ConfigEntry<bool> EnableMod { get; }

    public ConfigEntry<bool> EnableLogging { get; }

    public static UniformSeekerRandomConfig Bind(ConfigFile configFile)
    {
        var enableMod = configFile.Bind(
            "general",
            "EnableMod",
            true,
            "Use uniform seeker random selection in default mode.");
        var enableLogging = configFile.Bind(
            "general",
            "EnableLogging",
            false,
            "Log uniform seeker random overrides.");

        return new UniformSeekerRandomConfig(enableMod, enableLogging);
    }
}
