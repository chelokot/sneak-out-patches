using BepInEx.Logging;
using Fusion;
using Gameplay.Interactions;
using Gameplay.Interactions.Tasks.PotTask;
using Gameplay.Player.Components;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Types;
using UI.Interactions;
using UnityEngine;
using SneakOutGame = Game.Game;

namespace SneakOut.ChairWallThrowFix;

internal static class ChairWallThrowFixRuntime
{
    private const float SweepClearance = 0.03f;
    private const float VerticalOverlapTolerance = 0.08f;
    private static ManualLogSource? _logger;
    private static ChairWallThrowFixConfig? _configuration;
    private static Harmony? _harmony;
    private static bool _loggedFailure;
    private static bool _frontBlockOverlaySuppressed;
    private static readonly HashSet<string> LoggedPatchFailures = new(StringComparer.Ordinal);

    public static void Initialize(ManualLogSource logger, ChairWallThrowFixConfig configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _harmony ??= new Harmony(ChairWallThrowFixPlugin.PluginGuid);
        _harmony.PatchAll();
    }

    [HarmonyPatch(typeof(Chair), nameof(Chair.GetInteraction))]
    private static class ChairGetInteractionPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            Chair __instance,
            int internalId,
            InputActionType actionType,
            ref InteractionType __result)
        {
            TryOverrideBlockedRelease(
                __instance,
                __instance._interactionTargets,
                __instance.IsPossessed,
                internalId,
                actionType,
                "chair",
                ref __result);
        }
    }

    [HarmonyPatch(typeof(Barrel), nameof(Barrel.GetInteraction))]
    private static class BarrelGetInteractionPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            Barrel __instance,
            int internalId,
            InputActionType actionType,
            ref InteractionType __result)
        {
            TryOverrideBlockedRelease(
                __instance,
                __instance._interactionTargets,
                __instance.IsPossessed,
                internalId,
                actionType,
                "barrel",
                ref __result);
        }
    }

    [HarmonyPatch(typeof(ChairInteractionView), nameof(ChairInteractionView.UpdateView))]
    private static class ChairInteractionViewUpdatePatch
    {
        [HarmonyPostfix]
        private static void Postfix(ChairInteractionView __instance, NetworkId interactableId)
        {
            if (_configuration?.EnableMod.Value != true)
            {
                _frontBlockOverlaySuppressed = false;
                return;
            }

            try
            {
                var currentInteractable = __instance._currentInteractable;
                var localInternalId = SneakOutGame.InternalId;
                if (!__instance._isInputBlocked
                    || localInternalId < 0
                    || !IsHeldThrowableOwnedBy(currentInteractable, localInternalId))
                {
                    _frontBlockOverlaySuppressed = false;
                    return;
                }

                // The stock view reads InteractionTargets.IsSomethingInFrontOfPlayer and turns
                // this overlay on without consulting IsAvailableInteraction. Keep the shared
                // detector intact for other mechanics and suppress only this held-item view.
                __instance._isInputBlocked = false;
                var blockImage = __instance._blockImage;
                if (blockImage is not null && blockImage.gameObject.activeSelf)
                {
                    blockImage.gameObject.SetActive(false);
                }

                if (!_frontBlockOverlaySuppressed && _configuration.EnableLogging.Value)
                {
                    _logger?.LogInfo(
                        $"Suppressed held-throwable front-obstacle overlay: "
                        + $"interactable={interactableId}, player={localInternalId}.");
                }

                _frontBlockOverlaySuppressed = true;
            }
            catch (Exception exception)
            {
                _frontBlockOverlaySuppressed = false;
                LogPatchFailureOnce("ChairInteractionView.UpdateView", exception);
            }
        }
    }

    [HarmonyPatch(typeof(Chair), nameof(Chair.IsAvailableInteraction))]
    private static class ChairIsAvailableInteractionPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ref bool __result)
        {
            if (_configuration?.EnableMod.Value == true)
            {
                __result = true;
            }
        }
    }

    [HarmonyPatch(typeof(Barrel), nameof(Barrel.IsAvailableInteraction))]
    private static class BarrelIsAvailableInteractionPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ref bool __result)
        {
            if (_configuration?.EnableMod.Value == true)
            {
                __result = true;
            }
        }
    }

    [HarmonyPatch(typeof(Ingredient), nameof(Ingredient.IsAvailableInteraction))]
    private static class IngredientIsAvailableInteractionPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ref bool __result)
        {
            if (_configuration?.EnableMod.Value == true)
            {
                __result = true;
            }
        }
    }

    [HarmonyPatch(
        typeof(EntityInteractiveComponent),
        "ValidateInteraction",
        new[] { typeof(NetworkId), typeof(InteractionType) })]
    private static class EntityInteractiveComponentValidateThrowPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            InteractionType __1,
            ref bool __result)
        {
            if (__1 != InteractionType.Throw || _configuration?.EnableMod.Value != true)
            {
                return;
            }

            // A Throw request is emitted only for the object already carried by the player.
            // It must never be rejected by proximity, line-of-sight, overlap or the nearby
            // interactable registry; those checks are meaningful for pickup, not release.
            __result = true;
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

    private static void TryOverrideBlockedRelease(
        Interactable interactable,
        Collections.InteractionTargets? interactionTargets,
        bool isPossessed,
        int internalId,
        InputActionType actionType,
        string kind,
        ref InteractionType result)
    {
        if (_configuration?.EnableMod.Value != true)
        {
            return;
        }

        try
        {
            var isReleaseInput = actionType is InputActionType.ActionRelease
                or InputActionType.ActionReleaseAfterHold;
            var frontBlocked = interactionTargets is not null
                && internalId >= 0
                && interactionTargets[internalId].IsSomethingInFrontOfPlayer;
            if (!ChairReleasePolicy.ShouldOverrideBlockedRelease(
                    result == InteractionType.None,
                    isReleaseInput,
                    interactable.PlayerCurrentlyUsing,
                    internalId,
                    isPossessed,
                    frontBlocked))
            {
                return;
            }

            result = InteractionType.Throw;
            _logger?.LogInfo(
                $"Overrode stock front-obstacle throw block: kind={kind}, "
                + $"interactable={interactable.NetworkObjectId}, player={internalId}, action={actionType}.");
        }
        catch (Exception exception)
        {
            LogPatchFailureOnce($"{kind}.GetInteraction", exception);
        }
    }

    private static bool IsHeldThrowableOwnedBy(Interactable? interactable, int localInternalId)
    {
        if (interactable is null)
        {
            return false;
        }

        if (interactable.TryCast<Chair>() is { } chair)
        {
            return chair.PlayerCurrentlyUsing == localInternalId && !chair.IsPossessed;
        }

        if (interactable.TryCast<Barrel>() is { } barrel)
        {
            return barrel.PlayerCurrentlyUsing == localInternalId && !barrel.IsPossessed;
        }

        if (interactable.TryCast<Ingredient>() is { } ingredient)
        {
            return ingredient.PlayerCurrentlyUsing == localInternalId;
        }

        return false;
    }

    private static void LogPatchFailureOnce(string stage, Exception exception)
    {
        if (LoggedPatchFailures.Add(stage))
        {
            _logger?.LogWarning($"{stage} patch failed; preserving stock behavior: {exception}");
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

        ClampReleaseToNearSide(chair, chairCollider, chairTransform, registry, throwerTransform);

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

    private static void ClampReleaseToNearSide(
        Chair chair,
        Collider chairCollider,
        Transform chairTransform,
        NetworkPlayerRegistry registry,
        Transform throwerTransform)
    {
        Physics.SyncTransforms();
        GetSweepBox(chairCollider, out var desiredCenter, out var halfExtents, out var orientation);

        var playerSideAnchor = throwerTransform.position;
        playerSideAnchor.y = desiredCenter.y;
        var toDesired = desiredCenter - playerSideAnchor;
        toDesired.y = 0f;
        var desiredDistance = toDesired.magnitude;
        if (desiredDistance < 0.001f)
        {
            return;
        }

        var direction = toDesired / desiredDistance;
        var projectedRadius = ProjectedRadius(halfExtents, orientation, direction);
        var startBackoff = projectedRadius + SweepClearance;
        var castStart = playerSideAnchor - direction * startBackoff;
        var castDistance = desiredDistance + startBackoff;
        var hits = Physics.BoxCastAll(
            castStart,
            halfExtents * 0.98f,
            direction,
            orientation,
            castDistance,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore);

        RaycastHit? firstBlockingHit = null;
        for (var index = 0; index < hits.Length; index++)
        {
            var hit = hits[index];
            var candidate = hit.collider;
            if (candidate is null
                || IsIgnoredCollider(candidate, chair, registry)
                || !OverlapsChairVertically(candidate.bounds, desiredCenter, halfExtents))
            {
                continue;
            }

            if (firstBlockingHit is null || hit.distance < firstBlockingHit.Value.distance)
            {
                firstBlockingHit = hit;
            }
        }

        if (firstBlockingHit is not { } blockingHit)
        {
            if (_configuration?.EnableLogging.Value == true)
            {
                _logger?.LogInfo(
                    $"Chair release sweep clear: chair={chair.NetworkObjectId}, "
                    + $"player={chair.PlayerCurrentlyUsing}, distance={desiredDistance:0.000}m.");
            }

            return;
        }

        var safeCastDistance = ChairReleasePolicy.SafeTravelDistance(blockingHit.distance, SweepClearance);
        var safeCenter = castStart + direction * safeCastDistance;
        var offset = safeCenter - desiredCenter;
        chairTransform.position += offset;
        Physics.SyncTransforms();

        _logger?.LogInfo(
            $"Clamped chair release to player side: chair={chair.NetworkObjectId}, "
            + $"player={chair.PlayerCurrentlyUsing}, obstacle={blockingHit.collider?.name ?? "<unknown>"}, "
            + $"desired={desiredDistance:0.000}m, hit={blockingHit.distance:0.000}m, "
            + $"moved={offset.magnitude:0.000}m.");
    }

    private static void GetSweepBox(
        Collider chairCollider,
        out Vector3 center,
        out Vector3 halfExtents,
        out Quaternion orientation)
    {
        if (chairCollider.TryCast<BoxCollider>() is { } boxCollider)
        {
            var scale = boxCollider.transform.lossyScale;
            center = boxCollider.transform.TransformPoint(boxCollider.center);
            halfExtents = Vector3.Scale(
                boxCollider.size * 0.5f,
                new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            orientation = boxCollider.transform.rotation;
            return;
        }

        var bounds = chairCollider.bounds;
        center = bounds.center;
        halfExtents = bounds.extents;
        orientation = Quaternion.identity;
    }

    private static float ProjectedRadius(Vector3 halfExtents, Quaternion orientation, Vector3 direction)
    {
        return Mathf.Abs(Vector3.Dot(direction, orientation * Vector3.right)) * halfExtents.x
            + Mathf.Abs(Vector3.Dot(direction, orientation * Vector3.up)) * halfExtents.y
            + Mathf.Abs(Vector3.Dot(direction, orientation * Vector3.forward)) * halfExtents.z;
    }

    private static bool OverlapsChairVertically(Bounds candidate, Vector3 chairCenter, Vector3 chairHalfExtents)
    {
        var chairMinY = chairCenter.y - chairHalfExtents.y;
        var chairMaxY = chairCenter.y + chairHalfExtents.y;
        return candidate.max.y > chairMinY + VerticalOverlapTolerance
            && candidate.min.y < chairMaxY - VerticalOverlapTolerance;
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
