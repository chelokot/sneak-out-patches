using BepInEx.Logging;
using Cinemachine;
using Fusion;
using Gameplay.ArrowIndicators;
using Gameplay.Buffs;
using Gameplay.Camera;
using Gameplay.Enviro;
using Gameplay.Interactions;
using Gameplay.Interactions.Tasks.Labyrinth;
using Gameplay.Interactions.Tasks.Mustache;
using Gameplay.Interactions.Tasks.Toilet;
using Gameplay.Match;
using Gameplay.Player;
using Gameplay.Player.Components;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Types;
using UnityEngine;
using UnityEngine.InputSystem;
using UI.Interactions;
using UI.Views;

namespace SneakOut.FirstPersonExperiment;

internal static class FirstPersonExperimentRuntime
{
    private const float ToiletYawLimit = 30f;
    private const float LockerYawLimit = 30f;
    private const float HunterSelectionYawLimit = 30f;
    private const float MinimumSettledEyeHeight = 0.5f;
    private const float MaximumSettledEyeHeight = 2.5f;
    private const float MaximumSettledEyePlanarOffset = 1f;
    private const int FullBodyAnimatorLayer = 0;
    private const string YayJumpAnimatorStateName = "Base Layer.Full Body Emotes.Jump";
    private const string AnimatedHeadTransformPath =
        "Kinguin_Base_Merged_fbx/rig/root/DEF-spine/DEF-spine.001/"
        + "DEF-spine.002/DEF-spine.003/DEF-spine.004";
    private const string LeftEyeTransformName = "DEF-eye.L";
    private const string RightEyeTransformName = "DEF-eye.R";

    private static ManualLogSource? _logger;
    private static FirstPersonExperimentConfig? _configuration;
    private static Harmony? _harmony;
    private static SpookedNetworkPlayer? _localPlayer;
    private static SceneCameraManager? _sceneCameraManager;
    private static GameStartController? _gameStartController;
    private static UI.GameUIManager? _gameUiManager;
    private static bool _cameraEngaged;
    private static bool _controlsEngaged;
    private static bool _taskCameraEngaged;
    private static float _lookYawDegrees;
    private static float _lookPitchDegrees;
    private static int _lastMouseDeltaFrame = -1;
    private static Transform? _animatedCameraAnchor;
    private static Transform? _animatedCameraLeftEye;
    private static Transform? _animatedCameraRightEye;
    private static IntPtr _animatedCameraAnchorPlayerPointer;
    private static bool _stableEyeAnchorInitialized;
    private static bool _stableEyeAnchorNeedsGroundedRecapture;
    private static Vector3 _stableEyeLocalPosition;
    private static bool _toiletCameraEngaged;
    private static IntPtr _toiletTaskPointer;
    private static Quaternion _toiletBaseRotation;
    private static float _toiletYawDegrees;
    private static int _lastToiletMouseDeltaFrame = -1;
    private static bool _lockerCameraEngaged;
    private static IntPtr _lockerPointer;
    private static Quaternion _lockerBaseRotation;
    private static float _lockerYawDegrees;
    private static int _lastLockerMouseDeltaFrame = -1;
    private static bool _suppressPaintingTaskCameraTransition;
    private static bool _hunterSelectionOpen;
    private static bool _hunterCagingPhase;
    private static bool _hunterCagingReleasePending;
    private static bool _hunterSelectionCameraEngaged;
    private static Quaternion _hunterSelectionBaseRotation;
    private static float _hunterSelectionYawDegrees;
    private static float _hunterSelectionPitchDegrees;
    private static int _lastHunterSelectionMouseDeltaFrame = -1;
    private static bool _movementInputReadScope;

    internal readonly struct MovementInputOverrideState
    {
        public MovementInputOverrideState(bool previousScope)
        {
            PreviousScope = previousScope;
            Applied = true;
        }

        public bool Applied { get; }
        public bool PreviousScope { get; }
    }

    internal readonly struct BarrelThrowPositionState
    {
        public BarrelThrowPositionState(float releaseHeight)
        {
            ReleaseHeight = releaseHeight;
            Applied = true;
        }

        public bool Applied { get; }
        public float ReleaseHeight { get; }
    }

    internal readonly struct ChairThrowProbeState
    {
        public ChairThrowProbeState(Vector3 velocity)
        {
            Velocity = velocity;
            Applied = true;
        }

        public bool Applied { get; }
        public Vector3 Velocity { get; }
    }

    public static void Initialize(ManualLogSource logger, FirstPersonExperimentConfig configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _harmony ??= new Harmony(FirstPersonExperimentPlugin.PluginGuid);
        _harmony.PatchAll();
    }

    public static void ObservePlayer(SpookedNetworkPlayer player)
    {
        try
        {
            if (player is not null
                && player.Pointer != IntPtr.Zero
                && player.HasInputAuthority
                && !player.IsBot)
            {
                _localPlayer = player;
            }
        }
        catch (Exception exception)
        {
            _logger?.LogDebug($"Ignored unavailable local-player candidate: {exception.Message}");
        }
    }

    public static void ForgetPlayer(SpookedNetworkPlayer player)
    {
        if (_localPlayer is not null && _localPlayer.Pointer == player.Pointer)
        {
            FirstPersonVisibilityRuntime.RestoreStaminaHudOverride();
            _hunterSelectionOpen = false;
            _hunterCagingPhase = false;
            _hunterCagingReleasePending = false;
            _localPlayer = null;
            ResetAllOverrides();
        }
    }

    public static void ObserveCameraManager(SceneCameraManager sceneCameraManager)
    {
        FirstPersonVisibilityRuntime.RestoreStaminaHudOverride();
        _hunterSelectionOpen = false;
        _hunterCagingPhase = false;
        _hunterCagingReleasePending = false;
        _sceneCameraManager = sceneCameraManager;
        ResetAllOverrides();
    }

    public static void ObserveGameStartController(GameStartController gameStartController)
    {
        _gameStartController = gameStartController;
        _hunterCagingPhase = false;
        _hunterCagingReleasePending = false;
    }

    public static void ObserveGameUiManager(UI.GameUIManager gameUiManager)
    {
        _gameUiManager = gameUiManager;
    }

    public static void SetHunterSelectionOpen(bool open)
    {
        if (_hunterSelectionOpen == open)
        {
            return;
        }

        _hunterSelectionOpen = open;
        ResetHunterSelectionCameraState();
        _logger?.LogInfo(
            open
                ? "Hunter-selection camera priority detected; locking the rendered camera to first person"
                : "Hunter-selection camera priority cleared; releasing its first-person camera lock");
    }

