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
    private static readonly Dictionary<IntPtr, LobbyPropVisualState> LobbyPropVisuals = [];

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

        if (HasLobbyPropVisual(entitySkillsComponent))
        {
            try
            {
                RestoreLobbyPropVisual(entitySkillsComponent);
                entitySkillsComponent.RPC_VictimPropUnChange();
                Log("TryHandleLobbyPropChange: restored");
            }
            catch (Exception exception)
            {
                Warn($"Lobby prop restore failed: {exception.GetType().Name}");
            }
            finally
            {
                _activeLobbyPropOwner = null;
            }

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
            ApplyLobbyPropVisual(entitySkillsComponent, propType);
            entitySkillsComponent.RPC_VictimPropChange(propType);
            _activeLobbyPropOwner = entitySkillsComponent;
            Log($"TryHandleLobbyPropChange: visualNetworkChanged type={propType}, internalId={playerInternalId}");
        }
        catch (Exception exception)
        {
            RestoreLobbyPropVisual(entitySkillsComponent);
            _activeLobbyPropOwner = null;
            Warn($"Lobby prop change failed: {exception.GetType().Name}");
        }

        return true;
    }

    private static void RestoreLobbyProp()
    {
        foreach (var state in LobbyPropVisuals.Values.ToArray())
        {
            RestoreLobbyPropVisual(state);
        }

        LobbyPropVisuals.Clear();
        _activeLobbyPropOwner = null;
    }

    public static bool TryApplyLobbyPropVisual(EntitySkillsComponent entitySkillsComponent, PlayerPropType propType)
    {
        if (!Enabled || !_lobbyUiActive || !_configuration!.EnableLobbyPropChange.Value)
        {
            return false;
        }

        ApplyLobbyPropVisual(entitySkillsComponent, propType);
        return true;
    }

    public static bool TryRestoreLobbyPropVisual(EntitySkillsComponent entitySkillsComponent)
    {
        if (!Enabled || !_lobbyUiActive || !_configuration!.EnableLobbyPropChange.Value)
        {
            return false;
        }

        RestoreLobbyPropVisual(entitySkillsComponent);
        return true;
    }

    private static bool HasLobbyPropVisual(EntitySkillsComponent skills)
    {
        return skills.Pointer != IntPtr.Zero && LobbyPropVisuals.ContainsKey(skills.Pointer);
    }

    private static void ApplyLobbyPropVisual(EntitySkillsComponent skills, PlayerPropType propType)
    {
        RestoreLobbyPropVisual(skills);

        var playerRenderers = skills.gameObject.GetComponentsInChildren<Renderer>(true)
            .Where(renderer => renderer is not null && renderer.Pointer != IntPtr.Zero)
            .ToArray();
        var enabledStates = playerRenderers.Select(renderer => renderer.enabled).ToArray();
        var visual = LobbyPropPool.CreateVisual(propType, skills.transform);
        if (visual is null)
        {
            Warn($"Lobby prop visual source is unavailable for {propType}");
            return;
        }

        foreach (var renderer in playerRenderers)
        {
            renderer.enabled = false;
        }

        LobbyPropVisuals[skills.Pointer] = new LobbyPropVisualState(
            skills.Pointer,
            visual,
            playerRenderers,
            enabledStates);
    }

    private static void RestoreLobbyPropVisual(EntitySkillsComponent skills)
    {
        if (skills.Pointer == IntPtr.Zero
            || !LobbyPropVisuals.Remove(skills.Pointer, out var state))
        {
            return;
        }

        RestoreLobbyPropVisual(state);
    }

    private static void RestoreLobbyPropVisual(LobbyPropVisualState state)
    {
        for (var index = 0; index < state.Renderers.Length; index++)
        {
            var renderer = state.Renderers[index];
            try
            {
                if (renderer)
                {
                    renderer.enabled = state.EnabledStates[index];
                }
            }
            catch
            {
                // The player can despawn while leaving the lobby.
            }
        }

        try
        {
            if (state.Visual)
            {
                UnityEngine.Object.Destroy(state.Visual);
            }
        }
        catch
        {
            // The lobby scene may already have destroyed the visual.
        }
    }

    private sealed record LobbyPropVisualState(
        IntPtr Owner,
        GameObject Visual,
        Renderer[] Renderers,
        bool[] EnabledStates);

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
