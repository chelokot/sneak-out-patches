using BepInEx.Logging;
using HarmonyLib;
using TMPro;
using Types;
using UI.Buttons;
using UI.Views.Lobby;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SneakOut.PortalModeSelector;

internal static class PortalModeSelectorRuntime
{
    private static readonly Dictionary<IntPtr, PortalModeUiState> UiStateByView = new();
    private static readonly Dictionary<IntPtr, GameModeType> SelectedModeByView = new();

    private static ManualLogSource? _logger;
    private static Harmony? _harmony;
    private static IntPtr _pendingPlayViewPointer;
    private static bool _portalTreeLogged;

    public static void Initialize(ManualLogSource logger)
    {
        _logger = logger;
        _harmony ??= new Harmony(PortalModeSelectorPlugin.PluginGuid);
        _harmony.PatchAll();
    }

    public static bool TryEnsureModeRow(PortalPlayView view)
    {
        var viewPointer = view.Pointer;
        if (viewPointer == IntPtr.Zero)
        {
            return false;
        }

        if (UiStateByView.TryGetValue(viewPointer, out var existingState) && existingState.IsAlive)
        {
            LayoutModeRow(existingState);
            RefreshModeRow(existingState);
            return true;
        }

        var roleRowRoot = FindRoleRowRoot(view);
        if (roleRowRoot is null)
        {
            _logger?.LogWarning("Portal selector setup skipped: could not resolve preferred-role row root");
            return false;
        }

        var privateRowRoot = FindPrivateRowRoot(view);
        if (privateRowRoot is null)
        {
            _logger?.LogWarning("Portal selector setup skipped: could not resolve private-game row root");
            return false;
        }

        var roleSectionRoot = FindSectionRoot(roleRowRoot);
        if (roleSectionRoot is null)
        {
            _logger?.LogWarning("Portal selector setup skipped: could not resolve preferred-role section root");
            return false;
        }

        var privateSectionRoot = FindSectionRoot(privateRowRoot);
        if (privateSectionRoot is null)
        {
            _logger?.LogWarning("Portal selector setup skipped: could not resolve private-game section root");
            return false;
        }

        if (!_portalTreeLogged)
        {
            _portalTreeLogged = true;
            LogPortalViewTree(view, roleSectionRoot, roleRowRoot, privateSectionRoot, privateRowRoot);
        }

        var playSectionRoot = FindPlaySectionRoot(view);
        if (playSectionRoot is null)
        {
            _logger?.LogWarning("Portal selector setup skipped: could not resolve play section root");
            return false;
        }

        var contentRoot = FindCommonAncestor(roleSectionRoot, privateSectionRoot);
        if (contentRoot is null)
        {
            _logger?.LogWarning("Portal selector setup skipped: could not resolve content root");
            return false;
        }

        var popupRoot = FindCommonAncestor(roleSectionRoot, privateSectionRoot, playSectionRoot);
        if (popupRoot is null)
        {
            _logger?.LogWarning("Portal selector setup skipped: could not resolve popup root");
            return false;
        }

        var modeSectionObject = UnityEngine.Object.Instantiate(roleSectionRoot.gameObject, roleSectionRoot.parent, false).TryCast<GameObject>();
        if (modeSectionObject is null)
        {
            _logger?.LogWarning("Portal selector setup skipped: failed to clone preferred-role section");
            return false;
        }

        modeSectionObject.name = "CodexModeSection";

        var modeRowObject = modeSectionObject.GetComponentsInChildren<SpookedOutlineButton>(true)
            .Select(button => button.transform)
            .Select(FindRowRootFromButton)
            .FirstOrDefault(transform => transform is not null)?
            .gameObject;
        if (modeRowObject is null)
        {
            UnityEngine.Object.Destroy(modeSectionObject);
            _logger?.LogWarning("Portal selector setup skipped: cloned section does not contain a role-style row");
            return false;
        }

        var modeButton = modeRowObject.GetComponentInChildren<SpookedOutlineButton>(true);
        if (modeButton is null)
        {
            UnityEngine.Object.Destroy(modeSectionObject);
            _logger?.LogWarning("Portal selector setup skipped: cloned section does not contain SpookedOutlineButton");
            return false;
        }

        modeButton.onClick = new Button.ButtonClickedEvent();
        var modeClickAction = (UnityAction)(() => ToggleMode(view.Pointer));
        modeButton.onClick.AddListener(modeClickAction);
        modeButton.interactable = true;
        modeButton.enabled = true;
        modeButton.Refresh();

        if (modeButton.targetGraphic is not null)
        {
            modeButton.targetGraphic.raycastTarget = true;
        }

        var leftMovingPanel = modeRowObject.transform.Find("Victim")?.GetComponent<RectTransform>();
        var rightMovingPanel = modeRowObject.transform.Find("Hunter")?.GetComponent<RectTransform>();
        var checkboxRect = modeRowObject.transform.Find("Checkbox")?.GetComponent<RectTransform>();
        if (leftMovingPanel is null || rightMovingPanel is null)
        {
            UnityEngine.Object.Destroy(modeSectionObject);
            _logger?.LogWarning("Portal selector setup skipped: cloned section does not contain expected left/right moving panels");
            return false;
        }

        TMP_Text? labelText = null;
        for (var index = 0; index < modeSectionObject.transform.childCount; index++)
        {
            var child = modeSectionObject.transform.GetChild(index);
            var childText = child.GetComponent<TMP_Text>();
            if (childText is null)
            {
                continue;
            }

            labelText = childText;
            break;
        }
        var leftText = leftMovingPanel.GetComponentInChildren<TMP_Text>(true);
        var rightText = rightMovingPanel.GetComponentInChildren<TMP_Text>(true);
        if (labelText is null || leftText is null || rightText is null)
        {
            UnityEngine.Object.Destroy(modeSectionObject);
            _logger?.LogWarning("Portal selector setup skipped: cloned section does not contain expected label/left/right TMP texts");
            return false;
        }

        var leftObject = leftMovingPanel.gameObject;
        var rightObject = rightMovingPanel.gameObject;
        var roleSectionRect = roleSectionRoot.GetComponent<RectTransform>();
        var privateSectionRect = privateSectionRoot.GetComponent<RectTransform>();
        var playSectionRect = playSectionRoot.GetComponent<RectTransform>();
        var contentRootRect = contentRoot.GetComponent<RectTransform>();
        var popupRootRect = popupRoot.GetComponent<RectTransform>();
        if (roleSectionRect is null || privateSectionRect is null || playSectionRect is null || contentRootRect is null || popupRootRect is null)
        {
            UnityEngine.Object.Destroy(modeSectionObject);
            _logger?.LogWarning("Portal selector setup skipped: role/private/play/content/popup RectTransform missing");
            return false;
        }

        var checkboxX = checkboxRect is null ? 0f : checkboxRect.anchoredPosition.x;
        var leftClassicX = leftMovingPanel.anchoredPosition.x;
        var leftCrownX = checkboxX;
        var rightClassicX = checkboxX;
        var rightCrownX = rightMovingPanel.anchoredPosition.x;

        var modeState = new PortalModeUiState(
            view,
            roleSectionRoot.gameObject,
            roleRowRoot.gameObject,
            privateSectionRoot.gameObject,
            privateRowRoot.gameObject,
            modeSectionObject,
            modeRowObject,
            modeButton,
            modeClickAction,
            labelText,
            leftText,
            rightText,
            leftObject,
            rightObject,
            leftMovingPanel,
            rightMovingPanel,
            leftClassicX,
            leftCrownX,
            rightClassicX,
            rightCrownX,
            playSectionRoot.gameObject,
            contentRoot.gameObject,
            popupRoot.gameObject,
            roleSectionRect.anchoredPosition,
            privateSectionRect.anchoredPosition,
            playSectionRect.anchoredPosition,
            roleSectionRoot.GetSiblingIndex(),
            contentRootRect.anchoredPosition,
            contentRootRect.sizeDelta,
            popupRootRect.anchoredPosition,
            popupRootRect.sizeDelta
        );

        UiStateByView[viewPointer] = modeState;
        if (!SelectedModeByView.ContainsKey(viewPointer))
        {
            SelectedModeByView[viewPointer] = GameModeType.Default;
        }

        _logger?.LogInfo(
            $"Mode row positions prepared: leftClassic={leftClassicX}, leftCrown={leftCrownX}, rightClassic={rightClassicX}, rightCrown={rightCrownX}, checkbox={checkboxRect?.anchoredPosition}, " +
            $"sourceVictim={view._victimMovingPanel?.anchoredPosition}, sourceHunter={view._hunterMovingPanel?.anchoredPosition}");

        LayoutModeRow(modeState);
        RefreshModeRow(modeState);
        _logger?.LogInfo($"Portal selector injected for view 0x{viewPointer:x}");
        return true;
    }

