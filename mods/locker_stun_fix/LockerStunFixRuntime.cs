using BepInEx.Logging;
using Gameplay.Interactions;
using Gameplay.Skills;
using HarmonyLib;
using UnityEngine;

namespace SneakOut.LockerStunFix;

internal static class LockerStunFixRuntime
{
    private static readonly HashSet<IntPtr> SeekerOpenedLockers = new();
    private static readonly Dictionary<int, float> HookedPlayerExpiry = new();

    private static ManualLogSource? _logger;
    private static Harmony? _harmony;
    private static LockerStunFixConfig? _configuration;

    public static void Initialize(ManualLogSource logger, LockerStunFixConfig configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _harmony ??= new Harmony(LockerStunFixPlugin.PluginGuid);
        _harmony.PatchAll();
    }

    public static void MarkSeekerOpen(Locker locker, int playerId)
    {
        if (_configuration?.EnableMod.Value != true
            || locker.Pointer == IntPtr.Zero
            || locker.IsOpen
            || locker._duringInteraction)
        {
            return;
        }

        SeekerOpenedLockers.Add(locker.Pointer);
        LogInfo($"Tracked seeker-opened locker 0x{locker.Pointer:X} for player {playerId}");
    }

    public static bool ShouldApplyLockerStun(Locker locker, int playerId)
    {
        if (_configuration?.EnableMod.Value != true || locker.Pointer == IntPtr.Zero)
        {
            return true;
        }

        if (!SeekerOpenedLockers.Remove(locker.Pointer))
        {
            return true;
        }

        LogInfo($"Suppressed locker stun for player {playerId}: seeker already opened locker 0x{locker.Pointer:X}");
        return false;
    }

    public static void ClearCycle(Locker locker)
    {
        if (locker.Pointer != IntPtr.Zero)
        {
            SeekerOpenedLockers.Remove(locker.Pointer);
        }
    }

    public static void MarkHookedPlayer(ButcherHook hook)
    {
        if (_configuration?.EnableMod.Value != true
            || _configuration.FixHookExitSnap.Value != true
            || hook.Pointer == IntPtr.Zero)
        {
            return;
        }

        var playerId = hook._hookedPlayerId;
        if (playerId < 0 || playerId == hook._butcherInternalId)
        {
            return;
        }

        try
        {
            var player = hook._networkPlayerRegistry?[playerId];
            if (player is null || player.Pointer == IntPtr.Zero)
            {
                return;
            }
        }
        catch
        {
            return;
        }

        HookedPlayerExpiry[playerId] = Time.unscaledTime + 4f;
        LogInfo($"Tracked Butcher hook target {playerId} for locker-exit cancellation");
    }

    public static bool ShouldCancelExitLerp(int playerId)
    {
        if (_configuration?.EnableMod.Value != true
            || _configuration.FixHookExitSnap.Value != true
            || !HookedPlayerExpiry.Remove(playerId, out var expiresAt)
            || Time.unscaledTime > expiresAt)
        {
            return false;
        }

        LogInfo($"Cancelled stale locker-exit movement for hooked player {playerId}");
        return true;
    }

    private static void LogInfo(string message)
    {
        if (_configuration?.EnableLogging.Value == true)
        {
            _logger?.LogInfo(message);
        }
    }
}
