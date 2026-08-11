using Gameplay;
using Gameplay.Enviro;
using Gameplay.Interactions;
using Gameplay.Player.Customization;
using Gameplay.Player.Components;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Plugins.Easy_performant_outline.Scripts;
using Types;
using UI.Interactions;
using UI.Views;
using UnityEngine;
using UnityEngine.UI;

namespace SneakOut.FirstPersonExperiment;

internal static class FirstPersonVisibilityRuntime
{
    private const float RefreshInterval = 0.25f;
    private const float TargetHeightOffset = 0.35f;
    private const float OcclusionTolerance = 0.15f;
    private const float FirstPersonIndicatorScale = 0.3f;
    private const float MinimumIndicatorDepth = 1.25f;
    private const float TopStaminaOffset = 32f;
    private const int OcclusionHitCapacity = 64;

    private static readonly Vector2 StaminaCanvasOffset = new(250f, 277f);

    private static readonly Dictionary<IntPtr, OutlineState> SuppressedOutlines = new();
    private static readonly Dictionary<IntPtr, RendererState> HiddenLocalRenderers = new();
    private static readonly Dictionary<IntPtr, GameObjectState> HiddenLocalIdentityObjects = new();
    private static readonly Dictionary<IntPtr, TaskIconState> HiddenTaskIcons = new();
    private static readonly Dictionary<IntPtr, GeneratorCostState> HiddenGeneratorCosts = new();
    private static readonly Dictionary<IntPtr, InputCircleState> HiddenInputCircles = new();
    private static readonly Dictionary<IntPtr, WallCircleState> SuppressedWallCircles = new();
    private static readonly Dictionary<IntPtr, IndicatorTransformState> ScaledIndicators = new();
    private static readonly Dictionary<IntPtr, CanvasRendererState> CulledStaminaRenderers = new();
    private static readonly Il2CppStructArray<RaycastHit> OcclusionHits = new(OcclusionHitCapacity);
    private static float _nextRefreshAt;
    private static float _nextLocalRendererRefreshAt;
    private static float _nextIndicatorRefreshAt;
    private static IntPtr _renderedPlayerPointer;
    private static int _wallLayer = int.MinValue;
    private static StaminaHudState? _staminaHudState;

    public static void SuppressThroughMapOverlays(Camera camera)
    {
        if (Time.unscaledTime < _nextRefreshAt)
        {
            ReapplySuppressedOutlines();
            return;
        }

        _nextRefreshAt = Time.unscaledTime + RefreshInterval;
        foreach (var outline in Resources.FindObjectsOfTypeAll<Outlinable>())
        {
            if (IsLiveSceneObject(outline))
            {
                // The outline package's back pass is the generic through-wall fill path.
                // Disable it for every live outline so an object cannot bypass the targeted
                // hunter/coin lookup by owning its Outlinable elsewhere in the hierarchy.
                TrackOutline(outline, fullySuppress: false);
            }
        }

        foreach (var player in Resources.FindObjectsOfTypeAll<SpookedNetworkPlayer>())
        {
            if (IsLiveSceneObject(player)
                && CharacterTypeExtension.IsSeeker(player.CharacterType))
            {
                foreach (var outline in player.GetComponentsInChildren<Outlinable>(true))
                {
                    TrackOutline(outline, fullySuppress: true);
                }
            }
        }

        foreach (var coin in Resources.FindObjectsOfTypeAll<CollectableCoin>())
        {
            if (!IsLiveSceneObject(coin))
            {
                continue;
            }

            TrackOutline(coin._standardCoinOutline, fullySuppress: true);
            TrackOutline(coin._notMyCoinOutline, fullySuppress: true);
        }

        ReapplySuppressedOutlines();
    }

    public static void HideLocalPlayerVisuals(SpookedNetworkPlayer localPlayer)
    {
        if (_renderedPlayerPointer != IntPtr.Zero
            && _renderedPlayerPointer != localPlayer.Pointer)
        {
            RestoreLocalPlayerVisuals();
        }

        _renderedPlayerPointer = localPlayer.Pointer;
        HideLocalPlayerIdentity(localPlayer);
        if (Time.unscaledTime < _nextLocalRendererRefreshAt)
        {
            ReapplyHiddenLocalRenderers();
            ReapplyHiddenLocalIdentityObjects();
            return;
        }

        _nextLocalRendererRefreshAt = Time.unscaledTime + RefreshInterval;
        var character = localPlayer.GetComponentInChildren<PlayerCharacterPrefabController>(true);
        if (character is null || character.Pointer == IntPtr.Zero)
        {
            ReapplyHiddenLocalRenderers();
            ReapplyHiddenLocalIdentityObjects();
            return;
        }

        foreach (var partReference in character._partReferences)
        {
            if (partReference is null
                || partReference.Pointer == IntPtr.Zero
                || !IsHeadPart(partReference.PartType))
            {
                continue;
            }

            TrackAndHideLocalRenderer(partReference.SkinnedMeshRenderer);
        }
        TrackAndHideLocalRenderer(
            character.CurrentHead?.CustomizablePartSettings?.SkinnedMeshRenderer);

        ReapplyHiddenLocalRenderers();
        ReapplyHiddenLocalIdentityObjects();
    }