    public static bool TryHandleModeToggle(PortalPlayView view)
    {
        if (!TryEnsureModeRow(view))
        {
            return false;
        }

        if (!UiStateByView.TryGetValue(view.Pointer, out var state) || !state.IsAlive)
        {
            return false;
        }

        var selectedObject = EventSystem.current?.currentSelectedGameObject;
        if (selectedObject is null || !selectedObject.transform.IsChildOf(state.ModeRowObject.transform))
        {
            if (selectedObject is not null)
            {
                _logger?.LogInfo($"Role toggle passed through original path for selected object {selectedObject.name}");
            }
            return false;
        }

        var nextMode = GetSelectedMode(view) == GameModeType.Berek ? GameModeType.Default : GameModeType.Berek;
        SelectedModeByView[view.Pointer] = nextMode;
        RefreshModeRow(state);
        _logger?.LogInfo($"Portal mode toggled to {nextMode}");
        return true;
    }

    public static void LogOriginalRoleState(PortalPlayView view, string stage)
    {
        _logger?.LogInfo(
            $"Original role {stage}: greaterChance={view._greaterChanceForSeeker}, " +
            $"victim={DescribeRectDetailed(view._victimMovingPanel)}, hunter={DescribeRectDetailed(view._hunterMovingPanel)}, " +
            $"victimText={DescribeTransform(view._victimObject?.transform)}, seekerText={DescribeTransform(view._seekerObject?.transform)}, " +
            $"victimXValues={view._victimMovingPanelXValues}, hunterXValues={view._hunterMovingPanelXValues}");
    }

