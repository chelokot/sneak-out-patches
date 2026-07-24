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
            false,
            "Enable the experimental lobby-only penguin skill sandbox.");
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
            false,
            "Allow prop-change only when the lobby has a real room and initialized gameplay prop pool.");
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