    public static void ApplyControls(PlayerInputController inputController)
    {
        if (_configuration?.EnableMod.Value != true || !_cameraEngaged)
        {
            _controlsEngaged = false;
            return;
        }

        try
        {
            var player = _localPlayer;
            var controllerPlayer = inputController._spookedNetworkPlayer;
            if (player is null
                || player.Pointer == IntPtr.Zero
                || controllerPlayer is null
                || controllerPlayer.Pointer != player.Pointer
                || !inputController.HasInputAuthority
                || player.GamePlayerState != GamePlayerState.Alive)
            {
                _controlsEngaged = false;
                return;
            }

            if (!_controlsEngaged)
            {
                var initialForward = Vector3.ProjectOnPlane(player.transform.forward, Vector3.up);
                if (initialForward.sqrMagnitude < 0.0001f)
                {
                    return;
                }

                initialForward.Normalize();
                _lookYawDegrees = Mathf.Atan2(initialForward.x, initialForward.z) * Mathf.Rad2Deg;
                var lookUpLimit = Mathf.Clamp(_configuration.LookUpLimit.Value, 0f, 89f);
                var lookDownLimit = Mathf.Clamp(_configuration.LookDownLimit.Value, 0f, 89f);
                _lookPitchDegrees = Mathf.Clamp(
                    _configuration.Pitch.Value,
                    -lookUpLimit,
                    lookDownLimit);
                _lastMouseDeltaFrame = -1;
                _controlsEngaged = true;
                _logger?.LogInfo("First-person strafe controls engaged; facing now follows horizontal mouse motion");
            }

            var mouse = Mouse.current;
            var gameplayMouseActive = !IsUiOpen()
                && !FirstPersonCursorRuntime.IsReleaseHeld
                && !_hunterSelectionOpen;

            var rightMousePressed = gameplayMouseActive
                && mouse?.rightButton.isPressed == true;

            // This postfix runs after ResolveLocalInputs and immediately before the
            // game compresses its fields in SaveLocalClientInputs. Preserve physical
            // RMB as the native Aim flag. Throwable actions remain entirely stock.
            inputController._isAiming = rightMousePressed;

            if (gameplayMouseActive
                && !FirstPersonCursorRuntime.ShouldSuspendLook
                && _lastMouseDeltaFrame != Time.frameCount)
            {
                if (mouse is not null)
                {
                    var mouseDelta = mouse.delta.ReadValue();
                    var yawSensitivity = Mathf.Clamp(_configuration.MouseYawSensitivity.Value, 0.01f, 2f);
                    var pitchSensitivity = Mathf.Clamp(_configuration.MousePitchSensitivity.Value, 0.01f, 2f);
                    var lookUpLimit = Mathf.Clamp(_configuration.LookUpLimit.Value, 0f, 89f);
                    var lookDownLimit = Mathf.Clamp(_configuration.LookDownLimit.Value, 0f, 89f);
                    _lookYawDegrees = Mathf.Repeat(
                        _lookYawDegrees + mouseDelta.x * yawSensitivity,
                        360f);
                    _lookPitchDegrees = Mathf.Clamp(
                        _lookPitchDegrees - mouseDelta.y * pitchSensitivity,
                        -lookUpLimit,
                        lookDownLimit);
                }

                _lastMouseDeltaFrame = Time.frameCount;
            }

        }
        catch (Exception exception)
        {
            _controlsEngaged = false;
            _logger?.LogError($"First-person control override failed: {exception}");
        }
    }

    public static void ApplyNativeAimInput(int internalId, ref SpookedInput input)
    {
        if (_configuration?.EnableMod.Value != true
            || !_cameraEngaged
            || !_controlsEngaged
            || (!_movementInputReadScope
                && (IsUiOpen()
                    || FirstPersonCursorRuntime.IsReleaseHeld
                    || _hunterSelectionOpen)))
        {
            return;
        }

        try
        {
            var player = _localPlayer;
            if (player is null
                || player.Pointer == IntPtr.Zero
                || player.InternalId != internalId
                || player.GamePlayerState != GamePlayerState.Alive)
            {
                return;
            }

            // GetInput is the final, non-inlined read used by movement, animation,
            // and held interactables such as chairs. Movement receives a scoped
            // forced aim state for strafing; every other consumer receives real RMB.
            input.IsAiming = _movementInputReadScope
                || Mouse.current?.rightButton.isPressed == true;
            var forward = Quaternion.Euler(0f, _lookYawDegrees, 0f) * Vector3.forward;
            input.AimingDirection = new Vector2(forward.x, forward.z);
        }
        catch (Exception exception)
        {
            _logger?.LogError($"First-person shared aim-input override failed: {exception}");
        }
    }

    public static void PromoteThrowableRelease(
        PlayerInputController inputController,
        ref Types.InputActionType inputActionType)
    {
        if (inputActionType != Types.InputActionType.ActionRelease)
        {
            return;
        }

        try
        {
            var player = _localPlayer;
            var controllerPlayer = inputController._spookedNetworkPlayer;
            if (_configuration?.EnableMod.Value != true
                || player is null
                || player.Pointer == IntPtr.Zero
                || controllerPlayer is null
                || controllerPlayer.Pointer != player.Pointer
                || !inputController.HasInputAuthority
                || GetHeldChargeCapableThrowableType(player) == InteractableObjectType.None)
            {
                return;
            }

            inputActionType = Types.InputActionType.ActionReleaseAfterHold;
        }
        catch (Exception exception)
        {
            _logger?.LogError($"Immediate throwable charge promotion failed: {exception}");
        }
    }

    public static void ApplyLowThrowableCarryPose(
        EntityNetworkAnimatorComponent networkAnimator,
        InHandThrowableType inHandThrowableType)
    {
        try
        {
            if (!IsLocalFirstPersonAnimator(networkAnimator))
            {
                return;
            }

            var useLowCarryPose = IsChairOrBarrel(inHandThrowableType);
            networkAnimator.SetBool(CharacterAnimations.Hold, false);
            networkAnimator.SetBool(CharacterAnimations.HoldBarrel, useLowCarryPose);
            networkAnimator.SetLayerWeight(2, useLowCarryPose ? 1f : 0f);
        }
        catch (Exception exception)
        {
            _logger?.LogError($"Low throwable carry pose failed: {exception}");
        }
    }

    public static bool ShouldRunNativeThrowableHoldAnimation(
        EntityNetworkAnimatorComponent networkAnimator)
    {
        try
        {
            if (!IsLocalFirstPersonAnimator(networkAnimator))
            {
                return true;
            }

            var player = _localPlayer!;
            return GetHeldChargeCapableThrowableType(player) == InteractableObjectType.None;
        }
        catch (Exception exception)
        {
            _logger?.LogError($"Throwable hold-animation suppression failed: {exception}");
            return true;
        }
    }

    public static BarrelThrowPositionState BeginBarrelThrow(
        Barrel barrel,
        int internalId)
    {
        try
        {
            var player = _localPlayer;
            if (_configuration?.EnableMod.Value != true
                || !_cameraEngaged
                || player is null
                || player.Pointer == IntPtr.Zero
                || player.InternalId != internalId)
            {
                return default;
            }

            return new BarrelThrowPositionState(barrel.transform.position.y);
        }
        catch (Exception exception)
        {
            _logger?.LogError($"Failed to capture the barrel release height: {exception}");
            return default;
        }
    }

