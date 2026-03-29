using BepInEx.Configuration;

namespace SneakOut.DoublePartyInviteFix;

internal sealed class DoublePartyInviteFixConfig
{
    private DoublePartyInviteFixConfig(ConfigEntry<bool> enableMod, ConfigEntry<bool> enableLogging)
    {
        EnableMod = enableMod;
        EnableLogging = enableLogging;
    }

    public ConfigEntry<bool> EnableMod { get; }

    public ConfigEntry<bool> EnableLogging { get; }

    public static DoublePartyInviteFixConfig Bind(ConfigFile configFile)
    {
        var enableMod = configFile.Bind(
            "general",
            "EnableMod",
            true,
            "Use JoinLobbyEvent lobby id and region directly when joining from the first accepted invite.");
        var enableLogging = configFile.Bind(
            "general",
            "EnableLogging",
            false,
            "Log private-party invite join overrides.");

        return new DoublePartyInviteFixConfig(enableMod, enableLogging);
    }
}
