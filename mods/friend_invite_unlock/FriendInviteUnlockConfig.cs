using BepInEx.Configuration;

namespace SneakOut.FriendInviteUnlock;

internal sealed class FriendInviteUnlockConfig
{
    private FriendInviteUnlockConfig(
        ConfigEntry<bool> enableMod,
        ConfigEntry<bool> requireTeamLeader,
        ConfigEntry<bool> enableLogging)
    {
        EnableMod = enableMod;
        RequireTeamLeader = requireTeamLeader;
        EnableLogging = enableLogging;
    }

    public ConfigEntry<bool> EnableMod { get; }

    public ConfigEntry<bool> RequireTeamLeader { get; }

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
        var enableLogging = configFile.Bind(
            "general",
            "EnableLogging",
            false,
            "Log forced friend invite state transitions.");

        return new FriendInviteUnlockConfig(
            enableMod,
            requireTeamLeader,
            enableLogging);
    }
}