    public static void EndBarrelThrow(
        Barrel barrel,
        BarrelThrowPositionState state)
    {
        if (!state.Applied)
        {
            return;
        }

        try
        {
            var position = barrel.transform.position;
            position.y = state.ReleaseHeight;
            barrel.transform.position = position;

            var body = barrel._rigidbody;
            if (body is not null && body.Pointer != IntPtr.Zero)
            {
                var bodyPosition = body.position;
                bodyPosition.y = state.ReleaseHeight;
                body.position = bodyPosition;
            }
        }
        catch (Exception exception)
        {
            _logger?.LogError($"Failed to preserve the barrel release height: {exception}");
        }
    }

    public static ChairThrowProbeState BeginChairThrowProbe(Chair chair, int internalId)
    {
        try
        {
            var player = _localPlayer;
            if (_configuration?.EnableMod.Value != true
                || !_cameraEngaged
                || player is null
                || player.Pointer == IntPtr.Zero
                || player.InternalId != internalId)
            {
                return default;
            }

            var body = chair._bullet?.Rigidbody ?? chair._rigidbody;
            var forward = player.EntityTransformComponent?.Forward ?? Vector3.zero;
            var velocity = body?.velocity ?? Vector3.zero;
            var gameIsServer = global::Game.Game.IsServer;
            var bodyMatchesChair = body is not null
                && chair._rigidbody is not null
                && body.Pointer == chair._rigidbody.Pointer;

            _logger?.LogInfo(
                "Chair throw probe before: "
                + $"player={internalId}, gameServer={gameIsServer}, "
                + $"stateAuthority={chair.HasStateAuthority}, bodyMatchesChair={bodyMatchesChair}, "
                + $"kinematic={body?.isKinematic}, mass={body?.mass:0.###}, "
                + $"position={FormatVector(chair.transform.position)}, "
                + $"forward={FormatVector(forward)}, velocity={FormatVector(velocity)}.");

            return new ChairThrowProbeState(velocity);
        }
        catch (Exception exception)
        {
            _logger?.LogError($"Chair throw pre-impulse probe failed: {exception}");
            return default;
        }
    }

    public static void EndChairThrowProbe(Chair chair, ChairThrowProbeState state)
    {
        if (!state.Applied)
        {
            return;
        }

        try
        {
            var body = chair._bullet?.Rigidbody ?? chair._rigidbody;
            var velocity = body?.velocity ?? Vector3.zero;
            var gameIsServer = global::Game.Game.IsServer;
            _logger?.LogInfo(
                "Chair throw probe after: "
                + $"gameServer={gameIsServer}, stateAuthority={chair.HasStateAuthority}, "
                + $"kinematic={body?.isKinematic}, position={FormatVector(chair.transform.position)}, "
                + $"velocity={FormatVector(velocity)}, "
                + $"delta={FormatVector(velocity - state.Velocity)}.");
        }
        catch (Exception exception)
        {
            _logger?.LogError($"Chair throw post-impulse probe failed: {exception}");
        }
    }

    public static MovementInputOverrideState BeginMovementInputOverride(
        EntityTransformComponent transformComponent)
    {
        if (_configuration?.EnableMod.Value != true
            || !_cameraEngaged
            || !_controlsEngaged
            || _hunterSelectionOpen
            || IsUiOpen())
        {
            return default;
        }

        try
        {
            var player = _localPlayer;
            var componentPlayer = transformComponent._spookedNetworkPlayer;
            if (player is null
                || player.Pointer == IntPtr.Zero
                || componentPlayer is null
                || componentPlayer.Pointer != player.Pointer
                || !transformComponent.HasInputAuthority
                || player.GamePlayerState != GamePlayerState.Alive)
            {
                return default;
            }

            var previousScope = _movementInputReadScope;
            _movementInputReadScope = true;
            return new MovementInputOverrideState(previousScope);
        }
        catch (Exception exception)
        {
            _logger?.LogError($"First-person movement-input override failed: {exception}");
            return default;
        }
    }

    public static void EndMovementInputOverride(MovementInputOverrideState state)
    {
        if (!state.Applied)
        {
            return;
        }

        _movementInputReadScope = state.PreviousScope;
    }

    public static void BeginHunterCaging(int seekerInternalId)
    {
        try
        {
            var player = _localPlayer;
            if (_configuration?.EnableMod.Value != true
                || player is null
                || player.Pointer == IntPtr.Zero
                || player.InternalId == seekerInternalId)
            {
                return;
            }

            _hunterCagingPhase = true;
            _hunterCagingReleasePending = false;
            SetHunterSelectionOpen(true);
            _logger?.LogInfo(
                "Hunter caging began for the local penguin; retaining the first-person camera lock");
        }
        catch (Exception exception)
        {
            _logger?.LogError($"Hunter caging camera handoff failed: {exception}");
        }
    }

    public static void EndHunterCaging()
    {
        if (!_hunterCagingPhase)
        {
            return;
        }

        _hunterCagingReleasePending = true;
        _logger?.LogInfo(
            "Hunter caging completed; retaining the first-person lock through the gameplay-camera blend");
    }

    public static bool ShouldUpdatePlayerIndicator(
        PlayerIndicator indicator,
        Vector3 playerPosition,
        bool isSeeker)
    {
        if (!isSeeker || !IsFirstPersonWorldViewActive())
        {
            return true;
        }

        var camera = _sceneCameraManager?._mainCamera;
        if (!FirstPersonVisibilityRuntime.IsWorldPositionOccluded(camera, playerPosition))
        {
            return true;
        }

        indicator.DeactivateIndicator();
        return false;
    }

    public static void UpdateTaskIconVisibility(TaskIconView taskIcon)
    {
        if (IsFirstPersonWorldViewActive())
        {
            FirstPersonVisibilityRuntime.UpdateTaskIconVisibility(
                taskIcon,
                _sceneCameraManager?._mainCamera);
        }
    }

    public static void UpdateItemGeneratorCostVisibility(ItemGenerator generator)
    {
        if (IsFirstPersonWorldViewActive())
        {
            FirstPersonVisibilityRuntime.UpdateItemGeneratorCostVisibility(
                generator,
                _sceneCameraManager?._mainCamera);
        }
    }

    public static void ApplyWallCirclePolicy(InvisibleWallsManager manager)
    {
        if (IsModActiveForLocalPlayer())
        {
            FirstPersonVisibilityRuntime.SuppressWallCircle(manager);
        }
    }

    public static void UpdateInputActionCircle(
        InputActionCircle inputCircle,
        NetworkId interactableId)
    {
        var hide = IsFirstPersonWorldViewActive()
            && Keyboard.current?.eKey.isPressed == true
            && ResolveNativeInteractable(interactableId)?.TryCast<LabyrinthConsole>() is not null;
        FirstPersonVisibilityRuntime.UpdateLabyrinthInputCircle(inputCircle, hide);
    }

    public static bool BeginPaintingTaskCameraTransition(int internalId)
    {
        try
        {
            var player = _localPlayer;
            var suppress = _configuration?.EnableMod.Value == true
                && player is not null
                && player.Pointer != IntPtr.Zero
                && player.InternalId == internalId;
            if (suppress)
            {
                _suppressPaintingTaskCameraTransition = true;
            }

            return suppress;
        }
        catch
        {
            return false;
        }
    }

