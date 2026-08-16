using BepInEx.Configuration;

namespace SneakOut.PropBuff;

internal sealed class PropBuffConfig
{
    private PropBuffConfig(
        ConfigEntry<bool> enableMod,
        ConfigEntry<bool> enableModelCycling,
        ConfigEntry<bool> enableLogging)
    {
        EnableMod = enableMod;
        EnableModelCycling = enableModelCycling;
        EnableLogging = enableLogging;
    }

    public ConfigEntry<bool> EnableMod { get; }
    public ConfigEntry<bool> EnableModelCycling { get; }
    public ConfigEntry<bool> EnableLogging { get; }

    public static PropBuffConfig Bind(ConfigFile configFile)
    {
        return new PropBuffConfig(
            configFile.Bind("general", "EnableMod", true, "Allow model cycling while transformed into a prop."),
            configFile.Bind("models", "EnableMouseWheelCycling", true, "Cycle the active prop model with the mouse wheel while transformed."),
            configFile.Bind("general", "EnableLogging", false, "Log successful prop model changes."));
    }
}
