using Cinemachine;
using Collections;
using Fusion;
using Gameplay.ArrowIndicators;
using Gameplay.Buffs;
using Gameplay.Camera;
using Gameplay.Enviro;
using Gameplay.Interactions;
using Gameplay.Interactions.Tasks.Mustache;
using Gameplay.Match;
using Gameplay.Player;
using Gameplay.Player.Components;
using HarmonyLib;
using Types;
using UI.Interactions;
using UI.Views;
using UnityEngine;

namespace SneakOut.FirstPersonExperiment;

[HarmonyPatch(typeof(SpookedNetworkPlayer), nameof(SpookedNetworkPlayer.Spawned))]
internal static class FirstPersonPlayerSpawnedPatch
{
    [HarmonyPostfix]
    private static void Postfix(SpookedNetworkPlayer __instance)
    {
        FirstPersonExperimentRuntime.ObservePlayer(__instance);
    }
}

[HarmonyPatch(typeof(SpookedNetworkPlayer), nameof(SpookedNetworkPlayer.Init))]
internal static class FirstPersonPlayerInitializedPatch
{
    [HarmonyPostfix]
    private static void Postfix(SpookedNetworkPlayer __instance)
    {
        FirstPersonExperimentRuntime.ObservePlayer(__instance);
    }
}

[HarmonyPatch(typeof(SpookedNetworkPlayer), nameof(SpookedNetworkPlayer.Despawned))]
internal static class FirstPersonPlayerDespawnedPatch
{
    [HarmonyPrefix]
    private static void Prefix(SpookedNetworkPlayer __instance)
    {
        FirstPersonExperimentRuntime.ForgetPlayer(__instance);
    }
}

[HarmonyPatch(typeof(SceneCameraManager), "OnAwake")]
internal static class FirstPersonSceneCameraManagerAwakePatch
{
    [HarmonyPostfix]
    private static void Postfix(SceneCameraManager __instance)
    {
        FirstPersonExperimentRuntime.ObserveCameraManager(__instance);
    }
}

[HarmonyPatch(typeof(UI.GameUIManager), "OnAwake")]
internal static class FirstPersonGameUiManagerAwakePatch
{
    [HarmonyPostfix]
    private static void Postfix(UI.GameUIManager __instance)
    {
        FirstPersonExperimentRuntime.ObserveGameUiManager(__instance);
    }
}

[HarmonyPatch(typeof(GameStartController), "OnAwake")]
internal static class FirstPersonGameStartControllerAwakePatch
{
    [HarmonyPostfix]
    private static void Postfix(GameStartController __instance)
    {
        FirstPersonExperimentRuntime.ObserveGameStartController(__instance);
    }
}

[HarmonyPatch(typeof(CinemachineBrain), "LateUpdate")]
internal static class FirstPersonCinemachineBrainLateUpdatePatch
{
    [HarmonyPostfix]
    private static void Postfix(CinemachineBrain __instance)
    {
        FirstPersonExperimentRuntime.ApplyCamera(__instance);
    }
}

[HarmonyPatch(typeof(PlayerInputController), nameof(PlayerInputController.ResolveLocalInputs))]
internal static class FirstPersonPlayerInputControllerResolveLocalInputsPatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(PlayerInputController __instance)
    {
        FirstPersonExperimentRuntime.ApplyControls(__instance);
    }
}

[HarmonyPatch(typeof(SpookedInputs), nameof(SpookedInputs.GetInput))]
internal static class FirstPersonSpookedInputsGetInputPatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(ref int playerId, ref SpookedInput __result)
    {
        FirstPersonExperimentRuntime.ApplyNativeAimInput(playerId, ref __result);
    }
}

[HarmonyPatch(typeof(PlayerInputController), "SendInputActionRequest")]
internal static class FirstPersonImmediateThrowableChargePatch
{
    [HarmonyPrefix]
    private static void Prefix(
        PlayerInputController __instance,
        ref InputActionType inputActionType)
    {
        FirstPersonExperimentRuntime.PromoteThrowableRelease(
            __instance,
            ref inputActionType);
    }
}

[HarmonyPatch(typeof(EntityNetworkAnimatorComponent), nameof(EntityNetworkAnimatorComponent.OnThrowableRefresh))]
internal static class FirstPersonLowThrowableCarryPatch
{
    [HarmonyPostfix]
    private static void Postfix(
        EntityNetworkAnimatorComponent __instance,
        InHandThrowableType inHandThrowableType)
    {
        FirstPersonExperimentRuntime.ApplyLowThrowableCarryPose(
            __instance,
            inHandThrowableType);
    }
}

[HarmonyPatch(typeof(EntityNetworkAnimatorComponent), "OnAfterInputActionHoldPressedEvent")]
internal static class FirstPersonSuppressThrowableHoldPressedAnimationPatch
{
    [HarmonyPrefix]
    private static bool Prefix(EntityNetworkAnimatorComponent __instance)
    {
        return FirstPersonExperimentRuntime.ShouldRunNativeThrowableHoldAnimation(__instance);
    }
}

