using Collections;
using Gameplay.Player.Components;
using Gameplay.Skills;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;

namespace SneakOut.UnlockEverything;

[HarmonyPatch(typeof(MummySarcophagusManager), nameof(MummySarcophagusManager.AddNew))]
internal static class MummySarcophagusPlacementPatch
{
    private const float GroundSampleRadius = 2f;
    private const float MinimumPlanarDirectionSquared = 0.0001f;

    private readonly record struct PlacementState(bool ShouldApply, Quaternion Rotation);

    private static void Prefix(
        ref Vector3 sarcophagusPosition,
        Vector3 playerPosition,
        out PlacementState __state)
    {
        __state = default;
        try
        {
            // The replacement mesh has a floor-level root. Resolve the target against
            // the navigation surface so it also stays grounded on modest slopes.
            sarcophagusPosition.y = NavMesh.SamplePosition(
                sarcophagusPosition,
                out var ground,
                GroundSampleRadius,
                NavMesh.AllAreas)
                ? ground.position.y
                : playerPosition.y;

            var planarForward = new Vector3(
                playerPosition.x - sarcophagusPosition.x,
                0f,
                playerPosition.z - sarcophagusPosition.z);
            if (planarForward.sqrMagnitude < MinimumPlanarDirectionSquared)
            {
                planarForward = Vector3.forward;
            }

            __state = new PlacementState(
                true,
                Quaternion.LookRotation(planarForward.normalized, Vector3.up));
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Preparing upright Mummy sarcophagus placement failed", exception);
        }
    }

    private static void Postfix(
        MummySarcophagusManager __instance,
        PlacementState __state)
    {
        try
        {
            if (!__state.ShouldApply)
            {
                return;
            }

            var activeSarcophagi = __instance._activeSarcophagi;
            if (activeSarcophagi is null || activeSarcophagi.Count == 0)
            {
                return;
            }

            var sarcophagus = activeSarcophagi[activeSarcophagi.Count - 1];
            if (sarcophagus is null || sarcophagus.Pointer == IntPtr.Zero)
            {
                return;
            }

            // AddNew replaces rotation after positioning. Override that write so the
            // replacement visual faces away from the hunter without inheriting pitch.
            sarcophagus.transform.rotation = __state.Rotation;

            // ActionCircle adds this as a world-space offset to Interactable.Position.
            // Recalculate it after rotation so the E prompt follows the visual center
            // of the replacement sarcophagus instead of the stock prefab's anchor.
            sarcophagus.ActionCircleOffset =
                MummySarcophagusTeleportRuntime.GetPromptPosition(sarcophagus)
                - sarcophagus.Position;

            // Sarcophagus.OnTriggerEnter needs the prefab's overlap volume. Keep that
            // volume enabled, but make every solid collider non-blocking.
            foreach (var collider in sarcophagus.GetComponentsInChildren<Collider>(true))
            {
                if (collider is not null
                    && collider.Pointer != IntPtr.Zero
                    && !collider.isTrigger)
                {
                    collider.isTrigger = true;
                }
            }
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Applying upright Mummy sarcophagus placement failed", exception);
        }
    }
}

[HarmonyPatch(typeof(Sarcophagus), nameof(Sarcophagus.OnTriggerEnter))]
internal static class SarcophagusAutomaticProximitySnapPatch
{
    // Stock OnTriggerEnter contains only an unconditional position correction. The
    // replacement collider is a trigger, so positioning belongs to the E interaction.
    private static bool Prefix() => false;
}

[HarmonyPatch(typeof(Sarcophagus), nameof(Sarcophagus.GetForwardSarcophagusPosition))]
internal static class SarcophagusAnimationAnchorPatch
{
    private static void Postfix(
        Sarcophagus __instance,
        bool beforeEnter,
        ref Vector3 __result)
    {
        __result = beforeEnter
            ? MummySarcophagusTeleportRuntime.GetInsidePosition(__instance)
            : MummySarcophagusTeleportRuntime.GetExitPosition(__instance);
    }
}

[HarmonyPatch(
    typeof(Sarcophagus._ComeOut_d__16),
    nameof(Sarcophagus._ComeOut_d__16.MoveNext))]
internal static class SarcophagusComeOutTeleportOrderingPatch
{
    private static void Prefix(Sarcophagus._ComeOut_d__16 __instance)
    {
        try
        {
            MummySarcophagusTeleportRuntime.BeginComeOut(__instance);
            MummySarcophagusTeleportRuntime.MarkComeOutReady(__instance);
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Preparing Mummy sarcophagus teleport failed", exception);
        }
    }
}

[HarmonyPatch(typeof(Visibility), nameof(Visibility.SetHardBlockVisibility))]
internal static class SarcophagusVisibilityReleaseOrderingPatch
{
    private static bool Prefix(int internalId, bool value)
    {
        try
        {
            return !MummySarcophagusTeleportRuntime.ShouldDelayVisibilityRelease(internalId, value);
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Checking Mummy sarcophagus visibility ordering failed", exception);
            return true;
        }
    }
}

[HarmonyPatch(
    typeof(EntityInteractiveComponent._InteractWithSarcophagus_d__78),
    nameof(EntityInteractiveComponent._InteractWithSarcophagus_d__78.MoveNext))]
internal static class InteractWithSarcophagusTeleportOrderingPatch
{
    private static bool Prefix(
        EntityInteractiveComponent._InteractWithSarcophagus_d__78 __instance,
        ref bool __result,
        out int __state)
    {
        __state = __instance.__1__state;
        try
        {
            MummySarcophagusTeleportRuntime.PrepareInteractionStep(__instance, __state);
            if (!MummySarcophagusTeleportRuntime.CanAdvanceExit(__instance))
            {
                __result = true;
                return false;
            }
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Starting ordered Mummy sarcophagus exit failed", exception);
        }

        return true;
    }

    private static void Postfix(
        EntityInteractiveComponent._InteractWithSarcophagus_d__78 __instance,
        bool __result,
        int __state)
    {
        try
        {
            MummySarcophagusTeleportRuntime.ApplyInteractionAnchors(
                __instance,
                __state,
                __result);
            MummySarcophagusTeleportRuntime.FinishInteractionPresentation(
                __instance,
                __result);
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Finalizing ordered Mummy sarcophagus exit failed", exception);
        }
    }
}

[HarmonyPatch(typeof(EntityNetworkAnimatorComponent), nameof(EntityNetworkAnimatorComponent.FixedUpdateNetwork))]
internal static class MummySarcophagusFixedPresentationPatch
{
    private static void Postfix(EntityNetworkAnimatorComponent __instance)
    {
        try
        {
            MummySarcophagusTeleportRuntime.ApplyInteractionPresentation(__instance);
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Applying fixed Mummy sarcophagus presentation failed", exception);
        }
    }
}

[HarmonyPatch(typeof(EntityNetworkAnimatorComponent), nameof(EntityNetworkAnimatorComponent.Render))]
internal static class MummySarcophagusRenderPresentationPatch
{
    private static void Postfix(EntityNetworkAnimatorComponent __instance)
    {
        try
        {
            MummySarcophagusTeleportRuntime.ApplyInteractionPresentation(__instance);
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Applying rendered Mummy sarcophagus presentation failed", exception);
        }
    }
}