    private static void ToggleMode(IntPtr viewPointer)
    {
        if (!UiStateByView.TryGetValue(viewPointer, out var state) || !state.IsAlive)
        {
            _logger?.LogWarning($"Portal mode click ignored for dead view 0x{viewPointer:x}");
            return;
        }

        var nextMode = state.SelectedMode == GameModeType.Berek ? GameModeType.Default : GameModeType.Berek;
        SelectedModeByView[viewPointer] = nextMode;
        RefreshModeRow(state);
        _logger?.LogInfo($"Portal mode button clicked, toggled to {nextMode}");
    }

    public static void RememberPendingPlayView(PortalPlayView view)
    {
        _pendingPlayViewPointer = view.Pointer;
        _logger?.LogInfo($"Portal play pressed with requested mode {GetSelectedMode(view)}");
    }

    public static bool TryOverrideMatchMode(ref GameModeType gameModeType)
    {
        if (_pendingPlayViewPointer == IntPtr.Zero)
        {
            return false;
        }

        if (!SelectedModeByView.TryGetValue(_pendingPlayViewPointer, out var selectedMode))
        {
            return false;
        }

        gameModeType = selectedMode;
        _logger?.LogInfo($"PrepareMatch overridden to {selectedMode}");
        return true;
    }

    public static GameModeType GetSelectedMode(PortalPlayView view)
    {
        return SelectedModeByView.TryGetValue(view.Pointer, out var selectedMode)
            ? selectedMode
            : GameModeType.Default;
    }

    private static void LayoutModeRow(PortalModeUiState state)
    {
        var roleSectionRect = state.RoleSectionObject.GetComponent<RectTransform>();
        var privateSectionRect = state.PrivateSectionObject.GetComponent<RectTransform>();
        var modeSectionRect = state.ModeSectionObject.GetComponent<RectTransform>();
        var playSectionRect = state.PlaySectionObject.GetComponent<RectTransform>();
        var contentRootRect = state.ContentRootObject.GetComponent<RectTransform>();
        var popupRootRect = state.PopupRootObject.GetComponent<RectTransform>();
        if (roleSectionRect is null || privateSectionRect is null || modeSectionRect is null || playSectionRect is null || contentRootRect is null || popupRootRect is null)
        {
            return;
        }

        var verticalDelta = state.OriginalRoleSectionPosition.y - state.OriginalPrivateSectionPosition.y;
        modeSectionRect.anchoredPosition = state.OriginalRoleSectionPosition;
        modeSectionRect.sizeDelta = roleSectionRect.sizeDelta;
        modeSectionRect.anchorMin = roleSectionRect.anchorMin;
        modeSectionRect.anchorMax = roleSectionRect.anchorMax;
        modeSectionRect.pivot = roleSectionRect.pivot;
        modeSectionRect.localScale = roleSectionRect.localScale;

        roleSectionRect.anchoredPosition = state.OriginalPrivateSectionPosition;
        privateSectionRect.anchoredPosition = state.OriginalPrivateSectionPosition - new Vector2(0f, verticalDelta);
        playSectionRect.anchoredPosition = state.OriginalPlaySectionPosition - new Vector2(0f, verticalDelta);

        contentRootRect.sizeDelta = state.OriginalContentSize + new Vector2(0f, verticalDelta);
        contentRootRect.anchoredPosition = state.OriginalContentPosition - new Vector2(0f, verticalDelta * 0.5f);
        popupRootRect.sizeDelta = state.OriginalPopupSize + new Vector2(0f, verticalDelta);
        popupRootRect.anchoredPosition = state.OriginalPopupPosition - new Vector2(0f, verticalDelta * 0.5f);

        modeSectionRect.SetSiblingIndex(state.OriginalRoleSectionSiblingIndex);
        roleSectionRect.SetSiblingIndex(state.OriginalRoleSectionSiblingIndex + 1);
        privateSectionRect.SetSiblingIndex(state.OriginalRoleSectionSiblingIndex + 2);
    }

