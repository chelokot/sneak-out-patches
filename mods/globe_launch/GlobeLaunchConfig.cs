using BepInEx.Configuration;

namespace SneakOut.GlobeLaunch;

internal sealed class GlobeLaunchConfig
{
    private GlobeLaunchConfig(
        ConfigEntry<bool> enableMod,
        ConfigEntry<float> launchDistanceMeters,
        ConfigEntry<float> launchSpeedMetersPerSecond,
        ConfigEntry<bool> enableLogging)
    {
        EnableMod = enableMod;
        LaunchDistanceMeters = launchDistanceMeters;
        LaunchSpeedMetersPerSecond = launchSpeedMetersPerSecond;
        EnableLogging = enableLogging;
    }

    public ConfigEntry<bool> EnableMod { get; }

    public ConfigEntry<float> LaunchDistanceMeters { get; }

    public ConfigEntry<float> LaunchSpeedMetersPerSecond { get; }

    public ConfigEntry<bool> EnableLogging { get; }

    public static GlobeLaunchConfig Bind(ConfigFile configFile)
    {
        return new GlobeLaunchConfig(
            configFile.Bind(
                "general",
                "EnableMod",
                true,
                "Launch after the sixth distinct participant's third hit, then disable the empty stand."),
            configFile.Bind(
                "launch",
                "DistanceMeters",
                100f,
                "Maximum distance travelled toward the local camera before cleanup (1 to 1000 metres)."),
            configFile.Bind(
                "launch",
                "SpeedMetersPerSecond",
                20f,
                "Speed of the globe's leftward horizontal curve toward the local camera (1 to 200 metres per second)."),
            configFile.Bind(
                "diagnostics",
                "EnableLogging",
                false,
                "Log the sixth participant's hit count and concurrent-player state used to arm the launch."));
    }
}
