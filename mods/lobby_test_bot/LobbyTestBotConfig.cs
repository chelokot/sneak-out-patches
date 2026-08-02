using BepInEx.Configuration;

namespace SneakOut.LobbyTestBot;

internal enum DiagnosticMap
{
    Random,
    Map01,
    Map02,
    Map03,
    Map04,
    MapEast01,
    MapEast02,
    MapSchool01,
    MapSchool02,
}

internal enum DiagnosticGameMode
{
    Preserve,
    Classic,
    Crown,
}

internal sealed class LobbyTestBotConfig
{
    private LobbyTestBotConfig(
        ConfigEntry<bool> enableMod,
        ConfigEntry<bool> autoOpenPortalWhenLobbyReady,
        ConfigEntry<bool> capturePortalScreenshot,
        ConfigEntry<bool> autoAddBotWhenLobbyReady,
        ConfigEntry<bool> autoStartPrivateMatchWhenBotReady,
        ConfigEntry<float> autoStartDelaySeconds,
        ConfigEntry<DiagnosticGameMode> autoStartGameMode,
        ConfigEntry<DiagnosticMap> autoStartMap,
        ConfigEntry<string> botNickname,
        ConfigEntry<bool> enableLogging)
    {
        EnableMod = enableMod;
        AutoOpenPortalWhenLobbyReady = autoOpenPortalWhenLobbyReady;
        CapturePortalScreenshot = capturePortalScreenshot;
        AutoAddBotWhenLobbyReady = autoAddBotWhenLobbyReady;
        AutoStartPrivateMatchWhenBotReady = autoStartPrivateMatchWhenBotReady;
        AutoStartDelaySeconds = autoStartDelaySeconds;
        AutoStartGameMode = autoStartGameMode;
        AutoStartMap = autoStartMap;
        BotNickname = botNickname;
        EnableLogging = enableLogging;
    }

    public ConfigEntry<bool> EnableMod { get; }

    public ConfigEntry<bool> AutoOpenPortalWhenLobbyReady { get; }

    public ConfigEntry<bool> CapturePortalScreenshot { get; }

    public ConfigEntry<bool> AutoAddBotWhenLobbyReady { get; }

    public ConfigEntry<bool> AutoStartPrivateMatchWhenBotReady { get; }

    public ConfigEntry<float> AutoStartDelaySeconds { get; }

    public ConfigEntry<DiagnosticGameMode> AutoStartGameMode { get; }

    public ConfigEntry<DiagnosticMap> AutoStartMap { get; }

    public ConfigEntry<string> BotNickname { get; }

    public ConfigEntry<bool> EnableLogging { get; }

    public static LobbyTestBotConfig Bind(ConfigFile configFile)
    {
        var enableMod = configFile.Bind(
            "general",
            "EnableMod",
            true,
            "Show the host-only lobby button and allow it to spawn one real inert network player.");
        var autoOpenPortalWhenLobbyReady = configFile.Bind(
            "diagnostics",
            "AutoOpenPortalWhenLobbyReady",
            false,
            "Open the stock portal UI automatically after lobby initialization for unattended UI testing.");
        var capturePortalScreenshot = configFile.Bind(
            "diagnostics",
            "CapturePortalScreenshot",
            false,
            "Capture the game framebuffer shortly after diagnostic portal opening into BepInEx/ui-captures.");
        var autoAddBotWhenLobbyReady = configFile.Bind(
            "diagnostics",
            "AutoAddBotWhenLobbyReady",
            false,
            "Automatically add the bot after the host and player registry are ready. Intended for repeatable runtime testing.");
        var autoStartPrivateMatchWhenBotReady = configFile.Bind(
            "diagnostics",
            "AutoStartPrivateMatchWhenBotReady",
            false,
            "After diagnostic auto-add succeeds, switch the stock portal to a private game and invoke its real PLAY callback.");
        var autoStartDelaySeconds = configFile.Bind(
            "diagnostics",
            "AutoStartDelaySeconds",
            3f,
            "Delay after the authoritative bot is ready before the stock private-game and PLAY callbacks are invoked.");
        var autoStartGameMode = configFile.Bind(
            "diagnostics",
            "AutoStartGameMode",
            DiagnosticGameMode.Preserve,
            "Mode selected through the real portal mode button before diagnostic auto-start. Preserve keeps the current UI choice.");
        var autoStartMap = configFile.Bind(
            "diagnostics",
            "AutoStartMap",
            DiagnosticMap.Random,
            "Map forced only for diagnostic auto-start. Random preserves normal portal selection.");
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

        return new LobbyTestBotConfig(
            enableMod,
            autoOpenPortalWhenLobbyReady,
            capturePortalScreenshot,
            autoAddBotWhenLobbyReady,
            autoStartPrivateMatchWhenBotReady,
            autoStartDelaySeconds,
            autoStartGameMode,
            autoStartMap,
            botNickname,
            enableLogging);
    }
}
