using BepInEx.Configuration;

namespace SneakOut.LobbySkillSandbox;

internal sealed class LobbySkillSandboxConfig
{
    private LobbySkillSandboxConfig(
        ConfigEntry<bool> enableMod,
        ConfigEntry<bool> enableLobbySkillUi,
        ConfigEntry<bool> enableLobbySkillUse,
        ConfigEntry<bool> enableLobbyPropChange,
        ConfigEntry<bool> enableLogging)
    {
        EnableMod = enableMod;
        EnableLobbySkillUi = enableLobbySkillUi;
        EnableLobbySkillUse = enableLobbySkillUse;
        EnableLobbyPropChange = enableLobbyPropChange;
        EnableLogging = enableLogging;
    }

    public ConfigEntry<bool> EnableMod { get; }

    public ConfigEntry<bool> EnableLobbySkillUi { get; }

    public ConfigEntry<bool> EnableLobbySkillUse { get; }

    public ConfigEntry<bool> EnableLobbyPropChange { get; }

    public ConfigEntry<bool> EnableLogging { get; }

    public static LobbySkillSandboxConfig Bind(ConfigFile configFile)
    {
        var enableMod = configFile.Bind(
            "general",
            "EnableMod",
            true,
            "Enable the lobby-only penguin skill panel and supported lobby slide.");
        var enableLobbySkillUi = configFile.Bind(
            "general",
            "EnableLobbySkillUi",
            true,
            "Show the in-game penguin skill panel while in the lobby.");
        var enableLobbySkillUse = configFile.Bind(
            "general",
            "EnableLobbySkillUse",
            true,
            "Allow the local penguin to use slide and separately enabled experimental skills while in the lobby.");
        var enableLobbyPropChange = configFile.Bind(
            "general",
            "EnableLobbyPropChange",
            true,
            "Allow networked lobby prop-change using lobby scenery. Every lobby participant must run this mod.");
        var enableLogging = configFile.Bind(
            "general",
            "EnableLogging",
            false,
            "Log lobby penguin skill decisions.");

        return new LobbySkillSandboxConfig(
            enableMod,
            enableLobbySkillUi,
            enableLobbySkillUse,
            enableLobbyPropChange,
            enableLogging);
    }
}
