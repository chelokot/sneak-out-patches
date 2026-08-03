using BepInEx.Logging;
using Gameplay.Interactions;
using HarmonyLib;

namespace SneakOut.LockerStunFix;

internal static class LockerStunFixRuntime
{
    private static readonly HashSet<IntPtr> SeekerOpenedLockers = new();

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
            || locker.IsOpen)
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

    private static void LogInfo(string message)
    {
        if (_configuration?.EnableLogging.Value == true)
        {
            _logger?.LogInfo(message);
        }
    }
}
