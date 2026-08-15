using BepInEx.Logging;
using Fusion;
using HarmonyLib;
using Networking.Lobby;

namespace SneakOut.QuickReconnect;

internal static class QuickReconnectRuntime
{
    private static ManualLogSource? _logger;
    private static Harmony? _harmony;
    private static PhotonLobby? _lobby;
    private static Action<NetworkRunner, ShutdownReason, bool>? _managedHandler;
    private static NetworkRunner.CloudConnectionLostHandler? _interopHandler;

    public static void Initialize(ManualLogSource logger)
    {
        _logger = logger;
        _managedHandler ??= HandleCloudConnectionLost;
        _interopHandler ??= _managedHandler;
        _harmony ??= new Harmony(QuickReconnectPlugin.PluginGuid);
        _harmony.PatchAll();
    }

    public static void RegisterCloudConnectionLostHandler(PhotonLobby lobby)
    {
        _lobby = lobby;

        // Fusion requires this callback to be installed before NetworkRunner.StartGame.
        // Remove our own prior registration so repeated runner initialization stays idempotent.
        NetworkRunner.CloudConnectionLost -= _interopHandler;
        NetworkRunner.CloudConnectionLost += _interopHandler;
        _logger?.LogInfo("Registered the game's cloud-loss reconnect handler before runner initialization");
    }

    private static void HandleCloudConnectionLost(
        NetworkRunner runner,
        ShutdownReason shutdownReason,
        bool reconnecting)
    {
        var lobby = _lobby;
        if (lobby is null)
        {
            _logger?.LogWarning(
                $"Cloud connection lost without an active lobby handler: reason={shutdownReason}, reconnecting={reconnecting}");
            return;
        }

        _logger?.LogWarning(
            $"Cloud connection lost: reason={shutdownReason}, reconnecting={reconnecting}; forwarding to the game's reconnect flow");

        try
        {
            lobby.OnCloudConnectionLost(runner, shutdownReason, reconnecting);
        }
        catch (Exception exception)
        {
            _logger?.LogError($"The game's cloud-loss reconnect handler failed: {exception}");
        }
    }
}
