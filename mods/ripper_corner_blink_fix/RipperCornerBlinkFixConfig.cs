using BepInEx.Configuration;

namespace SneakOut.RipperCornerBlinkFix;

internal sealed class RipperCornerBlinkFixConfig
{
    private RipperCornerBlinkFixConfig(ConfigEntry<bool> enableMod, ConfigEntry<bool> enableLogging)
    {
        EnableMod = enableMod;
        EnableLogging = enableLogging;
    }

    public ConfigEntry<bool> EnableMod { get; }

    public ConfigEntry<bool> EnableLogging { get; }

    public static RipperCornerBlinkFixConfig Bind(ConfigFile configFile)
    {
        var enableMod = configFile.Bind(
            "general",
            "EnableMod",
            true,
            "Fix the ReaperHelloThere wall-blink perk at Intersections-layer room junctions when the destination is valid NavMesh. Does nothing without the perk.");
        var enableLogging = configFile.Bind(
            "general",
            "EnableLogging",
            false,
            "Log each shared-corner blink accepted by the patch.");
        return new RipperCornerBlinkFixConfig(enableMod, enableLogging);
    }
}