    private static void RefreshModeRow(PortalModeUiState state)
    {
        state.LabelText.text = "Game mode";
        state.LeftText.text = "Classic";
        state.RightText.text = "Crown";
        var classicSelected = state.SelectedMode == GameModeType.Default;
        ApplyModePanelLayout(state.LeftMovingPanel, classicSelected ? new Vector2(0.15f, 0.13f) : new Vector2(0.50f, 0.13f), classicSelected ? new Vector2(0.50f, 0.76f) : new Vector2(0.50f, 0.76f), classicSelected ? -78.87f : 0.06f);
        ApplyModePanelLayout(state.RightMovingPanel, classicSelected ? new Vector2(0.50f, 0.13f) : new Vector2(0.50f, 0.13f), classicSelected ? new Vector2(0.50f, 0.76f) : new Vector2(0.85f, 0.76f), classicSelected ? -0.07f : 78.87f);
        _logger?.LogInfo(
            $"Mode row refresh: selected={state.SelectedMode}, leftPos={DescribeRectDetailed(state.LeftMovingPanel)}, rightPos={DescribeRectDetailed(state.RightMovingPanel)}");
        state.ModeButton.Refresh();
    }

    private static float ResolvePanelPositions(RectTransform? sourcePanel, Vector2 xValues, bool sourceIsSelected, out float unselectedX)
    {
        if (sourcePanel is null)
        {
            unselectedX = xValues.y;
            return xValues.x;
        }

        var currentX = sourcePanel.anchoredPosition.x;
        var distanceToFirst = Mathf.Abs(currentX - xValues.x);
        var distanceToSecond = Mathf.Abs(currentX - xValues.y);
        var currentMatchesFirst = distanceToFirst <= distanceToSecond;
        var firstValue = xValues.x;
        var secondValue = xValues.y;

        if (sourceIsSelected)
        {
            if (currentMatchesFirst)
            {
                unselectedX = secondValue;
                return firstValue;
            }

            unselectedX = firstValue;
            return secondValue;
        }

        if (currentMatchesFirst)
        {
            unselectedX = firstValue;
            return secondValue;
        }

        unselectedX = secondValue;
        return firstValue;
    }

    private static Transform? FindRowRootFromButton(Transform buttonTransform)
    {
        for (var current = buttonTransform.parent; current is not null; current = current.parent)
        {
            var textCount = current.GetComponentsInChildren<TMP_Text>(true).Length;
            var buttonCount = current.GetComponentsInChildren<SpookedOutlineButton>(true).Length;
            if (textCount >= 2 && buttonCount >= 1)
            {
                return current;
            }
        }

        return null;
    }

    private static float GetWorldX(TMP_Text text)
    {
        return text.transform.position.x;
    }

    private static float GetWorldY(TMP_Text text)
    {
        return text.transform.position.y;
    }

