using BepInEx.Logging;
using Gameplay.Interactions;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using Kinguinverse.WebServiceProvider.Types_v2;
using UnityEngine;

namespace SneakOut.LockerStunFix;

internal static class LockerStunFixRuntime
{
    private const float IndicatorScanIntervalSeconds = 0.5f;
    private static readonly LockerBooPolicy<IntPtr> Policy = new();

    private static ManualLogSource? _logger;
    private static Harmony? _harmony;
    private static LockerStunFixConfig? _configuration;
    private static bool _loggedIndicatorFailure;
    private static bool _watcherInstalled;
    private static float _nextIndicatorScan;

    public static void Initialize(ManualLogSource logger, LockerStunFixConfig configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _harmony ??= new Harmony(LockerStunFixPlugin.PluginGuid);
        _harmony.PatchAll();
        EnsureIndicatorWatcher();
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

    private static void EnsureIndicatorWatcher()
    {
        if (_watcherInstalled)
        {
            return;
        }

        ClassInjector.RegisterTypeInIl2Cpp<LockerStunZoneWatcher>();
        var watcherObject = new GameObject("LockerStunZoneWatcher")
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        UnityEngine.Object.DontDestroyOnLoad(watcherObject);
        watcherObject.AddComponent<LockerStunZoneWatcher>();
        _watcherInstalled = true;
    }

    private static void RefreshPersistentIndicators()
    {
        if (_configuration?.EnableMod.Value != true
            || Time.unscaledTime < _nextIndicatorScan)
        {
            return;
        }

        var showStunZone = _configuration.HighlightStunZone.Value;
        var showInteractionZone = _configuration.HighlightInteractionZone.Value;
        if (!showStunZone && !showInteractionZone)
        {
            return;
        }

        _nextIndicatorScan = Time.unscaledTime + IndicatorScanIntervalSeconds;
        var created = 0;
        foreach (var locker in Resources.FindObjectsOfTypeAll<Locker>())
        {
            try
            {
                if (locker is null || locker.Pointer == IntPtr.Zero)
                {
                    continue;
                }

                var lockerObject = locker.gameObject;
                var scene = lockerObject?.scene;
                if (lockerObject is null || scene is null || !scene.Value.IsValid() || !scene.Value.isLoaded)
                {
                    continue;
                }

                var stunCreated = false;
                if (showStunZone
                    && !LockerStunZoneIndicator.TryEnsureVisible(locker, out stunCreated, out var stunFailure))
                {
                    LogIndicatorFailureOnce($"stun zone: {stunFailure}");
                }
                else if (showStunZone && stunCreated)
                {
                    created++;
                }

                var interactionCreated = false;
                if (showInteractionZone
                    && !LockerInteractionZoneIndicator.TryEnsureVisible(
                        locker,
                        out interactionCreated,
                        out var interactionFailure))
                {
                    LogIndicatorFailureOnce($"interaction area: {interactionFailure}");
                }
                else if (showInteractionZone && interactionCreated)
                {
                    created++;
                }
            }
            catch (Exception exception)
            {
                LogIndicatorFailureOnce($"{exception.GetType().Name}: {exception.Message}");
            }
        }

        if (created > 0)
        {
            LogTrace($"Created {created} persistent locker zone indicators");
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

    private sealed class LockerStunZoneWatcher : MonoBehaviour
    {
        public LockerStunZoneWatcher(IntPtr pointer) : base(pointer)
        {
        }

        public LockerStunZoneWatcher() : base(ClassInjector.DerivedConstructorPointer<LockerStunZoneWatcher>())
        {
            ClassInjector.DerivedConstructorBody(this);
        }

        private void Update()
        {
            try
            {
                RefreshPersistentIndicators();
            }
            catch (Exception exception)
            {
                LogIndicatorFailureOnce($"{exception.GetType().Name}: {exception.Message}");
            }
        }
    }
}
