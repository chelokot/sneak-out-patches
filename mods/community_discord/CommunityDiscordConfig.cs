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
            "Add a separate red Discord statue toward the lobby portal.");
        var inviteUrl = configFile.Bind(
            "general",
            "InviteUrl",
            "https://discord.gg/gFVTPqqCZD",
            "Open this invite when the separate red Discord statue is used.");
        var enableLogging = configFile.Bind(
            "general",
            "EnableLogging",
            false,
            "Log extra community-statue diagnostics.");
        return new CommunityDiscordConfig(enableMod, inviteUrl, enableLogging);
    }
}
