using Fusion;
using Gameplay.Player.Components;
using HarmonyLib;
using System.Reflection;

namespace SneakOut.ProximityVoiceChat;

internal static class VoiceSessionResolver
{
    private static Type? _runnerType;
    private static PropertyInfo? _sessionInfoProperty;
    private static Type? _sessionInfoType;
    private static PropertyInfo? _sessionNameProperty;

    public static bool TryGetSessionName(SpookedNetworkPlayer localPlayer, out string sessionName)
    {
        sessionName = string.Empty;
        try
        {
            NetworkRunner runner = localPlayer.Runner;
            if (runner is null || runner.Pointer == IntPtr.Zero)
            {
                return false;
            }

            // Fusion has changed SessionInfo's generated wrapper shape between game releases.
            // Read only this narrow seam reflectively, then require a non-empty server-provided
            // room name. There is deliberately no scene-name fallback: two rooms in one scene
            // must never share voice packets.
            if (_runnerType != runner.GetType())
            {
                _runnerType = runner.GetType();
                _sessionInfoProperty = AccessTools.Property(_runnerType, "SessionInfo");
            }
            var sessionInfo = _sessionInfoProperty?.GetValue(runner);
            if (sessionInfo is null)
            {
                return false;
            }
            if (_sessionInfoType != sessionInfo.GetType())
            {
                _sessionInfoType = sessionInfo.GetType();
                _sessionNameProperty = AccessTools.Property(_sessionInfoType, "Name");
            }
            sessionName = _sessionNameProperty?.GetValue(sessionInfo)?.ToString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(sessionName);
        }
        catch
        {
            sessionName = string.Empty;
            return false;
        }
    }
}
