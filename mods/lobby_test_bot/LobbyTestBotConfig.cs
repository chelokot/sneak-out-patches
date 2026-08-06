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

internal enum BotRolePreference
{
    Penguin,
    HunterPriority,
}

internal sealed class LobbyTestBotConfig
{
    private LobbyTestBotConfig(
        ConfigEntry<bool> enableMod,
        ConfigEntry<bool> autoOpenPortalWhenLobbyReady,
        ConfigEntry<bool> capturePortalScreenshot,
        ConfigEntry<bool> captureFlowSequence,
        ConfigEntry<bool> autoWalkBeforePortal,
        ConfigEntry<bool> autoAddBotWhenLobbyReady,
        ConfigEntry<bool> autoRemoveBotWhenReady,
        ConfigEntry<bool> autoStartPrivateMatchWhenBotReady,
        ConfigEntry<float> autoStartDelaySeconds,
        ConfigEntry<DiagnosticGameMode> autoStartGameMode,
        ConfigEntry<DiagnosticMap> autoStartMap,
        ConfigEntry<string> botNickname,
        ConfigEntry<BotRolePreference> rolePreference,
        ConfigEntry<bool> enableLogging)
    {
        EnableMod = enableMod;
        AutoOpenPortalWhenLobbyReady = autoOpenPortalWhenLobbyReady;
        CapturePortalScreenshot = capturePortalScreenshot;
        CaptureFlowSequence = captureFlowSequence;
        AutoWalkBeforePortal = autoWalkBeforePortal;
        AutoAddBotWhenLobbyReady = autoAddBotWhenLobbyReady;
        AutoRemoveBotWhenReady = autoRemoveBotWhenReady;
        AutoStartPrivateMatchWhenBotReady = autoStartPrivateMatchWhenBotReady;
        AutoStartDelaySeconds = autoStartDelaySeconds;
        AutoStartGameMode = autoStartGameMode;
        AutoStartMap = autoStartMap;
        BotNickname = botNickname;
        RolePreference = rolePreference;
        EnableLogging = enableLogging;
    }

    public ConfigEntry<bool> EnableMod { get; }

    public ConfigEntry<bool> AutoOpenPortalWhenLobbyReady { get; }

    public ConfigEntry<bool> CapturePortalScreenshot { get; }

    public ConfigEntry<bool> CaptureFlowSequence { get; }

    public ConfigEntry<bool> AutoWalkBeforePortal { get; }

    public ConfigEntry<bool> AutoAddBotWhenLobbyReady { get; }

    public ConfigEntry<bool> AutoRemoveBotWhenReady { get; }

    public ConfigEntry<bool> AutoStartPrivateMatchWhenBotReady { get; }

    public ConfigEntry<float> AutoStartDelaySeconds { get; }

    public ConfigEntry<DiagnosticGameMode> AutoStartGameMode { get; }

    public ConfigEntry<DiagnosticMap> AutoStartMap { get; }

    public ConfigEntry<string> BotNickname { get; }

    public ConfigEntry<BotRolePreference> RolePreference { get; }

    public ConfigEntry<bool> EnableLogging { get; }

    public static LobbyTestBotConfig Bind(ConfigFile configFile)
    {
        var enableMod = configFile.Bind(
            "general",
            "EnableMod",
            true,
            "Show the native Dummy bot settings row and allow it to spawn one real inert network player.");
        var autoOpenPortalWhenLobbyReady = configFile.Bind(
            "diagnostics",
            "AutoOpenPortalWhenLobbyReady",
            false,
            "Open the stock portal UI automatically after lobby initialization for unattended UI testing.");
        var capturePortalScreenshot = configFile.Bind(
            "diagnostics",
            "CapturePortalScreenshot",
            false,
            "Capture portal and post-start match framebuffers into BepInEx/ui-captures.");
        var captureFlowSequence = configFile.Bind(
            "diagnostics",
            "CaptureFlowSequence",
            false,
            "Capture the rendered game framebuffer every 0.5 seconds from the first plugin update. Intended only for unattended visual regression testing.");
        var autoWalkBeforePortal = configFile.Bind(
            "diagnostics",
            "AutoWalkBeforePortal",
            false,
            "Apply normal forward movement to the local player briefly before automatic portal opening. Intended only for unattended visual regression testing.");
        var autoAddBotWhenLobbyReady = configFile.Bind(
            "diagnostics",
            "AutoAddBotWhenLobbyReady",
            false,
            "Automatically add the bot after the host and player registry are ready. Intended for repeatable runtime testing.");
        var autoRemoveBotWhenReady = configFile.Bind(
            "diagnostics",
            "AutoRemoveBotWhenReady",
            false,
            "Invoke the real Dummy bot switch after diagnostic auto-add. Mutually exclusive with automatic match start.");
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
        var rolePreference = configFile.Bind(
            "bot",
            "RolePreference",
            BotRolePreference.Penguin,
            "Role requested through the portal control. HunterPriority makes this bot the first eligible hunter in Classic mode.");
        var enableLogging = configFile.Bind(
            "diagnostics",
            "EnableLogging",
            false,
            "Log lobby bot eligibility and authoritative spawn state.");

        return new LobbyTestBotConfig(
            enableMod,
            autoOpenPortalWhenLobbyReady,
            capturePortalScreenshot,
            captureFlowSequence,
            autoWalkBeforePortal,
            autoAddBotWhenLobbyReady,
            autoRemoveBotWhenReady,
            autoStartPrivateMatchWhenBotReady,
            autoStartDelaySeconds,
            autoStartGameMode,
            autoStartMap,
            botNickname,
            rolePreference,
            enableLogging);
    }
}
