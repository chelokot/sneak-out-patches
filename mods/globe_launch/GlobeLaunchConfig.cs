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
                "Launch the lobby globe when vanilla recognizes two concurrent players spinning it."),
            configFile.Bind(
                "launch",
                "DistanceMeters",
                100f,
                "Vertical distance travelled by the launched globe (1 to 1000 metres)."),
            configFile.Bind(
                "launch",
                "SpeedMetersPerSecond",
                25f,
                "Speed of the globe's straight-up flight (1 to 200 metres per second)."),
            configFile.Bind(
                "diagnostics",
                "EnableLogging",
                false,
                "Log the vanilla concurrent-player state used to arm the globe launch."));
    }
}