[HarmonyPatch(typeof(EntityNetworkAnimatorComponent), "AfterInputActionHoldRealesed")]
internal static class FirstPersonSuppressThrowableHoldReleasedAnimationPatch
{
    [HarmonyPrefix]
    private static bool Prefix(EntityNetworkAnimatorComponent __instance)
    {
        return FirstPersonExperimentRuntime.ShouldRunNativeThrowableHoldAnimation(__instance);
    }
}

[HarmonyPatch(typeof(Barrel), nameof(Barrel.Throw))]
internal static class FirstPersonBarrelThrowPositionPatch
{
    [HarmonyPrefix]
    private static void Prefix(
        Barrel __instance,
        int internalId,
        out FirstPersonExperimentRuntime.BarrelThrowPositionState __state)
    {
        __state = FirstPersonExperimentRuntime.BeginBarrelThrow(__instance, internalId);
    }

    [HarmonyPostfix]
    private static void Postfix(
        Barrel __instance,
        FirstPersonExperimentRuntime.BarrelThrowPositionState __state)
    {
        FirstPersonExperimentRuntime.EndBarrelThrow(__instance, __state);
    }
}

[HarmonyPatch(typeof(Chair), nameof(Chair.Throw))]
internal static class FirstPersonChairThrowProbePatch
{
    [HarmonyPrefix]
    private static void Prefix(
        Chair __instance,
        int internalId,
        out FirstPersonExperimentRuntime.ChairThrowProbeState __state)
    {
        __state = FirstPersonExperimentRuntime.BeginChairThrowProbe(__instance, internalId);
    }

    [HarmonyPostfix]
    private static void Postfix(
        Chair __instance,
        FirstPersonExperimentRuntime.ChairThrowProbeState __state)
    {
        FirstPersonExperimentRuntime.EndChairThrowProbe(__instance, __state);
    }
}

[HarmonyPatch(typeof(EntityTransformComponent), nameof(EntityTransformComponent.Move))]
internal static class FirstPersonEntityTransformMovePatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static void Prefix(
        EntityTransformComponent __instance,
        out FirstPersonExperimentRuntime.MovementInputOverrideState __state)
    {
        __state = FirstPersonExperimentRuntime.BeginMovementInputOverride(__instance);
    }

    [HarmonyFinalizer]
    private static Exception? Finalizer(
        Exception? __exception,
        FirstPersonExperimentRuntime.MovementInputOverrideState __state)
    {
        FirstPersonExperimentRuntime.EndMovementInputOverride(__state);
        return __exception;
    }
}

[HarmonyPatch(typeof(SeekerCage), nameof(SeekerCage.ShowSeekerCage))]
internal static class FirstPersonSeekerCageShowPatch
{
    [HarmonyPrefix]
    private static void Prefix(int seeker)
    {
        FirstPersonExperimentRuntime.BeginHunterCaging(seeker);
    }
}

[HarmonyPatch(
    typeof(GameStartController),
    nameof(GameStartController.RemoveInputBlockForAllPlayers))]
internal static class FirstPersonHunterCagingCompletePatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        FirstPersonExperimentRuntime.EndHunterCaging();
    }
}

[HarmonyPatch(typeof(PlayerIndicator), nameof(PlayerIndicator.UpdateIndicator))]
internal static class FirstPersonPlayerIndicatorUpdateIndicatorPatch
{
    [HarmonyPrefix]
    private static bool Prefix(
        PlayerIndicator __instance,
        Vector3 playerPosition,
        bool isSeeker)
    {
        return FirstPersonExperimentRuntime.ShouldUpdatePlayerIndicator(
            __instance,
            playerPosition,
            isSeeker);
    }
}

[HarmonyPatch(typeof(TaskIconView), "Update")]
internal static class FirstPersonTaskIconViewUpdatePatch
{
    [HarmonyPostfix]
    private static void Postfix(TaskIconView __instance)
    {
        FirstPersonExperimentRuntime.UpdateTaskIconVisibility(__instance);
    }
}

[HarmonyPatch(typeof(ItemGenerator), "LateUpdate")]
internal static class FirstPersonItemGeneratorLateUpdatePatch
{
    [HarmonyPostfix]
    private static void Postfix(ItemGenerator __instance)
    {
        FirstPersonExperimentRuntime.UpdateItemGeneratorCostVisibility(__instance);
    }
}

[HarmonyPatch(typeof(InputActionCircle), "UpdateView")]
internal static class FirstPersonInputActionCircleUpdateViewPatch
{
    [HarmonyPostfix]
    private static void Postfix(
        InputActionCircle __instance,
        NetworkId interactableId)
    {
        FirstPersonExperimentRuntime.UpdateInputActionCircle(__instance, interactableId);
    }
}

