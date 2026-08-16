using BepInEx.Configuration;

namespace SneakOut.CommunityDiscord;

internal sealed class CommunityDiscordConfig
{
    private CommunityDiscordConfig(
        ConfigEntry<bool> enableMod,
        ConfigEntry<string> inviteUrl,
        ConfigEntry<bool> enableLogging)
    {
        EnableMod = enableMod;
        InviteUrl = inviteUrl;
        EnableLogging = enableLogging;
    }

    public ConfigEntry<bool> EnableMod { get; }

    public ConfigEntry<string> InviteUrl { get; }

    public ConfigEntry<bool> EnableLogging { get; }

    public static CommunityDiscordConfig Bind(ConfigFile configFile)
    {
        var enableMod = configFile.Bind(
            "general",
            "EnableMod",
            true,
            "Replace the existing lobby Discord statue invite URL.");
        var inviteUrl = configFile.Bind(
            "general",
            "InviteUrl",
            "https://discord.gg/gFVTPqqCZD",
            "Open this invite when the existing Discord statue is used.");
        var enableLogging = configFile.Bind(
            "general",
            "EnableLogging",
            false,
            "Log Discord statue URL replacement diagnostics.");
        return new CommunityDiscordConfig(enableMod, inviteUrl, enableLogging);
    }
}
