using BepInEx.Logging;
using Events;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using Networking.PGOS;

namespace SneakOut.DoublePartyInviteFix;

internal static class DoublePartyInviteFixRuntime
{
    private static ManualLogSource? _logger;
    private static Harmony? _harmony;
    private static DoublePartyInviteFixConfig? _configuration;

    public static void Initialize(ManualLogSource logger, DoublePartyInviteFixConfig configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _harmony ??= new Harmony(DoublePartyInviteFixPlugin.PluginGuid);
        _harmony.PatchAll();
    }

    public static bool TryHandleJoinLobbyEvent(PgosLobby pgosLobby, Il2CppSystem.EventArgs? args)
    {
        if (_configuration is null || !_configuration.EnableMod.Value || args is null || args.Pointer == IntPtr.Zero)
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

        if (_configuration.EnableLogging.Value)
        {
            _logger?.LogInfo($"JoinLobbyEvent override: lobbyId={lobbyId}, region={region}");
        }

        return true;
    }
}
