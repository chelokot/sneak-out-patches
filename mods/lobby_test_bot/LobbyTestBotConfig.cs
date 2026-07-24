using BepInEx.Configuration;

namespace SneakOut.LobbyTestBot;

internal sealed class LobbyTestBotConfig
{
    private LobbyTestBotConfig(
        ConfigEntry<bool> enableMod,
        ConfigEntry<bool> autoAddBotWhenLobbyReady,
        ConfigEntry<string> botNickname,
        ConfigEntry<bool> enableLogging)
    {
        EnableMod = enableMod;
        AutoAddBotWhenLobbyReady = autoAddBotWhenLobbyReady;
        BotNickname = botNickname;
        EnableLogging = enableLogging;
    }

    public ConfigEntry<bool> EnableMod { get; }

    public ConfigEntry<bool> AutoAddBotWhenLobbyReady { get; }

    public ConfigEntry<string> BotNickname { get; }

    public ConfigEntry<bool> EnableLogging { get; }

    public static LobbyTestBotConfig Bind(ConfigFile configFile)
    {
        var enableMod = configFile.Bind(
            "general",
            "EnableMod",
            true,
            "Show the host-only lobby button and allow it to spawn one real inert network player.");
        var autoAddBotWhenLobbyReady = configFile.Bind(
            "diagnostics",
            "AutoAddBotWhenLobbyReady",
            false,
            "Automatically add the bot after the host and player registry are ready. Intended for repeatable runtime testing.");
        var botNickname = configFile.Bind(
            "bot",
            "Nickname",
            "TEST BOT",
            "Nickname used for the managed lobby bot.");
        var enableLogging = configFile.Bind(
            "diagnostics",
            "EnableLogging",
            false,
            "Log lobby bot eligibility and authoritative spawn state.");

        return new LobbyTestBotConfig(enableMod, autoAddBotWhenLobbyReady, botNickname, enableLogging);
    }
}