    public static void EndPaintingTaskCameraTransition(bool wasSuppressed)
    {
        if (wasSuppressed)
        {
            _suppressPaintingTaskCameraTransition = false;
        }
    }

    public static bool ShouldRunPaintingTaskCameraTransition()
    {
        return !_suppressPaintingTaskCameraTransition;
    }

    public static void UpdatePaintingTaskUi(MustacheTaskInteractionView view)
    {
        try
        {
            var player = _localPlayer;
            var task = view._task;
            var activeForLocalPlayer = _configuration?.EnableMod.Value == true
                && player is not null
                && player.Pointer != IntPtr.Zero
                && task is not null
                && task.Pointer != IntPtr.Zero
                && view.Active
                && task.PlayerCurrentlyUsing == player.InternalId;
            FirstPersonPaintingTaskUiRuntime.Update(view, activeForLocalPlayer);
        }
        catch (Exception exception)
        {
            FirstPersonPaintingTaskUiRuntime.Restore();
            _logger?.LogError($"First-person painting task UI failed: {exception}");
        }
    }

    public static void ReapplyPaintingTaskUi()
    {
        FirstPersonPaintingTaskUiRuntime.Reapply();
    }

    public static void FinishPaintingTaskInteraction(int internalId)
    {
        try
        {
            var player = _localPlayer;
            if (player is not null
                && player.Pointer != IntPtr.Zero
                && player.InternalId == internalId)
            {
                FirstPersonPaintingTaskUiRuntime.Restore();
            }
        }
        catch
        {
            FirstPersonPaintingTaskUiRuntime.Restore();
        }
    }

    public static void ReapplyFirstPersonIndicators()
    {
        if (!IsFirstPersonWorldViewActive())
        {
            return;
        }

        try
        {
            var player = _localPlayer;
            var camera = _sceneCameraManager?._mainCamera;
            if (player is not null
                && player.Pointer != IntPtr.Zero
                && camera is not null
                && camera.Pointer != IntPtr.Zero)
            {
                FirstPersonVisibilityRuntime.UpdateFirstPersonIndicators(
                    player,
                    camera,
                    _gameUiManager?._gameView);
            }
        }
        catch (Exception exception)
        {
            _logger?.LogError($"First-person indicator scaling failed: {exception}");
        }
    }

    public static void ReapplyFirstPersonStaminaHud()
    {
        try
        {
            var player = _localPlayer;
            var gameView = _gameUiManager?._gameView;
            if (_configuration?.EnableMod.Value == true
                && player is not null
                && player.Pointer != IntPtr.Zero)
            {
                FirstPersonVisibilityRuntime.UpdateStaminaHud(
                    player,
                    gameView);
                return;
            }

            FirstPersonVisibilityRuntime.RestoreStaminaHudOverride();
        }
        catch (Exception exception)
        {
            _logger?.LogError($"First-person stamina HUD override failed: {exception}");
        }
    }

    public static void ApplyCamera(CinemachineBrain brain)
    {
        if (_configuration?.EnableMod.Value != true)
        {
            FirstPersonVisibilityRuntime.RestoreStaminaHudOverride();
            _hunterSelectionOpen = false;
            _hunterCagingPhase = false;
            _hunterCagingReleasePending = false;
            ResetAllOverrides();
            return;
        }

        try
        {
            var player = _localPlayer;
            var sceneCameraManager = _sceneCameraManager;
            if (sceneCameraManager is null || sceneCameraManager.Pointer == IntPtr.Zero)
            {
                ResetAllOverrides();
                return;
            }

            ReleaseHunterCagingWhenGameplayCameraIsReady(brain, sceneCameraManager);
            RefreshHunterSelectionState(sceneCameraManager);
            if (player is null
                || player.Pointer == IntPtr.Zero
                || !player.HasInputAuthority
                || player.IsBot
                || (!_hunterSelectionOpen
                    && player.GamePlayerState != GamePlayerState.Alive))
            {
                ResetAllOverrides();
                return;
            }

            var mainCamera = sceneCameraManager._mainCamera;
            var outputCamera = brain.OutputCamera;
            if (mainCamera is null
                || outputCamera is null
                || outputCamera.Pointer != mainCamera.Pointer)
            {
                ResetAllOverrides();
                return;
            }

            FirstPersonCursorRuntime.Update(IsUiOpen());
            if (_hunterSelectionOpen)
            {
                ResetToiletCameraState();
                ResetLockerCameraState();
                ApplyHunterSelectionCamera(mainCamera, player);
                FirstPersonVisibilityRuntime.UpdateFirstPersonIndicators(
                    player,
                    mainCamera,
                    _gameUiManager?._gameView);
                return;
            }

            ResetHunterSelectionCameraState();
            var activeCamera = brain.ActiveVirtualCamera;
            if (activeCamera is null || brain.IsBlending)
            {
                ResetAllOverrides();
                return;
            }

            var activeCameraObject = activeCamera.VirtualCameraGameObject;
            var gameplayCamera = sceneCameraManager._gameplayCamera;
            if (gameplayCamera is not null
                && activeCameraObject.Pointer == gameplayCamera.gameObject.Pointer)
            {
                ResetToiletCameraState();
                var locker = FindLocalLocker(player);
                if (locker is not null)
                {
                    ResetFirstPersonView();
                    FirstPersonVisibilityRuntime.SuppressThroughMapOverlays(mainCamera);
                    FirstPersonVisibilityRuntime.HideLocalPlayerVisuals(player);
                    ApplyLockerCamera(mainCamera, player, locker);
                }
                else
                {
                    ResetLockerCameraState();
                    ApplyFirstPersonCamera(mainCamera, player);
                }
                FirstPersonVisibilityRuntime.UpdateFirstPersonIndicators(
                    player,
                    mainCamera,
                    _gameUiManager?._gameView);
                return;
            }

            var previousInteractionTarget = ResolveNativeInteractionTarget(player);
            ResetLockerCameraState();
            ResetFirstPersonView();
            if (IsTaskCamera(sceneCameraManager, activeCameraObject))
            {
                FirstPersonVisibilityRuntime.SuppressThroughMapOverlays(mainCamera);
                FirstPersonVisibilityRuntime.HideLocalPlayerVisuals(player);
                var toiletTask = FindLocalToiletTask(
                    player,
                    mainCamera,
                    previousInteractionTarget);
                if (toiletTask is not null)
                {
                    ApplyToiletCamera(mainCamera, toiletTask);
                }
                else
                {
                    ResetToiletCameraState();
                    ApplyTaskCameraPullback(mainCamera);
                }
                FirstPersonVisibilityRuntime.UpdateFirstPersonIndicators(
                    player,
                    mainCamera,
                    _gameUiManager?._gameView);
                return;
            }

            ResetAllOverrides();
        }
        catch (Exception exception)
        {
            ResetAllOverrides();
            _logger?.LogError($"First-person camera override failed: {exception}");
        }
    }

