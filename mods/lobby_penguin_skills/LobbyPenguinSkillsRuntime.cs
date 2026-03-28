using BepInEx.Logging;
using Gameplay.Player.Components;
using HarmonyLib;
using UI;
using UI.MVVM;
using UI.Views;
using UnityEngine;
using ClientCharacterType = Types.CharacterType;
using Types;

namespace SneakOut.LobbyPenguinSkills;

internal static class LobbyPenguinSkillsRuntime
{
    private static ManualLogSource? _logger;
    private static Harmony? _harmony;
    private static LobbyPenguinSkillsConfig? _configuration;
    private static bool _lobbyUiActive;

    private static readonly Type? GameType = AccessTools.TypeByName("Game");
    private static readonly System.Reflection.PropertyInfo? GameInternalIdProperty =
        GameType is null ? null : AccessTools.Property(GameType, "InternalId");
    private static readonly System.Reflection.MethodInfo? GameUiManagerGetPlayerActionsViewMethod =
        AccessTools.Method(typeof(GameUIManager), "get__playerActionsView");
    private static readonly System.Reflection.MethodInfo? GameUiManagerGetTutorialPlayerActionsViewMethod =
        AccessTools.Method(typeof(GameUIManager), "get__tutorialPlayerActionsView");
    private static readonly System.Reflection.MethodInfo? GameUiManagerGetNetworkPlayerRegistryMethod =
        AccessTools.Method(typeof(GameUIManager), "get__networkPlayerRegistry");
    private static readonly System.Reflection.MethodInfo? PlayerActionsViewModelSetCanBeVisibleMethod =
        AccessTools.Method(typeof(PlayerActionsViewModel), "set__canBeVisible");
    private static readonly System.Reflection.MethodInfo? PlayerActionsViewModelInitMethod =
        AccessTools.Method(typeof(PlayerActionsViewModel), "Init");
    private static readonly System.Reflection.MethodInfo? PlayerActionsViewModelRefreshSkillsMethod =
        AccessTools.Method(typeof(PlayerActionsViewModel), "RefreshSkills");
    private static readonly System.Reflection.MethodInfo? ViewGetViewModelMethod =
        AccessTools.Method(typeof(View<PlayerActionsViewModel>), "get_ViewModel");
    private static readonly System.Reflection.MethodInfo? ViewSetViewModelMethod =
        AccessTools.Method(typeof(View<PlayerActionsViewModel>), "set_ViewModel");
    private static readonly System.Reflection.MethodInfo? ViewInjectViewModelFromParentMethod =
        AccessTools.Method(typeof(View<PlayerActionsViewModel>), "InjectViewModelFromParent");
    private static readonly System.Reflection.MethodInfo? ViewAssertViewModelExistsMethod =
        AccessTools.Method(typeof(View<PlayerActionsViewModel>), "AssertViewModelExists");
    private static readonly System.Reflection.MethodInfo? EntitySkillsComponentOnVictimPropChangeMethod =
        AccessTools.Method(typeof(EntitySkillsComponent), "OnVictimPropChange");
    private static readonly System.Reflection.MethodInfo? EntitySkillsComponentOnFinishedBuffPropChangeMethod =
        AccessTools.Method(typeof(EntitySkillsComponent), "OnFinishedBuffPropChange");
    private static readonly System.Reflection.MethodInfo? EntitySkillsComponentHandleVictimSlideMethod =
        AccessTools.Method(typeof(EntitySkillsComponent), "HandleVictimSlide");
    private static readonly System.Reflection.MethodInfo? EntitySkillsComponentRefreshPlayerSkillsMethod =
        AccessTools.Method(typeof(EntitySkillsComponent), "RefreshPlayerSkills");
    private static readonly System.Reflection.MethodInfo? EntitySkillsComponentGetSkillMethod =
        AccessTools.Method(typeof(EntitySkillsComponent), "GetSkill");
    private static readonly System.Reflection.MethodInfo? EntitySkillsComponentRpcVictimPropChangeMethod =
        AccessTools.Method(typeof(EntitySkillsComponent), "RPC_VictimPropChange");
    private static readonly System.Reflection.MethodInfo? EntitySkillsComponentRpcVictimPropUnChangeMethod =
        AccessTools.Method(typeof(EntitySkillsComponent), "RPC_VictimPropUnChange");
    private static readonly System.Reflection.MethodInfo? EntitySkillsComponentChangeToPropMethod =
        AccessTools.Method(typeof(EntitySkillsComponent), "ChangeToProp");
    private static readonly System.Reflection.MethodInfo? EntitySkillsComponentChangeFromPropMethod =
        AccessTools.Method(typeof(EntitySkillsComponent), "ChangeFromProp");
    private static readonly System.Reflection.PropertyInfo? EntitySkillsComponentDuringPropChangeProperty =
        AccessTools.Property(typeof(EntitySkillsComponent), "DuringPropChange");
    private static readonly System.Reflection.FieldInfo? EntitySkillsComponentPlayerRoomRegistryField =
        AccessTools.Field(typeof(EntitySkillsComponent), "_playerRoomRegistry");
    private static readonly Type? PlayerRoomRegistryType = AccessTools.TypeByName("PlayerRoomRegistry");
    private static readonly System.Reflection.MethodInfo? PlayerRoomRegistryGetItemMethod =
        PlayerRoomRegistryType is null ? null : AccessTools.Method(PlayerRoomRegistryType, "get_Item");
    private static readonly Type? RoomType = AccessTools.TypeByName("Room");
    private static readonly System.Reflection.MethodInfo? RoomGetAvailablePropsMethod =
        RoomType is null ? null : AccessTools.Method(RoomType, "get_AvailableProps");
    private static readonly System.Reflection.PropertyInfo? SpookedNetworkPlayerInternalIdProperty =
        AccessTools.Property(typeof(SpookedNetworkPlayer), "InternalId");
    private static readonly System.Reflection.PropertyInfo? SpookedNetworkPlayerCharacterTypeProperty =
        AccessTools.Property(typeof(SpookedNetworkPlayer), "CharacterType");

