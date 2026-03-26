using BepInEx.Logging;
using Events;
using Gameplay.Match.MatchState;
using HarmonyLib;
using Networking.PGOS;
using System.Reflection;
using Types;
using UI.Views;

namespace SneakOut.CoreFixes;

internal static class CoreFixesRuntime
{
    private static ManualLogSource? _logger;
    private static Harmony? _harmony;
    private static CoreFixesConfig? _configuration;

    public static void Initialize(ManualLogSource logger, CoreFixesConfig configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _harmony ??= new Harmony(CoreFixesPlugin.PluginGuid);
        _harmony.PatchAll();
    }

    public static bool Enabled => _configuration is not null && _configuration.EnableMod.Value;

    public static bool ShouldFixPrivatePartyJoinOnFirstInvite =>
        Enabled && _configuration is not null && _configuration.FixPrivatePartyJoinOnFirstInvite.Value;

    public static bool ShouldMakeHunterRandomSelectionUniform =>
        Enabled && _configuration is not null && _configuration.MakeHunterRandomSelectionUniform.Value;

    public static bool ShouldDisableCrashyBattlepassRefreshHandler =>
        Enabled && _configuration is not null && _configuration.DisableCrashyBattlepassRefreshHandler.Value;

    public static bool TryHandleJoinLobbyEvent(PgosLobby pgosLobby, Il2CppSystem.EventArgs? args)
    {
        if (!ShouldFixPrivatePartyJoinOnFirstInvite || args is null || args.Pointer == IntPtr.Zero)
        {
            return false;
        }

        var joinLobbyEvent = new JoinLobbyEvent(args.Pointer);
        var lobbyId = joinLobbyEvent.LobbyId;
        var region = joinLobbyEvent.Region;
        if (string.IsNullOrEmpty(lobbyId) || string.IsNullOrEmpty(region))
        {
            return false;
        }

        pgosLobby.SetRegion(region);
        pgosLobby._spookedLobbyUtils.JoinLobby(lobbyId, region);

        if (_configuration!.EnableLogging.Value)
        {
            _logger?.LogInfo($"JoinLobbyEvent override: lobbyId={lobbyId}, region={region}");
        }

        return true;
    }

    public static bool TryHandleUniformHunterRandom(ShouldStartState shouldStartState, ref int result)
    {
        if (!ShouldMakeHunterRandomSelectionUniform)
        {
            return false;
        }

        if (shouldStartState._gameState.GameMode == GameModeType.Berek)
        {
            return false;
        }

        result = shouldStartState.RandomizeSeeker();

        if (_configuration!.EnableLogging.Value)
        {
            _logger?.LogInfo($"Uniform hunter random override selected seeker index {result}");
        }

        return true;
    }

    public static void LogBattlepassRefreshSuppressed()
    {
        if (_configuration is null || !_configuration.EnableLogging.Value)
        {
            return;
        }

        _logger?.LogInfo("Suppressed BattlepassView.OnOnWebplayerRefreshEvent");
    }

    public static void LogLoopSuppressed(string source)
    {
        if (_configuration is null || !_configuration.EnableLogging.Value)
        {
            return;
        }

        _logger?.LogInfo($"Suppressed {source}");
    }

    public static bool ShouldSuppressBackendStabilizerAvatarViewPatches()
    {
        return Enabled;
    }

    public static MethodBase? FindBackendStabilizerMethod(string typeName, string methodName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!string.Equals(assembly.GetName().Name, "SneakOut.BackendStabilizer", StringComparison.Ordinal))
            {
                continue;
            }

            var type = assembly.GetType(typeName, throwOnError: false);
            if (type is null)
            {
                return null;
            }

            return type.GetMethod(methodName, BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        return null;
    }
}
