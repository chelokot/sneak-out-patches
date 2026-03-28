using BepInEx.Logging;
using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SneakOut.FreeFly;

internal static class FreeFlyRuntime
{
    private static ManualLogSource? _logger;
    private static Harmony? _harmony;
    private static FreeFlyConfig? _configuration;
    private static bool _loggedUpdateTick;
    private static bool _loggedMissingInternalId;
    private static bool _loggedMissingPlayer;
    private static bool _loggedMissingKeyboard;
    private static string? _lastObservedPressedKeys;
    private static InputAction? _pageUpAction;
    private static InputAction? _pageDownAction;

    private static readonly System.Reflection.PropertyInfo? GameInternalIdProperty =
        AccessTools.TypeByName("Game") is { } gameType ? AccessTools.Property(gameType, "InternalId") : null;
    private static readonly Type? SpookedNetworkPlayerType =
        AccessTools.TypeByName("Gameplay.Player.Components.SpookedNetworkPlayer");
    private static readonly System.Reflection.MethodInfo? SpookedNetworkPlayerGetInternalIdMethod =
        SpookedNetworkPlayerType is null ? null : AccessTools.Method(SpookedNetworkPlayerType, "get_InternalId");
    private static readonly Type? EntityTransformComponentType =
        AccessTools.TypeByName("Gameplay.Player.Components.EntityTransformComponent");
    private static readonly System.Reflection.MethodInfo? EntityTransformComponentForceSetPositionMethod =
        EntityTransformComponentType is null ? null : AccessTools.Method(EntityTransformComponentType, "ForceSetPosition", new[] { typeof(Vector3), typeof(bool) });

    public static void Initialize(ManualLogSource logger, FreeFlyConfig configuration)
    {
        _logger = logger;
        _configuration = configuration;
        EnsureInputActions();
        _harmony ??= new Harmony(FreeFlyPlugin.PluginGuid);
        _harmony.PatchAll();
    }

    public static void TryApplyFreeFly(Component component)
    {
        if (_configuration is null || !_configuration.EnableMod.Value)
        {
            return;
        }

        if (_configuration.EnableLogging.Value && !_loggedUpdateTick)
        {
            _loggedUpdateTick = true;
            _logger?.LogInfo("FreeFly: updateTick");
        }

        var direction = GetInputDirection();
        if (direction == 0f)
        {
            return;
        }

        var networkPlayer = GetLocalNetworkPlayer();
        if (networkPlayer is null)
        {
            if (_configuration.EnableLogging.Value && !_loggedMissingPlayer)
            {
                _loggedMissingPlayer = true;
                _logger?.LogInfo("FreeFly: noLocalNetworkPlayer");
            }
            return;
        }

        var entityTransformComponent = networkPlayer.GetComponent("EntityTransformComponent");
        if (entityTransformComponent is null || EntityTransformComponentForceSetPositionMethod is null)
        {
            return;
        }

        var delta = _configuration.MovementSpeed.Value * Time.deltaTime * direction;
        var currentPosition = networkPlayer.transform.position;
        var nextPosition = _configuration.Axis.Value == FreeFlyAxis.Z
            ? new Vector3(currentPosition.x, currentPosition.y, currentPosition.z + delta)
            : new Vector3(currentPosition.x, currentPosition.y + delta, currentPosition.z);

        EntityTransformComponentForceSetPositionMethod.Invoke(entityTransformComponent, new object[] { nextPosition, true });

        if (_configuration.EnableLogging.Value)
        {
            _logger?.LogInfo($"FreeFly: axis={_configuration.Axis.Value}, direction={direction}, from={currentPosition}, to={nextPosition}");
        }
    }