    public static void UpdateFirstPersonIndicators(
        SpookedNetworkPlayer localPlayer,
        Camera camera,
        GameView? gameView)
    {
        UpdateStaminaHud(localPlayer, gameView);

        if (Time.unscaledTime >= _nextIndicatorRefreshAt)
        {
            _nextIndicatorRefreshAt = Time.unscaledTime + RefreshInterval;
            foreach (var interactionView in Resources.FindObjectsOfTypeAll<InteractionView>())
            {
                if (IsLiveSceneObject(interactionView))
                {
                    TrackIndicatorTransform(interactionView.Transform);
                }
            }
        }

        ReapplyFirstPersonIndicators(camera);
    }

    public static void UpdateStaminaHud(
        SpookedNetworkPlayer localPlayer,
        GameView? gameView)
    {
        var entityCanvas = localPlayer.EntityCanvasComponent;
        var sourceCanvas = entityCanvas?._staminaCanvasGroup;
        var staminaBar = gameView?._staminaBar;
        if (gameView is null
            || gameView.Pointer == IntPtr.Zero
            || sourceCanvas is null
            || sourceCanvas.Pointer == IntPtr.Zero
            || staminaBar is null
            || staminaBar.Pointer == IntPtr.Zero)
        {
            return;
        }

        var barRect = staminaBar.transform.TryCast<RectTransform>();
        if (barRect is null
            || barRect.Pointer == IntPtr.Zero)
        {
            return;
        }

        var visualCanvasRect = gameView._staminaBarImage?.transform.parent?.TryCast<RectTransform>();
        var screenCanvasRect = FindScreenCanvasRect(barRect);
        if (visualCanvasRect is null
            || visualCanvasRect.Pointer == IntPtr.Zero
            || screenCanvasRect is null
            || screenCanvasRect.Pointer == IntPtr.Zero)
        {
            return;
        }

        if (_staminaHudState is null
            || _staminaHudState.PlayerPointer != localPlayer.Pointer
            || _staminaHudState.SourceCanvas.Pointer != sourceCanvas.Pointer
            || _staminaHudState.BarRect.Pointer != barRect.Pointer
            || _staminaHudState.VisualCanvasState.Rect.Pointer != visualCanvasRect.Pointer
            || _staminaHudState.ScreenCanvasRect.Pointer != screenCanvasRect.Pointer)
        {
            RestoreStaminaHud();
            _staminaHudState = CreateStaminaHudState(
                localPlayer.Pointer,
                sourceCanvas,
                barRect,
                visualCanvasRect,
                screenCanvasRect,
                gameView._staminaBarPressSpaceForPossessedItem);
        }

        CullWorldStaminaCircle(sourceCanvas);
        ApplyTopStaminaLayout(_staminaHudState, sourceCanvas);
    }

    public static void UpdateTaskIconVisibility(TaskIconView taskIcon, Camera? camera)
    {
        var canvas = taskIcon._viewCanvas;
        if (canvas is null || canvas.Pointer == IntPtr.Zero)
        {
            return;
        }

        var task = taskIcon._spookedTask;
        var taskTransform = task?.Transform;
        if (!taskIcon.Active
            || taskTransform is null
            || taskTransform.Pointer == IntPtr.Zero)
        {
            HiddenTaskIcons.Remove(canvas.Pointer);
            return;
        }

        if (!IsWorldPositionOccluded(camera, taskTransform.position))
        {
            RestoreTaskIcon(canvas);
            return;
        }

        if (!HiddenTaskIcons.ContainsKey(canvas.Pointer))
        {
            HiddenTaskIcons.Add(canvas.Pointer, new TaskIconState(canvas, canvas.enabled));
        }

        canvas.enabled = false;
    }

    public static void UpdateItemGeneratorCostVisibility(ItemGenerator generator, Camera? camera)
    {
        var canvas = generator._costViewCanvas;
        var target = generator._costViewTransform;
        if (canvas is null
            || canvas.Pointer == IntPtr.Zero
            || target is null
            || target.Pointer == IntPtr.Zero)
        {
            return;
        }

        if (!IsWorldPositionOccluded(camera, target.position))
        {
            RestoreGeneratorCost(canvas);
            return;
        }

        if (!HiddenGeneratorCosts.ContainsKey(canvas.Pointer))
        {
            HiddenGeneratorCosts.Add(
                canvas.Pointer,
                new GeneratorCostState(canvas, canvas.enabled));
        }

        canvas.enabled = false;
    }

