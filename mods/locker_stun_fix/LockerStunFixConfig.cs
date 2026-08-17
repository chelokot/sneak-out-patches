using BepInEx.Configuration;

namespace SneakOut.LockerStunFix;

internal sealed class LockerStunFixConfig
{
    private LockerStunFixConfig(
        ConfigEntry<bool> enableMod,
        ConfigEntry<bool> highlightStunZone,
        ConfigEntry<bool> highlightInteractionZone,
        ConfigEntry<bool> enableLogging)
    {
        EnableMod = enableMod;
        HighlightStunZone = highlightStunZone;
        HighlightInteractionZone = highlightInteractionZone;
        EnableLogging = enableLogging;
    }

    public ConfigEntry<bool> EnableMod { get; }

    public ConfigEntry<bool> HighlightStunZone { get; }

    public ConfigEntry<bool> HighlightInteractionZone { get; }

    public ConfigEntry<bool> EnableLogging { get; }

    public static LockerStunFixConfig Bind(ConfigFile configFile)
    {
        var enableMod = configFile.Bind(
            "general",
            "EnableMod",
            true,
            "Prevent a penguin from stunning nearby seekers when a seeker already opened its locker.");
        var highlightStunZone = configFile.Bind(
            "visuals",
            "HighlightStunZone",
            true,
            "Always show the native Boo sphere's floor-level cross-section and facing arrow on every regular locker.");
        var highlightInteractionZone = configFile.Bind(
            "visuals",
            "HighlightInteractionZone",
            true,
            "Always show an amber floor area sampled from the native locker CanInteract predicate.");
        var enableLogging = configFile.Bind(
            "general",
            "EnableLogging",
            true,
            "Log locker opener attribution and every Boo allow/suppress decision without player audio or other payload data.");

        return new LockerStunFixConfig(enableMod, highlightStunZone, highlightInteractionZone, enableLogging);
    }
}