    private static void RefreshHunterSelectionState(SceneCameraManager sceneCameraManager)
    {
        // ShowSeekerCage starts a one-second reveal after the native magic-circle
        // camera is deactivated. _hunterCagingPhase bridges exactly that interval.
        var magicCircleCamera = sceneCameraManager._magicCircleCamera;
        var seekerCageCamera = sceneCameraManager._seekerCageCamera;
        var nativeSelectionCameraActive = (magicCircleCamera is not null
                && magicCircleCamera.Pointer != IntPtr.Zero
                && magicCircleCamera.Priority > 0)
            || (seekerCageCamera is not null
                && seekerCageCamera.Pointer != IntPtr.Zero
                && seekerCageCamera.Priority > 0);
        SetHunterSelectionOpen(nativeSelectionCameraActive || _hunterCagingPhase);
    }

    private static void ReleaseHunterCagingWhenGameplayCameraIsReady(
        CinemachineBrain brain,
        SceneCameraManager sceneCameraManager)
    {
        if (!_hunterCagingPhase
            || !_hunterCagingReleasePending
            || brain.IsBlending)
        {
            return;
        }

        var activeCamera = brain.ActiveVirtualCamera;
        var gameplayCamera = sceneCameraManager._gameplayCamera;
        if (activeCamera is null
            || gameplayCamera is null
            || gameplayCamera.Pointer == IntPtr.Zero
            || activeCamera.VirtualCameraGameObject.Pointer
                != gameplayCamera.gameObject.Pointer)
        {
            return;
        }

        _hunterCagingPhase = false;
        _hunterCagingReleasePending = false;
        _logger?.LogInfo(
            "Gameplay camera is stable; completing the hunter-caging first-person handoff");
    }

    private static void ApplyFirstPersonCamera(Camera mainCamera, SpookedNetworkPlayer player)
    {
        _taskCameraEngaged = false;
        FirstPersonVisibilityRuntime.SuppressThroughMapOverlays(mainCamera);
        FirstPersonVisibilityRuntime.HideLocalPlayerVisuals(player);

        var playerTransform = player.transform;
        var forward = _controlsEngaged
            ? Quaternion.Euler(0f, _lookYawDegrees, 0f) * Vector3.forward
            : Vector3.ProjectOnPlane(playerTransform.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.0001f)
        {
            return;
        }
        forward.Normalize();

        var headHeight = Mathf.Clamp(_configuration!.HeadHeight.Value, 0.1f, 3f);
        var forwardOffset = Mathf.Clamp(_configuration.ForwardOffset.Value, -1f, 1f);
        var eyeVerticalOffset = Mathf.Clamp(_configuration.EyeVerticalOffset.Value, -1f, 1f);
        var eyeForwardOffset = Mathf.Clamp(_configuration.EyeForwardOffset.Value, -1f, 1f);
        var cameraTransform = mainCamera.transform;
        var cameraRotation = _controlsEngaged
            ? Quaternion.Euler(_lookPitchDegrees, _lookYawDegrees, 0f)
            : Quaternion.LookRotation(forward, Vector3.up)
                * Quaternion.Euler(Mathf.Clamp(_configuration.Pitch.Value, -85f, 85f), 0f, 0f);
        var cameraPosition =
            playerTransform.position + Vector3.up * headHeight + forward * forwardOffset;
        var stabilizedEyeAnchorApplied = TryGetStabilizedEyePosition(
            player,
            out var stabilizedEyePosition);
        if (stabilizedEyeAnchorApplied)
        {
            cameraPosition = stabilizedEyePosition
                + Vector3.up * eyeVerticalOffset
                + forward * eyeForwardOffset;
        }
        cameraTransform.SetPositionAndRotation(
            cameraPosition,
            cameraRotation);

        if (!_cameraEngaged)
        {
            _cameraEngaged = true;
            _logger?.LogInfo(
                $"First-person camera engaged for local player {player.InternalId}: "
                + $"eyeVertical={eyeVerticalOffset:F2}, eyeForward={eyeForwardOffset:F2}, "
                + $"pitch={_lookPitchDegrees:F1}, "
                + $"anchor={(stabilizedEyeAnchorApplied ? "stabilized eyes" : "player root fallback")}");
        }
    }

    private static bool TryGetStabilizedEyePosition(
        SpookedNetworkPlayer player,
        out Vector3 position)
    {
        position = Vector3.zero;
        if (!TryGetAnimatedEyePosition(player, out var animatedEyePosition))
        {
            return false;
        }

        var playerTransform = player.transform;
        if (playerTransform is null || playerTransform.Pointer == IntPtr.Zero)
        {
            return false;
        }

        var yayJumpActive = IsYayJumpAnimationActive(player);
        if (!_stableEyeAnchorInitialized
            || (_stableEyeAnchorNeedsGroundedRecapture && !yayJumpActive))
        {
            if (!IsSettledEyeSample(playerTransform.position, animatedEyePosition))
            {
                // The rig transform can become discoverable one or two frames
                // before the animator has placed its bones. Keep the safe root
                // fallback until the eye sample represents an actual head pose.
                return false;
            }

            _stableEyeLocalPosition = playerTransform.InverseTransformPoint(animatedEyePosition);
            _stableEyeAnchorInitialized = true;
            _stableEyeAnchorNeedsGroundedRecapture = yayJumpActive;
            _logger?.LogInfo(
                "Stabilized camera anchor captured; locomotion eye sway will be ignored");
        }

        position = playerTransform.TransformPoint(_stableEyeLocalPosition);
        if (yayJumpActive)
        {
            // The emote moves only camera height. Its lateral head motion remains
            // excluded just like gait sway, so mouse aim stays visually stable.
            position.y = animatedEyePosition.y;
        }

        return true;
    }

    private static bool IsSettledEyeSample(Vector3 playerPosition, Vector3 eyePosition)
    {
        var offset = eyePosition - playerPosition;
        var height = Vector3.Dot(offset, Vector3.up);
        var planarOffset = offset - Vector3.up * height;
        return height >= MinimumSettledEyeHeight
            && height <= MaximumSettledEyeHeight
            && planarOffset.sqrMagnitude
                <= MaximumSettledEyePlanarOffset * MaximumSettledEyePlanarOffset;
    }

    private static bool TryGetAnimatedEyePosition(
        SpookedNetworkPlayer player,
        out Vector3 position)
    {
        position = Vector3.zero;
        if (!ResolveAnimatedCameraAnchor(player)
            || _animatedCameraLeftEye is null
            || _animatedCameraLeftEye.Pointer == IntPtr.Zero
            || _animatedCameraRightEye is null
            || _animatedCameraRightEye.Pointer == IntPtr.Zero)
        {
            return false;
        }

        position = (_animatedCameraLeftEye.position + _animatedCameraRightEye.position) * 0.5f;
        return true;
    }