    public static bool IsWorldPositionOccluded(Camera? camera, Vector3 position)
    {
        if (camera is null || camera.Pointer == IntPtr.Zero)
        {
            return false;
        }

        var origin = camera.transform.position + camera.transform.forward * 0.15f;
        var target = position + Vector3.up * TargetHeightOffset;
        var toTarget = target - origin;
        var distance = toTarget.magnitude;
        if (distance <= OcclusionTolerance)
        {
            return false;
        }

        var hitCount = Physics.RaycastNonAlloc(
            origin,
            toTarget / distance,
            OcclusionHits,
            distance - OcclusionTolerance,
            ~0,
            QueryTriggerInteraction.Ignore);
        for (var index = 0; index < hitCount; index++)
        {
            var collider = OcclusionHits[index].collider;
            if (IsWallOccluder(collider))
            {
                return true;
            }
        }

        return false;
    }

    public static void SuppressWallCircle(InvisibleWallsManager manager)
    {
        if (!SuppressedWallCircles.ContainsKey(manager.Pointer))
        {
            SuppressedWallCircles.Add(
                manager.Pointer,
                new WallCircleState(manager, manager._ellipseSize));
        }

        manager._ellipseSize = 0f;
    }

    public static void UpdateLabyrinthInputCircle(InputActionCircle inputCircle, bool hide)
    {
        var canvas = inputCircle.Canvas;
        if (canvas is null || canvas.Pointer == IntPtr.Zero)
        {
            return;
        }

        if (!hide)
        {
            if (HiddenInputCircles.Remove(canvas.Pointer, out var state))
            {
                canvas.enabled = state.CanvasWasEnabled;
            }

            return;
        }

        if (!HiddenInputCircles.ContainsKey(canvas.Pointer))
        {
            HiddenInputCircles.Add(
                canvas.Pointer,
                new InputCircleState(canvas, canvas.enabled));
        }

        canvas.enabled = false;
    }

    public static void RestoreThroughMapOverlays()
    {
        foreach (var state in SuppressedOutlines.Values)
        {
            try
            {
                if (state.Outline is not null
                    && state.Outline.Pointer != IntPtr.Zero)
                {
                    SetOutlinePropertyEnabled(
                        state.Outline.OutlineParameters,
                        state.OutlinePassWasEnabled);
                    SetOutlinePropertyEnabled(
                        state.Outline.BackParameters,
                        state.BackPassWasEnabled);
                    SetOutlinePropertyEnabled(
                        state.Outline.FrontParameters,
                        state.FrontPassWasEnabled);
                    state.Outline.enabled = state.OutlineWasEnabled;
                }
            }
            catch
            {
                // Scene teardown can invalidate native objects between Pointer and property access.
            }
        }

        SuppressedOutlines.Clear();
        RestoreLocalPlayerVisuals();
        RestoreTaskIcons();
        RestoreGeneratorCosts();
        RestoreInputCircles();
        RestoreWallCircles();
        RestoreIndicatorScales();
        _nextRefreshAt = 0f;
    }

    public static void RestoreStaminaHudOverride()
    {
        RestoreStaminaHud();
    }

    private static void TrackOutline(Outlinable? outline, bool fullySuppress)
    {
        if (outline is null
            || outline.Pointer == IntPtr.Zero)
        {
            return;
        }

        if (!SuppressedOutlines.TryGetValue(outline.Pointer, out var state))
        {
            state = new OutlineState(
                outline,
                GetOutlinePropertyEnabled(outline.OutlineParameters),
                GetOutlinePropertyEnabled(outline.BackParameters),
                GetOutlinePropertyEnabled(outline.FrontParameters),
                outline.enabled);
            SuppressedOutlines.Add(outline.Pointer, state);
        }

        state.FullySuppress |= fullySuppress;
        ApplyOutlinePolicy(state);
    }

    private static void ReapplySuppressedOutlines()
    {
        foreach (var state in SuppressedOutlines.Values)
        {
            try
            {
                if (state.Outline is not null
                    && state.Outline.Pointer != IntPtr.Zero)
                {
                    ApplyOutlinePolicy(state);
                }
            }
            catch
            {
                // Stale entries are discarded on the next scene/engagement reset.
            }
        }
    }

    private static void ReapplyHiddenLocalRenderers()
    {
        foreach (var state in HiddenLocalRenderers.Values)
        {
            try
            {
                if (state.Renderer is not null && state.Renderer.Pointer != IntPtr.Zero)
                {
                    state.Renderer.forceRenderingOff = true;
                }
            }
            catch
            {
                // Character refreshes can invalidate renderers until the next scan.
            }
        }
    }

