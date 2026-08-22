using Gameplay.Player.Components;
using Gameplay.Skills;
using UnityEngine;

namespace SneakOut.MummyUnlock;

internal static class MummySarcophagusTeleportRuntime
{
    private const float EntryMovementDuration = 0.3f;
    private const float ExitMovementDuration = 0.5f;
    private const string WardrobeAnimatorControllerName = "MurdererButcherAnimationController";

    private static readonly Vector3 InsideLocalPosition = new(0f, 0f, 0.5f);
    private static readonly Vector3 PromptLocalPosition = new(0f, 1f, 0.5f);
    private static readonly Vector3 ExitLocalPosition = new(0f, 0f, -1f);

    private static readonly HashSet<int> PendingComeOuts = new();
    private static readonly HashSet<int> ReadyComeOuts = new();
    private static readonly HashSet<int> ActiveExits = new();
    private static readonly HashSet<int> CompletedInteractionMotions = new();
    private static readonly Dictionary<int, InteractionMotion> InteractionMotions = new();
    private static readonly Dictionary<int, AnimatorControllerLease> AnimatorControllerLeases = new();

    private static RuntimeAnimatorController? _wardrobeAnimatorController;

    private readonly record struct InteractionMotion(
        Types.InteractionType InteractionType,
        float StartedAt,
        float Duration,
        Vector3 StartPosition,
        Vector3 TargetPosition,
        Quaternion StartRotation,
        Quaternion TargetRotation);

    private readonly record struct AnimatorControllerLease(
        Animator Animator,
        RuntimeAnimatorController? OriginalController);

    public static void PrepareInteractionStep(
        EntityInteractiveComponent._InteractWithSarcophagus_d__78 coroutine,
        int stateBeforeStep)
    {
        if (stateBeforeStep != 0
            || coroutine.Pointer == IntPtr.Zero
            || coroutine.interactionType != Types.InteractionType.Hide
            || coroutine.sarcophagus is not { } sarcophagus
            || sarcophagus.Pointer == IntPtr.Zero
            || coroutine.__4__this is not { } interactive
            || interactive.Pointer == IntPtr.Zero
            || !IsMummy(sarcophagus, interactive.InternalId))
        {
            return;
        }

        BeginInteractionMotion(
            interactive,
            sarcophagus,
            Types.InteractionType.Hide,
            GetInsidePosition(sarcophagus),
            EntryMovementDuration);
    }

    public static void BeginComeOut(Sarcophagus._ComeOut_d__16 coroutine)
    {
        if (coroutine.Pointer == IntPtr.Zero
            || coroutine.__1__state != 0
            || coroutine.__4__this is not { } sarcophagus
            || sarcophagus.Pointer == IntPtr.Zero
            || !IsMummy(sarcophagus, coroutine.internalId))
        {
            return;
        }

        PendingComeOuts.Add(coroutine.internalId);

        // Match the wardrobe ordering: complete the cross-map move while the player
        // is still hard-blocked, then let ComeOut's stock 0.55-second destination
        // phase settle the camera before visibility and the exit animation resume.
        if (TryGetInteractiveComponent(sarcophagus, coroutine.internalId, out var interactive))
        {
            TrySnapPlayer(interactive, GetInsidePosition(sarcophagus));
        }
    }

    public static void MarkComeOutReady(Sarcophagus._ComeOut_d__16 coroutine)
    {
        if (coroutine.Pointer != IntPtr.Zero
            && coroutine.__1__state == 1
            && PendingComeOuts.Contains(coroutine.internalId))
        {
            ReadyComeOuts.Add(coroutine.internalId);
        }
    }

    public static bool ShouldDelayVisibilityRelease(int internalId, bool value)
    {
        return !value && PendingComeOuts.Contains(internalId);
    }

    public static bool CanAdvanceExit(
        EntityInteractiveComponent._InteractWithSarcophagus_d__78 coroutine)
    {
        if (coroutine.Pointer == IntPtr.Zero
            || coroutine.__1__state != 3
            || coroutine.interactionType != Types.InteractionType.ComeOut
            || coroutine.__4__this is not { } interactive
            || interactive.Pointer == IntPtr.Zero)
        {
            return true;
        }

        var internalId = interactive.InternalId;
        if (!PendingComeOuts.Contains(internalId)
            || coroutine.sarcophagus is not { } sarcophagus
            || sarcophagus.Pointer == IntPtr.Zero)
        {
            return true;
        }

        if (!ReadyComeOuts.Contains(internalId))
        {
            return false;
        }

        PendingComeOuts.Remove(internalId);
        ReadyComeOuts.Remove(internalId);
        ActiveExits.Add(internalId);
        BeginInteractionMotion(
            interactive,
            sarcophagus,
            Types.InteractionType.ComeOut,
            GetExitPosition(sarcophagus),
            ExitMovementDuration);
        sarcophagus._visibility?.SetHardBlockVisibility(internalId, false);
        return true;
    }

