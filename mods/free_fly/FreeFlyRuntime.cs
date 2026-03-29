using BepInEx.Logging;
using HarmonyLib;
using Gameplay.Player.Components;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SneakOut.FreeFly;

internal static class FreeFlyRuntime
{
    private static ManualLogSource? _logger;
    private static Harmony? _harmony;
    private static FreeFlyConfig? _configuration;
    private static bool _loggedMissingPlayer;
    private static bool _loggedInputReady;
    private static bool _loggedRememberedPlayer;
    private static InputAction? _pageUpAction;
    private static InputAction? _pageDownAction;
    private static SpookedNetworkPlayer? _localNetworkPlayer;

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

        var direction = GetInputDirection();
        if (direction == 0f)
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

        var entityTransformComponent = networkPlayer.EntityTransformComponent;
        if (entityTransformComponent is null)
        {
            return;
        }

        var delta = _configuration.MovementSpeed.Value * Time.deltaTime * direction;
        var currentPosition = networkPlayer.transform.position;
        var nextPosition = _configuration.Axis.Value == FreeFlyAxis.Z
            ? new Vector3(currentPosition.x, currentPosition.y, currentPosition.z + delta)
            : new Vector3(currentPosition.x, currentPosition.y + delta, currentPosition.z);

        entityTransformComponent.ForceSetPosition(nextPosition, true);

        if (_configuration.EnableLogging.Value)
        {
            _logger?.LogInfo($"FreeFly: direction={direction}, axis={_configuration.Axis.Value}, from={currentPosition}, to={nextPosition}");
        }
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
}