    private static bool IsHeadPart(KinguinBasePartType partType)
    {
        return partType is KinguinBasePartType.Head
            or KinguinBasePartType.Beak
            or KinguinBasePartType.LFace
            or KinguinBasePartType.RFace
            or KinguinBasePartType.EyeDeathL
            or KinguinBasePartType.EyeDeathR
            or KinguinBasePartType.EyeHappyL
            or KinguinBasePartType.EyeHappyR
            or KinguinBasePartType.EyeMadL
            or KinguinBasePartType.EyeMadR
            or KinguinBasePartType.EyeNormalL
            or KinguinBasePartType.EyeNormalR
            or KinguinBasePartType.EyeScaredL
            or KinguinBasePartType.EyeScaredR;
    }

    private static void TrackAndHideLocalRenderer(Renderer? renderer)
    {
        if (renderer is null || renderer.Pointer == IntPtr.Zero)
        {
            return;
        }

        if (!HiddenLocalRenderers.ContainsKey(renderer.Pointer))
        {
            HiddenLocalRenderers.Add(
                renderer.Pointer,
                new RendererState(renderer, renderer.forceRenderingOff));
        }

        renderer.forceRenderingOff = true;
    }

    private static void HideLocalPlayerIdentity(SpookedNetworkPlayer localPlayer)
    {
        var canvas = localPlayer.EntityCanvasComponent;
        if (canvas is null || canvas.Pointer == IntPtr.Zero)
        {
            return;
        }

        TrackAndHideLocalIdentityObject(canvas._nickNameLabel?.gameObject);
        TrackAndHideLocalIdentityObject(canvas._avatarBackgroundObject);
        TrackAndHideLocalIdentityObject(canvas._levelText?.gameObject);
        TrackAndHideLocalIdentityObject(canvas._title?.gameObject);
    }

    private static void TrackAndHideLocalIdentityObject(GameObject? identityObject)
    {
        if (identityObject is null || identityObject.Pointer == IntPtr.Zero)
        {
            return;
        }

        if (!HiddenLocalIdentityObjects.ContainsKey(identityObject.Pointer))
        {
            HiddenLocalIdentityObjects.Add(
                identityObject.Pointer,
                new GameObjectState(identityObject, identityObject.activeSelf));
        }

        identityObject.SetActive(false);
    }

    private static void ReapplyHiddenLocalIdentityObjects()
    {
        foreach (var state in HiddenLocalIdentityObjects.Values)
        {
            try
            {
                if (state.Object is not null && state.Object.Pointer != IntPtr.Zero)
                {
                    state.Object.SetActive(false);
                }
            }
            catch
            {
                // Canvas refreshes can invalidate identity objects until the next scan.
            }
        }
    }

    private static void RestoreLocalPlayerVisuals()
    {
        foreach (var state in HiddenLocalRenderers.Values)
        {
            try
            {
                if (state.Renderer is not null && state.Renderer.Pointer != IntPtr.Zero)
                {
                    state.Renderer.forceRenderingOff = state.ForceRenderingOff;
                }
            }
            catch
            {
                // Scene teardown can invalidate native renderers.
            }
        }

        HiddenLocalRenderers.Clear();
        foreach (var state in HiddenLocalIdentityObjects.Values)
        {
            try
            {
                if (state.Object is not null && state.Object.Pointer != IntPtr.Zero)
                {
                    state.Object.SetActive(state.WasActive);
                }
            }
            catch
            {
                // Scene teardown can invalidate identity objects.
            }
        }

        HiddenLocalIdentityObjects.Clear();
        _renderedPlayerPointer = IntPtr.Zero;
        _nextLocalRendererRefreshAt = 0f;
    }

    private static void RestoreTaskIcon(Canvas canvas)
    {
        if (!HiddenTaskIcons.Remove(canvas.Pointer, out var state))
        {
            return;
        }

        canvas.enabled = state.CanvasWasEnabled;
    }

    private static void RestoreTaskIcons()
    {
        foreach (var state in HiddenTaskIcons.Values)
        {
            try
            {
                if (state.Canvas is not null && state.Canvas.Pointer != IntPtr.Zero)
                {
                    state.Canvas.enabled = state.CanvasWasEnabled;
                }
            }
            catch
            {
                // Scene teardown can invalidate native canvases.
            }
        }

        HiddenTaskIcons.Clear();
    }

    private static void RestoreGeneratorCost(Canvas canvas)
    {
        if (!HiddenGeneratorCosts.Remove(canvas.Pointer, out var state))
        {
            return;
        }

        canvas.enabled = state.CanvasWasEnabled;
    }

    private static void RestoreGeneratorCosts()
    {
        foreach (var state in HiddenGeneratorCosts.Values)
        {
            try
            {
                if (state.Canvas is not null && state.Canvas.Pointer != IntPtr.Zero)
                {
                    state.Canvas.enabled = state.CanvasWasEnabled;
                }
            }
            catch
            {
                // Scene teardown can invalidate native canvases.
            }
        }

        HiddenGeneratorCosts.Clear();
    }

