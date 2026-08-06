using BepInEx.Logging;
using Fusion;
using Gameplay.Interactions;
using Gameplay.Interactions.Tasks.PotTask;
using Gameplay.Player.Components;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppInterop.Runtime.Injection;
using Types;
using UI.Interactions;
using UnityEngine;
using SneakOutGame = Game.Game;

namespace SneakOut.ChairWallThrowFix;

internal static class ChairWallThrowFixRuntime
{
    private const float SweepClearance = 0.03f;
    private const float SweepSampleInset = 0.8f;
    private const int RaycastBufferSize = 32;
    private static readonly Il2CppStructArray<RaycastHit> RaycastBuffer = new(RaycastBufferSize);
    private static ManualLogSource? _logger;
    private static ChairWallThrowFixConfig? _configuration;
    private static Harmony? _harmony;
    private static bool _loggedFailure;
    private static bool _frontBlockOverlaySuppressed;
    private static bool _overlayWatcherInstalled;
    private static ChairInteractionView? _cachedChairView;
    private static ChairInteractionView? _suppressedChairView;
    private static float _nextOverlayViewSearchAt;
    private static readonly HashSet<string> LoggedPatchFailures = new(StringComparer.Ordinal);

    public static void Initialize(ManualLogSource logger, ChairWallThrowFixConfig configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _harmony ??= new Harmony(ChairWallThrowFixPlugin.PluginGuid);
        _harmony.PatchAll();
        EnsureOverlayWatcher();
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

    private static void EnsureOverlayWatcher()
    {
        if (_overlayWatcherInstalled)
        {
            return;
        }

        ClassInjector.RegisterTypeInIl2Cpp<ThrowableOverlayWatcher>();
        var watcherObject = new GameObject("ChairWallThrowFixOverlayWatcher");
        watcherObject.hideFlags = HideFlags.HideAndDontSave;
        UnityEngine.Object.DontDestroyOnLoad(watcherObject);
        watcherObject.AddComponent<ThrowableOverlayWatcher>();
        _overlayWatcherInstalled = true;
    }

    private static void UpdateCrossedOverlay()
    {
        if (_configuration?.EnableMod.Value != true)
        {
            RestoreCrossedOverlay();
            return;
        }

        var chairView = ResolveChairView();
        var localInternalId = SneakOutGame.InternalId;
        var currentInteractable = chairView?._currentInteractable;
        if (chairView is null
            || localInternalId < 0
            || !IsHeldThrowableOwnedBy(currentInteractable, localInternalId))
        {
            RestoreCrossedOverlay();
            return;
        }

        if (_suppressedChairView is not null && _suppressedChairView.Pointer != chairView.Pointer)
        {
            RestoreCrossedOverlay();
        }

        var blockImage = chairView._blockImage;
        if (blockImage is null)
        {
            return;
        }

        blockImage.enabled = false;
        _suppressedChairView = chairView;
        if (!_frontBlockOverlaySuppressed && _configuration.EnableLogging.Value)
        {
            _logger?.LogInfo(
                $"Suppressed held-throwable front-obstacle overlay: "
                + $"interactable={currentInteractable!.NetworkObjectId}, player={localInternalId}.");
        }

        _frontBlockOverlaySuppressed = true;
    }

    private static ChairInteractionView? ResolveChairView()
    {
        if (_cachedChairView is not null && _cachedChairView.Pointer != IntPtr.Zero)
        {
            var cachedObject = _cachedChairView.gameObject;
            if (cachedObject is not null && cachedObject.activeInHierarchy)
            {
                return _cachedChairView;
            }
        }

        if (Time.unscaledTime < _nextOverlayViewSearchAt)
        {
            return _cachedChairView;
        }

        _nextOverlayViewSearchAt = Time.unscaledTime + 0.5f;
        var activeChairView = UnityEngine.Object.FindObjectOfType<ChairInteractionView>();
        if (activeChairView is not null)
        {
            _cachedChairView = activeChairView;
        }

        return _cachedChairView;
    }

    private static void RestoreCrossedOverlay()
    {
        if (_suppressedChairView is not null && _suppressedChairView.Pointer != IntPtr.Zero)
        {
            var blockImage = _suppressedChairView._blockImage;
            if (blockImage is not null)
            {
                blockImage.enabled = true;
            }
        }

        _suppressedChairView = null;
        _frontBlockOverlaySuppressed = false;
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

    private sealed class ThrowableOverlayWatcher : MonoBehaviour
    {
        public ThrowableOverlayWatcher(IntPtr pointer) : base(pointer)
        {
        }

        public ThrowableOverlayWatcher() : base(ClassInjector.DerivedConstructorPointer<ThrowableOverlayWatcher>())
        {
            ClassInjector.DerivedConstructorBody(this);
        }

        private void Update()
        {
            try
            {
                UpdateCrossedOverlay();
            }
            catch (Exception exception)
            {
                _cachedChairView = null;
                _suppressedChairView = null;
                _frontBlockOverlaySuppressed = false;
                LogPatchFailureOnce("ThrowableOverlayWatcher.Update", exception);
            }
        }

        private void OnDestroy()
        {
            try
            {
                RestoreCrossedOverlay();
            }
            catch
            {
                // The referenced scene UI may already be destroyed during shutdown.
            }
        }
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

        GetSweepBox(chairCollider, out var center, out var halfExtents, out var orientation);
        if (!HasBlockingOverlap(center, halfExtents, orientation, chair, registry))
        {
            return;
        }

        var playerAnchor = throwerTransform.position;
        playerAnchor.y = center.y;
        var towardThrower = playerAnchor - center;
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
        var overlaps = Physics.OverlapBox(
            center,
            halfExtents * 0.98f,
            orientation,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore);
        for (var overlapIndex = 0; overlapIndex < overlaps.Length; overlapIndex++)
        {
            var obstacle = overlaps[overlapIndex];
            if (obstacle is null || IsIgnoredCollider(obstacle, chair, registry))
            {
                continue;
            }

            var playerDistance = DistanceToCollider(obstacle, playerAnchor);
            var chairDistance = DistanceToCollider(obstacle, center);
            var obstacleBetweenCenters = IsStrictlyBetweenCenters(
                obstacle,
                playerAnchor,
                center);
            var moveTowardPlayer = ChairReleasePolicy.ShouldMoveTowardPlayer(
                playerDistance,
                chairDistance,
                obstacleBetweenCenters);
            var correctionDirection = moveTowardPlayer ? towardThrower : -towardThrower;

            foreach (var distance in ChairReleasePolicy.CandidateDistances(maximumCorrection))
            {
                var offset = correctionDirection * distance;
                if (HasBlockingOverlap(
                        center + offset,
                        halfExtents,
                        orientation,
                        chair,
                        registry))
                {
                    continue;
                }

                chairTransform.position += offset;
                Physics.SyncTransforms();
                if (_configuration!.EnableLogging.Value)
                {
                    _logger?.LogInfo(
                        $"Moved chair {chair.NetworkObjectId} {distance:0.00} m "
                        + $"{(moveTowardPlayer ? "toward" : "away from")} player {internalId} "
                        + $"to clear {obstacle.name}: playerDistance={playerDistance:0.000}m, "
                        + $"chairDistance={chairDistance:0.000}m, "
                        + $"betweenCenters={obstacleBetweenCenters}.");
                }

                return;
            }
        }
    }

    private static bool ClampReleaseToNearSide(
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
            return false;
        }

        var direction = toDesired / desiredDistance;
        var projectedRadius = ProjectedRadius(halfExtents, orientation, direction);
        var lateral = new Vector3(-direction.z, 0f, direction.x);
        var lateralRadius = ProjectedRadius(halfExtents, orientation, lateral) * SweepSampleInset;
        var verticalRadius = halfExtents.y * SweepSampleInset;
        var castDistance = desiredDistance + projectedRadius + SweepClearance;
        var safeCenterDistance = desiredDistance;
        var blockingHit = default(RaycastHit);
        var foundBlockingHit = false;

        // The five held-pose support rays can all pass above a short wall while
        // an emote carries the chair through it. Probe at torso height to
        // recover the intervening wall plane and allow a signed center distance
        // behind the player when the chair does not physically fit in the gap.
        var playerPlaneProbe = throwerTransform.position;
        playerPlaneProbe.y = Mathf.Min(desiredCenter.y, throwerTransform.position.y + 0.5f);
        playerPlaneProbe -= direction * SweepClearance;
        FindNearestReleaseObstacle(
            playerPlaneProbe,
            direction,
            castDistance,
            projectedRadius,
            chair,
            registry,
            ref safeCenterDistance,
            ref blockingHit,
            ref foundBlockingHit,
            allowBehindPlayerAnchor: true);

        FindNearestReleaseObstacle(
            playerSideAnchor,
            direction,
            castDistance,
            projectedRadius,
            chair,
            registry,
            ref safeCenterDistance,
            ref blockingHit,
            ref foundBlockingHit);
        FindNearestReleaseObstacle(
            playerSideAnchor + lateral * lateralRadius,
            direction,
            castDistance,
            projectedRadius,
            chair,
            registry,
            ref safeCenterDistance,
            ref blockingHit,
            ref foundBlockingHit);
        FindNearestReleaseObstacle(
            playerSideAnchor - lateral * lateralRadius,
            direction,
            castDistance,
            projectedRadius,
            chair,
            registry,
            ref safeCenterDistance,
            ref blockingHit,
            ref foundBlockingHit);
        FindNearestReleaseObstacle(
            playerSideAnchor + Vector3.up * verticalRadius,
            direction,
            castDistance,
            projectedRadius,
            chair,
            registry,
            ref safeCenterDistance,
            ref blockingHit,
            ref foundBlockingHit);
        FindNearestReleaseObstacle(
            playerSideAnchor - Vector3.up * verticalRadius,
            direction,
            castDistance,
            projectedRadius,
            chair,
            registry,
            ref safeCenterDistance,
            ref blockingHit,
            ref foundBlockingHit);

        if (!foundBlockingHit || safeCenterDistance >= desiredDistance)
        {
            if (_configuration?.EnableLogging.Value == true)
            {
                _logger?.LogInfo(
                    $"Chair release sweep clear: chair={chair.NetworkObjectId}, "
                    + $"player={chair.PlayerCurrentlyUsing}, distance={desiredDistance:0.000}m.");
            }

            return false;
        }

        var safeCenter = playerSideAnchor + direction * safeCenterDistance;
        var offset = safeCenter - desiredCenter;
        chairTransform.position += offset;
        Physics.SyncTransforms();

        _logger?.LogInfo(
            $"Clamped chair release to player side: chair={chair.NetworkObjectId}, "
            + $"player={chair.PlayerCurrentlyUsing}, obstacle={blockingHit.collider?.name ?? "<unknown>"}, "
            + $"desired={desiredDistance:0.000}m, hit={blockingHit.distance:0.000}m, "
            + $"moved={offset.magnitude:0.000}m.");
        return true;
    }

    private static void FindNearestReleaseObstacle(
        Vector3 origin,
        Vector3 direction,
        float castDistance,
        float projectedRadius,
        Chair chair,
        NetworkPlayerRegistry registry,
        ref float safeCenterDistance,
        ref RaycastHit blockingHit,
        ref bool foundBlockingHit,
        bool allowBehindPlayerAnchor = false)
    {
        var hitCount = Physics.RaycastNonAlloc(
            origin,
            direction,
            RaycastBuffer,
            castDistance,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore);
        for (var index = 0; index < hitCount; index++)
        {
            var hit = RaycastBuffer[index];
            var candidate = hit.collider;
            if (candidate is null || IsIgnoredCollider(candidate, chair, registry))
            {
                continue;
            }

            var candidateSafeDistance = allowBehindPlayerAnchor
                ? ChairReleasePolicy.PlayerSideCenterDistance(
                    hit.distance,
                    projectedRadius,
                    SweepClearance)
                : ChairReleasePolicy.SafeCenterDistance(
                    hit.distance,
                    projectedRadius,
                    SweepClearance);
            if (foundBlockingHit && candidateSafeDistance >= safeCenterDistance)
            {
                continue;
            }

            safeCenterDistance = candidateSafeDistance;
            blockingHit = hit;
            foundBlockingHit = true;
        }
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

    private static float DistanceToCollider(Collider collider, Vector3 point)
    {
        return Vector3.Distance(point, collider.ClosestPoint(point));
    }

    private static bool IsStrictlyBetweenCenters(
        Collider collider,
        Vector3 playerCenter,
        Vector3 chairCenter)
    {
        const float endpointTolerance = 0.01f;
        var playerToChair = chairCenter - playerCenter;
        var centerDistance = playerToChair.magnitude;
        if (centerDistance <= endpointTolerance * 2f)
        {
            return false;
        }

        var direction = playerToChair / centerDistance;
        if (collider.Raycast(new Ray(playerCenter, direction), out var hit, centerDistance)
            && hit.distance < centerDistance - endpointTolerance)
        {
            return true;
        }

        return collider.Raycast(new Ray(chairCenter, -direction), out hit, centerDistance)
            && hit.distance < centerDistance - endpointTolerance;
    }

    private static bool HasBlockingOverlap(
        Vector3 center,
        Vector3 halfExtents,
        Quaternion orientation,
        Chair chair,
        NetworkPlayerRegistry registry)
    {
        var overlaps = Physics.OverlapBox(
            center,
            halfExtents * 0.98f,
            orientation,
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