    public static void ApplyInteractionAnchors(
        EntityInteractiveComponent._InteractWithSarcophagus_d__78 coroutine,
        int stateBeforeStep,
        bool hasNextStep)
    {
        if (!hasNextStep
            || stateBeforeStep != 0
            || coroutine.Pointer == IntPtr.Zero
            || coroutine.interactionType is not (Types.InteractionType.Hide or Types.InteractionType.ComeOut)
            || coroutine.sarcophagus is not { } sarcophagus
            || sarcophagus.Pointer == IntPtr.Zero
            || coroutine.__4__this is not { } interactive
            || interactive.Pointer == IntPtr.Zero
            || !IsMummy(sarcophagus, interactive.InternalId))
        {
            return;
        }

        var insidePosition = GetInsidePosition(sarcophagus);
        coroutine._sarcophagusPositionAfterCorrection_5__2 = insidePosition;

        // ComeOut's first native step writes the old prefab correction immediately.
        // Restore the replacement-model interior while the player is still hidden.
        if (coroutine.interactionType == Types.InteractionType.ComeOut)
        {
            TrySnapPlayer(interactive, insidePosition);
        }
    }

    public static void ApplyInteractionPresentation(EntityNetworkAnimatorComponent animatorComponent)
    {
        if (animatorComponent.Pointer == IntPtr.Zero
            || animatorComponent._spookedNetworkPlayer is not { } player
            || player.Pointer == IntPtr.Zero
            || player.EntityInteractiveComponent is not { } interactive
            || interactive.Pointer == IntPtr.Zero)
        {
            return;
        }

        var internalId = player.InternalId;
        if (!InteractionMotions.TryGetValue(internalId, out var motion))
        {
            return;
        }

        var progress = Mathf.Clamp01((Time.unscaledTime - motion.StartedAt) / motion.Duration);
        var easedProgress = Mathf.SmoothStep(0f, 1f, progress);
        ApplyPlayerPose(
            interactive,
            Vector3.LerpUnclamped(motion.StartPosition, motion.TargetPosition, easedProgress),
            Quaternion.SlerpUnclamped(motion.StartRotation, motion.TargetRotation, easedProgress));

        if (progress >= 1f && CompletedInteractionMotions.Remove(internalId))
        {
            InteractionMotions.Remove(internalId);
            CompleteExitPresentation(internalId, interactive, motion);
        }
    }

    public static void FinishInteractionPresentation(
        EntityInteractiveComponent._InteractWithSarcophagus_d__78 coroutine,
        bool hasNextStep)
    {
        if (hasNextStep
            || coroutine.Pointer == IntPtr.Zero
            || coroutine.__4__this is not { } interactive
            || interactive.Pointer == IntPtr.Zero)
        {
            return;
        }

        var internalId = interactive.InternalId;
        if (!InteractionMotions.TryGetValue(internalId, out var motion)
            || motion.InteractionType != coroutine.interactionType)
        {
            return;
        }

        if (Time.unscaledTime - motion.StartedAt < motion.Duration)
        {
            CompletedInteractionMotions.Add(internalId);
            return;
        }

        InteractionMotions.Remove(internalId);
        CompletedInteractionMotions.Remove(internalId);
        CompleteExitPresentation(internalId, interactive, motion);
    }

    public static void Clear()
    {
        foreach (var pair in AnimatorControllerLeases)
        {
            RestoreAnimatorController(pair.Key, pair.Value);
        }

        PendingComeOuts.Clear();
        ReadyComeOuts.Clear();
        ActiveExits.Clear();
        CompletedInteractionMotions.Clear();
        InteractionMotions.Clear();
        AnimatorControllerLeases.Clear();
    }

