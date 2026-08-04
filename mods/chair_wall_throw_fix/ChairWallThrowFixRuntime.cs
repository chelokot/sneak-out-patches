using BepInEx.Logging;
using Fusion;
using Gameplay.Interactions;
using Gameplay.Interactions.Tasks.PotTask;
using Gameplay.Player.Components;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Types;
using UnityEngine;

namespace SneakOut.ChairWallThrowFix;

internal static class ChairWallThrowFixRuntime
{
    private static readonly Dictionary<IntPtr, int> HeldThrowableOwners = new();
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
                if (IsThrowInteraction(__instance, internalId))
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

    [HarmonyPatch(typeof(Barrel), nameof(Barrel.IsAvailableInteraction))]
    private static class BarrelIsAvailableInteractionPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Barrel __instance, int internalId, ref bool __result)
        {
            if (!__result && _configuration?.EnableMod.Value == true && IsThrowInteraction(__instance, internalId))
            {
                __result = true;
            }
        }
    }

    [HarmonyPatch(typeof(Ingredient), nameof(Ingredient.IsAvailableInteraction))]
    private static class IngredientIsAvailableInteractionPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Ingredient __instance, int internalId, ref bool __result)
        {
            if (!__result && _configuration?.EnableMod.Value == true && IsThrowInteraction(__instance, internalId))
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
            EntityInteractiveComponent __instance,
            NetworkId networkId,
            InteractionType interactionType,
            ref bool __result)
        {
            if (__result
                || interactionType != InteractionType.Throw
                || _configuration?.EnableMod.Value != true)
            {
                return;
            }

            try
            {
                var interactable = __instance._interactableObjectsRegistry?[networkId];
                if (interactable is not null && IsSupportedThrowable(interactable))
                {
                    // Throw is an explicit request for the object already carried by this player.
                    // Distance, wall overlap and the normal nearby-interactable filters must not
                    // be allowed to turn that release request into the crossed-out prompt state.
                    __result = true;
                }
            }
            catch (Exception exception)
            {
                LogFailureOnce("Throwable validation override failed", exception);
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

        [HarmonyPostfix]
        private static void Postfix(Chair __instance)
        {
            ForgetHeldThrowable(__instance);
        }
    }

    [HarmonyPatch(typeof(Chair), nameof(Chair.PickUp))]
    private static class ChairPickUpPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Chair __instance, int internalId) => RememberHeldThrowable(__instance, internalId);
    }

    [HarmonyPatch(typeof(Barrel), nameof(Barrel.PickUp))]
    private static class BarrelPickUpPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Barrel __instance, int internalId) => RememberHeldThrowable(__instance, internalId);
    }

    [HarmonyPatch(typeof(Barrel), nameof(Barrel.Throw))]
    private static class BarrelThrowPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Barrel __instance) => ForgetHeldThrowable(__instance);
    }

    [HarmonyPatch(typeof(Ingredient), nameof(Ingredient.PickUp))]
    private static class IngredientPickUpPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Ingredient __instance, int internalId) => RememberHeldThrowable(__instance, internalId);
    }

    [HarmonyPatch(typeof(Ingredient), nameof(Ingredient.Throw))]
    private static class IngredientThrowPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Ingredient __instance) => ForgetHeldThrowable(__instance);
    }

    [HarmonyPatch(typeof(Chair), nameof(Chair.ForceStopInteraction))]
    private static class ChairForceStopPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Chair __instance) => ForgetHeldThrowable(__instance);
    }

    [HarmonyPatch(typeof(Barrel), nameof(Barrel.ForceStopInteraction))]
    private static class BarrelForceStopPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Barrel __instance) => ForgetHeldThrowable(__instance);
    }

    [HarmonyPatch(typeof(Ingredient), nameof(Ingredient.ForceStopInteraction))]
    private static class IngredientForceStopPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Ingredient __instance) => ForgetHeldThrowable(__instance);
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

    private static bool IsThrowInteraction(Chair chair, int internalId)
    {
        return IsRememberedHeldThrowable(chair, internalId)
            || chair.PlayerCurrentlyUsing == internalId
            || HasThrowAction(action => chair.GetInteraction(internalId, action));
    }

    private static bool IsThrowInteraction(Barrel barrel, int internalId)
    {
        return IsRememberedHeldThrowable(barrel, internalId)
            || barrel.PlayerCurrentlyUsing == internalId
            || HasThrowAction(action => barrel.GetInteraction(internalId, action));
    }

    private static bool IsThrowInteraction(Ingredient ingredient, int internalId)
    {
        return IsRememberedHeldThrowable(ingredient, internalId)
            || ingredient.PlayerCurrentlyUsing == internalId
            || HasThrowAction(action => ingredient.GetInteraction(internalId, action));
    }

    private static bool HasThrowAction(Func<InputActionType, InteractionType> resolve)
    {
        return resolve(InputActionType.ActionStart) == InteractionType.Throw
            || resolve(InputActionType.ActionHold) == InteractionType.Throw
            || resolve(InputActionType.ActionRelease) == InteractionType.Throw
            || resolve(InputActionType.ActionReleaseAfterHold) == InteractionType.Throw;
    }

    private static bool IsSupportedThrowable(Interactable interactable)
    {
        return interactable.TryCast<Chair>() is not null
            || interactable.TryCast<Barrel>() is not null
            || interactable.TryCast<Ingredient>() is not null;
    }

    private static void RememberHeldThrowable(Interactable interactable, int internalId)
    {
        if (_configuration?.EnableMod.Value == true
            && interactable.Pointer != IntPtr.Zero
            && internalId > 0)
        {
            HeldThrowableOwners[interactable.Pointer] = internalId;
        }
    }

    private static bool IsRememberedHeldThrowable(Interactable interactable, int internalId)
    {
        return interactable.Pointer != IntPtr.Zero
            && HeldThrowableOwners.TryGetValue(interactable.Pointer, out var ownerId)
            && ownerId == internalId;
    }

    private static void ForgetHeldThrowable(Interactable interactable)
    {
        if (interactable.Pointer != IntPtr.Zero)
        {
            HeldThrowableOwners.Remove(interactable.Pointer);
        }
    }

    private static void LogFailureOnce(string message, Exception exception)
    {
        if (_loggedFailure)
        {
            return;
        }

        _loggedFailure = true;
        _logger?.LogWarning($"{message}; leaving the stock behavior for this call: {exception}");
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
