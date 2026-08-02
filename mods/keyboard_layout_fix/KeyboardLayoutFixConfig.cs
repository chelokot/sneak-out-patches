using BepInEx.Configuration;

namespace SneakOut.KeyboardLayoutFix;

internal sealed class KeyboardLayoutFixConfig
{
    private KeyboardLayoutFixConfig(
        ConfigEntry<bool> enableMod,
        ConfigEntry<bool> cycleLayoutsForFlow,
        ConfigEntry<bool> enableLogging)
    {
        EnableMod = enableMod;
        CycleLayoutsForFlow = cycleLayoutsForFlow;
        EnableLogging = enableLogging;
    }

    public ConfigEntry<bool> EnableMod { get; }

    public ConfigEntry<bool> CycleLayoutsForFlow { get; }

    public ConfigEntry<bool> EnableLogging { get; }

    public static KeyboardLayoutFixConfig Bind(ConfigFile configFile)
    {
        var enableMod = configFile.Bind(
            "general",
            "EnableMod",
            true,
            "Keep physical keyboard controls and their displayed labels synchronized with the active Windows keyboard layout.");
        var enableLogging = configFile.Bind(
            "diagnostics",
            "EnableLogging",
            false,
            "Log Windows and Unity keyboard layout transitions and refreshed control labels.");
        var cycleLayoutsForFlow = configFile.Bind(
            "diagnostics",
            "CycleLayoutsForFlow",
            false,
            "Switch the visible selection prompt to Russian and restore English six seconds later. Intended only for unattended visual regression testing.");
        return new KeyboardLayoutFixConfig(enableMod, cycleLayoutsForFlow, enableLogging);
    }
}