    private static void RestoreInputCircles()
    {
        foreach (var state in HiddenInputCircles.Values)
        {
            try
            {
                if (state.Canvas is not null && state.Canvas.Pointer != IntPtr.Zero)
                {
                    state.Canvas.enabled = state.CanvasWasEnabled;
                }
            }
            catch
            {
                // Scene teardown can invalidate native canvases.
            }
        }

        HiddenInputCircles.Clear();
    }

    private static void RestoreWallCircles()
    {
        foreach (var state in SuppressedWallCircles.Values)
        {
            try
            {
                if (state.Manager is not null && state.Manager.Pointer != IntPtr.Zero)
                {
                    state.Manager._ellipseSize = state.CurrentEllipseSize;
                }
            }
            catch
            {
                // Scene teardown can invalidate the wall manager.
            }
        }

        SuppressedWallCircles.Clear();
    }

    private static StaminaHudState CreateStaminaHudState(
        IntPtr playerPointer,
        CanvasGroup sourceCanvas,
        RectTransform barRect,
        RectTransform visualCanvasRect,
        RectTransform screenCanvasRect,
        GameObject? spaceIcon)
    {
        var barObject = barRect.gameObject;
        var barCanvasGroup = barObject.GetComponent<CanvasGroup>();
        var canvasGroupWasAdded = barCanvasGroup is null
            || barCanvasGroup.Pointer == IntPtr.Zero;
        if (canvasGroupWasAdded)
        {
            barCanvasGroup = barObject.AddComponent<CanvasGroup>();
        }

        var rectSize = barRect.rect.size;
        if (rectSize.x <= 1f || rectSize.y <= 1f)
        {
            rectSize = barRect.sizeDelta;
        }
        if (rectSize.x <= 1f || rectSize.y <= 1f)
        {
            rectSize = new Vector2(360f, 32f);
        }

        var visualStates = new List<StaminaVisualState>();
        for (var index = 0; index < visualCanvasRect.childCount; index++)
        {
            var visualRect = visualCanvasRect.GetChild(index).TryCast<RectTransform>();
            if (visualRect is not null && visualRect.Pointer != IntPtr.Zero)
            {
                visualStates.Add(CaptureStaminaVisualState(visualRect));
            }
        }

        return new StaminaHudState(
            playerPointer,
            sourceCanvas,
            barRect,
            CaptureStaminaVisualState(visualCanvasRect),
            visualStates,
            screenCanvasRect,
            barRect.parent,
            barRect.GetSiblingIndex(),
            barRect.anchorMin,
            barRect.anchorMax,
            barRect.pivot,
            barRect.anchoredPosition,
            barRect.sizeDelta,
            barRect.localScale,
            barRect.localRotation,
            rectSize,
            barCanvasGroup!,
            canvasGroupWasAdded,
            barCanvasGroup!.alpha,
            barCanvasGroup.interactable,
            barCanvasGroup.blocksRaycasts,
            barObject.activeSelf,
            spaceIcon,
            spaceIcon is not null
                && spaceIcon.Pointer != IntPtr.Zero
                && spaceIcon.activeSelf);
    }

    private static StaminaVisualState CaptureStaminaVisualState(RectTransform rect)
    {
        return new StaminaVisualState(
            rect,
            rect.anchorMin,
            rect.anchorMax,
            rect.pivot,
            rect.anchoredPosition,
            rect.sizeDelta,
            rect.localScale,
            rect.localRotation);
    }

    private static RectTransform? FindScreenCanvasRect(RectTransform barRect)
    {
        var current = barRect.parent;
        for (var depth = 0; depth < 12 && current is not null; depth++)
        {
            var canvas = current.GetComponent<Canvas>();
            if (canvas is not null
                && canvas.Pointer != IntPtr.Zero
                && canvas.renderMode != RenderMode.WorldSpace)
            {
                return canvas.transform.TryCast<RectTransform>();
            }

            current = current.parent;
        }

        return barRect.parent?.TryCast<RectTransform>();
    }

    private static void CullWorldStaminaCircle(CanvasGroup sourceCanvas)
    {
        foreach (var canvasRenderer in sourceCanvas.GetComponentsInChildren<CanvasRenderer>(true))
        {
            if (canvasRenderer is null || canvasRenderer.Pointer == IntPtr.Zero)
            {
                continue;
            }

            if (!CulledStaminaRenderers.ContainsKey(canvasRenderer.Pointer))
            {
                CulledStaminaRenderers.Add(
                    canvasRenderer.Pointer,
                    new CanvasRendererState(canvasRenderer, canvasRenderer.cull));
            }

            canvasRenderer.cull = true;
        }

        foreach (var state in CulledStaminaRenderers.Values)
        {
            try
            {
                if (state.Renderer is not null && state.Renderer.Pointer != IntPtr.Zero)
                {
                    state.Renderer.cull = true;
                }
            }
            catch
            {
                // Player-canvas rebuilds can invalidate individual renderers.
            }
        }
    }