    private static bool IsYayJumpAnimationActive(SpookedNetworkPlayer player)
    {
        var animator = player.EntityNetworkAnimatorComponent?.Animator;
        if (animator is null
            || animator.Pointer == IntPtr.Zero
            || animator.layerCount <= FullBodyAnimatorLayer)
        {
            return false;
        }

        if (animator.GetCurrentAnimatorStateInfo(FullBodyAnimatorLayer)
            .IsName(YayJumpAnimatorStateName))
        {
            return true;
        }

        return animator.IsInTransition(FullBodyAnimatorLayer)
            && animator.GetNextAnimatorStateInfo(FullBodyAnimatorLayer)
                .IsName(YayJumpAnimatorStateName);
    }

    private static bool ResolveAnimatedCameraAnchor(SpookedNetworkPlayer player)
    {
        if (_animatedCameraAnchorPlayerPointer == player.Pointer
            && _animatedCameraAnchor is not null
            && _animatedCameraAnchor.Pointer != IntPtr.Zero
            && _animatedCameraLeftEye is not null
            && _animatedCameraLeftEye.Pointer != IntPtr.Zero
            && _animatedCameraRightEye is not null
            && _animatedCameraRightEye.Pointer != IntPtr.Zero)
        {
            return true;
        }

        ResetAnimatedCameraAnchor();
        var networkAnimator = player.EntityNetworkAnimatorComponent;
        var animator = networkAnimator?.Animator;
        var animatorTransform = animator?.transform;
        if (animatorTransform is null || animatorTransform.Pointer == IntPtr.Zero)
        {
            return false;
        }

        var anchor = animatorTransform.Find(AnimatedHeadTransformPath);
        if (anchor is null || anchor.Pointer == IntPtr.Zero)
        {
            return false;
        }

        var leftEye = anchor.Find(LeftEyeTransformName);
        var rightEye = anchor.Find(RightEyeTransformName);
        if (leftEye is null
            || leftEye.Pointer == IntPtr.Zero
            || rightEye is null
            || rightEye.Pointer == IntPtr.Zero)
        {
            return false;
        }

        _animatedCameraAnchor = anchor;
        _animatedCameraLeftEye = leftEye;
        _animatedCameraRightEye = rightEye;
        _animatedCameraAnchorPlayerPointer = player.Pointer;
        _logger?.LogInfo("Animated camera anchor resolved at the live eye midpoint");
        return true;
    }

    private static void ResetAnimatedCameraAnchor()
    {
        _animatedCameraAnchor = null;
        _animatedCameraLeftEye = null;
        _animatedCameraRightEye = null;
        _animatedCameraAnchorPlayerPointer = IntPtr.Zero;
        _stableEyeAnchorInitialized = false;
        _stableEyeAnchorNeedsGroundedRecapture = false;
        _stableEyeLocalPosition = Vector3.zero;
    }

    private static void ApplyTaskCameraPullback(Camera mainCamera)
    {
        var pullback = Mathf.Clamp(_configuration!.TaskCameraPullback.Value, 0f, 8f);
        if (pullback > 0f)
        {
            var cameraTransform = mainCamera.transform;
            cameraTransform.position -= cameraTransform.forward * pullback;
        }

        if (!_taskCameraEngaged)
        {
            _taskCameraEngaged = true;
            _logger?.LogInfo($"Task camera pullback engaged: distance={pullback:F2}");
        }
    }

    private static void ApplyHunterSelectionCamera(
        Camera mainCamera,
        SpookedNetworkPlayer player)
    {
        _taskCameraEngaged = false;
        FirstPersonVisibilityRuntime.SuppressThroughMapOverlays(mainCamera);
        FirstPersonVisibilityRuntime.HideLocalPlayerVisuals(player);

        if (!_hunterSelectionCameraEngaged)
        {
            var playerTransform = player.transform;
            var forward = ResolveHunterSelectionInwardForward(playerTransform);
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = _controlsEngaged
                    ? Quaternion.Euler(0f, _lookYawDegrees, 0f) * Vector3.forward
                    : Vector3.ProjectOnPlane(playerTransform.forward, Vector3.up);
            }
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.forward;
            }
            forward.Normalize();

            var circleFacingRotation = Quaternion.LookRotation(forward, Vector3.up);
            var transformComponent = player.EntityTransformComponent;
            if (transformComponent is not null && transformComponent.Pointer != IntPtr.Zero)
            {
                // Apply immediately to the local KCC/render transform, then queue the
                // same authoritative rotation through the game's normal network path.
                transformComponent.SetLocalRotation(circleFacingRotation);
                transformComponent.SetRotation(circleFacingRotation);
            }

            _lookYawDegrees = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;

