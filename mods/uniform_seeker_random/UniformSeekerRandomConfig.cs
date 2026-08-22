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
            "Use authoritative uniform seeker selection in compatible private parties. Crown mode is unchanged.");
        var enableLogging = configFile.Bind(
            "general",
            "EnableLogging",
            false,
            "Log launch handshake validation, state authority, candidates, selection, and replicated results.");

        return new UniformSeekerRandomConfig(enableMod, enableLogging);
    }
}
