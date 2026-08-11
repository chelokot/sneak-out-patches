using UI.Interactions;
using UnityEngine;
using UnityEngine.UI;

namespace SneakOut.FirstPersonExperiment;

internal static class FirstPersonPaintingTaskUiRuntime
{
    private static readonly Vector2 ReferenceResolution = new(1920f, 1080f);
    private static readonly Vector2 HudPanelSize = new(640f, 240f);

    private static Canvas? _overlayCanvas;
    private static PaintingTaskUiState? _state;

    public static void Update(MustacheTaskInteractionView view, bool activeForLocalPlayer)
    {
        if (!activeForLocalPlayer)
        {
            if (_state?.View.Pointer == view.Pointer)
            {
                Restore();
            }

            return;
        }

        var contentTransform = view.Transform;
        var viewCanvas = view.Canvas;
        if (contentTransform is null
            || contentTransform.Pointer == IntPtr.Zero
            || viewCanvas is null
            || viewCanvas.Pointer == IntPtr.Zero)
        {
            return;
        }

        var canvasTransform = viewCanvas.transform;
        if (canvasTransform is null || canvasTransform.Pointer == IntPtr.Zero)
        {
            return;
        }

        var overlayCanvas = EnsureOverlayCanvas();
        if (overlayCanvas is null)
        {
            return;
        }

        if (_state is null
            || _state.View.Pointer != view.Pointer
            || _state.Root.Transform.Pointer != canvasTransform.Pointer
            || _state.Content.Transform.Pointer != contentTransform.Pointer)
        {
            Restore();
            var rootState = CaptureTransform(canvasTransform, HudPanelSize);
            var keyPrompt = contentTransform.Find("MustacheTask_Bar/Panel/IndicatorPivot/Key");
            _state = new PaintingTaskUiState(
                view,
                rootState,
                CaptureTransform(contentTransform, HudPanelSize),
                CalculateHudScale(rootState.AuthoredSize),
                keyPrompt,
                keyPrompt is not null
                    && keyPrompt.Pointer != IntPtr.Zero
                    && keyPrompt.gameObject.activeSelf,
                viewCanvas,
                viewCanvas.enabled,
                viewCanvas.renderMode,
                viewCanvas.worldCamera,
                viewCanvas.overrideSorting,
                viewCanvas.sortingOrder);
        }

        Apply(_state, overlayCanvas);
    }

    public static void Reapply()
    {
        var state = _state;
        if (state is null)
        {
            return;
        }

        var overlayCanvas = EnsureOverlayCanvas();
        if (overlayCanvas is not null)
        {
            Apply(state, overlayCanvas);
        }
    }

    public static void Restore()
    {
        var state = _state;
        _state = null;
        if (state is not null)
        {
            try
            {
                RestoreTransform(state.Root, true);
                if (state.Content.Transform.Pointer != state.Root.Transform.Pointer)
                {
                    RestoreTransform(state.Content, false);
                }

                if (state.KeyPrompt is not null && state.KeyPrompt.Pointer != IntPtr.Zero)
                {
                    state.KeyPrompt.gameObject.SetActive(state.OriginalKeyPromptActive);
                }

                if (state.Canvas is not null && state.Canvas.Pointer != IntPtr.Zero)
                {
                    state.Canvas.renderMode = state.OriginalRenderMode;
                    state.Canvas.worldCamera = state.OriginalWorldCamera;
                    state.Canvas.overrideSorting = state.OriginalOverrideSorting;
                    state.Canvas.sortingOrder = state.OriginalSortingOrder;
                    state.Canvas.enabled = state.OriginalCanvasEnabled;
                }
            }
            catch
            {
                // Scene teardown can invalidate the original world-space hierarchy.
            }
        }

        try
        {
            if (_overlayCanvas is not null
                && _overlayCanvas.Pointer != IntPtr.Zero
                && _overlayCanvas.gameObject.activeSelf)
            {
                _overlayCanvas.gameObject.SetActive(false);
            }
        }
        catch
        {
            _overlayCanvas = null;
        }
    }

    private static TransformState CaptureTransform(Transform transform, Vector2 fallbackSize)
    {
        var rect = transform.TryCast<RectTransform>();
        var authoredSize = rect?.sizeDelta ?? Vector2.zero;
        if (authoredSize.x <= 0.001f || authoredSize.y <= 0.001f)
        {
            authoredSize = rect?.rect.size ?? Vector2.zero;
        }
        if (authoredSize.x <= 0.001f || authoredSize.y <= 0.001f)
        {
            authoredSize = fallbackSize;
        }

        return new TransformState(
            transform,
            rect,
            transform.parent,
            transform.GetSiblingIndex(),
            transform.localPosition,
            transform.localRotation,
            transform.localScale,
            rect?.anchorMin ?? Vector2.zero,
            rect?.anchorMax ?? Vector2.zero,
            rect?.pivot ?? new Vector2(0.5f, 0.5f),
            rect?.anchoredPosition ?? Vector2.zero,
            rect?.sizeDelta ?? Vector2.zero,
            authoredSize);
    }