            _hunterSelectionBaseRotation = circleFacingRotation;
            _hunterSelectionYawDegrees = 0f;
            var lookUpLimit = Mathf.Clamp(_configuration!.LookUpLimit.Value, 0f, 89f);
            var lookDownLimit = Mathf.Clamp(_configuration.LookDownLimit.Value, 0f, 89f);
            _hunterSelectionPitchDegrees = Mathf.Clamp(
                _lookPitchDegrees,
                -lookUpLimit,
                lookDownLimit);
            _lastHunterSelectionMouseDeltaFrame = -1;
            _hunterSelectionCameraEngaged = true;
            _logger?.LogInfo(
                "Hunter-selection first-person camera engaged with 30-degree horizontal limits and normal vertical look");
        }

        if (!IsUiOpen()
            && !FirstPersonCursorRuntime.ShouldSuspendLook
            && _lastHunterSelectionMouseDeltaFrame != Time.frameCount)
        {
            var mouse = Mouse.current;
            if (mouse is not null)
            {
                var mouseDelta = mouse.delta.ReadValue();
                var yawSensitivity = Mathf.Clamp(
                    _configuration!.MouseYawSensitivity.Value,
                    0.01f,
                    2f);
                var pitchSensitivity = Mathf.Clamp(
                    _configuration.MousePitchSensitivity.Value,
                    0.01f,
                    2f);
                var lookUpLimit = Mathf.Clamp(_configuration.LookUpLimit.Value, 0f, 89f);
                var lookDownLimit = Mathf.Clamp(_configuration.LookDownLimit.Value, 0f, 89f);
                _hunterSelectionYawDegrees = Mathf.Clamp(
                    _hunterSelectionYawDegrees + mouseDelta.x * yawSensitivity,
                    -HunterSelectionYawLimit,
                    HunterSelectionYawLimit);
                _hunterSelectionPitchDegrees = Mathf.Clamp(
                    _hunterSelectionPitchDegrees - mouseDelta.y * pitchSensitivity,
                    -lookUpLimit,
                    lookDownLimit);
                _lookPitchDegrees = _hunterSelectionPitchDegrees;
            }

            _lastHunterSelectionMouseDeltaFrame = Time.frameCount;
        }

        // Native selection can reposition the player between the circle reveal and
        // the cage shot. Follow the current head position while retaining the inward
        // camera basis established at the start of the sequence.
        var lockedForward = _hunterSelectionBaseRotation * Vector3.forward;
        var headHeight = Mathf.Clamp(_configuration!.HeadHeight.Value, 0.1f, 3f);
        var forwardOffset = Mathf.Clamp(_configuration.ForwardOffset.Value, -1f, 1f);
        var cameraPosition = player.transform.position
            + Vector3.up * headHeight
            + lockedForward * forwardOffset;
        _cameraEngaged = true;
        mainCamera.transform.SetPositionAndRotation(
            cameraPosition,
            Quaternion.AngleAxis(_hunterSelectionYawDegrees, Vector3.up)
                * _hunterSelectionBaseRotation
                * Quaternion.Euler(_hunterSelectionPitchDegrees, 0f, 0f));
    }

    private static Vector3 ResolveHunterSelectionInwardForward(Transform playerTransform)
    {
        try
        {
            var positionProvider = _gameStartController?._scenePositionProvider;
            var circleRenderer = positionProvider?.GetMagicCircleRenderer();
            if (circleRenderer is null || circleRenderer.Pointer == IntPtr.Zero)
            {
                return Vector3.zero;
            }

            // Renderer.bounds.center is the ritual circle's authored world-space
            // center. Subtracting the player's position gives the unambiguous inward
            // direction and avoids inferring it from an offset cinematic camera.
            return Vector3.ProjectOnPlane(
                circleRenderer.bounds.center - playerTransform.position,
                Vector3.up);
        }
        catch (Exception exception)
        {
            _logger?.LogError($"Could not resolve the ritual-circle center: {exception}");
            return Vector3.zero;
        }
    }

    private static void ResetHunterSelectionCameraState()
    {
        _hunterSelectionCameraEngaged = false;
        _hunterSelectionBaseRotation = Quaternion.identity;
        _hunterSelectionYawDegrees = 0f;
        _hunterSelectionPitchDegrees = 0f;
        _lastHunterSelectionMouseDeltaFrame = -1;
    }

    private static ToiletTask? FindLocalToiletTask(
        SpookedNetworkPlayer player,
        Camera mainCamera,
        Interactable? previousInteractionTarget)
    {
        var previousToiletTarget = previousInteractionTarget?.TryCast<ToiletTask>();
        ToiletTask? cameraMatchedTask = null;
        var closestCameraDistance = 0.25f * 0.25f;
        foreach (var toiletTask in Resources.FindObjectsOfTypeAll<ToiletTask>())
        {
            if (toiletTask is null
                || toiletTask.Pointer == IntPtr.Zero
                || !toiletTask.gameObject.scene.IsValid())
            {
                continue;
            }

            var cameraPosition = toiletTask._cameraPosition;
            if (cameraPosition is null || cameraPosition.Pointer == IntPtr.Zero)
            {
                continue;
            }

            if ((_toiletCameraEngaged && toiletTask.Pointer == _toiletTaskPointer)
                || (previousToiletTarget is not null
                    && previousToiletTarget.Pointer == toiletTask.Pointer)
                || (toiletTask.PlayerCurrentlyUsing == player.InternalId
                    && (toiletTask._isCurrentlyUsingToilet || toiletTask._duringInteraction)))
            {
                return toiletTask;
            }

            // During the actual Toilet task, the stock interaction clears its
            // occupant flags before the dedicated camera is activated. The task
            // camera is nevertheless placed at this authored transform, which is
            // an exact and isolated way to recognize the Toilet camera.
            var cameraDistance = (
                cameraPosition.position - mainCamera.transform.position).sqrMagnitude;
            if (cameraDistance < closestCameraDistance)
            {
                closestCameraDistance = cameraDistance;
                cameraMatchedTask = toiletTask;
            }
        }

        return cameraMatchedTask;
    }

    private static void ApplyToiletCamera(Camera mainCamera, ToiletTask toiletTask)
    {
        var cameraPosition = toiletTask._cameraPosition;
        if (cameraPosition is null || cameraPosition.Pointer == IntPtr.Zero)
        {
            ResetToiletCameraState();
            ApplyTaskCameraPullback(mainCamera);
            return;
        }

        if (!_toiletCameraEngaged || _toiletTaskPointer != toiletTask.Pointer)
        {
            _toiletCameraEngaged = true;
            _toiletTaskPointer = toiletTask.Pointer;
            _toiletBaseRotation = cameraPosition.rotation;
            _toiletYawDegrees = 0f;
            _lastToiletMouseDeltaFrame = -1;
            _logger?.LogInfo("Immersive Toilet camera engaged with 30-degree horizontal look limits");
        }

        if (!IsUiOpen()
            && !FirstPersonCursorRuntime.ShouldSuspendLook
            && _lastToiletMouseDeltaFrame != Time.frameCount)
        {
            var mouse = Mouse.current;
            if (mouse is not null)
            {
                var yawSensitivity = Mathf.Clamp(
                    _configuration!.MouseYawSensitivity.Value,
                    0.01f,
                    2f);
                _toiletYawDegrees = Mathf.Clamp(
                    _toiletYawDegrees + mouse.delta.ReadValue().x * yawSensitivity,
                    -ToiletYawLimit,
                    ToiletYawLimit);
            }

            _lastToiletMouseDeltaFrame = Time.frameCount;
        }

        _taskCameraEngaged = true;
        mainCamera.transform.SetPositionAndRotation(
            cameraPosition.position,
            Quaternion.AngleAxis(_toiletYawDegrees, Vector3.up) * _toiletBaseRotation);
    }

    private static void ResetToiletCameraState()
    {
        _toiletCameraEngaged = false;
        _toiletTaskPointer = IntPtr.Zero;
        _toiletBaseRotation = Quaternion.identity;
        _toiletYawDegrees = 0f;
        _lastToiletMouseDeltaFrame = -1;
    }

    private static Locker? FindLocalLocker(SpookedNetworkPlayer player)
    {
        foreach (var locker in Resources.FindObjectsOfTypeAll<Locker>())
        {
            if (locker is not null
                && locker.Pointer != IntPtr.Zero
                && locker.gameObject.scene.IsValid()
                && locker.PlayerCurrentlyUsing == player.InternalId)
            {
                return locker;
            }
        }

        return null;
    }

    private static void ApplyLockerCamera(
        Camera mainCamera,
        SpookedNetworkPlayer player,
        Locker locker)
    {
        var lockerTransform = locker.Transform;
        if (lockerTransform is null || lockerTransform.Pointer == IntPtr.Zero)
        {
            ResetLockerCameraState();
            ApplyFirstPersonCamera(mainCamera, player);
            return;
        }

        if (!_lockerCameraEngaged || _lockerPointer != locker.Pointer)
        {
            var outward = Vector3.ProjectOnPlane(
                locker.GetForwardLockerPosition() - lockerTransform.position,
                Vector3.up);
            if (outward.sqrMagnitude < 0.0001f)
            {
                outward = Vector3.ProjectOnPlane(lockerTransform.forward, Vector3.up);
            }

            if (outward.sqrMagnitude < 0.0001f)
            {
                ResetLockerCameraState();
                ApplyFirstPersonCamera(mainCamera, player);
                return;
            }

            _lockerCameraEngaged = true;
            _lockerPointer = locker.Pointer;
            _lockerBaseRotation = Quaternion.LookRotation(outward.normalized, Vector3.up);
            _lockerYawDegrees = 0f;
            _lastLockerMouseDeltaFrame = -1;
            _logger?.LogInfo("Immersive Locker camera engaged with 30-degree horizontal look limits");
        }

        if (!IsUiOpen()
            && !FirstPersonCursorRuntime.ShouldSuspendLook
            && _lastLockerMouseDeltaFrame != Time.frameCount)
        {
            var mouse = Mouse.current;
            if (mouse is not null)
            {
                var yawSensitivity = Mathf.Clamp(
                    _configuration!.MouseYawSensitivity.Value,
                    0.01f,
                    2f);
                _lockerYawDegrees = Mathf.Clamp(
                    _lockerYawDegrees + mouse.delta.ReadValue().x * yawSensitivity,
                    -LockerYawLimit,
                    LockerYawLimit);
            }

            _lastLockerMouseDeltaFrame = Time.frameCount;
        }

        var headHeight = Mathf.Clamp(_configuration!.HeadHeight.Value, 0.1f, 3f);
        _taskCameraEngaged = true;
        mainCamera.transform.SetPositionAndRotation(
            player.transform.position + Vector3.up * headHeight,
            Quaternion.AngleAxis(_lockerYawDegrees, Vector3.up) * _lockerBaseRotation);
    }

    private static void ResetLockerCameraState()
    {
        _lockerCameraEngaged = false;
        _lockerPointer = IntPtr.Zero;
        _lockerBaseRotation = Quaternion.identity;
        _lockerYawDegrees = 0f;
        _lastLockerMouseDeltaFrame = -1;
    }

    private static bool IsTaskCamera(SceneCameraManager manager, GameObject activeCameraObject)
    {
        return IsVirtualCameraActive(manager._taskCamera, activeCameraObject)
            || IsVirtualCameraActive(manager._chemicalTaskButtonsCamera, activeCameraObject)
            || IsVirtualCameraActive(manager._telescopeTaskCamera, activeCameraObject);
    }

    private static bool IsVirtualCameraActive(
        CinemachineVirtualCamera? virtualCamera,
        GameObject activeCameraObject)
    {
        return virtualCamera is not null
            && virtualCamera.Pointer != IntPtr.Zero
            && virtualCamera.gameObject.Pointer == activeCameraObject.Pointer;
    }

    private static void ResetFirstPersonView()
    {
        _cameraEngaged = false;
        _controlsEngaged = false;
        _lastMouseDeltaFrame = -1;
        ResetAnimatedCameraAnchor();
    }

    private static void ResetAllOverrides()
    {
        _suppressPaintingTaskCameraTransition = false;
        FirstPersonPaintingTaskUiRuntime.Restore();
        FirstPersonCursorRuntime.Restore();
        FirstPersonVisibilityRuntime.RestoreThroughMapOverlays();
        ResetFirstPersonView();
        ResetToiletCameraState();
        ResetLockerCameraState();
        ResetHunterSelectionCameraState();
        _taskCameraEngaged = false;
    }

    private static InteractableObjectType GetHeldChargeCapableThrowableType(
        SpookedNetworkPlayer player)
    {
        var interactiveComponent = player.EntityInteractiveComponent;
        if (interactiveComponent is null
            || interactiveComponent.Pointer == IntPtr.Zero
            || !interactiveComponent._isPlayerHolding)
        {
            return InteractableObjectType.None;
        }

        var heldInteractable = interactiveComponent.GetPlayerInteractable();
        if (heldInteractable is null || heldInteractable.Pointer == IntPtr.Zero)
        {
            return InteractableObjectType.None;
        }

        return heldInteractable.InteractableType is InteractableObjectType.Chair
            or InteractableObjectType.Barrel
            ? heldInteractable.InteractableType
            : InteractableObjectType.None;
    }

    private static bool IsLocalFirstPersonAnimator(
        EntityNetworkAnimatorComponent networkAnimator)
    {
        var player = _localPlayer;
        var animatorPlayer = networkAnimator._spookedNetworkPlayer;
        return _configuration?.EnableMod.Value == true
            && player is not null
            && player.Pointer != IntPtr.Zero
            && animatorPlayer is not null
            && animatorPlayer.Pointer == player.Pointer
            && networkAnimator.HasInputAuthority;
    }

    private static bool IsChairOrBarrel(InHandThrowableType throwableType)
    {
        return throwableType is InHandThrowableType.Chair_b
            or InHandThrowableType.TableSet_b_Chair
            or InHandThrowableType.TableSet_a_Chair
            or InHandThrowableType.Barrel
            or InHandThrowableType.ExplosiveBarrel
            or InHandThrowableType.ChineseChairSet_a_chair_a
            or InHandThrowableType.ChineseChairSet_a_chair_b
            or InHandThrowableType.ChineseChairSet_a_chair_c
            or InHandThrowableType.ChineseChairSet_a_chair_d
            or InHandThrowableType.WarTable_Stool_a
            or InHandThrowableType.School_Chair_A
            or InHandThrowableType.School_Chair_B;
    }

    private static Interactable? ResolveNativeInteractionTarget(SpookedNetworkPlayer player)
    {
        var interactiveComponent = player.EntityInteractiveComponent;
        if (interactiveComponent is null || interactiveComponent.Pointer == IntPtr.Zero)
        {
            return null;
        }

        var activeId = interactiveComponent.ActiveInteractiveNetworkId;
        if (activeId.IsValid)
        {
            var activeInteractable = ResolveNativeInteractable(activeId);
            if (activeInteractable is not null)
            {
                return activeInteractable;
            }
        }

        return ResolveNativeInteractable(interactiveComponent.SelectedInteractiveNetworkId);
    }

    private static Interactable? ResolveNativeInteractable(NetworkId interactableId)
    {
        if (!interactableId.IsValid)
        {
            return null;
        }

        try
        {
            return _localPlayer?.EntityInteractiveComponent?._interactableObjectsRegistry?[interactableId];
        }
        catch
        {
            return null;
        }
    }

    private static bool IsUiOpen()
    {
        try
        {
            if (UI.GameUIManager.CurrentUIOpened != UIViewType.None)
            {
                return true;
            }

            var gameUiManager = _gameUiManager;
            var gameMenu = gameUiManager?._gameMenuView;
            return gameMenu is not null
                && gameMenu.Pointer != IntPtr.Zero
                && gameMenu._isMenuOpen;
        }
        catch
        {
            // Failing open preserves access to UI if its state is unavailable during teardown.
            return true;
        }
    }

    private static bool IsFirstPersonWorldViewActive()
    {
        return _configuration?.EnableMod.Value == true && (_cameraEngaged || _taskCameraEngaged);
    }

    private static bool IsModActiveForLocalPlayer()
    {
        try
        {
            return _configuration?.EnableMod.Value == true
                && _localPlayer is not null
                && _localPlayer.Pointer != IntPtr.Zero
                && _localPlayer.HasInputAuthority
                && !_localPlayer.IsBot
                && _localPlayer.GamePlayerState == GamePlayerState.Alive;
        }
        catch
        {
            return false;
        }
    }

    private static string FormatVector(Vector3 value)
    {
        return $"({value.x:0.###},{value.y:0.###},{value.z:0.###})";
    }

}
