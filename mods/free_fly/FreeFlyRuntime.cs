using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace SneakOut.FreeFly;

internal static class FreeFlyRuntime
{
    private static ManualLogSource? _logger;
    private static Harmony? _harmony;
    private static FreeFlyConfig? _configuration;

    private static readonly System.Reflection.PropertyInfo? GameInternalIdProperty =
        AccessTools.TypeByName("Game") is { } gameType ? AccessTools.Property(gameType, "InternalId") : null;
    private static readonly Type? SpookedNetworkPlayerType =
        AccessTools.TypeByName("Gameplay.Player.Components.SpookedNetworkPlayer");
    private static readonly System.Reflection.MethodInfo? SpookedNetworkPlayerGetInternalIdMethod =
        SpookedNetworkPlayerType is null ? null : AccessTools.Method(SpookedNetworkPlayerType, "get_InternalId");

    public static void Initialize(ManualLogSource logger, FreeFlyConfig configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _harmony ??= new Harmony(FreeFlyPlugin.PluginGuid);
        _harmony.PatchAll();
    }

    public static void TryApplyFreeFly(Component component)
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

        var networkPlayer = GetLocalNetworkPlayer();
        if (networkPlayer is null)
        {
            return;
        }

        var delta = _configuration.MovementSpeed.Value * Time.deltaTime * direction;
        var currentPosition = networkPlayer.transform.position;
        var nextPosition = _configuration.Axis.Value == FreeFlyAxis.Z
            ? new Vector3(currentPosition.x, currentPosition.y, currentPosition.z + delta)
            : new Vector3(currentPosition.x, currentPosition.y + delta, currentPosition.z);

        networkPlayer.transform.position = nextPosition;

        if (_configuration.EnableLogging.Value)
        {
            _logger?.LogInfo($"FreeFly: axis={_configuration.Axis.Value}, direction={direction}, position={nextPosition}");
        }
    }

    private static float GetInputDirection()
    {
        var direction = 0f;
        if (Input.GetKey(KeyCode.PageUp))
        {
            direction += 1f;
        }

        if (Input.GetKey(KeyCode.PageDown))
        {
            direction -= 1f;
        }

        return direction;
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