    public static void Initialize(ManualLogSource logger, LobbyPenguinSkillsConfig configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _harmony ??= new Harmony(LobbyPenguinSkillsPlugin.PluginGuid);
        _harmony.PatchAll();
    }

    public static bool Enabled => _configuration is not null && _configuration.EnableMod.Value;

    public static void SetLobbyUiActive(bool active)
    {
        _lobbyUiActive = active;
        Log($"SetLobbyUiActive: active={active}");
    }

    public static void EnableLobbySkillView(GameUIManager gameUiManager)
    {
        if (!Enabled || !_configuration!.EnableLobbySkillUi.Value)
        {
            return;
        }

        var playerActionsView = GetPlayerActionsView(gameUiManager);
        if (playerActionsView is null)
        {
            Log("EnableLobbySkillView: noPlayerActionsView");
            return;
        }

        playerActionsView.gameObject.SetActive(true);
        ForcePlayerActionsViewVisible(gameUiManager, playerActionsView);
        Log("EnableLobbySkillView: activated");
    }

    public static void TryEnableLobbySkillViewAfterSpawn(SpookedNetworkPlayer networkPlayer)
    {
        if (!Enabled || !_configuration!.EnableLobbySkillUi.Value || !_lobbyUiActive)
        {
            return;
        }

        var currentInternalId = GetCurrentInternalId();
        var playerInternalId = SpookedNetworkPlayerInternalIdProperty?.GetValue(networkPlayer) as int? ?? 0;
        if (currentInternalId == 0 || playerInternalId != currentInternalId)
        {
            return;
        }

        var playerCharacterType = SpookedNetworkPlayerCharacterTypeProperty?.GetValue(networkPlayer) is ClientCharacterType characterType
            ? characterType
            : ClientCharacterType.spectator;
        if (playerCharacterType != ClientCharacterType.victim_penguin)
        {
            return;
        }

        var gameUiManager = UnityEngine.Object.FindObjectOfType<GameUIManager>();
        if (gameUiManager is null)
        {
            Log("TryEnableLobbySkillViewAfterSpawn: noGameUiManager");
            return;
        }

        EnableLobbySkillView(gameUiManager);
        Log("TryEnableLobbySkillViewAfterSpawn: enabled");
    }

    public static void ForcePlayerActionsViewVisible(PlayerActionsView playerActionsView)
    {
        ForcePlayerActionsViewVisible(null, playerActionsView);
    }

