using BepInEx.Configuration;

namespace SneakOut.StartDelayReducer;

internal sealed class StartDelayReducerConfig
{
    private StartDelayReducerConfig(
        ConfigEntry<bool> enableMod,
        ConfigEntry<float> beforeStartSeconds,
        ConfigEntry<float> countingToStartSeconds,
        ConfigEntry<bool> enableLogging)
    {
        EnableMod = enableMod;
        BeforeStartSeconds = beforeStartSeconds;
        CountingToStartSeconds = countingToStartSeconds;
        EnableLogging = enableLogging;
    }

    public ConfigEntry<bool> EnableMod { get; }

    public ConfigEntry<float> BeforeStartSeconds { get; }

    public ConfigEntry<float> CountingToStartSeconds { get; }

    public ConfigEntry<bool> EnableLogging { get; }

    public static StartDelayReducerConfig Bind(ConfigFile configFile)
    {
        var enableMod = configFile.Bind(
            "general",
            "EnableMod",
            true,
            "Reduce host-side start delays during match startup.");
        var beforeStartSeconds = configFile.Bind(
            "timings",
            "BeforeStartSeconds",
            10f,
            "Target duration in seconds for the BeforeStart phase.");
        var countingToStartSeconds = configFile.Bind(
            "timings",
            "CountingToStartSeconds",
            3f,
            "Target duration in seconds for the CountingToStart phase.");
        var enableLogging = configFile.Bind(
            "general",
            "EnableLogging",
            false,
            "Log tick adjustments for matchmaking startup phases.");

        return new StartDelayReducerConfig(
            enableMod,
            beforeStartSeconds,
            countingToStartSeconds,
            enableLogging);
    }
}