    private static void ApplyTopStaminaLayout(
        StaminaHudState state,
        CanvasGroup sourceCanvas)
    {
        var barRect = state.BarRect;
        var targetParent = state.ScreenCanvasRect;
        if (targetParent is not null
            && targetParent.Pointer != IntPtr.Zero
            && (barRect.parent is null
                || barRect.parent.Pointer != targetParent.Pointer))
        {
            barRect.SetParent(targetParent, false);
        }

        barRect.anchorMin = new Vector2(0.5f, 1f);
        barRect.anchorMax = new Vector2(0.5f, 1f);
        barRect.pivot = new Vector2(0.5f, 1f);
        barRect.anchoredPosition = new Vector2(0f, -TopStaminaOffset);
        barRect.sizeDelta = state.TopSize;
        barRect.localScale = state.OriginalLocalScale;
        barRect.localRotation = Quaternion.identity;

        var visualCanvasRect = state.VisualCanvasState.Rect;
        visualCanvasRect.anchorMin = Vector2.zero;
        visualCanvasRect.anchorMax = Vector2.one;
        visualCanvasRect.pivot = new Vector2(0.5f, 0.5f);
        visualCanvasRect.anchoredPosition = Vector2.zero;
        visualCanvasRect.sizeDelta = Vector2.zero;
        visualCanvasRect.localScale = state.VisualCanvasState.OriginalLocalScale;
        visualCanvasRect.localRotation = Quaternion.identity;

        var screenBounds = state.ScreenCanvasRect.rect;
        var referencePosition = Vector2.zero;
        var hasReference = false;
        foreach (var visualState in state.VisualStates)
        {
            if (string.Equals(visualState.Rect.name, "Background", StringComparison.Ordinal))
            {
                referencePosition = GetOriginalCanvasPosition(visualState, screenBounds);
                hasReference = true;
                break;
            }
        }

        if (!hasReference && state.VisualStates.Count > 0)
        {
            referencePosition = GetOriginalCanvasPosition(state.VisualStates[0], screenBounds);
        }

        var targetReferencePosition = new Vector2(
            StaminaCanvasOffset.x,
            -state.TopSize.y * 0.5f + StaminaCanvasOffset.y);
        foreach (var visualState in state.VisualStates)
        {
            var visualRect = visualState.Rect;
            if (visualRect is not null && visualRect.Pointer != IntPtr.Zero)
            {
                var originalPosition = GetOriginalCanvasPosition(visualState, screenBounds);
                visualRect.anchorMin = new Vector2(0.5f, 1f);
                visualRect.anchorMax = new Vector2(0.5f, 1f);
                visualRect.pivot = visualState.OriginalPivot;
                visualRect.anchoredPosition = targetReferencePosition
                    + originalPosition
                    - referencePosition;
                visualRect.sizeDelta = visualState.OriginalSizeDelta;
                visualRect.localScale = visualState.OriginalLocalScale;
                visualRect.localRotation = visualState.OriginalLocalRotation;
            }
        }

        var barObject = barRect.gameObject;
        if (!barObject.activeSelf)
        {
            barObject.SetActive(true);
        }

        state.BarCanvasGroup.alpha = sourceCanvas.gameObject.activeInHierarchy
            ? Mathf.Clamp01(sourceCanvas.alpha)
            : 0f;
        state.BarCanvasGroup.interactable = false;
        state.BarCanvasGroup.blocksRaycasts = false;

        var spaceIcon = state.SpaceIcon;
        if (spaceIcon is not null
            && spaceIcon.Pointer != IntPtr.Zero
            && spaceIcon.activeSelf)
        {
            spaceIcon.SetActive(false);
        }
    }

    private static Vector2 GetOriginalCanvasPosition(
        StaminaVisualState state,
        Rect screenBounds)
    {
        var anchor = new Vector2(
            Mathf.Lerp(state.OriginalAnchorMin.x, state.OriginalAnchorMax.x, state.OriginalPivot.x),
            Mathf.Lerp(state.OriginalAnchorMin.y, state.OriginalAnchorMax.y, state.OriginalPivot.y));
        return new Vector2(
            Mathf.Lerp(screenBounds.xMin, screenBounds.xMax, anchor.x),
            Mathf.Lerp(screenBounds.yMin, screenBounds.yMax, anchor.y))
            + state.OriginalAnchoredPosition;
    }

