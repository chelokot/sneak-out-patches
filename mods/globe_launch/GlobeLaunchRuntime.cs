using BepInEx.Logging;
using Gameplay.Interactions;
using HarmonyLib;
using UnityEngine;

namespace SneakOut.GlobeLaunch;

internal static class GlobeLaunchRuntime
{
    private const int RequiredConcurrentPlayers = 6;
    private const int RequiredHitCount = 3;
    private const float StockHitDelaySeconds = 0.25f;
    private const float CameraImpactOffsetMeters = 0.35f;
    private const float CameraAcquireTimeoutSeconds = 5f;
    private const float MinimumHorizontalShiftMeters = 1.5f;
    private const float MaximumHorizontalShiftMeters = 3f;
    private const float HorizontalShiftDistanceRatio = 0.3f;
    private const float CameraApproachControlRatio = 0.15f;
    private const float MinimumFlightDurationSeconds = 0.1f;
    private const int IgnoreRaycastLayer = 2;

    private sealed class FlightState
    {
        public float BeginAt;
        public bool Flying;
        public bool Complete;
        public Vector3 Origin;
        public Vector3 HorizontalDirection;
        public float DurationSeconds;
        public float ElapsedSeconds;
        public float HorizontalShift;
        public float MaximumDistance;
        public float DistanceTravelled;
        public readonly List<Transform> ParticleRoots = new();
    }

    private sealed class HitState
    {
        public bool HasTriggerPlayer;
        public int TriggerPlayerId;
        public int TriggerPlayerHitCount;
    }

    private static readonly Dictionary<IntPtr, FlightState> Flights = new();
    private static readonly Dictionary<IntPtr, HitState> HitStates = new();
    private static readonly HashSet<string> LoggedFailures = new(StringComparer.Ordinal);

    private static ManualLogSource? _logger;
    private static GlobeLaunchConfig? _configuration;
    private static Harmony? _harmony;

    public static void Initialize(ManualLogSource logger, GlobeLaunchConfig configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _harmony ??= new Harmony(GlobeLaunchPlugin.PluginGuid);
        _harmony.PatchAll();
    }

    public static void Reset(Globe globe)
    {
        if (globe.Pointer == IntPtr.Zero)
        {
            return;
        }

        Flights.Remove(globe.Pointer);
        HitStates.Remove(globe.Pointer);
    }

    public static void ObserveVanillaHit(Globe._AddDelayedForce_d__21 delayedForce)
    {
        if (_configuration?.EnableMod.Value != true
            || delayedForce.Pointer == IntPtr.Zero)
        {
            return;
        }

        try
        {
            if (delayedForce.__1__state != 0)
            {
                return;
            }

            var globe = delayedForce.__4__this;
            if (globe is null || globe.Pointer == IntPtr.Zero)
            {
                return;
            }

            var playersInInteraction = globe._playersInInteraction;
            if (playersInInteraction is null || playersInInteraction.Pointer == IntPtr.Zero)
            {
                return;
            }

            if (!HitStates.TryGetValue(globe.Pointer, out var hitState))
            {
                hitState = new HitState();
                HitStates[globe.Pointer] = hitState;
            }

            var playerId = delayedForce.playerId;

            if (!hitState.HasTriggerPlayer)
            {
                var isExistingParticipant = playersInInteraction.Contains(playerId);
                var playerCountAfterHit = playersInInteraction.Count
                    + (isExistingParticipant ? 0 : 1);

                if (playerCountAfterHit < RequiredConcurrentPlayers)
                {
                    return;
                }

                hitState.HasTriggerPlayer = true;
                hitState.TriggerPlayerId = playerId;
                hitState.TriggerPlayerHitCount = 1;
            }
            else if (playerId == hitState.TriggerPlayerId)
            {
                hitState.TriggerPlayerHitCount = Math.Min(
                    hitState.TriggerPlayerHitCount + 1,
                    RequiredHitCount);
            }
            else
            {
                return;
            }

            if (_configuration.EnableLogging.Value)
            {
                _logger?.LogInfo(
                    $"Sixth globe participant hit observed: "
                    + $"{hitState.TriggerPlayerHitCount}/{RequiredHitCount}.");
            }
        }
        catch (Exception exception)
        {
            LogFailureOnce("hit counter", exception);
        }
    }

