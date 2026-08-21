using BepInEx.Logging;
using Gameplay.Interactions;
using Gameplay.Player.Components;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace SneakOut.LockerStunFix;

internal static class LockerStunFixRuntime
{
    private const float IndicatorScanIntervalSeconds = 0.5f;

    private static ManualLogSource? _logger;
    private static Harmony? _harmony;
    private static LockerStunFixConfig? _configuration;
    private static bool _loggedIndicatorFailure;
    private static bool _watcherInstalled;
    private static float _nextIndicatorScan;
    private static Collider? _balancedBooLockerCollider;
    private static IntPtr _balancedBooLockerPointer;
    private static bool _balancedBooOverlapPrepared;

    public static void Initialize(ManualLogSource logger, LockerStunFixConfig configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _harmony ??= new Harmony(LockerStunFixPlugin.PluginGuid);
        _harmony.PatchAll();
        EnsureIndicatorWatcher();
    }

    public static bool TryBeginBalancedBooQuery(Locker locker)
    {
        if (_configuration?.EnableMod.Value != true
            || locker.Pointer == IntPtr.Zero)
        {
            return false;
        }

        var lockerCollider = locker._collider;
        if (lockerCollider is null || !lockerCollider)
        {
            LogTrace($"boo-zone unavailable locker=0x{locker.Pointer:X} reason=collider-unavailable");
            return false;
        }

        if (_balancedBooLockerCollider is not null)
        {
            LogTrace("boo-zone recovered a stale nested query scope");
        }

        _balancedBooLockerCollider = lockerCollider;
        _balancedBooLockerPointer = locker.Pointer;
        _balancedBooOverlapPrepared = false;
        return true;
    }

    public static void EndBalancedBooQuery(bool began)
    {
        if (!began)
        {
            return;
        }

        _balancedBooLockerCollider = null;
        _balancedBooLockerPointer = IntPtr.Zero;
        _balancedBooOverlapPrepared = false;
    }

    public static bool TryPrepareBalancedBooOverlap(ref Vector3 center, ref float radius)
    {
        var lockerCollider = _balancedBooLockerCollider;
        if (lockerCollider is null
            || !lockerCollider
            || _balancedBooOverlapPrepared)
        {
            return false;
        }

        var bounds = lockerCollider.bounds;
        if (!LockerStunZonePolicy.TryResolveBroadPhaseRadius(
                new LockerStunZonePoint(bounds.extents.x, bounds.extents.y, bounds.extents.z),
                out var broadPhaseRadius))
        {
            LogTrace($"boo-zone unavailable locker=0x{_balancedBooLockerPointer:X} reason=invalid-bounds");
            return false;
        }

        center = bounds.center;
        radius = broadPhaseRadius;
        _balancedBooOverlapPrepared = true;
        return true;
    }

    public static void FilterBalancedBooOverlap(
        bool prepared,
        ref Il2CppReferenceArray<Collider> colliders)
    {
        var lockerCollider = _balancedBooLockerCollider;
        if (!prepared
            || lockerCollider is null
            || !lockerCollider
            || colliders is null)
        {
            return;
        }

        var accepted = new List<Collider>(colliders.Length);
        for (var index = 0; index < colliders.Length; index++)
        {
            var candidate = colliders[index];
            if (candidate is null || !candidate)
            {
                continue;
            }

            var player = candidate.GetComponent<SpookedNetworkPlayer>();
            if (player is null || player.Pointer == IntPtr.Zero)
            {
                continue;
            }

            var playerPosition = player.EntityTransformComponent?.Position
                ?? player.transform.position;
            var interactionPoint = playerPosition
                + Vector3.up * LockerStunZonePolicy.PlayerInteractionHeight;
            var closestLockerPoint = lockerCollider.ClosestPoint(interactionPoint);
            if (!LockerStunZonePolicy.IsWithinStunDistance(
                    ToPolicyPoint(interactionPoint),
                    ToPolicyPoint(closestLockerPoint)))
            {
                continue;
            }

            accepted.Add(candidate);
        }

        var filtered = new Il2CppReferenceArray<Collider>(accepted.Count);
        for (var index = 0; index < accepted.Count; index++)
        {
            filtered[index] = accepted[index];
        }

        LogTrace(
            $"boo-zone locker=0x{_balancedBooLockerPointer:X} "
            + $"broadCandidates={colliders.Length} accepted={accepted.Count} "
            + $"distance={LockerStunZonePolicy.StunDistance:0.0}m");
        colliders = filtered;
    }

    private static LockerStunZonePoint ToPolicyPoint(Vector3 point)
    {
        return new LockerStunZonePoint(point.x, point.y, point.z);
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