    private static void RestoreStaminaHud()
    {
        foreach (var state in CulledStaminaRenderers.Values)
        {
            try
            {
                if (state.Renderer is not null && state.Renderer.Pointer != IntPtr.Zero)
                {
                    state.Renderer.cull = state.WasCulled;
                }
            }
            catch
            {
                // Scene teardown can invalidate stamina renderers.
            }
        }

        CulledStaminaRenderers.Clear();
        var hudState = _staminaHudState;
        _staminaHudState = null;
        if (hudState is null)
        {
            return;
        }

        try
        {
            var barRect = hudState.BarRect;
            if (barRect is null || barRect.Pointer == IntPtr.Zero)
            {
                return;
            }

            var originalParent = hudState.OriginalParent;
            if (originalParent is not null && originalParent.Pointer != IntPtr.Zero)
            {
                barRect.SetParent(originalParent, false);
                barRect.SetSiblingIndex(hudState.OriginalSiblingIndex);
            }

            barRect.anchorMin = hudState.OriginalAnchorMin;
            barRect.anchorMax = hudState.OriginalAnchorMax;
            barRect.pivot = hudState.OriginalPivot;
            barRect.anchoredPosition = hudState.OriginalAnchoredPosition;
            barRect.sizeDelta = hudState.OriginalSizeDelta;
            barRect.localScale = hudState.OriginalLocalScale;
            barRect.localRotation = hudState.OriginalLocalRotation;

            RestoreStaminaVisualState(hudState.VisualCanvasState);
            foreach (var visualState in hudState.VisualStates)
            {
                RestoreStaminaVisualState(visualState);
            }

            var barCanvasGroup = hudState.BarCanvasGroup;
            if (barCanvasGroup is not null && barCanvasGroup.Pointer != IntPtr.Zero)
            {
                if (hudState.CanvasGroupWasAdded)
                {
                    UnityEngine.Object.Destroy(barCanvasGroup);
                }
                else
                {
                    barCanvasGroup.alpha = hudState.OriginalAlpha;
                    barCanvasGroup.interactable = hudState.OriginalInteractable;
                    barCanvasGroup.blocksRaycasts = hudState.OriginalBlocksRaycasts;
                }
            }

            barRect.gameObject.SetActive(hudState.OriginalActiveSelf);

            var spaceIcon = hudState.SpaceIcon;
            if (spaceIcon is not null && spaceIcon.Pointer != IntPtr.Zero)
            {
                spaceIcon.SetActive(hudState.OriginalSpaceIconActiveSelf);
            }
        }
        catch
        {
            // The original HUD hierarchy may already be gone during scene teardown.
        }
    }

    private static void RestoreStaminaVisualState(StaminaVisualState state)
    {
        var rect = state.Rect;
        if (rect is null || rect.Pointer == IntPtr.Zero)
        {
            return;
        }

        rect.anchorMin = state.OriginalAnchorMin;
        rect.anchorMax = state.OriginalAnchorMax;
        rect.pivot = state.OriginalPivot;
        rect.anchoredPosition = state.OriginalAnchoredPosition;
        rect.sizeDelta = state.OriginalSizeDelta;
        rect.localScale = state.OriginalLocalScale;
        rect.localRotation = state.OriginalLocalRotation;
    }

    private static void TrackIndicatorTransform(Transform? indicatorTransform)
    {
        if (indicatorTransform is null || indicatorTransform.Pointer == IntPtr.Zero)
        {
            return;
        }

        if (!ScaledIndicators.ContainsKey(indicatorTransform.Pointer))
        {
            ScaledIndicators.Add(
                indicatorTransform.Pointer,
                new IndicatorTransformState(
                    indicatorTransform,
                    indicatorTransform.localScale));
        }
    }

    private static void ReapplyFirstPersonIndicators(Camera camera)
    {
        var cameraTransform = camera.transform;
        var cameraPosition = cameraTransform.position;
        var cameraForward = cameraTransform.forward;
        foreach (var state in ScaledIndicators.Values)
        {
            try
            {
                var indicatorTransform = state.Transform;
                if (indicatorTransform is null || indicatorTransform.Pointer == IntPtr.Zero)
                {
                    continue;
                }

                indicatorTransform.localScale = state.LocalScale * FirstPersonIndicatorScale;
                if (!indicatorTransform.gameObject.activeInHierarchy)
                {
                    continue;
                }

                var depth = Vector3.Dot(
                    indicatorTransform.position - cameraPosition,
                    cameraForward);
                if (depth < MinimumIndicatorDepth)
                {
                    indicatorTransform.position += cameraForward * (MinimumIndicatorDepth - depth);
                }
            }
            catch
            {
                // Task transitions can invalidate world-space UI between frames.
            }
        }
    }

    private static void RestoreIndicatorScales()
    {
        foreach (var state in ScaledIndicators.Values)
        {
            try
            {
                if (state.Transform is not null && state.Transform.Pointer != IntPtr.Zero)
                {
                    state.Transform.localScale = state.LocalScale;
                }
            }
            catch
            {
                // Scene teardown can invalidate world-space UI transforms.
            }
        }

        ScaledIndicators.Clear();
        _nextIndicatorRefreshAt = 0f;
    }

    private static void ApplyOutlinePolicy(OutlineState state)
    {
        var outline = state.Outline;
        SetOutlinePropertyEnabled(outline.BackParameters, false);
        if (!state.FullySuppress)
        {
            return;
        }

        SetOutlinePropertyEnabled(outline.OutlineParameters, false);
        SetOutlinePropertyEnabled(outline.FrontParameters, false);
        var activeOutlines = Outlinable.outlinables;
        if (activeOutlines is not null)
        {
            activeOutlines.Remove(outline);
        }

        outline.enabled = false;
    }