    public static void ObserveVanillaInteractionState(Globe globe)
    {
        if (_configuration?.EnableMod.Value != true
            || globe.Pointer == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var playersInInteraction = globe._playersInInteraction;
            if (playersInInteraction is null || playersInInteraction.Pointer == IntPtr.Zero)
            {
                return;
            }

            var concurrentPlayerCount = playersInInteraction.Count;
            var hitState = HitStates.TryGetValue(globe.Pointer, out var currentHitState)
                ? currentHitState
                : null;

            if (hitState?.HasTriggerPlayer == true
                && !playersInInteraction.Contains(hitState.TriggerPlayerId))
            {
                HitStates.Remove(globe.Pointer);
                hitState = null;
            }

            var triggerPlayerHitCount = hitState?.TriggerPlayerHitCount ?? 0;

            if (_configuration.EnableLogging.Value)
            {
                _logger?.LogInfo(
                    $"Globe vanilla interaction state: globe=0x{globe.Pointer:X}, "
                    + $"concurrentPlayers={concurrentPlayerCount}/{RequiredConcurrentPlayers}, "
                    + $"sixthPlayerHits={triggerPlayerHitCount}/{RequiredHitCount}.");
            }

            if (concurrentPlayerCount < RequiredConcurrentPlayers
                || triggerPlayerHitCount < RequiredHitCount
                || Flights.ContainsKey(globe.Pointer))
            {
                return;
            }

            Flights[globe.Pointer] = new FlightState
            {
                BeginAt = Time.time + StockHitDelaySeconds,
            };
            _logger?.LogInfo(
                "Globe launch armed by the sixth participant's third vanilla hit; "
                + "flight begins after the stock hit lands.");
        }
        catch (Exception exception)
        {
            LogFailureOnce("hit", exception);
        }
    }

    public static void TickFlight(Globe globe)
    {
        if (globe.Pointer == IntPtr.Zero
            || !Flights.TryGetValue(globe.Pointer, out var flight)
            || flight.Complete)
        {
            return;
        }

        try
        {
            var body = globe._rigidbody;
            if (body is null || body.Pointer == IntPtr.Zero)
            {
                return;
            }

            if (!flight.Flying)
            {
                if (Time.time < flight.BeginAt)
                {
                    return;
                }

                var camera = Camera.main;
                if (camera is null || camera.Pointer == IntPtr.Zero)
                {
                    if (Time.time >= flight.BeginAt + CameraAcquireTimeoutSeconds)
                    {
                        CompleteFlight(body, flight, "no local camera became available");
                    }

                    return;
                }

                var initialCameraTransform = camera.transform;
                if (initialCameraTransform is null || initialCameraTransform.Pointer == IntPtr.Zero)
                {
                    if (Time.time >= flight.BeginAt + CameraAcquireTimeoutSeconds)
                    {
                        CompleteFlight(body, flight, "no local camera transform became available");
                    }

                    return;
                }

                flight.Origin = body.position;
                var initialTarget = initialCameraTransform.position
                    + initialCameraTransform.forward * CameraImpactOffsetMeters;
                var directDistance = Vector3.Distance(flight.Origin, initialTarget);
                var horizontalDirection = initialCameraTransform.right * -1f;
                horizontalDirection.y = 0f;
                flight.HorizontalDirection = horizontalDirection.sqrMagnitude > 0.0001f
                    ? horizontalDirection.normalized
                    : Vector3.left;
                flight.HorizontalShift = Mathf.Clamp(
                    directDistance * HorizontalShiftDistanceRatio,
                    MinimumHorizontalShiftMeters,
                    MaximumHorizontalShiftMeters);

                var initialControlPoint1 = flight.Origin
                    + flight.HorizontalDirection * flight.HorizontalShift;
                var initialControlPoint2 = initialControlPoint1
                    + (initialTarget - flight.Origin) * CameraApproachControlRatio;
                var controlPolygonLength = Vector3.Distance(
                        flight.Origin,
                        initialControlPoint1)
                    + Vector3.Distance(initialControlPoint1, initialControlPoint2)
                    + Vector3.Distance(initialControlPoint2, initialTarget);
                flight.DurationSeconds = Mathf.Max(
                    controlPolygonLength / LaunchSpeed,
                    MinimumFlightDurationSeconds);
                flight.MaximumDistance = LaunchDistance;
                flight.Flying = true;

                body.useGravity = false;
                body.detectCollisions = false;
                PrepareParticlesForFlight(globe, flight);
                DisableStandInteraction(globe);

                _logger?.LogInfo(
                    $"Globe launched toward the local camera on a leftward horizontal curve: "
                    + $"horizontalShift={flight.HorizontalShift:F1}m, "
                    + $"maxDistance={flight.MaximumDistance:F1}m, "
                    + $"speed={LaunchSpeed:F1}m/s.");
            }

            var activeCamera = Camera.main;
            if (activeCamera is null || activeCamera.Pointer == IntPtr.Zero)
            {
                CompleteFlight(body, flight, "the local camera disappeared during flight");
                return;
            }

            var position = body.position;
            var cameraTransform = activeCamera.transform;
            if (cameraTransform is null || cameraTransform.Pointer == IntPtr.Zero)
            {
                CompleteFlight(body, flight, "the local camera transform was unavailable");
                return;
            }

            var target = cameraTransform.position
                + cameraTransform.forward * CameraImpactOffsetMeters;
            var deltaTime = Mathf.Max(Time.deltaTime, 0f);
            flight.ElapsedSeconds += deltaTime;
            var progress = Mathf.Clamp01(flight.ElapsedSeconds / flight.DurationSeconds);
            var horizontalOffset = flight.HorizontalDirection * flight.HorizontalShift;
            var controlPoint1 = flight.Origin + horizontalOffset;
            var controlPoint2 = controlPoint1
                + (target - flight.Origin) * CameraApproachControlRatio;
            var nextPosition = EvaluateCubicBezier(
                flight.Origin,
                controlPoint1,
                controlPoint2,
                target,
                progress);
            var travelledThisFrame = Vector3.Distance(position, nextPosition);
            flight.DistanceTravelled += travelledThisFrame;

            if (flight.DistanceTravelled >= flight.MaximumDistance)
            {
                CompleteFlight(body, flight, "it reached the configured distance limit");
                return;
            }

            var movementDelta = nextPosition - position;
            body.position = nextPosition;
            body.velocity = Vector3.zero;
            MoveParticleRoots(flight, movementDelta);

            if (progress >= 1f)
            {
                CompleteFlight(body, flight, "it reached the local camera");
            }
        }
        catch (Exception exception)
        {
            LogFailureOnce("flight", exception);
        }
    }

