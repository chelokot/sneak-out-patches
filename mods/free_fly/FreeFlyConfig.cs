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
        ConfigEntry<bool> autoTraverseMap02,
        ConfigEntry<float> autoTraverseSpeed,
        ConfigEntry<int> autoTraverseLoops,
        ConfigEntry<bool> enableLogging)
    {
        EnableMod = enableMod;
        MovementSpeed = movementSpeed;
        Axis = axis;
        AutoTraverseMap02 = autoTraverseMap02;
        AutoTraverseSpeed = autoTraverseSpeed;
        AutoTraverseLoops = autoTraverseLoops;
        EnableLogging = enableLogging;
    }

    public ConfigEntry<bool> EnableMod { get; }

    public ConfigEntry<float> MovementSpeed { get; }

    public ConfigEntry<FreeFlyAxis> Axis { get; }

    public ConfigEntry<bool> AutoTraverseMap02 { get; }

    public ConfigEntry<float> AutoTraverseSpeed { get; }

    public ConfigEntry<int> AutoTraverseLoops { get; }

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
        var autoTraverseMap02 = configFile.Bind(
            "diagnostics",
            "AutoTraverseMap02",
            false,
            "Move the local host through a repeatable Map02 route for unattended profiling. Never enable in a real match.");
        var autoTraverseSpeed = configFile.Bind(
            "diagnostics",
            "AutoTraverseSpeed",
            8f,
            "World-space speed used by the unattended Map02 traversal probe.");
        var autoTraverseLoops = configFile.Bind(
            "diagnostics",
            "AutoTraverseLoops",
            3,
            "Number of forward/back Map02 traversal loops before the probe stops.");
        var enableLogging = configFile.Bind(
            "general",
            "EnableLogging",
            false,
            "Log local free-fly movement.");

        return new FreeFlyConfig(
            enableMod,
            movementSpeed,
            axis,
            autoTraverseMap02,
            autoTraverseSpeed,
            autoTraverseLoops,
            enableLogging);
    }
}
