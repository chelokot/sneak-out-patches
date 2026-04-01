using BepInEx.Logging;
using Gameplay.Player.Components;
using Types;
using UnityEngine;

namespace SneakOut.MummyUnlock;

internal static class MummyAnimatorFallbackRuntime
{
    private static readonly CharacterType MummyCharacterType = CharacterType.murderer_mummy;
    private static readonly Dictionary<int, ControllerSwapState> ActiveSwaps = new();
    private static readonly string[] SlipControllerCandidates =
    {
        "MurdererButcherAnimationController",
        "MurdererClownAnimationController",
        "MurdererDraculaAnimationController",
        "RipperAnimationController"
    };
    private static readonly string[] SnareControllerCandidates =
    {
        "MurdererDraculaAnimationController",
        "RipperAnimationController",
        "MurdererButcherAnimationController",
        "MurdererClownAnimationController"
    };

    private static ManualLogSource? _logger;
    private static RuntimeAnimatorController? _cachedSlipController;
    private static RuntimeAnimatorController? _cachedSnareController;

    public static void Initialize(ManualLogSource logger)
    {
        _logger = logger;
    }

    public static void TryApplyReactionController(EntityNetworkAnimatorComponent networkAnimator, SpookedBuffType buffType, float duration)
    {
        if (!IsReactionBuff(buffType) || !IsMummy(networkAnimator))
        {
            return;
        }

        var animator = networkAnimator.Animator;
        if (animator is null)
        {
            return;
        }

        var donorController = GetDonorController(buffType);
        if (donorController is null)
        {
            _logger?.LogWarning($"Mummy reaction controller unavailable for buff={buffType}");
            return;
        }

        var currentController = animator.runtimeAnimatorController;
        if (currentController is null)
        {
            return;
        }

        var animatorId = networkAnimator.GetInstanceID();
        if (!ActiveSwaps.TryGetValue(animatorId, out var swapState))
        {
            swapState = new ControllerSwapState(currentController, donorController, CalculateRestoreAt(duration));
            ActiveSwaps[animatorId] = swapState;
        }
        else
        {
            if (currentController != swapState.ActiveController && currentController != donorController)
            {
                swapState.OriginalController = currentController;
            }

            swapState.ActiveController = donorController;
            swapState.RestoreAt = Mathf.Max(swapState.RestoreAt, CalculateRestoreAt(duration));
        }

        if (animator.runtimeAnimatorController != donorController)
        {
            animator.runtimeAnimatorController = donorController;
        }

        _logger?.LogInfo($"Applied mummy reaction controller for buff={buffType}, donor={donorController.name}, duration={duration:0.###}");
    }

    public static void RestoreIfDue(EntityNetworkAnimatorComponent networkAnimator)
    {
        var animatorId = networkAnimator.GetInstanceID();
        if (!ActiveSwaps.TryGetValue(animatorId, out var swapState))
        {
            return;
        }

        if (Time.unscaledTime < swapState.RestoreAt)
        {
            return;
        }

        var animator = networkAnimator.Animator;
        if (animator is not null && swapState.OriginalController is not null)
        {
            animator.runtimeAnimatorController = swapState.OriginalController;
            _logger?.LogInfo($"Restored mummy reaction controller to {swapState.OriginalController.name}");
        }

        ActiveSwaps.Remove(animatorId);
    }

    private static bool IsReactionBuff(SpookedBuffType buffType)
    {
        return buffType == SpookedBuffType.Slip
               || buffType == SpookedBuffType.BananaFail
               || buffType == SpookedBuffType.Trap
               || buffType == SpookedBuffType.SnaresTrap;
    }

    private static bool IsMummy(EntityNetworkAnimatorComponent networkAnimator)
    {
        var networkPlayer = networkAnimator.GetComponent<SpookedNetworkPlayer>();
        return networkPlayer is not null && networkPlayer.CharacterType == MummyCharacterType;
    }

    private static RuntimeAnimatorController? GetDonorController(SpookedBuffType buffType)
    {
        if (buffType == SpookedBuffType.SnaresTrap)
        {
            _cachedSnareController ??= FindController(SnareControllerCandidates);
            return _cachedSnareController;
        }

        _cachedSlipController ??= FindController(SlipControllerCandidates);
        return _cachedSlipController;
    }

    private static RuntimeAnimatorController? FindController(IEnumerable<string> candidateNames)
    {
        var controllers = Resources.FindObjectsOfTypeAll<RuntimeAnimatorController>();
        if (controllers is null || controllers.Length == 0)
        {
            return null;
        }

        foreach (var candidateName in candidateNames)
        {
            for (var controllerIndex = 0; controllerIndex < controllers.Length; controllerIndex++)
            {
                var controller = controllers[controllerIndex];
                if (controller is not null && controller.name == candidateName)
                {
                    _logger?.LogInfo($"Resolved mummy donor controller '{candidateName}'");
                    return controller;
                }
            }
        }

        return null;
    }

    private static float CalculateRestoreAt(float duration)
    {
        return Time.unscaledTime + Mathf.Max(duration, 1.25f) + 0.35f;
    }

    private sealed class ControllerSwapState
    {
        public ControllerSwapState(RuntimeAnimatorController originalController, RuntimeAnimatorController activeController, float restoreAt)
        {
            OriginalController = originalController;
            ActiveController = activeController;
            RestoreAt = restoreAt;
        }

        public RuntimeAnimatorController OriginalController { get; set; }

        public RuntimeAnimatorController ActiveController { get; set; }

        public float RestoreAt { get; set; }
    }
}
