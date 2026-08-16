using BepInEx.Logging;
using Gameplay.Interactions;
using HarmonyLib;
using Kinguinverse.WebServiceProvider.Types_v2;

namespace SneakOut.LockerStunFix;

internal static class LockerStunFixRuntime
{
    private static readonly LockerBooPolicy<IntPtr> Policy = new();

    private static ManualLogSource? _logger;
    private static Harmony? _harmony;
    private static LockerStunFixConfig? _configuration;
    private static bool _loggedIndicatorFailure;

    public static void Initialize(ManualLogSource logger, LockerStunFixConfig configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _harmony ??= new Harmony(LockerStunFixPlugin.PluginGuid);
        _harmony.PatchAll();
    }

    public static void ObserveOpen(Locker locker, int openerPlayerId, string source)
    {
        if (_configuration?.EnableMod.Value != true
            || locker.Pointer == IntPtr.Zero)
        {
            return;
        }

        var occupantPlayerId = locker.PlayerCurrentlyUsing;
        var observation = Policy.ObserveOpen(
            locker.Pointer,
            openerPlayerId,
            occupantPlayerId,
            locker.IsOpen,
            locker._duringInteraction,
            source);

        if (observation is LockerOpenObservation.RecordedExternalOpener
            or LockerOpenObservation.RefreshedExternalOpener)
        {
            LogInfo(
                $"open-observed locker=0x{locker.Pointer:X} source={source} opener={openerPlayerId} "
                + $"occupant={occupantPlayerId} isOpen={locker.IsOpen} duringInteraction={locker._duringInteraction} "
                + $"result={observation}");
        }
        else
        {
            LogTrace(
                $"open-ignored locker=0x{locker.Pointer:X} source={source} opener={openerPlayerId} "
                + $"occupant={occupantPlayerId} isOpen={locker.IsOpen} duringInteraction={locker._duringInteraction} "
                + $"result={observation}");
        }
    }

    public static bool ShouldApplyLockerStun(Locker locker, int playerId)
    {
        if (_configuration?.EnableMod.Value != true || locker.Pointer == IntPtr.Zero)
        {
            return true;
        }

        var decision = Policy.ConsumeForExit(locker.Pointer, playerId, out var externalOpen);
        var hasBoo = TryGetBooEquipped(locker, playerId, out var equipped) ? equipped.ToString() : "unknown";

        if (decision == LockerBooDecision.SuppressExternalOpen)
        {
            LogInfo(
                $"boo-decision locker=0x{locker.Pointer:X} exitingPlayer={playerId} hasBoo={hasBoo} "
                + $"decision=suppress reason=external-opener opener={externalOpen.OpenerPlayerId} source={externalOpen.Source}; "
                + "vanilla handler and cooldown consumption skipped");
            return false;
        }

        var reason = decision == LockerBooDecision.AllowVanillaDifferentOccupant
            ? $"marker-for-other-occupant:{externalOpen.OccupantPlayerId}"
            : "no-external-opener";
        LogInfo(
            $"boo-decision locker=0x{locker.Pointer:X} exitingPlayer={playerId} hasBoo={hasBoo} "
            + $"decision=allow-vanilla reason={reason}");

        return true;
    }

    public static void HighlightLockerStunZone(Locker locker, int playerId)
    {
        if (_configuration?.EnableMod.Value != true
            || _configuration.HighlightStunZone.Value != true
            || locker.Pointer == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var player = locker.NetworkPlayerRegistry?[playerId];
            var skills = locker._playersActiveSkills;
            var entitySkills = player?.EntitySkillsComponent;
            if (player is null
                || player.Pointer == IntPtr.Zero
                || skills is null
                || skills.Pointer == IntPtr.Zero
                || entitySkills is null
                || entitySkills.Pointer == IntPtr.Zero
                || !skills.HaveSkillEquipped(playerId, SkillType.PenguinBoo, player.CharacterType)
                || !entitySkills.CanUseBooSkill())
            {
                return;
            }

            if (!LockerStunZoneIndicator.TryShow(locker, out var failure))
            {
                LogIndicatorFailureOnce(failure);
                return;
            }

            LogTrace(
                $"boo-zone locker=0x{locker.Pointer:X} exitingPlayer={playerId} "
                + $"radius={LockerStunZonePolicy.Radius:0.##} duration={LockerStunZonePolicy.IndicatorDurationSeconds:0.##}");
        }
        catch (Exception exception)
        {
            LogIndicatorFailureOnce($"{exception.GetType().Name}: {exception.Message}");
        }
    }

    public static void ClearCycle(Locker locker, string source)
    {
        if (locker.Pointer != IntPtr.Zero && Policy.Clear(locker.Pointer))
        {
            LogTrace($"cycle-cleared locker=0x{locker.Pointer:X} source={source}");
        }
    }

    private static bool TryGetBooEquipped(Locker locker, int playerId, out bool equipped)
    {
        equipped = false;
        try
        {
            var skills = locker._playersActiveSkills;
            if (skills is null || skills.Pointer == IntPtr.Zero)
            {
                return false;
            }

            equipped = skills.HaveSkillEquipped(playerId, SkillType.PenguinBoo, Types.CharacterType.victim_penguin);
            return true;
        }
        catch (Exception exception)
        {
            LogTrace($"boo-equipment-unavailable player={playerId} error={exception.GetType().Name}");
            return false;
        }
    }

    private static void LogInfo(string message)
    {
        _logger?.LogInfo(message);
    }

    private static void LogTrace(string message)
    {
        if (_configuration?.EnableLogging.Value == true)
        {
            _logger?.LogInfo(message);
        }
    }

    private static void LogIndicatorFailureOnce(string failure)
    {
        if (_loggedIndicatorFailure)
        {
            return;
        }

        _loggedIndicatorFailure = true;
        _logger?.LogWarning($"Locker Boo stun-zone indicator unavailable; gameplay is unchanged: {failure}");
    }
}