    private static void DisableStandInteraction(Globe globe)
    {
        var standObject = globe.gameObject;
        if (standObject is not null && standObject.Pointer != IntPtr.Zero)
        {
            standObject.layer = IgnoreRaycastLayer;
        }
    }

    private static void PrepareParticlesForFlight(Globe globe, FlightState flight)
    {
        var particleSystems = globe._particleSystems;
        if (particleSystems is null || particleSystems.Pointer == IntPtr.Zero)
        {
            return;
        }

        var globeTransform = globe.transform;
        if (globeTransform is null || globeTransform.Pointer == IntPtr.Zero)
        {
            return;
        }

        var knownRoots = new HashSet<IntPtr>();

        for (var index = 0; index < particleSystems.Length; index++)
        {
            var particleSystem = particleSystems[index];
            if (particleSystem is null || particleSystem.Pointer == IntPtr.Zero)
            {
                continue;
            }

            var main = particleSystem.main;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;

            var particleTransform = particleSystem.transform;
            if (particleTransform is null || particleTransform.Pointer == IntPtr.Zero)
            {
                continue;
            }

            var effectRoot = FindEffectRoot(particleTransform, globeTransform);
            if (knownRoots.Add(effectRoot.Pointer))
            {
                flight.ParticleRoots.Add(effectRoot);
            }
        }
    }

    private static Transform FindEffectRoot(Transform particleTransform, Transform globeTransform)
    {
        var effectRoot = particleTransform;
        var parent = effectRoot.parent;

        while (parent is not null
            && parent.Pointer != IntPtr.Zero
            && parent.Pointer != globeTransform.Pointer)
        {
            effectRoot = parent;
            parent = effectRoot.parent;
        }

        return parent is not null && parent.Pointer == globeTransform.Pointer
            ? effectRoot
            : particleTransform;
    }

    private static void MoveParticleRoots(FlightState flight, Vector3 movementDelta)
    {
        if (movementDelta.sqrMagnitude <= 0f)
        {
            return;
        }

        foreach (var particleRoot in flight.ParticleRoots)
        {
            if (particleRoot is not null && particleRoot.Pointer != IntPtr.Zero)
            {
                particleRoot.position += movementDelta;
            }
        }
    }

    private static Vector3 EvaluateCubicBezier(
        Vector3 start,
        Vector3 controlPoint1,
        Vector3 controlPoint2,
        Vector3 end,
        float progress)
    {
        var inverseProgress = 1f - progress;
        return start * (inverseProgress * inverseProgress * inverseProgress)
            + controlPoint1 * (3f * inverseProgress * inverseProgress * progress)
            + controlPoint2 * (3f * inverseProgress * progress * progress)
            + end * (progress * progress * progress);
    }

    private static void CompleteFlight(Rigidbody body, FlightState flight, string reason)
    {
        body.velocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        flight.Complete = true;

        var globeObject = body.gameObject;
        if (globeObject is not null && globeObject.Pointer != IntPtr.Zero)
        {
            globeObject.SetActive(false);
        }

        _logger?.LogInfo(
            $"Globe flight completed and the launched globe was hidden because {reason}; "
            + $"distanceTravelled={flight.DistanceTravelled:F1}m.");
    }

    private static float LaunchSpeed => Mathf.Clamp(
        _configuration?.LaunchSpeedMetersPerSecond.Value ?? 20f,
        1f,
        200f);

    private static float LaunchDistance => Mathf.Clamp(
        _configuration?.LaunchDistanceMeters.Value ?? 100f,
        1f,
        1000f);

    private static void LogFailureOnce(string stage, Exception exception)
    {
        var key = $"{stage}:{exception.GetType().FullName}:{exception.Message}";
        if (LoggedFailures.Add(key))
        {
            _logger?.LogWarning($"Globe launch {stage} step failed: {exception}");
        }
    }
}