    private static bool IsWallOccluder(Collider? collider)
    {
        if (collider is null || collider.Pointer == IntPtr.Zero)
        {
            return false;
        }

        if (_wallLayer == int.MinValue)
        {
            _wallLayer = LayerMask.NameToLayer("Wall");
        }

        var colliderObject = collider.gameObject;
        if (_wallLayer >= 0 && colliderObject.layer == _wallLayer)
        {
            return true;
        }

        // Authored maps reuse Environment and room layers for both furniture and walls.
        // Their wall colliders are nevertheless consistently named Wall*/Door* in the
        // collider object or an ancestor, while tables/stalls use generic environment names.
        var current = collider.transform;
        for (var depth = 0; depth < 8 && current is not null; depth++)
        {
            var objectName = current.name;
            if (objectName.Contains("wall", StringComparison.OrdinalIgnoreCase)
                || objectName.Contains("door", StringComparison.OrdinalIgnoreCase)
                || objectName.Contains("labyrinth_collision", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private static bool GetOutlinePropertyEnabled(Outlinable.OutlineProperties? properties)
    {
        return properties is not null
            && properties.Pointer != IntPtr.Zero
            && properties.Enabled;
    }

    private static void SetOutlinePropertyEnabled(
        Outlinable.OutlineProperties? properties,
        bool enabled)
    {
        if (properties is not null && properties.Pointer != IntPtr.Zero)
        {
            properties.Enabled = enabled;
        }
    }

    private static bool IsLiveSceneObject(Component? component)
    {
        return component is not null
            && component.Pointer != IntPtr.Zero
            && component.gameObject.scene.IsValid();
    }

    private sealed class OutlineState
    {
        public OutlineState(
            Outlinable outline,
            bool outlinePassWasEnabled,
            bool backPassWasEnabled,
            bool frontPassWasEnabled,
            bool outlineWasEnabled)
        {
            Outline = outline;
            OutlinePassWasEnabled = outlinePassWasEnabled;
            BackPassWasEnabled = backPassWasEnabled;
            FrontPassWasEnabled = frontPassWasEnabled;
            OutlineWasEnabled = outlineWasEnabled;
        }

        public Outlinable Outline { get; }

        public bool OutlinePassWasEnabled { get; }

        public bool BackPassWasEnabled { get; }

        public bool FrontPassWasEnabled { get; }

        public bool OutlineWasEnabled { get; }

        public bool FullySuppress { get; set; }
    }

    private sealed record RendererState(Renderer Renderer, bool ForceRenderingOff);

    private sealed record GameObjectState(GameObject Object, bool WasActive);

    private sealed record TaskIconState(Canvas Canvas, bool CanvasWasEnabled);

    private sealed record GeneratorCostState(Canvas Canvas, bool CanvasWasEnabled);

    private sealed record InputCircleState(Canvas Canvas, bool CanvasWasEnabled);

    private sealed record WallCircleState(
        InvisibleWallsManager Manager,
        float CurrentEllipseSize);

    private sealed record IndicatorTransformState(
        Transform Transform,
        Vector3 LocalScale);

    private sealed record CanvasRendererState(
        CanvasRenderer Renderer,
        bool WasCulled);

    private sealed record StaminaVisualState(
        RectTransform Rect,
        Vector2 OriginalAnchorMin,
        Vector2 OriginalAnchorMax,
        Vector2 OriginalPivot,
        Vector2 OriginalAnchoredPosition,
        Vector2 OriginalSizeDelta,
        Vector3 OriginalLocalScale,
        Quaternion OriginalLocalRotation);

    private sealed record StaminaHudState(
        IntPtr PlayerPointer,
        CanvasGroup SourceCanvas,
        RectTransform BarRect,
        StaminaVisualState VisualCanvasState,
        IReadOnlyList<StaminaVisualState> VisualStates,
        RectTransform ScreenCanvasRect,
        Transform? OriginalParent,
        int OriginalSiblingIndex,
        Vector2 OriginalAnchorMin,
        Vector2 OriginalAnchorMax,
        Vector2 OriginalPivot,
        Vector2 OriginalAnchoredPosition,
        Vector2 OriginalSizeDelta,
        Vector3 OriginalLocalScale,
        Quaternion OriginalLocalRotation,
        Vector2 TopSize,
        CanvasGroup BarCanvasGroup,
        bool CanvasGroupWasAdded,
        float OriginalAlpha,
        bool OriginalInteractable,
        bool OriginalBlocksRaycasts,
        bool OriginalActiveSelf,
        GameObject? SpaceIcon,
        bool OriginalSpaceIconActiveSelf);
}