    private static float CalculateHudScale(Vector2 authoredSize)
    {
        if (authoredSize.x <= 0.001f || authoredSize.y <= 0.001f)
        {
            return 1f;
        }

        return Mathf.Min(
            HudPanelSize.x / authoredSize.x,
            HudPanelSize.y / authoredSize.y);
    }

    private static Canvas? EnsureOverlayCanvas()
    {
        try
        {
            if (_overlayCanvas is not null
                && _overlayCanvas.Pointer != IntPtr.Zero
                && _overlayCanvas)
            {
                return _overlayCanvas;
            }
        }
        catch
        {
            _overlayCanvas = null;
        }

        var overlayObject = new GameObject("FirstPersonPaintingTaskOverlay");
        overlayObject.hideFlags = HideFlags.HideAndDontSave;
        _overlayCanvas = overlayObject.AddComponent<Canvas>();
        _overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _overlayCanvas.overrideSorting = true;
        _overlayCanvas.sortingOrder = 28000;
        _overlayCanvas.pixelPerfect = false;

        var scaler = overlayObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = ReferenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        overlayObject.SetActive(false);
        return _overlayCanvas;
    }

    private static void Apply(PaintingTaskUiState state, Canvas overlayCanvas)
    {
        try
        {
            if (!overlayCanvas.gameObject.activeSelf)
            {
                overlayCanvas.gameObject.SetActive(true);
            }

            var root = state.Root.Transform;
            if (root.parent is null
                || root.parent.Pointer != overlayCanvas.transform.Pointer)
            {
                root.SetParent(overlayCanvas.transform, false);
            }

            ApplyCenteredTransform(state.Root, state.HudScale);
            if (state.Content.Transform.Pointer != root.Pointer)
            {
                ApplyCenteredTransform(state.Content, 1f);
            }

            if (state.KeyPrompt is not null && state.KeyPrompt.Pointer != IntPtr.Zero)
            {
                state.KeyPrompt.gameObject.SetActive(false);
            }

            state.Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            state.Canvas.worldCamera = null;
            state.Canvas.overrideSorting = true;
            state.Canvas.sortingOrder = 1;
            state.Canvas.enabled = true;
        }
        catch
        {
            Restore();
        }
    }

    private static void ApplyCenteredTransform(TransformState state, float scale)
    {
        var transform = state.Transform;
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one * scale;

        var rect = state.Rect;
        if (rect is null || rect.Pointer == IntPtr.Zero)
        {
            return;
        }

        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = state.AuthoredSize;
    }

    private static void RestoreTransform(TransformState state, bool restoreParent)
    {
        var transform = state.Transform;
        if (transform is null || transform.Pointer == IntPtr.Zero)
        {
            return;
        }

        if (restoreParent)
        {
            var originalParent = state.OriginalParent;
            if (originalParent is not null && originalParent.Pointer != IntPtr.Zero)
            {
                transform.SetParent(originalParent, false);
                transform.SetSiblingIndex(state.OriginalSiblingIndex);
            }
        }

        transform.localPosition = state.OriginalLocalPosition;
        transform.localRotation = state.OriginalLocalRotation;
        transform.localScale = state.OriginalLocalScale;

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
    }

    private sealed record PaintingTaskUiState(
        MustacheTaskInteractionView View,
        TransformState Root,
        TransformState Content,
        float HudScale,
        Transform? KeyPrompt,
        bool OriginalKeyPromptActive,
        Canvas Canvas,
        bool OriginalCanvasEnabled,
        RenderMode OriginalRenderMode,
        Camera? OriginalWorldCamera,
        bool OriginalOverrideSorting,
        int OriginalSortingOrder);

    private sealed record TransformState(
        Transform Transform,
        RectTransform? Rect,
        Transform? OriginalParent,
        int OriginalSiblingIndex,
        Vector3 OriginalLocalPosition,
        Quaternion OriginalLocalRotation,
        Vector3 OriginalLocalScale,
        Vector2 OriginalAnchorMin,
        Vector2 OriginalAnchorMax,
        Vector2 OriginalPivot,
        Vector2 OriginalAnchoredPosition,
        Vector2 OriginalSizeDelta,
        Vector2 AuthoredSize);
}
