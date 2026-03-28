using BepInEx.Configuration;

namespace SneakOut.FreeFly;

internal enum FreeFlyAxis
{
    Y,
    Z
}

internal sealed class FreeFlyConfig
{
    private FreeFlyConfig(
        ConfigEntry<bool> enableMod,
        ConfigEntry<float> movementSpeed,
        ConfigEntry<FreeFlyAxis> axis,
        ConfigEntry<bool> enableLogging)
    {
        EnableMod = enableMod;
        MovementSpeed = movementSpeed;
        Axis = axis;
        EnableLogging = enableLogging;
    }

    public ConfigEntry<bool> EnableMod { get; }

    public ConfigEntry<float> MovementSpeed { get; }

    public ConfigEntry<FreeFlyAxis> Axis { get; }

    public ConfigEntry<bool> EnableLogging { get; }

    public static FreeFlyConfig Bind(ConfigFile configFile)
    {
        var enableMod = configFile.Bind(
            "general",
            "EnableMod",
            true,
            "Enable local free-fly controls on PageUp and PageDown.");
        var movementSpeed = configFile.Bind(
            "movement",
            "MovementSpeed",
            8f,
            "Vertical movement speed in units per second.");
        var axis = configFile.Bind(
            "movement",
            "Axis",
            FreeFlyAxis.Y,
            "Axis to move on. Y is the normal Unity vertical axis.");
        var enableLogging = configFile.Bind(
            "general",
            "EnableLogging",
            false,
            "Log local free-fly movement.");

        return new FreeFlyConfig(
            enableMod,
            movementSpeed,
            axis,
            enableLogging);
    }
}
