using BepInEx.Logging;
using HarmonyLib;
using Gameplay.Player.Components;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace SneakOut.FreeFly;

internal static class FreeFlyRuntime
{
    private static readonly Vector3[] Map02TraversalRoute =
    {
        new(-10.7f, 0f, -10.7f),
        new(-4.7f, 0f, -9.0f),
        new(1.8f, 0f, -11.6f),
        new(9.8f, 0f, -15.6f),
        new(13.4f, 0f, -17.9f),
        new(19.3f, 0f, -27.1f),
        new(22.8f, 0f, -32.7f),
        new(10.8f, 0f, -47.0f),
        new(4.8f, 0f, -41.7f),
        new(1.3f, 0f, -29.6f),
        new(-1.3f, 0f, -17.9f),
        new(5.4f, 0f, 3.1f),
        new(11.0f, 0f, 8.8f),
        new(1.7f, 0f, 18.2f),
        new(-1.6f, 0f, 32.8f),
        new(-13.4f, 0f, 41.4f),
        new(-25.7f, 0f, 36.5f),
        new(-31.4f, 0f, 23.8f),
        new(-25.3f, 0f, 20.7f),
        new(-8.7f, 0f, 28.0f),
        new(7.3f, 0f, 38.8f),
        new(25.0f, 0f, 33.1f),
        new(12.8f, 0f, 13.5f),
        new(-10.7f, 0f, -10.7f),
    };

    private static ManualLogSource? _logger;
    private static Harmony? _harmony;
    private static FreeFlyConfig? _configuration;
    private static bool _loggedMissingPlayer;
    private static bool _loggedInputReady;
    private static bool _loggedRememberedPlayer;
    private static InputAction? _pageUpAction;
    private static InputAction? _pageDownAction;
    private static SpookedNetworkPlayer? _localNetworkPlayer;
    private static bool _freeFlyActive;
    private static float _targetAxisCoordinate;
    private static float _map02ReadyAt = -1f;
    private static int _traversalWaypoint;
    private static int _traversalLoopsCompleted;
    private static bool _traversalReverse;
    private static bool _traversalComplete;

    public static void Initialize(ManualLogSource logger, FreeFlyConfig configuration)
    {
        _logger = logger;
        _configuration = configuration;
        EnsureInputActions();
        _harmony ??= new Harmony(FreeFlyPlugin.PluginGuid);
        _harmony.PatchAll();
    }

    public static void RememberLocalNetworkPlayer(SpookedNetworkPlayer networkPlayer)
    {
        if (_configuration is null || !_configuration.EnableMod.Value || !networkPlayer.HasInputAuthority)
        {
            return;
        }

        _localNetworkPlayer = networkPlayer;
        _loggedMissingPlayer = false;
        _freeFlyActive = false;
        _targetAxisCoordinate = GetAxisCoordinate(networkPlayer.transform.position);
        ResetTraversal();

        if (_configuration.EnableLogging.Value && !_loggedRememberedPlayer)
        {
            _loggedRememberedPlayer = true;
            _logger?.LogInfo($"FreeFly: cachedLocalPlayer internalId={networkPlayer.InternalId}");
        }
    }

    public static void TryApplyFreeFly()
    {
        if (_configuration is null || !_configuration.EnableMod.Value)
        {
            return;
        }

        var networkPlayer = _localNetworkPlayer;
        if (networkPlayer is null)
        {
            if (_configuration.EnableLogging.Value && !_loggedMissingPlayer)
            {
                _loggedMissingPlayer = true;
                _logger?.LogInfo("FreeFly: noLocalNetworkPlayer");
            }
            return;
        }

        if (TryApplyAutomaticTraversal(networkPlayer))
        {
            return;
        }

        var direction = GetInputDirection();
        if (!_freeFlyActive && direction == 0f)
        {
            return;
        }

        var entityTransformComponent = networkPlayer.EntityTransformComponent;
        if (entityTransformComponent is null)
        {
            return;
        }

        var currentPosition = networkPlayer.transform.position;
        if (!_freeFlyActive)
        {
            _freeFlyActive = true;
            _targetAxisCoordinate = GetAxisCoordinate(currentPosition);
        }

        if (direction != 0f)
        {
            _targetAxisCoordinate += _configuration.MovementSpeed.Value * Time.deltaTime * direction;
        }

        var nextPosition = WithAxisCoordinate(currentPosition, _targetAxisCoordinate);

        entityTransformComponent.ForceSetPosition(nextPosition, true);

        if (_configuration.EnableLogging.Value)
        {
            _logger?.LogInfo($"FreeFly: direction={direction}, axis={_configuration.Axis.Value}, target={_targetAxisCoordinate}, from={currentPosition}, to={nextPosition}");
        }
    }

