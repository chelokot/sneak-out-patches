using BepInEx.Logging;
using DG.Tweening;
using Events;
using Gameplay.Player.Components;
using HarmonyLib;
using Il2CppSystem.Collections;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Kinguinverse.DataUtils.Events;
using Networking.PGOS;
using System.Reflection;
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
    public static readonly SceneType[] ClassicMapPool =
    {
        SceneType.Map01,
        SceneType.Map02,
        SceneType.Map03,
        SceneType.Map04,
        SceneType.Map_East01,
        SceneType.Map_East02
    };

    public static readonly SceneType[] CrownMapPool =
    {
        SceneType.Map_School01,
        SceneType.Map_School02,
        SceneType.Map05_TagGame
    };

    private static readonly Dictionary<IntPtr, PortalModeUiState> UiStateByView = new();
    private static readonly Dictionary<IntPtr, GameModeType> SelectedModeByView = new();
    private static readonly Dictionary<IntPtr, PortalMapSelectionState> SelectedMapsByView = new();
    private static readonly Color ClassicModeColor = new(0.08627451f, 0.5372549f, 0.654902f, 1f);
    private static readonly Color CrownModeColor = new(0.8117647f, 0.62352943f, 0f, 1f);
    private static readonly Color MapOptionHoverColor = new(1f, 1f, 1f, 0.16f);
    private static readonly Color MapOptionPressedColor = new(1f, 1f, 1f, 0.24f);
    private static readonly Color MapCheckboxOutlineColor = new(1f, 1f, 1f, 0.92f);
    private static readonly Color MapCheckboxOffFillColor = new(0.04f, 0.06f, 0.12f, 0.9f);
    private const float ToggleAnimationDuration = 0.36f;

    private static ManualLogSource? _logger;
    private static Harmony? _harmony;
    private static IntPtr _pendingPlayViewPointer;
    private static GameModeType? _lastRequestedMode;
    private static bool _portalTreeLogged;
    private static bool _mapsToPlayOnLogged;
    private static bool _kinguinverseTypeLogged;
    private static Sprite? _crownIconSprite;
    private static bool _crownIconSearchCompleted;
    private static MethodInfo? _handleBerekModeStartMethod;
    private static bool _berekStartupTriggered;

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

        if (!_portalTreeLogged)
        {
            _portalTreeLogged = true;
            LogPortalViewTree(view, roleSectionRoot, roleRowRoot, privateSectionRoot, privateRowRoot);
        }

        if (!_mapsToPlayOnLogged)
        {
            _mapsToPlayOnLogged = true;
            LogMapsToPlayOn(view);
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
        var mapSelectionState = SelectedMapsByView.TryGetValue(viewPointer, out var existingMapSelectionState)
            ? existingMapSelectionState
            : new PortalMapSelectionState();
        SelectedMapsByView[viewPointer] = mapSelectionState;
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
            _logger?.LogWarning("Portal selector map section disabled: failed to create any map options");
            UnityEngine.Object.Destroy(mapSectionObject);
            mapSectionObject = modeSectionObject;
            mapTitleText = labelText;
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
        if (!SelectedModeByView.ContainsKey(viewPointer))
        {
            SelectedModeByView[viewPointer] = GameModeType.Default;
        }

        _logger?.LogInfo(
            $"Mode row positions prepared: leftClassic={leftClassicX}, leftCrown={leftCrownX}, rightClassic={rightClassicX}, rightCrown={rightCrownX}, checkbox={checkboxRect?.anchoredPosition}, " +
            $"sourceVictim={view._victimMovingPanel?.anchoredPosition}, sourceHunter={view._hunterMovingPanel?.anchoredPosition}");

        LayoutModeRow(modeState);
        RefreshModeRow(modeState, false);
        if (modeState.MapOptions.Length > 0)
        {
            RefreshMapSection(modeState);
        }
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
        PublishRequestedGameMode(nextMode);
        RefreshModeRow(state, true);
        if (state.MapOptions.Length > 0)
        {
            RefreshMapSection(state);
        }
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
        PublishRequestedGameMode(nextMode);
        RefreshModeRow(state, true);
        if (state.MapOptions.Length > 0)
        {
            RefreshMapSection(state);
        }
        _logger?.LogInfo($"Portal mode button clicked, toggled to {nextMode}");
    }

    public static bool TryHandlePlay(PortalPlayView view)
    {
        _pendingPlayViewPointer = view.Pointer;
        var selectedMode = GetSelectedMode(view);
        _lastRequestedMode = selectedMode;
        _berekStartupTriggered = false;
        PublishRequestedGameMode(selectedMode);
        _logger?.LogInfo($"Portal play pressed with requested mode {selectedMode}");
        return false;
    }

    public static void LogPortalPlayViewClose(PortalPlayView view)
    {
        _logger?.LogInfo($"PortalPlayView.Close invoked for view 0x{view.Pointer:x}");
    }

    public static void LogGameUiLobbyPlay(UI.GameUIManager gameUiManager, string stage)
    {
        var openedViews = new List<string>();
        foreach (var viewType in UI.GameUIManager.CurrentViewsOpened)
        {
            openedViews.Add(viewType.ToString());
        }

        _logger?.LogInfo(
            $"GameUIManager.OnLobbyPlayButton {stage}: currentUI={UI.GameUIManager.CurrentUIOpened}, opened=[{string.Join(", ", openedViews)}]");
    }

    public static void LogMatchmakerOnStart(Matchmaker matchmaker, Il2CppSystem.Object? sender, Il2CppSystem.EventArgs? args, string stage)
    {
        var senderDescription = sender is null ? "<null>" : sender.GetType().FullName ?? sender.GetType().Name;
        var argsDescription = args is null ? "<null>" : args.GetType().FullName ?? args.GetType().Name;
        _logger?.LogInfo(
            $"Matchmaker.OnStartMatchmaking {stage}: sender={senderDescription}, args={argsDescription}, currentTicket={matchmaker.CurrentMatchmakingTicket ?? "<null>"}");
    }

    public static void LogFindPlayersByMatchmaking(Matchmaker matchmaker, GameModeType gameModeType, string stage)
    {
        _logger?.LogInfo(
            $"Matchmaker.FindPlayersByMatchmaking {stage}: requestedMode={gameModeType}, ticket={matchmaker.CurrentMatchmakingTicket ?? "<null>"}, isInMatchmaking={matchmaker.IsInMatchmaking}");
    }

    public static void LogMatchmakingView(MatchmakingView matchmakingView, string stage)
    {
        _logger?.LogInfo(
            $"MatchmakingView {stage}: playButtonsPanelActive={matchmakingView._playButtonsPanel?.activeSelf}, waitingPanelActive={matchmakingView._waitingPanel?.activeSelf}");
    }

    public static void LogMatchmakingViewState(MatchmakingView matchmakingView, Gameplay.LobbyMatchmakingStateType state, string stage)
    {
        _logger?.LogInfo(
            $"MatchmakingView {stage}: state={state}, playButtonsPanelActive={matchmakingView._playButtonsPanel?.activeSelf}, waitingPanelActive={matchmakingView._waitingPanel?.activeSelf}, playButtonText={(matchmakingView._playButtonText is null ? "<null>" : matchmakingView._playButtonText.text)}");
    }

    public static bool TryOverrideMatchMode(ref GameModeType gameModeType)
    {
        if (!TryGetEffectiveRequestedMode(out var selectedMode))
        {
            return false;
        }

        gameModeType = selectedMode;
        _logger?.LogInfo($"Match mode overridden to {selectedMode}");
        return true;
    }

    public static bool TryRedirectDefaultModeStart(object controller, CharacterType seekerCharacterType, ref IEnumerator enumerator)
    {
        if (!TryGetEffectiveRequestedMode(out var selectedMode) || selectedMode != GameModeType.Berek)
        {
            _logger?.LogInfo($"HandleDefaultModeStart entered with effective mode {selectedMode}");
            return false;
        }

        _handleBerekModeStartMethod ??= AccessTools.Method(controller.GetType(), "HandleBerekModeStart");
        if (_handleBerekModeStartMethod is null)
        {
            _logger?.LogWarning("HandleDefaultModeStart redirect failed: HandleBerekModeStart method not found");
            return false;
        }

        var redirectedEnumerator = _handleBerekModeStartMethod.Invoke(controller, new object[] { seekerCharacterType }) as IEnumerator;
        if (redirectedEnumerator is null)
        {
            _logger?.LogWarning("HandleDefaultModeStart redirect failed: HandleBerekModeStart returned null");
            return false;
        }

        _logger?.LogInfo($"Redirected HandleDefaultModeStart to HandleBerekModeStart for seeker {seekerCharacterType}");
        enumerator = redirectedEnumerator;
        return true;
    }

    public static void LogBerekModeStart(CharacterType seekerCharacterType)
    {
        WireAllBerekComponents("HandleBerekModeStart");
        _logger?.LogInfo($"HandleBerekModeStart entered for seeker {seekerCharacterType}");
    }

    public static void LogHandleSeeker(CharacterType seekerCharacterType)
    {
        if (!TryGetEffectiveRequestedMode(out var selectedMode))
        {
            return;
        }

        _logger?.LogInfo($"HandleSeeker entered with effective mode {selectedMode} and seeker {seekerCharacterType}");
    }

    public static void LogPrepareVictims(CharacterType seekerCharacterType)
    {
        if (!TryGetEffectiveRequestedMode(out var selectedMode))
        {
            return;
        }

        _logger?.LogInfo($"PrepareVictims entered with effective mode {selectedMode} and seeker {seekerCharacterType}");
    }

    public static void LogConfirmSeekerCharacterEvent(Il2CppSystem.EventArgs? args)
    {
        if (!TryGetEffectiveRequestedMode(out var selectedMode))
        {
            return;
        }

        _logger?.LogInfo($"OnConfirmSeekerCharacterEvent entered with effective mode {selectedMode}; argsType={DescribeArgsType(args)}");
    }

    public static bool TryStartBerekModeFromPrepareVictims(object controller, CharacterType seekerCharacterType)
    {
        if (!TryGetEffectiveRequestedMode(out var selectedMode))
        {
            return false;
        }

        _logger?.LogInfo($"PrepareVictims entered with effective mode {selectedMode} and seeker {seekerCharacterType}");
        if (selectedMode != GameModeType.Berek)
        {
            return false;
        }

        WireAllBerekComponents("PrepareVictims");
        if (_berekStartupTriggered)
        {
            _logger?.LogInfo("PrepareVictims skipped original path because Berek startup already triggered");
            return true;
        }

        if (controller is not MonoBehaviour monoBehaviour)
        {
            _logger?.LogWarning("PrepareVictims Berek redirect failed: controller is not MonoBehaviour");
            return false;
        }

        _handleBerekModeStartMethod ??= AccessTools.Method(controller.GetType(), "HandleBerekModeStart");
        if (_handleBerekModeStartMethod is null)
        {
            _logger?.LogWarning("PrepareVictims Berek redirect failed: HandleBerekModeStart method not found");
            return false;
        }

        var berekEnumerator = _handleBerekModeStartMethod.Invoke(controller, new object[] { seekerCharacterType }) as IEnumerator;
        if (berekEnumerator is null)
        {
            _logger?.LogWarning("PrepareVictims Berek redirect failed: HandleBerekModeStart returned null");
            return false;
        }

        monoBehaviour.StartCoroutine(berekEnumerator);
        _berekStartupTriggered = true;
        _logger?.LogInfo($"PrepareVictims redirected to HandleBerekModeStart for seeker {seekerCharacterType}");
        return true;
    }

    public static void LogKinguinverseStartMatch(object matchStateHelper)
    {
        if (!TryGetEffectiveRequestedMode(out var selectedMode))
        {
            _logger?.LogInfo("KinguinverseStartMatch entered; no effective requested mode");
            return;
        }

        var gameStateProperty = AccessTools.Property(matchStateHelper.GetType(), "_gameState");
        var gameState = gameStateProperty?.GetValue(matchStateHelper, null);
        if (gameState is null)
        {
            _logger?.LogInfo("KinguinverseStartMatch entered; gameState unresolved");
            return;
        }

        var gameModeProperty = AccessTools.Property(gameState.GetType(), "GameMode");
        var incomingMode = gameModeProperty?.GetValue(gameState, null);
        _logger?.LogInfo($"KinguinverseStartMatch entered; requestedMode={selectedMode}, gameState.GameMode={incomingMode}");

        if (selectedMode != GameModeType.Berek || gameModeProperty is null)
        {
            return;
        }

        if (!_kinguinverseTypeLogged)
        {
            _kinguinverseTypeLogged = true;
            _logger?.LogInfo(
                $"KinguinverseStartMatch types: helperType={matchStateHelper.GetType().FullName}, gameStateType={gameState.GetType().FullName}, gameModePropertyType={gameModeProperty.PropertyType.FullName}");
        }

        var coercedMode = Enum.ToObject(gameModeProperty.PropertyType, (int)GameModeType.Berek);
        gameModeProperty.SetValue(gameState, coercedMode, null);
        var forcedMode = gameModeProperty.GetValue(gameState, null);
        _logger?.LogInfo($"KinguinverseStartMatch forced gameState.GameMode to {forcedMode}");
    }

    public static bool TryRedirectBeforeSelectionState(object beforeSelectionState, object stateMachine)
    {
        if (!TryGetEffectiveRequestedMode(out var selectedModeForLog))
        {
            _logger?.LogInfo("BeforeSelectionState.Tick entered; no effective requested mode");
            return false;
        }

        _logger?.LogInfo($"BeforeSelectionState.Tick entered with effective mode {selectedModeForLog}");

        if (selectedModeForLog != GameModeType.Berek)
        {
            return false;
        }

        var berekSelectionStateProperty = AccessTools.Property(stateMachine.GetType(), "BerekSelectionState");
        var berekSelectionState = berekSelectionStateProperty?.GetValue(stateMachine, null);
        if (berekSelectionState is null)
        {
            _logger?.LogWarning("BeforeSelectionState redirect failed: BerekSelectionState unresolved");
            return false;
        }

        var enqueueSwitchStateMethod = AccessTools.Method(stateMachine.GetType(), "EnqueueSwitchState");
        if (enqueueSwitchStateMethod is null)
        {
            _logger?.LogWarning("BeforeSelectionState redirect failed: EnqueueSwitchState unresolved");
            return false;
        }

        enqueueSwitchStateMethod.Invoke(stateMachine, new[] { berekSelectionState });
        _logger?.LogInfo("BeforeSelectionState redirected to BerekSelectionState");
        return true;
    }

    public static void LogBerekSelectionStateTick(object berekSelectionState, object stateMachine)
    {
        if (!TryGetEffectiveRequestedMode(out var selectedMode))
        {
            return;
        }

        _logger?.LogInfo($"BerekSelectionState.Tick entered with effective mode {selectedMode}");
    }

    public static void WireAllBerekComponents(string stage)
    {
        var gameObjects = Resources.FindObjectsOfTypeAll<GameObject>();
        var wiredPlayers = 0;
        var foundPlayers = 0;
        foreach (var gameObject in gameObjects)
        {
            var player = gameObject.GetComponent("SpookedNetworkPlayer");
            if (player is null)
            {
                continue;
            }

            foundPlayers++;
            if (TryWireBerekComponent(player, stage))
            {
                wiredPlayers++;
            }
        }

        _logger?.LogInfo($"{stage} wired EntityBerekComponent for {wiredPlayers}/{foundPlayers} players");
    }

    public static void WirePlayerBerekComponent(object player, string stage)
    {
        TryWireBerekComponent(player, stage);
    }

    public static void LogGivePlayerCrown(object controller)
    {
        _logger?.LogInfo("GivePlayerCrown entered");
        WireAllBerekComponents("GivePlayerCrown");
    }

    public static void LogInitializeBerekComponents(object controller)
    {
        _logger?.LogInfo("InitializeBerekComponents entered");
        WireAllBerekComponents("InitializeBerekComponents");
    }

    public static void LogEntityBerekHandleCrown(EntityBerekComponent component)
    {
        var crownObject = component._crownObject;
        _logger?.LogInfo(
            $"EntityBerekComponent.HandleCrown entered: internalId={component.InternalId}, hasCrown={component.HasCrown()}, crownActive={crownObject?.activeSelf}");
    }

    public static void TryOverrideKinguinverseTag(object displayClassInstance)
    {
        if (!TryGetEffectiveRequestedMode(out var selectedMode))
        {
            _logger?.LogInfo("KinguinverseStartMatch closure entered; no effective requested mode");
            return;
        }

        var isTagProperty = AccessTools.Property(displayClassInstance.GetType(), "isTag");
        if (isTagProperty is null)
        {
            _logger?.LogInfo($"KinguinverseStartMatch closure entered; isTag field unresolved on {displayClassInstance.GetType().FullName}");
            return;
        }

        var incomingValue = isTagProperty.GetValue(displayClassInstance, null);
        var targetValue = selectedMode == GameModeType.Berek;
        isTagProperty.SetValue(displayClassInstance, targetValue, null);
        var forcedValue = isTagProperty.GetValue(displayClassInstance, null);
        _logger?.LogInfo(
            $"KinguinverseStartMatch closure tag override: requestedMode={selectedMode}, incomingIsTag={incomingValue}, forcedIsTag={forcedValue}");
    }

    public static void TryOverrideWebMatchTag(ref bool value)
    {
        if (!TryGetEffectiveRequestedMode(out var selectedMode))
        {
            return;
        }

        var targetValue = selectedMode == GameModeType.Berek;
        if (value == targetValue)
        {
            return;
        }

        _logger?.LogInfo($"WebMatch.Tag overridden from {value} to {targetValue} for requestedMode={selectedMode}");
        value = targetValue;
    }

    public static bool TryOverrideRandomScene(Il2CppStructArray<SceneType> mapsToPlayOn, GameModeType gameModeType, ref SceneType sceneType)
    {
        if (!TryGetActiveMapSelectionState(out var mapSelectionState))
        {
            return false;
        }

        var effectiveMode = TryGetEffectiveRequestedMode(out var selectedMode)
            ? selectedMode
            : gameModeType;
        var selectedPool = mapSelectionState.GetSelectedMaps(effectiveMode).ToArray();
        if (selectedPool.Length == 0)
        {
            return false;
        }

        sceneType = selectedPool[UnityEngine.Random.Range(0, selectedPool.Length)];
        _logger?.LogInfo(
            $"GetRandomScene overridden to {sceneType} for mode {effectiveMode} (incoming {gameModeType}); sourceMaps=[{string.Join(", ", mapsToPlayOn.Select(map => map.ToString()))}], selectedPool=[{string.Join(", ", selectedPool.Select(map => map.ToString()))}]");
        return true;
    }

    public static void TryOverrideStartMatchmakingArgs(Il2CppSystem.EventArgs? args)
    {
        if (!TryGetEffectiveRequestedMode(out var selectedMode))
        {
            return;
        }

        if (args is not StartMatchmakingEvent matchmakingEvent)
        {
            _logger?.LogInfo($"Matchmaker.OnStartMatchmaking args not overridden: {DescribeArgsType(args)}");
            return;
        }

        var incomingMode = matchmakingEvent.GameModeType;
        matchmakingEvent.GameModeType = selectedMode;
        _logger?.LogInfo($"StartMatchmakingEvent mode overridden from {incomingMode} to {selectedMode}");
    }

    public static void TryOverrideRequestChangeGameModeArgs(Il2CppSystem.EventArgs? args)
    {
        if (!TryGetEffectiveRequestedMode(out var selectedMode))
        {
            return;
        }

        if (args is not RequestChangeGameModeEvent changeGameModeEvent)
        {
            _logger?.LogInfo($"RequestChangeGameModeEvent args not overridden: {DescribeArgsType(args)}");
            return;
        }

        var incomingMode = changeGameModeEvent.RequestedGameModeType;
        changeGameModeEvent.RequestedGameModeType = selectedMode;
        _logger?.LogInfo($"RequestChangeGameModeEvent mode overridden from {incomingMode} to {selectedMode}");
    }

    public static void TryOverrideSendMatchInfoArgs(Il2CppSystem.EventArgs? args)
    {
        if (!TryGetEffectiveRequestedMode(out var selectedMode))
        {
            return;
        }

        if (args is not SendMatchInfoToTeamEvent sendMatchInfoEvent)
        {
            _logger?.LogInfo($"SendMatchInfoToTeamEvent args not overridden: {DescribeArgsType(args)}");
            return;
        }

        var incomingMode = sendMatchInfoEvent.SelectedGameModeType;
        sendMatchInfoEvent.SelectedGameModeType = selectedMode;
        _logger?.LogInfo($"SendMatchInfoToTeamEvent mode overridden from {incomingMode} to {selectedMode} for match {sendMatchInfoEvent.MatchId}");
    }

    private static void PublishRequestedGameMode(GameModeType selectedMode)
    {
        try
        {
            _lastRequestedMode = selectedMode;
            GameEventsManager.Publish<RequestChangeGameModeEvent>(null, new RequestChangeGameModeEvent(selectedMode));
            _logger?.LogInfo($"Published RequestChangeGameModeEvent: {selectedMode}");
        }
        catch (Exception exception)
        {
            LogError("Portal selector failed to publish RequestChangeGameModeEvent", exception);
        }
    }

    public static GameModeType GetSelectedMode(PortalPlayView view)
    {
        return SelectedModeByView.TryGetValue(view.Pointer, out var selectedMode)
            ? selectedMode
            : GameModeType.Default;
    }

    private static bool TryGetEffectiveRequestedMode(out GameModeType selectedMode)
    {
        if (_lastRequestedMode.HasValue)
        {
            selectedMode = _lastRequestedMode.Value;
            return true;
        }

        if (_pendingPlayViewPointer != IntPtr.Zero && SelectedModeByView.TryGetValue(_pendingPlayViewPointer, out selectedMode))
        {
            return true;
        }

        selectedMode = GameModeType.Default;
        return false;
    }

    private static bool TryWireBerekComponent(object player, string stage)
    {
        var playerType = player.GetType();
        var entityBerekProperty = AccessTools.Property(playerType, "EntityBerekComponent");
        var gameObjectProperty = AccessTools.Property(playerType, "gameObject");
        var internalIdProperty = AccessTools.Property(playerType, "InternalId");
        var characterTypeProperty = AccessTools.Property(playerType, "CharacterType");
        if (entityBerekProperty is null || gameObjectProperty is null)
        {
            return false;
        }

        var entityBerekComponent = entityBerekProperty.GetValue(player, null) as EntityBerekComponent;
        if (entityBerekComponent is null)
        {
            var playerGameObject = gameObjectProperty.GetValue(player, null) as GameObject;
            if (playerGameObject is null)
            {
                return false;
            }

            entityBerekComponent = playerGameObject.GetComponent<EntityBerekComponent>();
        }

        if (entityBerekComponent is null)
        {
            return false;
        }

        var changed = false;
        if (entityBerekProperty.GetValue(player, null) is null)
        {
            entityBerekProperty.SetValue(player, entityBerekComponent, null);
            changed = true;
        }

        var berekType = entityBerekComponent.GetType();
        var spookedNetworkPlayerProperty = AccessTools.Property(berekType, "_spookedNetworkPlayer");
        if (spookedNetworkPlayerProperty is not null && spookedNetworkPlayerProperty.GetValue(entityBerekComponent, null) is null)
        {
            spookedNetworkPlayerProperty.SetValue(entityBerekComponent, player, null);
            changed = true;
        }

        if (changed)
        {
            var internalId = internalIdProperty?.GetValue(player, null);
            var characterType = characterTypeProperty?.GetValue(player, null);
            var pointerProperty = AccessTools.Property(berekType, "Pointer");
            var componentPointer = pointerProperty?.GetValue(entityBerekComponent, null);
            _logger?.LogInfo(
                $"{stage} wired EntityBerekComponent for player {internalId} ({characterType}) component={componentPointer}");
        }

        return true;
    }

    private static bool TryGetActiveMapSelectionState(out PortalMapSelectionState mapSelectionState)
    {
        if (_pendingPlayViewPointer != IntPtr.Zero
            && SelectedMapsByView.TryGetValue(_pendingPlayViewPointer, out var pendingMapSelectionState))
        {
            mapSelectionState = pendingMapSelectionState;
            return true;
        }

        foreach (var state in UiStateByView.Values)
        {
            if (!state.IsAlive)
            {
                continue;
            }

            if (SelectedMapsByView.TryGetValue(state.View.Pointer, out var activeMapSelectionState))
            {
                mapSelectionState = activeMapSelectionState;
                return true;
            }
        }

        mapSelectionState = null!;
        return false;
    }

    private static string DescribeArgsType(Il2CppSystem.EventArgs? args)
    {
        if (args is null)
        {
            return "<null>";
        }

        return args.GetType().FullName ?? args.GetType().Name;
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

    private static void RefreshModeRow(PortalModeUiState state, bool animate)
    {
        state.LabelText.text = "Game mode";
        state.LeftText.text = "Classic";
        state.RightText.text = "Crown";
        var classicSelected = state.SelectedMode == GameModeType.Default;
        var selectedColor = classicSelected ? ClassicModeColor : CrownModeColor;

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
        _logger?.LogInfo(
            $"Mode row refresh: selected={state.SelectedMode}, leftPos={DescribeRectDetailed(state.LeftMovingPanel)}, rightPos={DescribeRectDetailed(state.RightMovingPanel)}");
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
        var singleX = -84f;
        var activeMode = state.SelectedMode == GameModeType.Berek ? GameModeType.Berek : GameModeType.Default;
        var activeSelections = SelectedMapsByView[state.View.Pointer].GetSelectedMaps(activeMode);

        var classicLayout = new Dictionary<SceneType, Vector2>
        {
            [SceneType.Map01] = new Vector2(leftX, lineStartY),
            [SceneType.Map02] = new Vector2(rightX, lineStartY),
            [SceneType.Map03] = new Vector2(leftX, lineStartY - lineSpacing),
            [SceneType.Map04] = new Vector2(rightX, lineStartY - lineSpacing),
            [SceneType.Map_East01] = new Vector2(singleX, lineStartY - lineSpacing * 2f),
            [SceneType.Map_East02] = new Vector2(singleX, lineStartY - lineSpacing * 3f)
        };

        var crownLayout = new Dictionary<SceneType, Vector2>
        {
            [SceneType.Map_School01] = new Vector2(singleX, lineStartY),
            [SceneType.Map_School02] = new Vector2(singleX, lineStartY - lineSpacing),
            [SceneType.Map05_TagGame] = new Vector2(singleX, lineStartY - lineSpacing * 2f)
        };

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
            optionRect.sizeDelta = option.GameModeType == GameModeType.Default ? new Vector2(150f, 24f) : new Vector2(196f, 24f);
            optionRect.anchoredPosition = option.GameModeType == GameModeType.Default
                ? classicLayout[option.SceneType]
                : crownLayout[option.SceneType];

            option.LabelText.fontSize = 18f;
            option.LabelText.alignment = TextAlignmentOptions.Left;
            option.LabelText.text = option.SceneType.ToString();
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

        foreach (var map in ClassicMapPool)
        {
            var option = CreateMapOption(viewPointer, mapSectionTransform, textTemplate, rowBackgroundSprite, checkboxOutlineSprite, checkboxFillSprite, map, GameModeType.Default);
            if (option is not null)
            {
                options.Add(option);
            }
        }

        foreach (var map in CrownMapPool)
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
            checkboxOutlineImage.sprite = null;
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
            checkboxFillImage.sprite = null;
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
        if (!SelectedMapsByView.TryGetValue(viewPointer, out var mapSelectionState))
        {
            return;
        }

        var activeSelections = mapSelectionState.GetSelectedMaps(gameModeType);
        if (activeSelections.Contains(sceneType))
        {
            if (activeSelections.Count == 1)
            {
                _logger?.LogInfo($"Map toggle ignored for {sceneType}: would clear the last available map for {gameModeType}");
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

        _logger?.LogInfo($"Map selection toggled for {gameModeType}: [{string.Join(", ", activeSelections.Select(map => map.ToString()))}]");
    }

    private static void RefreshMapOptionVisual(PortalMapOptionUiState option, bool selected, GameModeType activeMode)
    {
        option.RowBackgroundImage.color = new Color(1f, 1f, 1f, 0f);
        option.CheckboxOutlineImage.color = MapCheckboxOutlineColor;
        option.CheckboxFillImage.color = selected ? ClassicModeColor : MapCheckboxOffFillColor;
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

    private static void LogMapsToPlayOn(PortalPlayView view)
    {
        try
        {
            var maps = view._spookedSettings?.MapsToPlayOn;
            if (maps is null)
            {
                _logger?.LogWarning("MapsToPlayOn log skipped: _spookedSettings or MapsToPlayOn is null");
                return;
            }

            var sceneNames = new List<string>();
            foreach (var sceneType in maps)
            {
                sceneNames.Add(sceneType.ToString());
            }

            _logger?.LogInfo($"MapsToPlayOn: [{string.Join(", ", sceneNames)}]");
        }
        catch (Exception exception)
        {
            _logger?.LogError($"MapsToPlayOn log failed: {exception}");
        }
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
        if (_crownIconSearchCompleted)
        {
            return _crownIconSprite;
        }

        _crownIconSearchCompleted = true;
        var sprites = Resources.FindObjectsOfTypeAll<Sprite>();
        var preferredSprite = sprites.FirstOrDefault(sprite =>
            sprite is not null
            && sprite.name.Contains("crown", StringComparison.OrdinalIgnoreCase)
            && !sprite.name.Contains("crowns", StringComparison.OrdinalIgnoreCase)
            && !sprite.name.Contains("currency", StringComparison.OrdinalIgnoreCase));

        _crownIconSprite = preferredSprite ?? sprites.FirstOrDefault(sprite =>
            sprite is not null && sprite.name.Contains("crown", StringComparison.OrdinalIgnoreCase));

        _logger?.LogInfo(_crownIconSprite is null
            ? "Crown icon sprite not found in loaded resources"
            : $"Crown icon sprite resolved: {_crownIconSprite.name}");

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