    private static void BeginInteractionMotion(
        EntityInteractiveComponent interactive,
        Sarcophagus sarcophagus,
        Types.InteractionType interactionType,
        Vector3 targetPosition,
        float duration)
    {
        var internalId = interactive.InternalId;
        if (InteractionMotions.TryGetValue(internalId, out var activeMotion)
            && activeMotion.InteractionType == interactionType)
        {
            return;
        }

        if (!TryGetPlayerPresentationComponents(
                interactive,
                out var transformComponent,
                out var animatorComponent))
        {
            return;
        }

        var startPosition = transformComponent.Position;
        var planarDirection = targetPosition - startPosition;
        planarDirection.y = 0f;
        if (planarDirection.sqrMagnitude < 0.0001f)
        {
            planarDirection = interactionType == Types.InteractionType.ComeOut
                ? GetExitPosition(sarcophagus) - GetInsidePosition(sarcophagus)
                : sarcophagus.transform.forward;
            planarDirection.y = 0f;
        }

        var startRotation = transformComponent.Rotation;
        var targetRotation = planarDirection.sqrMagnitude < 0.0001f
            ? startRotation
            : Quaternion.LookRotation(planarDirection.normalized, Vector3.up);
        InteractionMotions[internalId] = new InteractionMotion(
            interactionType,
            Time.unscaledTime,
            duration,
            startPosition,
            targetPosition,
            startRotation,
            targetRotation);

        if (interactionType == Types.InteractionType.Hide)
        {
            TryBorrowWardrobeAnimatorController(internalId, animatorComponent);
        }

        var trigger = interactionType == Types.InteractionType.Hide
            ? Types.CharacterAnimations.WardrobeHide
            : Types.CharacterAnimations.LockerStepOut;
        animatorComponent.SetTrigger(trigger);
        MummyUnlockRuntime.LogInfo(
            $"Mummy sarcophagus presentation started: player={internalId}, interaction={interactionType}, trigger={trigger}, borrowedController={AnimatorControllerLeases.ContainsKey(internalId)}");
        ApplyPlayerPose(interactive, startPosition, startRotation);
    }

    private static void CompleteExitPresentation(
        int internalId,
        EntityInteractiveComponent interactive,
        InteractionMotion motion)
    {
        if (motion.InteractionType != Types.InteractionType.ComeOut)
        {
            return;
        }

        ActiveExits.Remove(internalId);
        if (!TrySnapPlayer(interactive, motion.TargetPosition))
        {
            MummyUnlockRuntime.LogInfo(
                $"Mummy sarcophagus exit could not finalize player {internalId} at the destination");
        }

        RestoreAnimatorController(internalId);
    }

    private static bool TryBorrowWardrobeAnimatorController(
        int internalId,
        EntityNetworkAnimatorComponent animatorComponent)
    {
        if (AnimatorControllerLeases.ContainsKey(internalId))
        {
            return true;
        }

        var animator = animatorComponent._animator;
        if (animator is null || animator.Pointer == IntPtr.Zero)
        {
            MummyUnlockRuntime.LogInfo(
                $"Mummy sarcophagus could not borrow a wardrobe controller for player {internalId}: animator unavailable");
            return false;
        }

        var wardrobeController = ResolveWardrobeAnimatorController();
        if (wardrobeController is null || wardrobeController.Pointer == IntPtr.Zero)
        {
            MummyUnlockRuntime.LogInfo(
                $"Mummy sarcophagus could not borrow a wardrobe controller for player {internalId}: '{WardrobeAnimatorControllerName}' was not loaded");
            return false;
        }

        var originalController = animator.runtimeAnimatorController;
        AnimatorControllerLeases[internalId] = new AnimatorControllerLease(
            animator,
            originalController);
        animator.runtimeAnimatorController = wardrobeController;
        animator.Rebind();
        animator.Update(0f);
        return true;
    }

    private static RuntimeAnimatorController? ResolveWardrobeAnimatorController()
    {
        if (_wardrobeAnimatorController is not null
            && _wardrobeAnimatorController.Pointer != IntPtr.Zero)
        {
            return _wardrobeAnimatorController;
        }

        _wardrobeAnimatorController = Resources.FindObjectsOfTypeAll<RuntimeAnimatorController>()
            .FirstOrDefault(controller =>
                controller is not null
                && controller.Pointer != IntPtr.Zero
                && string.Equals(
                    controller.name,
                    WardrobeAnimatorControllerName,
                    StringComparison.Ordinal));
        return _wardrobeAnimatorController;
    }

    private static void RestoreAnimatorController(int internalId)
    {
        if (!AnimatorControllerLeases.Remove(internalId, out var lease))
        {
            return;
        }

        RestoreAnimatorController(internalId, lease);
    }