    private static bool TryApplyAutomaticTraversal(SpookedNetworkPlayer networkPlayer)
    {
        if (_configuration?.AutoTraverseMap02.Value != true || _traversalComplete)
        {
            return false;
        }

        if (!string.Equals(SceneManager.GetActiveScene().name, "Map02", StringComparison.Ordinal))
        {
            _map02ReadyAt = -1f;
            return false;
        }

        if (_map02ReadyAt < 0f)
        {
            _map02ReadyAt = Time.unscaledTime + 8f;
            _traversalWaypoint = 0;
            _traversalLoopsCompleted = 0;
            _traversalReverse = false;
            _logger?.LogInfo("FreeFly traversal: Map02 detected; route starts after an 8 second settle window");
            return true;
        }

        if (Time.unscaledTime < _map02ReadyAt)
        {
            return true;
        }

        var entityTransformComponent = networkPlayer.EntityTransformComponent;
        if (entityTransformComponent is null)
        {
            return true;
        }

        var targetIndex = _traversalReverse
            ? Map02TraversalRoute.Length - 1 - _traversalWaypoint
            : _traversalWaypoint;
        var currentPosition = networkPlayer.transform.position;
        // The route is a streaming probe, not a physics test. Recover from any collision
        // correction immediately so an invalid intermediate position cannot turn the rest
        // of the capture into an out-of-bounds/falling profile.
        currentPosition.y = 0f;
        var targetPosition = Map02TraversalRoute[targetIndex];
        var distance = Vector3.Distance(currentPosition, targetPosition);
        var maximumStep = Mathf.Max(0.5f, _configuration.AutoTraverseSpeed.Value) * Time.unscaledDeltaTime;
        var nextPosition = Vector3.MoveTowards(currentPosition, targetPosition, maximumStep);
        entityTransformComponent.ForceSetPosition(nextPosition, true);
        networkPlayer.transform.position = nextPosition;

        if (distance > 0.15f)
        {
            return true;
        }

        _traversalWaypoint++;
        if (_traversalWaypoint < Map02TraversalRoute.Length)
        {
            return true;
        }

        _traversalWaypoint = 0;
        if (!_traversalReverse)
        {
            _traversalReverse = true;
            _logger?.LogInfo("FreeFly traversal: forward route complete; starting reverse route");
            return true;
        }

        _traversalReverse = false;
        _traversalLoopsCompleted++;
        _logger?.LogInfo($"FreeFly traversal: loop {_traversalLoopsCompleted} complete");
        if (_traversalLoopsCompleted >= Math.Clamp(_configuration.AutoTraverseLoops.Value, 1, 20))
        {
            _traversalComplete = true;
            _logger?.LogInfo("FreeFly traversal: all requested loops complete");
        }

        return true;
    }

    private static void ResetTraversal()
    {
        _map02ReadyAt = -1f;
        _traversalWaypoint = 0;
        _traversalLoopsCompleted = 0;
        _traversalReverse = false;
        _traversalComplete = false;
    }

    private static float GetInputDirection()
    {
        var pageUpPressed = _pageUpAction?.IsPressed() == true;
        var pageDownPressed = _pageDownAction?.IsPressed() == true;

        if (_configuration?.EnableLogging.Value == true && !_loggedInputReady)
        {
            _loggedInputReady = true;
            _logger?.LogInfo("FreeFly: inputReady");
        }

        if (pageUpPressed == pageDownPressed)
        {
            return 0f;
        }

        return pageUpPressed ? 1f : -1f;
    }

    private static void EnsureInputActions()
    {
        if (_pageUpAction is null)
        {
            _pageUpAction = new InputAction("FreeFlyPageUp", binding: "<Keyboard>/pageUp");
            _pageUpAction.Enable();
        }

        if (_pageDownAction is null)
        {
            _pageDownAction = new InputAction("FreeFlyPageDown", binding: "<Keyboard>/pageDown");
            _pageDownAction.Enable();
        }
    }

    private static float GetAxisCoordinate(Vector3 position)
    {
        return _configuration?.Axis.Value == FreeFlyAxis.Z ? position.z : position.y;
    }

    private static Vector3 WithAxisCoordinate(Vector3 position, float axisCoordinate)
    {
        return _configuration?.Axis.Value == FreeFlyAxis.Z
            ? new Vector3(position.x, position.y, axisCoordinate)
            : new Vector3(position.x, axisCoordinate, position.z);
    }
}