    private static float GetInputDirection()
    {
        var direction = 0f;
        var legacyPageUpPressed = Input.GetKey(KeyCode.PageUp);
        var legacyPageDownPressed = Input.GetKey(KeyCode.PageDown);
        var actionPageUpPressed = _pageUpAction?.IsPressed() == true;
        var actionPageDownPressed = _pageDownAction?.IsPressed() == true;
        if (legacyPageUpPressed)
        {
            direction += 1f;
        }

        if (legacyPageDownPressed)
        {
            direction -= 1f;
        }

        if (actionPageUpPressed)
        {
            direction += 1f;
        }

        if (actionPageDownPressed)
        {
            direction -= 1f;
        }

        var keyboard = Keyboard.current;
        if (keyboard is null)
        {
            if (_configuration?.EnableLogging.Value == true && !_loggedMissingKeyboard)
            {
                _loggedMissingKeyboard = true;
                _logger?.LogInfo("FreeFly: keyboardCurrent=null");
            }
            return direction;
        }

        var inputSystemPageUpPressed = keyboard.pageUpKey.isPressed;
        var inputSystemPageDownPressed = keyboard.pageDownKey.isPressed;
        if (inputSystemPageUpPressed)
        {
            direction += 1f;
        }

        if (inputSystemPageDownPressed)
        {
            direction -= 1f;
        }

        LogObservedKeys(
            keyboard,
            legacyPageUpPressed,
            legacyPageDownPressed,
            inputSystemPageUpPressed,
            inputSystemPageDownPressed,
            actionPageUpPressed,
            actionPageDownPressed);

        return direction;
    }

    private static void LogObservedKeys(
        Keyboard keyboard,
        bool legacyPageUpPressed,
        bool legacyPageDownPressed,
        bool inputSystemPageUpPressed,
        bool inputSystemPageDownPressed,
        bool actionPageUpPressed,
        bool actionPageDownPressed)
    {
        if (_configuration?.EnableLogging.Value != true)
        {
            return;
        }

        var pressedKeys = new List<string>();
        var allKeys = keyboard.allKeys;
        if (allKeys is null)
        {
            return;
        }

        foreach (var control in allKeys)
        {
            if (control is not null && control.isPressed)
            {
                pressedKeys.Add(control.name);
            }
        }

        var signature = string.Join(",", pressedKeys);
        if (signature == _lastObservedPressedKeys)
        {
            return;
        }

        _lastObservedPressedKeys = signature;
        if (pressedKeys.Count == 0
            && !legacyPageUpPressed
            && !legacyPageDownPressed
            && !actionPageUpPressed
            && !actionPageDownPressed)
        {
            return;
        }

        _logger?.LogInfo(
            $"FreeFly: observedKeys=[{signature}] legacyPageUp={legacyPageUpPressed} legacyPageDown={legacyPageDownPressed} inputSystemPageUp={inputSystemPageUpPressed} inputSystemPageDown={inputSystemPageDownPressed} actionPageUp={actionPageUpPressed} actionPageDown={actionPageDownPressed}");
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

    private static Component? GetLocalNetworkPlayer()
    {
        if (SpookedNetworkPlayerType is null)
        {
            return null;
        }

        var currentInternalId = GameInternalIdProperty?.GetValue(null) as int? ?? 0;
        if (currentInternalId == 0)
        {
            if (_configuration?.EnableLogging.Value == true && !_loggedMissingInternalId)
            {
                _loggedMissingInternalId = true;
                _logger?.LogInfo("FreeFly: currentInternalId=0");
            }
            return null;
        }

        var il2CppType = Il2CppInterop.Runtime.Il2CppType.From(SpookedNetworkPlayerType);
        foreach (var networkPlayer in Resources.FindObjectsOfTypeAll(il2CppType))
        {
            if (networkPlayer is not Component component)
            {
                continue;
            }

            var playerInternalId = SpookedNetworkPlayerGetInternalIdMethod?.Invoke(networkPlayer, Array.Empty<object>()) as int? ?? 0;
            if (playerInternalId == currentInternalId)
            {
                return component;
            }
        }

        return null;
    }
}