    private static void RestoreAnimatorController(
        int internalId,
        AnimatorControllerLease lease)
    {
        if (lease.Animator is null || lease.Animator.Pointer == IntPtr.Zero)
        {
            return;
        }

        lease.Animator.runtimeAnimatorController = lease.OriginalController;
        lease.Animator.Rebind();
        lease.Animator.Update(0f);
        MummyUnlockRuntime.LogInfo(
            $"Mummy sarcophagus restored the native animator controller for player {internalId}");
    }

    private static bool IsMummy(Sarcophagus sarcophagus, int internalId)
    {
        var registry = sarcophagus._networkPlayerRegistry;
        var player = registry?[internalId];
        return player is not null
            && player.Pointer != IntPtr.Zero
            && player.CharacterType == MummyUnlockRuntime.MummyCharacterType;
    }

    private static bool TryGetInteractiveComponent(
        Sarcophagus sarcophagus,
        int internalId,
        out EntityInteractiveComponent interactive)
    {
        interactive = null!;
        var registry = sarcophagus._networkPlayerRegistry;
        var player = registry?[internalId];
        if (player is null || player.Pointer == IntPtr.Zero)
        {
            return false;
        }

        interactive = player.EntityInteractiveComponent;
        return interactive is not null && interactive.Pointer != IntPtr.Zero;
    }

    private static bool TrySnapPlayer(EntityInteractiveComponent interactive, Vector3 position)
    {
        var transformComponent = interactive._spookedNetworkPlayer?.EntityTransformComponent;
        var characterMovement = transformComponent?._characterMovement;
        if (transformComponent is null
            || transformComponent.Pointer == IntPtr.Zero
            || characterMovement is null
            || characterMovement.Pointer == IntPtr.Zero)
        {
            return false;
        }

        // SetPosition queues work until the next Fusion update. Updating KCC directly
        // establishes the destination before the stock exit coroutine reads Position;
        // ForceSetPosition preserves the teleport in the component's normal queue too.
        characterMovement.SetPosition(position, true, false);
        transformComponent.ForceSetPosition(position, true);
        return true;
    }

    private static bool TryGetPlayerPresentationComponents(
        EntityInteractiveComponent interactive,
        out EntityTransformComponent transformComponent,
        out EntityNetworkAnimatorComponent animatorComponent)
    {
        transformComponent = null!;
        animatorComponent = null!;
        var player = interactive._spookedNetworkPlayer;
        if (player is null || player.Pointer == IntPtr.Zero)
        {
            return false;
        }

        transformComponent = player.EntityTransformComponent;
        animatorComponent = player.EntityNetworkAnimatorComponent;
        return transformComponent is not null
            && transformComponent.Pointer != IntPtr.Zero
            && transformComponent._characterMovement is not null
            && transformComponent._characterMovement.Pointer != IntPtr.Zero
            && animatorComponent is not null
            && animatorComponent.Pointer != IntPtr.Zero;
    }

    private static void ApplyPlayerPose(
        EntityInteractiveComponent interactive,
        Vector3 position,
        Quaternion rotation)
    {
        var transformComponent = interactive._spookedNetworkPlayer?.EntityTransformComponent;
        var characterMovement = transformComponent?._characterMovement;
        if (transformComponent is null
            || transformComponent.Pointer == IntPtr.Zero
            || characterMovement is null
            || characterMovement.Pointer == IntPtr.Zero)
        {
            return;
        }

        // The wardrobe path owns both translation and facing while its animation is
        // active. Apply the same rule here so movement input cannot pull the Mummy out
        // of the sarcophagus path and the camera follows the outward exit direction.
        characterMovement.SetInputDirection(Vector3.zero, true);
        transformComponent.ForceSetPosition(position, false);
        characterMovement.SetPosition(position, false, false);
        transformComponent.SetRotation(rotation);
        characterMovement.SetLookRotation(rotation, true, false);
    }

    public static Vector3 GetInsidePosition(Sarcophagus sarcophagus)
    {
        return sarcophagus.transform.TransformPoint(InsideLocalPosition);
    }

    public static Vector3 GetPromptPosition(Sarcophagus sarcophagus)
    {
        return sarcophagus.transform.TransformPoint(PromptLocalPosition);
    }

    public static Vector3 GetExitPosition(Sarcophagus sarcophagus)
    {
        return sarcophagus.transform.TransformPoint(ExitLocalPosition);
    }
}
