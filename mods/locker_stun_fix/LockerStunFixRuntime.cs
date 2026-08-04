using BepInEx.Logging;
using Gameplay.Interactions;
using HarmonyLib;

namespace SneakOut.LockerStunFix;

internal static class LockerStunFixRuntime
{
    private readonly record struct ExitState(int PlayerId, bool BooAllowed);

    private static readonly Dictionary<IntPtr, ExitState> PendingExits = new();

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

    public static void BeginExit(Locker locker, int playerId)
    {
        if (_configuration?.EnableMod.Value != true
            || locker.Pointer == IntPtr.Zero)
        {
            return;
        }

        // Capture the state before ComeOut's iterator opens the locker. By the time
        // HandleBooSkill runs, vanilla has already set IsOpen=true for both paths.
        var booAllowed = LockerBooPolicy.CanArmBoo(locker.IsOpen);
        PendingExits[locker.Pointer] = new ExitState(playerId, booAllowed);
        LogInfo($"Locker exit 0x{locker.Pointer:X} for player {playerId}: closedAtStart={!locker.IsOpen}, booAllowed={booAllowed}");
    }

    public static bool ShouldApplyLockerStun(Locker locker, int playerId)
    {
        if (_configuration?.EnableMod.Value != true || locker.Pointer == IntPtr.Zero)
        {
            return true;
        }

        if (!PendingExits.Remove(locker.Pointer, out var exitState)
            || exitState.PlayerId != playerId)
        {
            // Boo is allowed only for an exit that was positively observed to start
            // from a closed locker. Unknown/stale calls fail closed and therefore do
            // not consume the skill cooldown either.
            LogInfo($"Suppressed locker stun for player {playerId}: no matching closed-locker exit");
            return false;
        }

        if (!exitState.BooAllowed)
        {
            LogInfo($"Suppressed locker stun for player {playerId}: locker 0x{locker.Pointer:X} was already open when exit began");
        }

        return exitState.BooAllowed;
    }

    public static void ClearCycle(Locker locker)
    {
        if (locker.Pointer != IntPtr.Zero)
        {
            PendingExits.Remove(locker.Pointer);
        }
    }

    private static void LogInfo(string message)
    {
        if (_configuration?.EnableLogging.Value == true)
        {
            _logger?.LogInfo(message);
        }
    }
}
