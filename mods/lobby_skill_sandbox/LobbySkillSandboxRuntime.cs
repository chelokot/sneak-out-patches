using BepInEx.Logging;
using Gameplay.Enviro;
using Gameplay.Player.Components;
using Gameplay.Player.Gameplay;
using HarmonyLib;
using UI;
using UI.Views;
using UnityEngine;
using ClientCharacterType = Types.CharacterType;
using SneakOutGame = Game.Game;
using Types;

namespace SneakOut.LobbySkillSandbox;

internal static class LobbySkillSandboxRuntime
{
    private static ManualLogSource? _logger;
    private static Harmony? _harmony;
    private static LobbySkillSandboxConfig? _configuration;
    private static bool _lobbyUiActive;

    public static void Initialize(ManualLogSource logger, LobbySkillSandboxConfig configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _harmony ??= new Harmony(LobbySkillSandboxPlugin.PluginGuid);
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

        if (!TryPreparePlayerActionsView(playerActionsView))
        {
            Log("EnableLobbySkillView: viewModelUnavailable");
            return;
        }

        playerActionsView.gameObject.SetActive(true);
        Log("EnableLobbySkillView: activated");
    }

    public static void TryEnableLobbySkillViewAfterSpawn(SpookedNetworkPlayer networkPlayer)
    {
        if (!Enabled || !_configuration!.EnableLobbySkillUi.Value || !_lobbyUiActive)
        {
            return;
        }

        var playerInternalId = networkPlayer.InternalId;
        if (!SneakOutGame.IsMyInternalId(playerInternalId))
        {
            return;
        }

        if (networkPlayer.CharacterType != ClientCharacterType.victim_penguin)
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

    private static bool TryPreparePlayerActionsView(PlayerActionsView playerActionsView)
    {
        if (!Enabled || !_configuration!.EnableLobbySkillUi.Value)
        {
            return false;
        }

        if (!_lobbyUiActive)
        {
            return false;
        }

        TryInjectViewModel(playerActionsView);

        var viewModel = playerActionsView.ViewModel;
        if (viewModel is null)
        {
            return false;
        }

        viewModel._canBeVisible = true;
        viewModel.RefreshSkills();
        return true;
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

        var playerInternalId = networkPlayer.InternalId;
        if (!SneakOutGame.IsMyInternalId(playerInternalId))
        {
            return false;
        }

        if (networkPlayer.CharacterType != ClientCharacterType.victim_penguin)
        {
            return false;
        }

        entitySkillsComponent.RefreshPlayerSkills();
        var skillType = entitySkillsComponent.GetSkill(!secondSkill);
        Log($"TryHandleLobbySkillUse: second={secondSkill}, skill={skillType}, internalId={playerInternalId}");

        if (skillType == SpookedSkillType.VictimPropChange)
        {
            return TryHandleLobbyPropChange(entitySkillsComponent, playerInternalId);
        }

        if (skillType == SpookedSkillType.VictimSlide)
        {
            entitySkillsComponent.HandleVictimSlide();
            return true;
        }

        return false;
    }

    public static bool TryHandleLobbySkillInput(Component component, bool secondSkill)
    {
        if (component.GetComponent<EntitySkillsComponent>() is not EntitySkillsComponent entitySkillsComponent)
        {
            return false;
        }

        Log($"TryHandleLobbySkillInput: second={secondSkill}");
        return TryHandleLobbySkillUse(entitySkillsComponent, secondSkill);
    }

    private static bool TryHandleLobbyPropChange(EntitySkillsComponent entitySkillsComponent, int playerInternalId)
    {
        if (!_configuration!.EnableLobbyPropChange.Value)
        {
            Log("TryHandleLobbyPropChange: disabled");
            return true;
        }

        if (entitySkillsComponent.DuringPropChange)
        {
            Log("TryHandleLobbyPropChange: alreadyChanging");
            return true;
        }

        if (!HasInitializedPropPool(entitySkillsComponent))
        {
            Log("TryHandleLobbyPropChange: propPoolUnavailable");
            return true;
        }

        if (!HasAvailableLobbyProp(entitySkillsComponent._playerRoomRegistry, playerInternalId))
        {
            Log("TryHandleLobbyPropChange: roomPropsUnavailable");
            return true;
        }

        entitySkillsComponent.OnVictimPropChange();
        Log("TryHandleLobbyPropChange: invoked");
        return true;
    }

    private static bool HasInitializedPropPool(EntitySkillsComponent entitySkillsComponent)
    {
        var propPool = entitySkillsComponent._propPool;
        return propPool is not null
            && propPool._propPoolInitialization is not null
            && propPool._pool is not null
            && propPool._poolTransform is not null;
    }

    private static bool HasAvailableLobbyProp(PlayerRoomRegistry? playerRoomRegistry, int playerInternalId)
    {
        if (playerRoomRegistry is null)
        {
            return false;
        }

        var room = playerRoomRegistry[playerInternalId];
        if (room is null)
        {
            return false;
        }

        var availableProps = room.AvailableProps;
        if (availableProps is null)
        {
            return false;
        }

        foreach (var availableProp in availableProps)
        {
            if (availableProp != PlayerPropType.None)
            {
                return true;
            }
        }

        return false;
    }

    private static void TryInjectViewModel(PlayerActionsView playerActionsView)
    {
        if (playerActionsView.ViewModel is not null)
        {
            return;
        }

        try
        {
            playerActionsView.InjectViewModelFromParent();
        }
        catch (Exception exception)
        {
            Log($"TryInjectViewModel: parentInjectionFailed={exception.GetType().Name}");
        }

        if (playerActionsView.ViewModel is not null)
        {
            return;
        }

        Log("TryInjectViewModel: unavailable");
    }

    private static PlayerActionsView? GetPlayerActionsView(GameUIManager gameUiManager)
    {
        return gameUiManager._playerActionsView ?? gameUiManager._tutorialPlayerActionsView;
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
