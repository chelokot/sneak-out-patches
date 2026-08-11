using BepInEx.Logging;
using Gameplay.Interactions;
using HarmonyLib;
using UnityEngine;

namespace SneakOut.GlobeLaunch;

internal static class GlobeLaunchRuntime
{
    private const int RequiredConcurrentPlayers = 2;
    private const float StockHitDelaySeconds = 0.25f;

    private sealed class FlightState
    {
        public float BeginAt;
        public bool Flying;
        public bool Complete;
        public Vector3 Origin;
        public float TargetY;
        public float Distance;
    }

    private static readonly Dictionary<IntPtr, FlightState> Flights = new();
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

            if (_configuration.EnableLogging.Value)
            {
                _logger?.LogInfo(
                    $"Globe vanilla interaction state: globe=0x{globe.Pointer:X}, "
                    + $"concurrentPlayers={concurrentPlayerCount}/{RequiredConcurrentPlayers}.");
            }

            if (concurrentPlayerCount < RequiredConcurrentPlayers
                || Flights.ContainsKey(globe.Pointer))
            {
                return;
            }

            Flights[globe.Pointer] = new FlightState
            {
                BeginAt = Time.time + StockHitDelaySeconds,
            };
            _logger?.LogInfo(
                "Globe launch armed by the vanilla two-player interaction; "
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

                flight.Distance = LaunchDistance;
                flight.Origin = body.position;
                flight.TargetY = flight.Origin.y + flight.Distance;
                flight.Flying = true;

                body.useGravity = false;
                body.detectCollisions = false;
                body.velocity = Vector3.up * LaunchSpeed;

                _logger?.LogInfo(
                    $"Globe launched straight up: originY={flight.Origin.y:F2}, "
                    + $"targetY={flight.TargetY:F2}, distance={flight.Distance:F1}m, "
                    + $"speed={LaunchSpeed:F1}m/s.");
            }

            var position = body.position;
            if (position.y >= flight.TargetY)
            {
                body.position = new Vector3(flight.Origin.x, flight.TargetY, flight.Origin.z);
                body.velocity = Vector3.zero;
                flight.Complete = true;
                _logger?.LogInfo($"Globe completed its {flight.Distance:F1}-metre vertical flight.");
                return;
            }

            // The stock angular velocity remains untouched so the globe keeps spinning while
            // its local Rigidbody follows the same collision-free path on every client. The
            // stock globe also replays its unsynchronized child-Rigidbody motion this way.
            body.position = new Vector3(flight.Origin.x, position.y, flight.Origin.z);
            body.velocity = Vector3.up * LaunchSpeed;
        }
        catch (Exception exception)
        {
            LogFailureOnce("flight", exception);
        }
    }

    private static float LaunchSpeed => Mathf.Clamp(
        _configuration?.LaunchSpeedMetersPerSecond.Value ?? 25f,
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