    private static void LogPortalViewTree(
        PortalPlayView view,
        Transform roleSectionRoot,
        Transform roleRowRoot,
        Transform privateSectionRoot,
        Transform privateRowRoot
    )
    {
        _logger?.LogInfo($"Portal canvas: {DescribeTransform(view._canvasObject?.transform)}");
        _logger?.LogInfo($"Role section root: {DescribeTransform(roleSectionRoot)}");
        _logger?.LogInfo($"Role row root: {DescribeTransform(roleRowRoot)}");
        _logger?.LogInfo($"Private section root: {DescribeTransform(privateSectionRoot)}");
        _logger?.LogInfo($"Private row root: {DescribeTransform(privateRowRoot)}");
        _logger?.LogInfo($"Play section root: {DescribeTransform(FindPlaySectionRoot(view))}");
        _logger?.LogInfo($"Content root: {DescribeTransform(FindCommonAncestor(roleSectionRoot, privateSectionRoot))}");
        _logger?.LogInfo($"Popup root: {DescribeTransform(FindCommonAncestor(roleSectionRoot, privateSectionRoot, FindPlaySectionRoot(view)))}");
        LogChildren(roleSectionRoot, 0, 4);
        LogChildren(privateSectionRoot, 0, 4);
    }

    private static void LogChildren(Transform root, int depth, int maxDepth)
    {
        if (depth > maxDepth)
        {
            return;
        }

        var indent = new string(' ', depth * 2);
        _logger?.LogInfo($"{indent}- {DescribeTransform(root)}");
        for (var index = 0; index < root.childCount; index++)
        {
            var child = root.GetChild(index);
            LogChildren(child, depth + 1, maxDepth);
        }
    }

    private static string DescribeTransform(Transform? transform)
    {
        if (transform is null)
        {
            return "<null>";
        }

        var rectTransform = transform.GetComponent<RectTransform>();
        return rectTransform is null
            ? transform.name
            : $"{transform.name} pos={rectTransform.anchoredPosition} size={rectTransform.sizeDelta}";
    }

    private static string DescribeRectDetailed(RectTransform? rectTransform)
    {
        if (rectTransform is null)
        {
            return "<null>";
        }

        return $"{rectTransform.name} anchored={rectTransform.anchoredPosition} local={rectTransform.localPosition} " +
               $"anchorMin={rectTransform.anchorMin} anchorMax={rectTransform.anchorMax} pivot={rectTransform.pivot} " +
               $"offsetMin={rectTransform.offsetMin} offsetMax={rectTransform.offsetMax} size={rectTransform.sizeDelta}";
    }

    private static void ApplyModePanelLayout(RectTransform panel, Vector2 anchorMin, Vector2 anchorMax, float localX)
    {
        panel.anchorMin = anchorMin;
        panel.anchorMax = anchorMax;
        var localPosition = panel.localPosition;
        panel.localPosition = new Vector3(localX, localPosition.y, localPosition.z);
    }

    public static void LogError(string message, Exception exception)
    {
        _logger?.LogError($"{message}: {exception}");
    }

    private static Transform? FindRoleRowRoot(PortalPlayView view)
    {
        return FindCommonAncestor(
            view._preferredRoleButton?.transform,
            view._seekerObject?.transform,
            view._victimObject?.transform,
            view._victimMovingPanel?.transform,
            view._hunterMovingPanel?.transform
        );
    }

    private static Transform? FindPrivateRowRoot(PortalPlayView view)
    {
        return FindCommonAncestor(
            view._privateGameButton?.transform,
            view._publicMovingPanel?.transform,
            view._privateMovingPanel?.transform
        );
    }

    private static Transform? FindCommonAncestor(params Transform?[] transforms)
    {
        var activeTransforms = transforms.Where(transform => transform is not null).Cast<Transform>().ToArray();
        if (activeTransforms.Length == 0)
        {
            return null;
        }

        var firstAncestors = GetAncestors(activeTransforms[0]);
        foreach (var candidate in firstAncestors)
        {
            if (activeTransforms.All(transform => transform == candidate || transform.IsChildOf(candidate)))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<Transform> GetAncestors(Transform transform)
    {
        for (var current = transform; current is not null; current = current.parent)
        {
            yield return current;
        }
    }

    private static Transform? FindSectionRoot(Transform rowRoot)
    {
        for (var current = rowRoot.parent; current is not null; current = current.parent)
        {
            var textCount = current.GetComponentsInChildren<TMP_Text>(true).Length;
            var buttonCount = current.GetComponentsInChildren<SpookedOutlineButton>(true).Length;
            if (textCount >= 3 && buttonCount >= 1)
            {
                return current;
            }
        }

        return null;
    }

    private static Transform? FindPlaySectionRoot(PortalPlayView view)
    {
        return FindCommonAncestor(
            view._playButton?.transform,
            view._playButtonGamepadIcon?.transform
        );
    }
}
