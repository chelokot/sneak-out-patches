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
            "Suppress Boo after another player opens an occupied locker and use a balanced 1.2 metre Boo zone.");
        var highlightStunZone = configFile.Bind(
            "visuals",
            "HighlightStunZone",
            false,
            "Show the balanced Boo zone as a cyan rounded rectangle around every regular locker.");
        var highlightInteractionZone = configFile.Bind(
            "visuals",
            "HighlightInteractionZone",
            false,
            "Show the amber floor area accepted by the local locker prompt resolver.");
        var enableLogging = configFile.Bind(
            "general",
            "EnableLogging",
            true,
            "Log locker opener attribution, Boo suppression decisions, and balanced spatial queries.");

        return new LockerStunFixConfig(enableMod, highlightStunZone, highlightInteractionZone, enableLogging);
    }
}
