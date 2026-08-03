using BepInEx.Logging;
using Gameplay.Interactions;
using Gameplay.Player.Components;
using HarmonyLib;
using UnityEngine;

namespace SneakOut.ChairWallThrowFix;

internal static class ChairWallThrowFixRuntime
{
    private static ManualLogSource? _logger;
    private static ChairWallThrowFixConfig? _configuration;
    private static Harmony? _harmony;
    private static bool _loggedFailure;

    public static void Initialize(ManualLogSource logger, ChairWallThrowFixConfig configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _harmony ??= new Harmony(ChairWallThrowFixPlugin.PluginGuid);
        _harmony.PatchAll();
    }

    [HarmonyPatch(typeof(Chair), nameof(Chair.IsAvailableInteraction))]
    private static class ChairIsAvailableInteractionPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Chair __instance, int internalId, ref bool __result)
        {
            if (__result || _configuration?.EnableMod.Value != true)
            {
                return;
            }

            try
            {
                // A held chair is already committed to this player. Release-space overlap must
                // not turn the action prompt into the crossed-out state and prevent Throw from
                // reaching state authority; the Throw prefix below finds a safe release pose.
                if (__instance.PlayerCurrentlyUsing == internalId)
                {
                    __result = true;
                }
            }
            catch (Exception exception)
            {
                if (!_loggedFailure)
                {
                    _loggedFailure = true;
                    _logger?.LogWarning($"Chair availability correction failed; using the stock prompt state: {exception}");
                }
            }
        }
    }

    [HarmonyPatch(typeof(Chair), nameof(Chair.Throw))]
    private static class ChairThrowPatch
    {
        [HarmonyPrefix]
        private static void Prefix(Chair __instance, int internalId)
        {
            if (_configuration is null || !_configuration.EnableMod.Value || !__instance.HasStateAuthority)
            {
                return;
            }

            try
            {
                TryClearReleaseOverlap(__instance, internalId, _configuration.MaximumReleaseCorrection.Value);
            }
            catch (Exception exception)
            {
                if (!_loggedFailure)
                {
                    _loggedFailure = true;
                    _logger?.LogWarning($"Chair overlap correction failed; using the stock release position: {exception}");
                }
            }
        }
    }

    private static void TryClearReleaseOverlap(Chair chair, int internalId, float maximumCorrection)
    {
        var chairCollider = chair.Collider;
        var chairTransform = chair.transform;
        var rigidbody = chair._rigidbody;
        var registry = chair._networkPlayerRegistry;
        if (chairCollider is null || chairTransform is null || rigidbody is null || registry is null)
        {
            return;
        }

        var thrower = registry[internalId];
        var throwerTransform = thrower?.EntityTransformComponent?.Transform;
        if (throwerTransform is null)
        {
            return;
        }

        var bounds = chairCollider.bounds;
        if (!HasBlockingOverlap(bounds.center, bounds.extents, chair, registry))
        {
            return;
        }

        var towardThrower = throwerTransform.position - bounds.center;
        towardThrower.y = 0f;
        if (towardThrower.sqrMagnitude < 0.0001f)
        {
            towardThrower = -throwerTransform.forward;
            towardThrower.y = 0f;
        }

        if (towardThrower.sqrMagnitude < 0.0001f)
        {
            return;
        }

        towardThrower.Normalize();
        foreach (var distance in ChairReleasePolicy.CandidateDistances(maximumCorrection))
        {
            var offset = towardThrower * distance;
            if (HasBlockingOverlap(bounds.center + offset, bounds.extents, chair, registry))
            {
                continue;
            }

            chairTransform.position += offset;
            Physics.SyncTransforms();
            if (_configuration!.EnableLogging.Value)
            {
                _logger?.LogInfo($"Moved chair {chair.NetworkObjectId} {distance:0.00} m toward player {internalId} to clear its release overlap.");
            }

            return;
        }
    }

    private static bool HasBlockingOverlap(
        Vector3 center,
        Vector3 halfExtents,
        Chair chair,
        NetworkPlayerRegistry registry)
    {
        var overlaps = Physics.OverlapBox(
            center,
            halfExtents * 0.98f,
            Quaternion.identity,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore);

        for (var index = 0; index < overlaps.Length; index++)
        {
            var candidate = overlaps[index];
            if (candidate is null || IsIgnoredCollider(candidate, chair, registry))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool IsIgnoredCollider(Collider candidate, Chair chair, NetworkPlayerRegistry registry)
    {
        var candidateTransform = candidate.transform;
        if (candidate == chair.Collider
            || candidate.attachedRigidbody == chair._rigidbody
            || candidateTransform.IsChildOf(chair.transform))
        {
            return true;
        }

        var players = registry._components;
        if (players is null)
        {
            return false;
        }

        for (var index = 0; index < players.Length; index++)
        {
            var playerTransform = players[index]?.transform;
            if (playerTransform is not null && candidateTransform.IsChildOf(playerTransform))
            {
                return true;
            }
        }

        return false;
    }
}
