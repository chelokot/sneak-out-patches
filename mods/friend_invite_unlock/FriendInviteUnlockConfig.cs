using BepInEx.Configuration;

namespace SneakOut.FriendInviteUnlock;

internal sealed class FriendInviteUnlockConfig
{
    private FriendInviteUnlockConfig(
        ConfigEntry<bool> enableMod,
        ConfigEntry<bool> requireTeamLeader,
        ConfigEntry<bool> enableSteamInvites,
        ConfigEntry<bool> autoJoinSteamInvites,
        ConfigEntry<bool> enableLogging)
    {
        EnableMod = enableMod;
        RequireTeamLeader = requireTeamLeader;
        EnableSteamInvites = enableSteamInvites;
        AutoJoinSteamInvites = autoJoinSteamInvites;
        EnableLogging = enableLogging;
    }

    public ConfigEntry<bool> EnableMod { get; }

    public ConfigEntry<bool> RequireTeamLeader { get; }

    public ConfigEntry<bool> EnableSteamInvites { get; }

    public ConfigEntry<bool> AutoJoinSteamInvites { get; }

    public ConfigEntry<bool> EnableLogging { get; }

    public static FriendInviteUnlockConfig Bind(ConfigFile configFile)
    {
        var enableMod = configFile.Bind(
            "general",
            "EnableMod",
            true,
            "Allow party invites to stay active for offline friends.");
        var requireTeamLeader = configFile.Bind(
            "general",
            "RequireTeamLeader",
            true,
            "Only force invite buttons when the local player is the current team leader.");
        var enableSteamInvites = configFile.Bind(
            "steam",
            "EnableSteamInvites",
            true,
            "Send real Steam game invites and publish Join Game support for an eligible lobby party.");
        var autoJoinSteamInvites = configFile.Bind(
            "steam",
            "AutoJoinSteamInvites",
            true,
            "Join the advertised party after accepting a Steam invite or Steam overlay Join Game request.");
        var enableLogging = configFile.Bind(
            "general",
            "EnableLogging",
            false,
            "Log friend invite and Steam party join state transitions.");

        return new FriendInviteUnlockConfig(
            enableMod,
            requireTeamLeader,
            enableSteamInvites,
            autoJoinSteamInvites,
            enableLogging);
    }
}
