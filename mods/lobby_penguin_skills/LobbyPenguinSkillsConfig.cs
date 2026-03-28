using BepInEx.Configuration;

namespace SneakOut.LobbyPenguinSkills;

internal sealed class LobbyPenguinSkillsConfig
{
    private LobbyPenguinSkillsConfig(
        ConfigEntry<bool> enableMod,
        ConfigEntry<bool> enableLobbySkillUi,
        ConfigEntry<bool> enableLobbySkillUse,
        ConfigEntry<bool> enableLogging)
    {
        EnableMod = enableMod;
        EnableLobbySkillUi = enableLobbySkillUi;
        EnableLobbySkillUse = enableLobbySkillUse;
        EnableLogging = enableLogging;
    }

    public ConfigEntry<bool> EnableMod { get; }

    public ConfigEntry<bool> EnableLobbySkillUi { get; }

    public ConfigEntry<bool> EnableLobbySkillUse { get; }

    public ConfigEntry<bool> EnableLogging { get; }

    public static LobbyPenguinSkillsConfig Bind(ConfigFile configFile)
    {
        var enableMod = configFile.Bind(
            "general",
            "EnableMod",
            true,
            "Enable lobby-only penguin skill UI and use hooks.");
        var enableLobbySkillUi = configFile.Bind(
            "general",
            "EnableLobbySkillUi",
            true,
            "Show the in-game penguin skill panel while in the lobby.");
        var enableLobbySkillUse = configFile.Bind(
            "general",
            "EnableLobbySkillUse",
            true,
            "Allow the local penguin to use slide and prop-change while in the lobby.");
        var enableLogging = configFile.Bind(
            "general",
            "EnableLogging",
            false,
            "Log lobby penguin skill decisions.");

        return new LobbyPenguinSkillsConfig(
            enableMod,
            enableLobbySkillUi,
            enableLobbySkillUse,
            enableLogging);
    }
}
