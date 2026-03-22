using BepInEx.Logging;
using HarmonyLib;
using TMPro;
using Types;
using UI.Buttons;
using UI.Views.Lobby;
using UnityEngine;
using UnityEngine.EventSystems;

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

        var textComponents = modeSectionObject.GetComponentsInChildren<TMP_Text>(true);
        var orderedTexts = textComponents
            .Where(text => text is not null)
            .OrderByDescending(GetWorldY)
            .ThenBy(GetWorldX)
            .ToArray();
        if (orderedTexts.Length < 3)
        {
            UnityEngine.Object.Destroy(modeSectionObject);
            _logger?.LogWarning($"Portal selector setup skipped: expected at least 3 TMP texts inside cloned section, got {orderedTexts.Length}");
            return false;
        }

        var labelText = orderedTexts[0];
        var sideTexts = orderedTexts.Skip(1).OrderBy(GetWorldX).Take(2).ToList();
        var leftText = sideTexts[0];
        var rightText = sideTexts[1];

        var modeState = new PortalModeUiState(
            view,
            roleSectionRoot.gameObject,
            roleRowRoot.gameObject,
            privateSectionRoot.gameObject,
            privateRowRoot.gameObject,
            modeSectionObject,
            modeRowObject,
            modeButton,
            labelText,
            leftText,
            rightText
        );

        UiStateByView[viewPointer] = modeState;
        if (!SelectedModeByView.ContainsKey(viewPointer))
        {
            SelectedModeByView[viewPointer] = GameModeType.Default;
        }

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
            return false;
        }

        var nextMode = GetSelectedMode(view) == GameModeType.Berek ? GameModeType.Default : GameModeType.Berek;
        SelectedModeByView[view.Pointer] = nextMode;
        RefreshModeRow(state);
        _logger?.LogInfo($"Portal mode toggled to {nextMode}");
        return true;
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
        if (roleSectionRect is null || privateSectionRect is null || modeSectionRect is null)
        {
            return;
        }

        var verticalDelta = roleSectionRect.anchoredPosition.y - privateSectionRect.anchoredPosition.y;
        var direction = verticalDelta >= 0f ? 1f : -1f;
        var rowSpacing = Mathf.Abs(verticalDelta);
        var targetPosition = roleSectionRect.anchoredPosition + new Vector2(0f, rowSpacing * direction);
        modeSectionRect.anchoredPosition = targetPosition;
        modeSectionRect.sizeDelta = roleSectionRect.sizeDelta;
        modeSectionRect.anchorMin = roleSectionRect.anchorMin;
        modeSectionRect.anchorMax = roleSectionRect.anchorMax;
        modeSectionRect.pivot = roleSectionRect.pivot;
        modeSectionRect.localScale = roleSectionRect.localScale;
        modeSectionRect.SetSiblingIndex(roleSectionRect.GetSiblingIndex());
    }

    private static void RefreshModeRow(PortalModeUiState state)
    {
        state.LabelText.text = "Game mode";
        state.LeftText.text = state.SelectedMode == GameModeType.Default ? "[Classic]" : "Classic";
        state.RightText.text = state.SelectedMode == GameModeType.Berek ? "[Crown]" : "Crown";
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

    public static void LogError(string message, Exception exception)
    {
        _logger?.LogError($"{message}: {exception}");
    }
}
