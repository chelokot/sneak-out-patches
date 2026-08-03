using BepInEx.Configuration;

namespace SneakOut.MagicWardrobeHookFix;

internal sealed class MagicWardrobeHookFixConfig
{
    private MagicWardrobeHookFixConfig(ConfigEntry<bool> enableMod, ConfigEntry<bool> enableLogging)
    {
        EnableMod = enableMod;
        EnableLogging = enableLogging;
    }

    public ConfigEntry<bool> EnableMod { get; }

    public ConfigEntry<bool> EnableLogging { get; }

    public static MagicWardrobeHookFixConfig Bind(ConfigFile configFile)
    {
        var enableMod = configFile.Bind(
            "general",
            "EnableMod",
            true,
            "Prevent an interrupted magic-wardrobe entry from moving a Butcher-hooked player back to the wardrobe.");
        var enableLogging = configFile.Bind(
            "general",
            "EnableLogging",
            false,
            "Log magic-wardrobe entries cancelled by a Butcher hook.");

        return new MagicWardrobeHookFixConfig(enableMod, enableLogging);
    }
}
