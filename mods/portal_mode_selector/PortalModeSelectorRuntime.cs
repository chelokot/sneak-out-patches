using BepInEx.Logging;
using DG.Tweening;
using Events;
using Gameplay.Player.Components;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Kinguinverse.DataUtils.Events;
using Networking;
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
    private static readonly PortalMapSelectionState PreferredMapSelection = new();
    private static readonly Color ClassicModeColor = new(0.08627451f, 0.5372549f, 0.654902f, 1f);
    private static readonly Color CrownModeColor = new(0.8117647f, 0.62352943f, 0f, 1f);
    private static readonly Color MapOptionHoverColor = new(1f, 1f, 1f, 0.16f);
    private static readonly Color MapOptionPressedColor = new(1f, 1f, 1f, 0.24f);
    private static readonly Color MapCheckboxOutlineColor = new(1f, 1f, 1f, 0.92f);
    private static readonly Color MapCheckboxOffFillColor = new(0.04f, 0.06f, 0.12f, 0.9f);
    private const float ToggleAnimationDuration = 0.36f;

    private static ManualLogSource? _logger;
    private static Harmony? _harmony;
    private static GameModeType _preferredMode = GameModeType.Default;
    private static GameModeType? _activeMode;
    private static PortalMapSelectionState? _activeMapSelection;
    private static Sprite? _crownIconSprite;

    public static void Initialize(ManualLogSource logger)
    {
        _logger = logger;
        _harmony ??= new Harmony(PortalModeSelectorPlugin.PluginGuid);
        _harmony.PatchAll();
    }

    public static void OpenPortal(PortalPlayView view)
    {
        ClearActiveSelection();
        PreferredMapSelection.Synchronize(view._spookedSettings.MapsToPlayOn);
        TryEnsureModeRow(view);
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
            RefreshModeRow(existingState, false);
            if (existingState.MapOptions.Length > 0)
            {
                RefreshMapSection(existingState);
            }
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
        var leftPanelImage = leftMovingPanel?.GetComponent<Image>();
        var rightPanelImage = rightMovingPanel?.GetComponent<Image>();
        var checkboxBackgroundImage = checkboxRect?.GetComponent<Image>();
        var checkboxOutlineImage = checkboxRect?.Find("Outline")?.GetComponent<Image>();
        var checkboxVictimImage = checkboxRect?.Find("VictimImage")?.GetComponent<Image>();
        var checkboxHunterImage = checkboxRect?.Find("HunterImage")?.GetComponent<Image>();
        if (leftMovingPanel is null
            || rightMovingPanel is null
            || checkboxRect is null
            || leftPanelImage is null
            || rightPanelImage is null
            || checkboxBackgroundImage is null
            || checkboxOutlineImage is null
            || checkboxVictimImage is null
            || checkboxHunterImage is null)
        {
            UnityEngine.Object.Destroy(modeSectionObject);
            _logger?.LogWarning("Portal selector setup skipped: cloned section does not contain expected moving panels or checkbox images");
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

        var mapSectionObject = new GameObject("CodexMapSection");
        mapSectionObject.transform.SetParent(contentRoot, false);
        var mapSectionRect = mapSectionObject.AddComponent<RectTransform>();
        mapSectionRect.anchorMin = roleSectionRect.anchorMin;
        mapSectionRect.anchorMax = roleSectionRect.anchorMax;
        mapSectionRect.pivot = roleSectionRect.pivot;
        mapSectionRect.localScale = roleSectionRect.localScale;

        var mapTitleObject = UnityEngine.Object.Instantiate(labelText.gameObject, mapSectionObject.transform, false).TryCast<GameObject>();
        if (mapTitleObject is null)
        {
            UnityEngine.Object.Destroy(modeSectionObject);
            UnityEngine.Object.Destroy(mapSectionObject);
            _logger?.LogWarning("Portal selector setup skipped: failed to clone map title");
            return false;
        }

        mapTitleObject.name = "CodexMapTitle";
        var mapTitleText = mapTitleObject.GetComponent<TMP_Text>();
        if (mapTitleText is null)
        {
            UnityEngine.Object.Destroy(modeSectionObject);
            UnityEngine.Object.Destroy(mapSectionObject);
            _logger?.LogWarning("Portal selector setup skipped: cloned map title does not contain TMP_Text");
            return false;
        }

        mapTitleText.text = "Maps";
        mapTitleText.fontSize = 13f;
        var mapOptions = CreateMapOptions(
            viewPointer,
            mapSectionObject.transform,
            leftText,
            leftPanelImage.sprite,
            checkboxOutlineImage.sprite,
            checkboxBackgroundImage.sprite
        );
        if (mapOptions.Length == 0)
        {
            _logger?.LogWarning("Portal selector found no playable maps in the current game settings");
        }

        var leftClassicX = -78.87f;
        var leftCrownX = 0.06f;
        var rightClassicX = -0.07f;
        var rightCrownX = 78.87f;

        checkboxVictimImage.gameObject.SetActive(true);
        checkboxHunterImage.gameObject.SetActive(true);

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
            leftPanelImage,
            rightPanelImage,
            leftMovingPanel,
            rightMovingPanel,
            checkboxBackgroundImage,
            checkboxOutlineImage,
            checkboxVictimImage,
            checkboxHunterImage,
            leftClassicX,
            leftCrownX,
            rightClassicX,
            rightCrownX,
            checkboxHunterImage.sprite,
            mapSectionObject,
            mapTitleText,
            mapOptions,
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

        LayoutModeRow(modeState);
        RefreshModeRow(modeState, false);
        if (modeState.MapOptions.Length > 0)
        {
            RefreshMapSection(modeState);
        }
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

        ToggleMode(state);
        return true;
    }

    public static void ActivateSelection(PortalPlayView view)
    {
        PreferredMapSelection.Synchronize(view._spookedSettings.MapsToPlayOn);
        _activeMode = _preferredMode;
        _activeMapSelection = PreferredMapSelection.Snapshot();
        PublishRequestedGameMode(_activeMode.Value);
        _logger?.LogInfo($"Portal play requested {_activeMode.Value}");
    }

    public static void ClearActiveSelection()
    {
        _activeMode = null;
        _activeMapSelection = null;
    }

    public static void ReleasePortalView(PortalPlayView view)
    {
        if (!UiStateByView.Remove(view.Pointer, out var state))
        {
            return;
        }

        state.ModeButton.onClick.RemoveListener(state.ModeClickAction);
        foreach (var option in state.MapOptions)
        {
            option.Button.onClick.RemoveListener(option.ClickAction);
        }

        RestoreLayout(state);
        UnityEngine.Object.Destroy(state.MapSectionObject);
        UnityEngine.Object.Destroy(state.ModeSectionObject);
    }

    private static void ToggleMode(IntPtr viewPointer)
    {
        if (!UiStateByView.TryGetValue(viewPointer, out var state) || !state.IsAlive)
        {
            return;
        }

        ToggleMode(state);
    }

    private static void ToggleMode(PortalModeUiState state)
    {
        _preferredMode = _preferredMode == GameModeType.Berek ? GameModeType.Default : GameModeType.Berek;
        PublishRequestedGameMode(_preferredMode);
        RefreshModeRow(state, true);
        RefreshMapSection(state);
    }

    public static bool TryOverrideMatchMode(ref GameModeType gameModeType)
    {
        if (!_activeMode.HasValue)
        {
            return false;
        }

        gameModeType = _activeMode.Value;
        return true;
    }

    public static void ApplyActiveMode(GameState gameState)
    {
        if (_activeMode.HasValue)
        {
            gameState.GameMode = _activeMode.Value;
        }
    }

    public static void WireAllBerekComponents()
    {
        if (_activeMode != GameModeType.Berek)
        {
            return;
        }

        foreach (var player in Resources.FindObjectsOfTypeAll<SpookedNetworkPlayer>())
        {
            WirePlayerBerekComponent(player);
        }
    }

    public static void WirePlayerBerekComponent(SpookedNetworkPlayer player)
    {
        var berekComponent = player.EntityBerekComponent ?? player.GetComponent<EntityBerekComponent>();
        if (berekComponent is null)
        {
            return;
        }

        player.EntityBerekComponent = berekComponent;
        if (berekComponent._spookedNetworkPlayer is null)
        {
            berekComponent._spookedNetworkPlayer = player;
        }
    }

    public static bool TryOverrideRandomScene(Il2CppStructArray<SceneType> mapsToPlayOn, GameModeType gameModeType, ref SceneType sceneType)
    {
        if (!_activeMode.HasValue || _activeMapSelection is null)
        {
            return false;
        }

        var availableMaps = mapsToPlayOn.ToHashSet();
        var selectedPool = _activeMapSelection
            .GetSelectedMaps(_activeMode.Value)
            .Where(availableMaps.Contains)
            .ToArray();
        if (selectedPool.Length == 0)
        {
            return false;
        }

        sceneType = selectedPool[UnityEngine.Random.Range(0, selectedPool.Length)];
        _logger?.LogInfo($"Portal selected map {sceneType} for {_activeMode.Value}");
        return true;
    }

    public static void TryOverrideStartMatchmakingArgs(Il2CppSystem.EventArgs? args)
    {
        if (!_activeMode.HasValue || args is not StartMatchmakingEvent matchmakingEvent)
        {
            return;
        }

        matchmakingEvent.GameModeType = _activeMode.Value;
    }

    public static void TryOverrideRequestChangeGameModeArgs(Il2CppSystem.EventArgs? args)
    {
        if (!_activeMode.HasValue || args is not RequestChangeGameModeEvent changeGameModeEvent)
        {
            return;
        }

        changeGameModeEvent.RequestedGameModeType = _activeMode.Value;
    }

    public static void TryOverrideBroadcastMatchArgs(Il2CppSystem.EventArgs? args)
    {
        if (!_activeMode.HasValue || args is not BroadcastMatchSessionToOtherLobbyMembers broadcastEvent)
        {
            return;
        }

        broadcastEvent.SelectedGameModeType = _activeMode.Value;
    }

    private static void PublishRequestedGameMode(GameModeType selectedMode)
    {
        try
        {
            GameEventsManager.Publish<RequestChangeGameModeEvent>(null, new RequestChangeGameModeEvent(selectedMode));
        }
        catch (Exception exception)
        {
            LogError("Portal selector failed to publish RequestChangeGameModeEvent", exception);
        }
    }

    public static GameModeType GetSelectedMode(PortalPlayView view)
    {
        return _preferredMode;
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
        var groupOffsetDown = new Vector2(0f, -verticalDelta * 0.06f);
        modeSectionRect.anchoredPosition = state.OriginalRoleSectionPosition + new Vector2(0f, verticalDelta * 1.58f) + groupOffsetDown;
        modeSectionRect.sizeDelta = roleSectionRect.sizeDelta;
        modeSectionRect.anchorMin = roleSectionRect.anchorMin;
        modeSectionRect.anchorMax = roleSectionRect.anchorMax;
        modeSectionRect.pivot = roleSectionRect.pivot;
        modeSectionRect.localScale = roleSectionRect.localScale;

        roleSectionRect.anchoredPosition = state.OriginalRoleSectionPosition - new Vector2(0f, verticalDelta * 0.90f) + groupOffsetDown;
        privateSectionRect.gameObject.SetActive(true);
        privateSectionRect.anchoredPosition = state.OriginalPrivateSectionPosition - new Vector2(0f, verticalDelta * 1.12f) + groupOffsetDown;
        playSectionRect.anchoredPosition = state.OriginalPlaySectionPosition - new Vector2(0f, verticalDelta * 1.34f) + groupOffsetDown;

        contentRootRect.sizeDelta = state.OriginalContentSize + new Vector2(0f, verticalDelta * 1.88f);
        contentRootRect.anchoredPosition = state.OriginalContentPosition + new Vector2(0f, verticalDelta * 0.08f);
        popupRootRect.sizeDelta = state.OriginalPopupSize + new Vector2(0f, verticalDelta * 1.88f);
        popupRootRect.anchoredPosition = state.OriginalPopupPosition + new Vector2(0f, verticalDelta * 0.08f);

        modeSectionRect.SetSiblingIndex(state.OriginalRoleSectionSiblingIndex);
        roleSectionRect.SetSiblingIndex(state.OriginalRoleSectionSiblingIndex + 1);
        privateSectionRect.SetSiblingIndex(state.OriginalRoleSectionSiblingIndex + 2);
    }

    private static void RestoreLayout(PortalModeUiState state)
    {
        var roleSectionRect = state.RoleSectionObject.GetComponent<RectTransform>();
        var privateSectionRect = state.PrivateSectionObject.GetComponent<RectTransform>();
        var playSectionRect = state.PlaySectionObject.GetComponent<RectTransform>();
        var contentRootRect = state.ContentRootObject.GetComponent<RectTransform>();
        var popupRootRect = state.PopupRootObject.GetComponent<RectTransform>();
        if (roleSectionRect is null
            || privateSectionRect is null
            || playSectionRect is null
            || contentRootRect is null
            || popupRootRect is null)
        {
            return;
        }

        roleSectionRect.anchoredPosition = state.OriginalRoleSectionPosition;
        privateSectionRect.anchoredPosition = state.OriginalPrivateSectionPosition;
        playSectionRect.anchoredPosition = state.OriginalPlaySectionPosition;
        contentRootRect.anchoredPosition = state.OriginalContentPosition;
        contentRootRect.sizeDelta = state.OriginalContentSize;
        popupRootRect.anchoredPosition = state.OriginalPopupPosition;
        popupRootRect.sizeDelta = state.OriginalPopupSize;
        roleSectionRect.SetSiblingIndex(state.OriginalRoleSectionSiblingIndex);
        privateSectionRect.SetSiblingIndex(state.OriginalRoleSectionSiblingIndex + 1);
    }

    private static void RefreshModeRow(PortalModeUiState state, bool animate)
    {
        state.LabelText.text = state.View._gameTranslator.Translate("CHOOSE_GAME_MODE");
        state.LeftText.text = "Classic";
        state.RightText.text = "Crown";
        var classicSelected = state.SelectedMode == GameModeType.Default;

        state.LeftPanelImage.color = ClassicModeColor;
        state.RightPanelImage.color = CrownModeColor;
        ApplyModePanelLayout(
            state.LeftMovingPanel,
            classicSelected ? new Vector2(0.15f, 0.13f) : new Vector2(0.50f, 0.13f),
            classicSelected ? new Vector2(0.50f, 0.76f) : new Vector2(0.50f, 0.76f),
            classicSelected ? state.LeftClassicX : state.LeftCrownX,
            animate
        );
        ApplyModePanelLayout(
            state.RightMovingPanel,
            classicSelected ? new Vector2(0.50f, 0.13f) : new Vector2(0.50f, 0.13f),
            classicSelected ? new Vector2(0.50f, 0.76f) : new Vector2(0.85f, 0.76f),
            classicSelected ? state.RightClassicX : state.RightCrownX,
            animate
        );
        ApplyModeCheckboxVisual(state, classicSelected, animate);
    }

    private static void RefreshMapSection(PortalModeUiState state)
    {
        var modeSectionRect = state.ModeSectionObject.GetComponent<RectTransform>();
        var roleSectionRect = state.RoleSectionObject.GetComponent<RectTransform>();
        var mapSectionRect = state.MapSectionObject.GetComponent<RectTransform>();
        if (modeSectionRect is null || roleSectionRect is null || mapSectionRect is null)
        {
            return;
        }

        var topBoundary = modeSectionRect.anchoredPosition.y - modeSectionRect.sizeDelta.y * 0.52f - 44f;
        var bottomBoundary = roleSectionRect.anchoredPosition.y + roleSectionRect.sizeDelta.y * 0.52f + 6f;
        var sectionHeight = Mathf.Max(126f, topBoundary - bottomBoundary);
        mapSectionRect.anchoredPosition = new Vector2(state.OriginalRoleSectionPosition.x, (topBoundary + bottomBoundary) * 0.5f + 10f);
        mapSectionRect.sizeDelta = new Vector2(modeSectionRect.sizeDelta.x, sectionHeight);

        var titleRect = state.MapTitleText.GetComponent<RectTransform>();
        if (titleRect is not null)
        {
            titleRect.anchorMin = new Vector2(0.5f, 1f);
            titleRect.anchorMax = new Vector2(0.5f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -2f);
            titleRect.sizeDelta = new Vector2(modeSectionRect.sizeDelta.x, 24f);
        }

        state.MapTitleText.fontSize = 20f;
        var lineStartY = -44f;
        var lineSpacing = 28f;
        var leftX = -84f;
        var rightX = 34f;
        var activeMode = state.SelectedMode == GameModeType.Berek ? GameModeType.Berek : GameModeType.Default;
        var activeSelections = PreferredMapSelection.GetSelectedMaps(activeMode);
        var visibleOptions = state.MapOptions
            .Where(option => option.GameModeType == activeMode)
            .OrderBy(option => (int)option.SceneType)
            .ToArray();
        var columnCount = visibleOptions.Length > 3 ? 2 : 1;

        foreach (var option in state.MapOptions)
        {
            var optionModeMatches = option.GameModeType == activeMode;
            option.RootObject.SetActive(optionModeMatches);
            if (!optionModeMatches)
            {
                continue;
            }

            var optionRect = option.RootObject.GetComponent<RectTransform>();
            if (optionRect is null)
            {
                continue;
            }

            optionRect.anchorMin = new Vector2(0.5f, 1f);
            optionRect.anchorMax = new Vector2(0.5f, 1f);
            optionRect.pivot = new Vector2(0.5f, 1f);
            optionRect.sizeDelta = columnCount == 2 ? new Vector2(150f, 24f) : new Vector2(196f, 24f);
            var optionIndex = Array.IndexOf(visibleOptions, option);
            var optionColumn = optionIndex % columnCount;
            var optionRow = optionIndex / columnCount;
            optionRect.anchoredPosition = new Vector2(
                columnCount == 1 || optionColumn == 0 ? leftX : rightX,
                lineStartY - lineSpacing * optionRow
            );

            option.LabelText.fontSize = 18f;
            option.LabelText.alignment = TextAlignmentOptions.Left;
            option.LabelText.text = FormatMapName(option.SceneType);
            RefreshMapOptionVisual(option, activeSelections.Contains(option.SceneType), activeMode);
        }
    }

    private static PortalMapOptionUiState[] CreateMapOptions(
        IntPtr viewPointer,
        Transform mapSectionTransform,
        TMP_Text textTemplate,
        Sprite? rowBackgroundSprite,
        Sprite? checkboxOutlineSprite,
        Sprite? checkboxFillSprite
    )
    {
        var options = new List<PortalMapOptionUiState>();

        foreach (var map in PreferredMapSelection.GetAvailableMaps(GameModeType.Default).OrderBy(sceneType => (int)sceneType))
        {
            var option = CreateMapOption(viewPointer, mapSectionTransform, textTemplate, rowBackgroundSprite, checkboxOutlineSprite, checkboxFillSprite, map, GameModeType.Default);
            if (option is not null)
            {
                options.Add(option);
            }
        }

        foreach (var map in PreferredMapSelection.GetAvailableMaps(GameModeType.Berek).OrderBy(sceneType => (int)sceneType))
        {
            var option = CreateMapOption(viewPointer, mapSectionTransform, textTemplate, rowBackgroundSprite, checkboxOutlineSprite, checkboxFillSprite, map, GameModeType.Berek);
            if (option is not null)
            {
                options.Add(option);
            }
        }

        return options.ToArray();
    }

    private static PortalMapOptionUiState? CreateMapOption(
        IntPtr viewPointer,
        Transform mapSectionTransform,
        TMP_Text textTemplate,
        Sprite? rowBackgroundSprite,
        Sprite? checkboxOutlineSprite,
        Sprite? checkboxFillSprite,
        SceneType sceneType,
        GameModeType gameModeType
    )
    {
        try
        {
            var optionObject = new GameObject($"CodexMapOption_{sceneType}");
            optionObject.transform.SetParent(mapSectionTransform, false);

            var optionRect = optionObject.AddComponent<RectTransform>();
            optionRect.localScale = Vector3.one;

            var backgroundImage = optionObject.AddComponent<Image>();
            backgroundImage.sprite = rowBackgroundSprite;
            backgroundImage.type = Image.Type.Sliced;
            backgroundImage.color = new Color(0f, 0f, 0f, 0f);
            backgroundImage.raycastTarget = true;

            var button = optionObject.AddComponent<Button>();
            button.targetGraphic = backgroundImage;
            button.onClick = new Button.ButtonClickedEvent();
            button.transition = Selectable.Transition.None;
            button.colors = new ColorBlock
            {
                normalColor = new Color(1f, 1f, 1f, 0f),
                highlightedColor = MapOptionHoverColor,
                pressedColor = MapOptionPressedColor,
                selectedColor = MapOptionHoverColor,
                disabledColor = new Color(1f, 1f, 1f, 0.02f),
                colorMultiplier = 1f,
                fadeDuration = 0.1f
            };

            var hoverTrigger = optionObject.AddComponent<EventTrigger>();
            var hoverEnter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            hoverEnter.callback.AddListener((UnityAction<BaseEventData>)(_ =>
            {
                ShortcutExtensions.DOKill(backgroundImage, false);
                DOTweenModuleUI.DOColor(backgroundImage, MapOptionHoverColor, 0.08f);
            }));
            hoverTrigger.triggers.Add(hoverEnter);

            var hoverExit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            hoverExit.callback.AddListener((UnityAction<BaseEventData>)(_ =>
            {
                ShortcutExtensions.DOKill(backgroundImage, false);
                DOTweenModuleUI.DOColor(backgroundImage, new Color(1f, 1f, 1f, 0f), 0.08f);
            }));
            hoverTrigger.triggers.Add(hoverExit);

            var checkboxObject = new GameObject("Checkbox");
            checkboxObject.transform.SetParent(optionObject.transform, false);
            var checkboxRect = checkboxObject.AddComponent<RectTransform>();
            checkboxRect.anchorMin = new Vector2(0f, 0.5f);
            checkboxRect.anchorMax = new Vector2(0f, 0.5f);
            checkboxRect.pivot = new Vector2(0f, 0.5f);
            checkboxRect.anchoredPosition = new Vector2(0f, 0f);
            checkboxRect.sizeDelta = new Vector2(20f, 20f);

            var checkboxOutlineImage = checkboxObject.AddComponent<Image>();
            checkboxOutlineImage.sprite = checkboxOutlineSprite;
            checkboxOutlineImage.color = MapCheckboxOutlineColor;
            checkboxOutlineImage.raycastTarget = false;

            var checkboxFillObject = new GameObject("Fill");
            checkboxFillObject.transform.SetParent(checkboxObject.transform, false);
            var checkboxFillRect = checkboxFillObject.AddComponent<RectTransform>();
            checkboxFillRect.anchorMin = new Vector2(0.5f, 0.5f);
            checkboxFillRect.anchorMax = new Vector2(0.5f, 0.5f);
            checkboxFillRect.pivot = new Vector2(0.5f, 0.5f);
            checkboxFillRect.anchoredPosition = Vector2.zero;
            checkboxFillRect.sizeDelta = new Vector2(12f, 12f);

            var checkboxFillImage = checkboxFillObject.AddComponent<Image>();
            checkboxFillImage.sprite = checkboxFillSprite;
            checkboxFillImage.color = MapCheckboxOffFillColor;
            checkboxFillImage.raycastTarget = false;

            var labelObject = UnityEngine.Object.Instantiate(textTemplate.gameObject, optionObject.transform, false).TryCast<GameObject>();
            if (labelObject is null)
            {
                UnityEngine.Object.Destroy(optionObject);
                return null;
            }

            labelObject.name = "Label";
            var labelText = labelObject.GetComponent<TMP_Text>();
            if (labelText is null)
            {
                UnityEngine.Object.Destroy(optionObject);
                return null;
            }

            var labelRect = labelObject.GetComponent<RectTransform>();
            if (labelRect is not null)
            {
                labelRect.anchorMin = new Vector2(0f, 0.5f);
                labelRect.anchorMax = new Vector2(0f, 0.5f);
                labelRect.pivot = new Vector2(0f, 0.5f);
                labelRect.anchoredPosition = new Vector2(30f, 0f);
                labelRect.sizeDelta = new Vector2(190f, 22f);
            }

            labelText.raycastTarget = false;
            var clickAction = (UnityAction)(() => ToggleMap(viewPointer, sceneType, gameModeType));
            button.onClick.AddListener(clickAction);
            return new PortalMapOptionUiState(sceneType, gameModeType, optionObject, backgroundImage, checkboxOutlineImage, checkboxFillImage, labelText, button, clickAction);
        }
        catch (Exception exception)
        {
            _logger?.LogError($"CreateMapOption failed for {sceneType} / {gameModeType}: {exception}");
            return null;
        }
    }

    private static void ToggleMap(IntPtr viewPointer, SceneType sceneType, GameModeType gameModeType)
    {
        var activeSelections = PreferredMapSelection.GetSelectedMaps(gameModeType);
        if (activeSelections.Contains(sceneType))
        {
            if (activeSelections.Count == 1)
            {
                return;
            }

            activeSelections.Remove(sceneType);
        }
        else
        {
            activeSelections.Add(sceneType);
        }

        if (UiStateByView.TryGetValue(viewPointer, out var state) && state.IsAlive)
        {
            RefreshMapSection(state);
        }
    }

    private static void RefreshMapOptionVisual(PortalMapOptionUiState option, bool selected, GameModeType activeMode)
    {
        option.RowBackgroundImage.color = new Color(1f, 1f, 1f, 0f);
        option.CheckboxOutlineImage.color = MapCheckboxOutlineColor;
        option.CheckboxFillImage.color = selected
            ? activeMode == GameModeType.Berek ? CrownModeColor : ClassicModeColor
            : MapCheckboxOffFillColor;
    }

    private static string FormatMapName(SceneType sceneType)
    {
        return sceneType switch
        {
            SceneType.Map01 => "Map 1",
            SceneType.Map02 => "Map 2",
            SceneType.Map03 => "Map 3",
            SceneType.Map04 => "Map 4",
            SceneType.Map_East01 => "East 1",
            SceneType.Map_East02 => "East 2",
            SceneType.Map_School01 => "School 1",
            SceneType.Map_School02 => "School 2",
            SceneType.Map05_TagGame => "Crown arena",
            _ => sceneType.ToString()
        };
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

    private static void ApplyModePanelLayout(RectTransform panel, Vector2 anchorMin, Vector2 anchorMax, float localX, bool animate)
    {
        var localPosition = panel.localPosition;
        if (!animate)
        {
            panel.anchorMin = anchorMin;
            panel.anchorMax = anchorMax;
            panel.localPosition = new Vector3(localX, localPosition.y, localPosition.z);
            return;
        }

        ShortcutExtensions.DOKill(panel, false);
        DOTweenModuleUI.DOAnchorMin(panel, anchorMin, ToggleAnimationDuration, false);
        DOTweenModuleUI.DOAnchorMax(panel, anchorMax, ToggleAnimationDuration, false);
        ShortcutExtensions.DOLocalMoveX(panel, localX, ToggleAnimationDuration, false);
    }

    private static void ApplyModeCheckboxVisual(PortalModeUiState state, bool classicSelected, bool animate)
    {
        var targetColor = classicSelected ? ClassicModeColor : CrownModeColor;
        state.ModeButton._currentColor = targetColor;
        state.ModeButton._isSelected = !classicSelected;
        state.CheckboxVictimImage.gameObject.SetActive(classicSelected);
        state.CheckboxHunterImage.gameObject.SetActive(!classicSelected);
        state.CheckboxVictimImage.sprite = state.ClassicIconSprite;
        state.CheckboxHunterImage.sprite = classicSelected ? state.ClassicIconSprite : ResolveCrownIconSprite() ?? state.ClassicIconSprite;
        state.CheckboxVictimImage.color = Color.white;
        state.CheckboxHunterImage.color = Color.white;
        state.CheckboxOutlineImage.color = Color.white;

        if (!animate)
        {
            state.CheckboxBackgroundImage.color = targetColor;
            return;
        }

        ShortcutExtensions.DOKill(state.CheckboxBackgroundImage, false);
        DOTweenModuleUI.DOColor(state.CheckboxBackgroundImage, targetColor, ToggleAnimationDuration);
    }

    private static Sprite? ResolveCrownIconSprite()
    {
        if (_crownIconSprite is not null && _crownIconSprite.Pointer != IntPtr.Zero)
        {
            return _crownIconSprite;
        }

        var sprites = Resources.FindObjectsOfTypeAll<Sprite>();
        var preferredSprite = sprites.FirstOrDefault(sprite =>
            sprite is not null
            && sprite.name.Contains("crown", StringComparison.OrdinalIgnoreCase)
            && !sprite.name.Contains("crowns", StringComparison.OrdinalIgnoreCase)
            && !sprite.name.Contains("currency", StringComparison.OrdinalIgnoreCase));

        _crownIconSprite = preferredSprite ?? sprites.FirstOrDefault(sprite =>
            sprite is not null && sprite.name.Contains("crown", StringComparison.OrdinalIgnoreCase));

        return _crownIconSprite;
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
