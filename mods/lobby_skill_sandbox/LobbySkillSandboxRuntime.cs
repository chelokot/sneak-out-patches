using BepInEx.Logging;
using Gameplay.Player.Components;
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
    private static EntitySkillsComponent? _activeLobbyPropOwner;

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
        if (_lobbyUiActive && !active)
        {
            RestoreLobbyProp();
            LobbyPropPool.Dispose();
        }

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
            try
            {
                entitySkillsComponent.ChangeFromProp();
                entitySkillsComponent.RPC_VictimPropUnChange();
                Log("TryHandleLobbyPropChange: restored");
            }
            catch (Exception exception)
            {
                Warn($"Lobby prop restore failed: {exception.GetType().Name}");
            }
            finally
            {
                entitySkillsComponent._duringPropChange = false;
                _activeLobbyPropOwner = null;
            }

            return true;
        }

        if (!LobbyPropPool.EnsureInitialized(entitySkillsComponent))
        {
            Log("TryHandleLobbyPropChange: propPoolUnavailable");
            return true;
        }

        var propType = LobbyPropPool.ChooseRandomType();
        if (propType == PlayerPropType.None)
        {
            Log("TryHandleLobbyPropChange: noAvailableProp");
            return true;
        }

        try
        {
            entitySkillsComponent.ChangeToProp(propType);
            entitySkillsComponent.RPC_VictimPropChange(propType);
            entitySkillsComponent._duringPropChange = true;
            _activeLobbyPropOwner = entitySkillsComponent;
            Log($"TryHandleLobbyPropChange: networkChanged type={propType}, internalId={playerInternalId}");
        }
        catch (Exception exception)
        {
            try
            {
                entitySkillsComponent.ChangeFromProp();
            }
            catch
            {
                // The initial change may have failed before registering a prop instance.
            }

            entitySkillsComponent._duringPropChange = false;
            _activeLobbyPropOwner = null;
            Warn($"Lobby prop change failed: {exception.GetType().Name}");
        }

        return true;
    }

    private static void RestoreLobbyProp()
    {
        if (_activeLobbyPropOwner is not EntitySkillsComponent entitySkillsComponent)
        {
            return;
        }

        try
        {
            if (entitySkillsComponent.DuringPropChange)
            {
                entitySkillsComponent.ChangeFromProp();
                entitySkillsComponent.RPC_VictimPropUnChange();
            }
        }
        catch (Exception exception)
        {
            Warn($"Lobby prop cleanup failed: {exception.GetType().Name}");
        }
        finally
        {
            entitySkillsComponent._duringPropChange = false;
            _activeLobbyPropOwner = null;
        }
    }

    public static bool PrepareLobbyPropRpc(EntitySkillsComponent entitySkillsComponent)
    {
        if (!Enabled || !_lobbyUiActive || !_configuration!.EnableLobbyPropChange.Value)
        {
            return true;
        }

        if (LobbyPropPool.EnsureInitialized(entitySkillsComponent))
        {
            return true;
        }

        Warn("Suppressed lobby prop RPC because this client could not initialize its lobby prop pool.");
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

    internal static void Warn(string message)
    {
        _logger?.LogWarning(message);
    }
}