    private static void ForcePlayerActionsViewVisible(GameUIManager? gameUiManager, PlayerActionsView playerActionsView)
    {
        if (!Enabled || !_configuration!.EnableLobbySkillUi.Value)
        {
            return;
        }

        if (!_lobbyUiActive)
        {
            return;
        }

        TryEnsureViewModel(gameUiManager, playerActionsView);

        var viewModel = ViewGetViewModelMethod?.Invoke(playerActionsView, Array.Empty<object>()) as PlayerActionsViewModel;
        if (viewModel is null)
        {
            Log("ForcePlayerActionsViewVisible: noViewModel");
            return;
        }

        PlayerActionsViewModelSetCanBeVisibleMethod?.Invoke(viewModel, [true]);
        PlayerActionsViewModelRefreshSkillsMethod?.Invoke(viewModel, Array.Empty<object>());
        Log("ForcePlayerActionsViewVisible: refreshed");
    }

    public static bool TryHandleLobbySkillUse(EntitySkillsComponent entitySkillsComponent, bool secondSkill)
    {
        if (!Enabled || !_configuration!.EnableLobbySkillUse.Value || !_lobbyUiActive)
        {
            return false;
        }

        var networkPlayer = entitySkillsComponent.GetComponent<SpookedNetworkPlayer>();
        if (networkPlayer is null)
        {
            Log("TryHandleLobbySkillUse: noNetworkPlayer");
            return false;
        }

        var currentInternalId = GetCurrentInternalId();
        var playerInternalId = SpookedNetworkPlayerInternalIdProperty?.GetValue(networkPlayer) as int? ?? 0;
        if (currentInternalId == 0 || playerInternalId != currentInternalId)
        {
            return false;
        }

        var playerCharacterType = SpookedNetworkPlayerCharacterTypeProperty?.GetValue(networkPlayer) is ClientCharacterType characterType
            ? characterType
            : ClientCharacterType.spectator;
        if (playerCharacterType != ClientCharacterType.victim_penguin)
        {
            return false;
        }

        EntitySkillsComponentRefreshPlayerSkillsMethod?.Invoke(entitySkillsComponent, Array.Empty<object>());
        var resolvedSkillType = EntitySkillsComponentGetSkillMethod?.Invoke(entitySkillsComponent, new object[] { !secondSkill });
        var skillType = resolvedSkillType is SpookedSkillType currentSkillType
            ? currentSkillType
            : secondSkill ? entitySkillsComponent.SecondSkillType : entitySkillsComponent.FirstSkillType;
        Log($"TryHandleLobbySkillUse: second={secondSkill}, skill={skillType}, internalId={playerInternalId}");

        if (skillType == SpookedSkillType.VictimPropChange)
        {
            return TryHandleLobbyPropChange(entitySkillsComponent, playerInternalId);
        }

        if (skillType == SpookedSkillType.VictimSlide)
        {
            EntitySkillsComponentHandleVictimSlideMethod?.Invoke(entitySkillsComponent, Array.Empty<object>());
            return true;
        }

        return false;
    }

    public static void TryHandleLobbySkillHotkeys(EntitySkillsComponent entitySkillsComponent)
    {
    }

    public static bool TryHandleLobbySkillInput(Component? component, bool secondSkill)
    {
        if (component?.GetComponent<EntitySkillsComponent>() is not EntitySkillsComponent entitySkillsComponent)
        {
            return false;
        }

        Log($"TryHandleLobbySkillInput: second={secondSkill}");
        return TryHandleLobbySkillUse(entitySkillsComponent, secondSkill);
    }

    private static int GetCurrentInternalId()
    {
        return GameInternalIdProperty?.GetValue(null) as int? ?? 0;
    }

    private static bool TryHandleLobbyPropChange(EntitySkillsComponent entitySkillsComponent, int playerInternalId)
    {
        var duringPropChange = EntitySkillsComponentDuringPropChangeProperty?.GetValue(entitySkillsComponent) as bool? ?? false;
        Log($"TryHandleLobbyPropChange: during={duringPropChange}");

        if (duringPropChange)
        {
            EntitySkillsComponentOnFinishedBuffPropChangeMethod?.Invoke(entitySkillsComponent, Array.Empty<object>());
            EntitySkillsComponentChangeFromPropMethod?.Invoke(entitySkillsComponent, Array.Empty<object>());
            EntitySkillsComponentRpcVictimPropUnChangeMethod?.Invoke(entitySkillsComponent, Array.Empty<object>());
            return true;
        }

        EntitySkillsComponentOnVictimPropChangeMethod?.Invoke(entitySkillsComponent, Array.Empty<object>());

        var propType = TryGetLobbyPropType(entitySkillsComponent, playerInternalId);
        if (propType == PlayerPropType.None)
        {
            Log("TryHandleLobbyPropChange: noAvailableProp");
            return true;
        }

        Log($"TryHandleLobbyPropChange: propType={propType}");
        EntitySkillsComponentChangeToPropMethod?.Invoke(entitySkillsComponent, [propType]);
        EntitySkillsComponentRpcVictimPropChangeMethod?.Invoke(entitySkillsComponent, [propType]);
        return true;
    }

