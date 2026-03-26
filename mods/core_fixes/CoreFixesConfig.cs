using BepInEx.Configuration;

namespace SneakOut.CoreFixes;

internal sealed class CoreFixesConfig
{
    private CoreFixesConfig(
        ConfigEntry<bool> enableMod,
        ConfigEntry<bool> fixPrivatePartyJoinOnFirstInvite,
        ConfigEntry<bool> makeHunterRandomSelectionUniform,
        ConfigEntry<bool> disableCrashyBattlepassRefreshHandler,
        ConfigEntry<bool> enableLogging)
    {
        EnableMod = enableMod;
        FixPrivatePartyJoinOnFirstInvite = fixPrivatePartyJoinOnFirstInvite;
        MakeHunterRandomSelectionUniform = makeHunterRandomSelectionUniform;
        DisableCrashyBattlepassRefreshHandler = disableCrashyBattlepassRefreshHandler;
        EnableLogging = enableLogging;
    }

    public ConfigEntry<bool> EnableMod { get; }

    public ConfigEntry<bool> FixPrivatePartyJoinOnFirstInvite { get; }

    public ConfigEntry<bool> MakeHunterRandomSelectionUniform { get; }

    public ConfigEntry<bool> DisableCrashyBattlepassRefreshHandler { get; }

    public ConfigEntry<bool> EnableLogging { get; }

    public static CoreFixesConfig Bind(ConfigFile configFile)
    {
        var enableMod = configFile.Bind(
            "general",
            "EnableMod",
            true,
            "Enable runtime replacements for the former GameAssembly byte patches.");
        var fixPrivatePartyJoinOnFirstInvite = configFile.Bind(
            "general",
            "FixPrivatePartyJoinOnFirstInvite",
            true,
            "Use JoinLobbyEvent lobby id and region directly when joining from the first accepted invite.");
        var makeHunterRandomSelectionUniform = configFile.Bind(
            "general",
            "MakeHunterRandomSelectionUniform",
            true,
            "Use uniform hunter random selection in default mode.");
        var disableCrashyBattlepassRefreshHandler = configFile.Bind(
            "general",
            "DisableCrashyBattlepassRefreshHandler",
            true,
            "Turn BattlepassView.OnOnWebplayerRefreshEvent into a no-op.");
        var enableLogging = configFile.Bind(
            "general",
            "EnableLogging",
            false,
            "Log runtime replacements for the former GameAssembly byte patches.");

        return new CoreFixesConfig(
            enableMod,
            fixPrivatePartyJoinOnFirstInvite,
            makeHunterRandomSelectionUniform,
            disableCrashyBattlepassRefreshHandler,
            enableLogging);
    }
}