[HarmonyPatch(typeof(ActionCircle), "LateUpdate")]
internal static class FirstPersonActionCircleLateUpdatePatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        FirstPersonExperimentRuntime.ReapplyFirstPersonIndicators();
        FirstPersonExperimentRuntime.ReapplyPaintingTaskUi();
    }
}

[HarmonyPatch(typeof(MustacheTaskInteractionView), nameof(MustacheTaskInteractionView.UpdateView))]
internal static class FirstPersonMustacheTaskInteractionViewUpdatePatch
{
    [HarmonyPostfix]
    private static void Postfix(MustacheTaskInteractionView __instance)
    {
        FirstPersonExperimentRuntime.UpdatePaintingTaskUi(__instance);
    }
}

[HarmonyPatch(typeof(MustachesTask), "StartInteraction")]
internal static class FirstPersonMustachesTaskStartInteractionPatch
{
    [HarmonyPrefix]
    private static void Prefix(int internalId, out bool __state)
    {
        __state = FirstPersonExperimentRuntime.BeginPaintingTaskCameraTransition(internalId);
    }

    [HarmonyFinalizer]
    private static Exception? Finalizer(Exception? __exception, bool __state)
    {
        FirstPersonExperimentRuntime.EndPaintingTaskCameraTransition(__state);
        return __exception;
    }
}

[HarmonyPatch(typeof(MustachesTask), "StopInteraction")]
internal static class FirstPersonMustachesTaskStopInteractionPatch
{
    [HarmonyPrefix]
    private static void Prefix(int internalId, out bool __state)
    {
        __state = FirstPersonExperimentRuntime.BeginPaintingTaskCameraTransition(internalId);
    }

    [HarmonyFinalizer]
    private static Exception? Finalizer(
        Exception? __exception,
        int internalId,
        bool __state)
    {
        FirstPersonExperimentRuntime.EndPaintingTaskCameraTransition(__state);
        FirstPersonExperimentRuntime.FinishPaintingTaskInteraction(internalId);
        return __exception;
    }
}

[HarmonyPatch(typeof(MustachesTask), nameof(MustachesTask.ForceStopInteraction))]
internal static class FirstPersonMustachesTaskForceStopInteractionPatch
{
    [HarmonyPrefix]
    private static void Prefix(int playerId, out bool __state)
    {
        __state = FirstPersonExperimentRuntime.BeginPaintingTaskCameraTransition(playerId);
    }

    [HarmonyFinalizer]
    private static Exception? Finalizer(
        Exception? __exception,
        int playerId,
        bool __state)
    {
        FirstPersonExperimentRuntime.EndPaintingTaskCameraTransition(__state);
        FirstPersonExperimentRuntime.FinishPaintingTaskInteraction(playerId);
        return __exception;
    }
}

[HarmonyPatch(typeof(Gameplay.CameraManager), nameof(Gameplay.CameraManager.ActivateTaskCamera))]
internal static class FirstPersonPaintingTaskCameraActivatePatch
{
    [HarmonyPrefix]
    private static bool Prefix()
    {
        return FirstPersonExperimentRuntime.ShouldRunPaintingTaskCameraTransition();
    }
}

[HarmonyPatch(typeof(Gameplay.CameraManager), nameof(Gameplay.CameraManager.DeactivateTaskCamera))]
internal static class FirstPersonPaintingTaskCameraDeactivatePatch
{
    [HarmonyPrefix]
    private static bool Prefix()
    {
        return FirstPersonExperimentRuntime.ShouldRunPaintingTaskCameraTransition();
    }
}

[HarmonyPatch(typeof(EntityCanvasComponent), "LateUpdate")]
internal static class FirstPersonEntityCanvasLateUpdatePatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        FirstPersonExperimentRuntime.ReapplyFirstPersonStaminaHud();
    }
}

[HarmonyPatch(typeof(UI.Views.GameView), "Update")]
internal static class FirstPersonGameViewUpdatePatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        FirstPersonExperimentRuntime.ReapplyFirstPersonStaminaHud();
    }
}

[HarmonyPatch(typeof(Canvas), "SendWillRenderCanvases")]
internal static class FirstPersonCanvasWillRenderPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        FirstPersonExperimentRuntime.ReapplyFirstPersonStaminaHud();
        FirstPersonExperimentRuntime.ReapplyPaintingTaskUi();
    }
}

[HarmonyPatch(typeof(InvisibleWallsManager), "FixedUpdate")]
internal static class FirstPersonInvisibleWallsManagerFixedUpdatePatch
{
    [HarmonyPrefix]
    private static void Prefix(InvisibleWallsManager __instance)
    {
        FirstPersonExperimentRuntime.ApplyWallCirclePolicy(__instance);
    }
}