    private static PlayerPropType TryGetLobbyPropType(EntitySkillsComponent entitySkillsComponent, int playerInternalId)
    {
        var playerRoomRegistry = EntitySkillsComponentPlayerRoomRegistryField?.GetValue(entitySkillsComponent);
        if (playerRoomRegistry is null)
        {
            return PlayerPropType.None;
        }

        var room = PlayerRoomRegistryGetItemMethod?.Invoke(playerRoomRegistry, [playerInternalId]);
        if (room is null)
        {
            return PlayerPropType.None;
        }

        var availableProps = RoomGetAvailablePropsMethod?.Invoke(room, Array.Empty<object>()) as System.Collections.IEnumerable;
        if (availableProps is null)
        {
            return PlayerPropType.None;
        }

        var lobbyProps = new List<PlayerPropType>();
        foreach (var availableProp in availableProps)
        {
            if (availableProp is PlayerPropType playerPropType && playerPropType != PlayerPropType.None)
            {
                lobbyProps.Add(playerPropType);
            }
        }

        if (lobbyProps.Count == 0)
        {
            return PlayerPropType.None;
        }

        var randomIndex = UnityEngine.Random.Range(0, lobbyProps.Count);
        return lobbyProps[randomIndex];
    }

    private static void TryEnsureViewModel(GameUIManager? gameUiManager, PlayerActionsView playerActionsView)
    {
        if (ViewGetViewModelMethod?.Invoke(playerActionsView, Array.Empty<object>()) is PlayerActionsViewModel)
        {
            return;
        }

        try
        {
            ViewInjectViewModelFromParentMethod?.Invoke(playerActionsView, Array.Empty<object>());
            ViewAssertViewModelExistsMethod?.Invoke(playerActionsView, Array.Empty<object>());
        }
        catch (Exception exception)
        {
            Log($"TryEnsureViewModel: parentInjectionFailed={exception.GetType().Name}");
        }

        if (ViewGetViewModelMethod?.Invoke(playerActionsView, Array.Empty<object>()) is PlayerActionsViewModel)
        {
            return;
        }

        var networkPlayerRegistry = GetNetworkPlayerRegistry(gameUiManager, playerActionsView);
        if (networkPlayerRegistry is null)
        {
            Log("TryEnsureViewModel: noNetworkPlayerRegistry");
            return;
        }

        var viewModel = new PlayerActionsViewModel(networkPlayerRegistry);
        PlayerActionsViewModelInitMethod?.Invoke(viewModel, Array.Empty<object>());
        ViewSetViewModelMethod?.Invoke(playerActionsView, [viewModel]);
        Log("TryEnsureViewModel: createdViewModel");
    }

    private static PlayerActionsView? GetPlayerActionsView(GameUIManager gameUiManager)
    {
        return GameUiManagerGetPlayerActionsViewMethod?.Invoke(gameUiManager, Array.Empty<object>()) as PlayerActionsView
            ?? GameUiManagerGetTutorialPlayerActionsViewMethod?.Invoke(gameUiManager, Array.Empty<object>()) as PlayerActionsView;
    }

    private static NetworkPlayerRegistry? GetNetworkPlayerRegistry(GameUIManager? gameUiManager, PlayerActionsView playerActionsView)
    {
        if (gameUiManager is not null)
        {
            return GameUiManagerGetNetworkPlayerRegistryMethod?.Invoke(gameUiManager, Array.Empty<object>()) as NetworkPlayerRegistry;
        }

        var playerActionsViewNetworkPlayerRegistryField = AccessTools.Field(typeof(PlayerActionsView), "_networkPlayerRegistry");
        return playerActionsViewNetworkPlayerRegistryField?.GetValue(playerActionsView) as NetworkPlayerRegistry;
    }

    private static void Log(string message)
    {
        if (_configuration is null || !_configuration.EnableLogging.Value)
        {
            return;
        }

        _logger?.LogInfo(message);
    }
}
