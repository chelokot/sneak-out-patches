using Fusion;
using Gameplay.Match;
using Gameplay.Match.MatchState;
using Gameplay.Player.Components;
using Gameplay.Spawn;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Kinguinverse.DataUtils.Events;
using Networking.Matchmaking;
using Networking.Photon;
using Types;
using UI;
using UI.Views.Lobby;

namespace SneakOut.PortalModeSelector;

[HarmonyPatch(typeof(GameUIManager), "OnAwake")]
internal static class GameUiManagerOnAwakePatch
{
    private static void Postfix(GameUIManager __instance)
    {
        PortalModeSelectorRuntime.BindPortalManager(__instance);
    }
}

[HarmonyPatch(typeof(PortalPlayView), "OnPlay")]
internal static class PortalPlayViewOnPlayPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static void Prefix(PortalPlayView __instance)
    {
        // UnityEvent listeners run in registration order. The stock PLAY listener is registered
        // before our UI listener, so activate the selection before stock matchmaking observes it.
        PortalModeSelectorRuntime.ActivateSelection(__instance);
    }
}

[HarmonyPatch(typeof(PortalPlayView), nameof(PortalPlayView.Open))]
internal static class PortalPlayViewOpenProbePatch
{
    [HarmonyPostfix]
    private static void Postfix(PortalPlayView __instance)
    {
        PortalModeSelectorRuntime.ObserveStockPortalOpen(__instance);
    }
}

[HarmonyPatch(typeof(SceneSpawner), nameof(SceneSpawner.Spawn))]
internal static class SceneSpawnerSpawnPatch
{
    [HarmonyPostfix]
    private static void Postfix(SceneSpawner __instance)
    {
        // Spawn initializes a fresh scene GameState and can restore the serialized Default value.
        // Apply the selected mode after that initialization has completed.
        PortalModeSelectorRuntime.ApplyActiveMode(__instance._gameState);
    }
}

[HarmonyPatch(typeof(Matchmaker), "PrepareMatch")]
internal static class MatchmakerPrepareMatchPatch
{
    private static void Prefix(ref GameModeType gameModeType)
    {
        PortalModeSelectorRuntime.TryOverrideMatchMode(ref gameModeType);
    }
}

[HarmonyPatch(typeof(Matchmaker), "OnStartMatchmaking")]
internal static class MatchmakerOnStartMatchmakingPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static void Prefix(Il2CppSystem.EventArgs args)
    {
        var startEvent = args.Cast<Events.StartMatchmakingEvent>();
        var gameModeType = startEvent.GameModeType;
        if (PortalModeSelectorRuntime.TryOverrideMatchMode(ref gameModeType))
        {
            startEvent.GameModeType = gameModeType;
        }
    }
}

[HarmonyPatch(typeof(NetworkRunner), nameof(NetworkRunner.StartGame), new[] { typeof(StartGameArgs) })]
internal static class NetworkRunnerStartGamePatch
{
    [HarmonyPrefix]
    private static void Prefix(ref StartGameArgs args)
    {
        PortalModeSelectorRuntime.ApplyActiveModeToSessionProperties(args);
    }
}

[HarmonyPatch(typeof(SceneTypeExtension), nameof(SceneTypeExtension.GetRandomScene))]
internal static class SceneTypeExtensionGetRandomScenePatch
{
    private static bool Prefix(Il2CppStructArray<SceneType> mapsToPlayOn, GameModeType gameModeType, ref SceneType __result)
    {
        return !PortalModeSelectorRuntime.TryOverrideRandomScene(mapsToPlayOn, gameModeType, ref __result);
    }
}

[HarmonyPatch(typeof(PhotonPlayFabLobbyController), nameof(PhotonPlayFabLobbyController.HostChosenGameMode), MethodType.Setter)]
internal static class PhotonPlayFabLobbyControllerSetHostChosenGameModePatch
{
    private static void Prefix(ref GameModeType value)
    {
        PortalModeSelectorRuntime.TryOverrideMatchMode(ref value);
    }
}

[HarmonyPatch(typeof(PhotonPlayFabLobbyController), "RPC_RequestChangeGameMode")]
internal static class PhotonPlayFabLobbyControllerRpcRequestChangeGameModePatch
{
    private static void Prefix(ref GameModeType gameMode)
    {
        PortalModeSelectorRuntime.TryOverrideMatchMode(ref gameMode);
    }
}

[HarmonyPatch(typeof(PhotonPlayFabLobbyController), "RPC_SendMatchInfoToTeam")]
internal static class PhotonPlayFabLobbyControllerRpcSendMatchInfoToTeamPatch
{
    private static void Prefix(ref GameModeType selectedGameModeType)
    {
        PortalModeSelectorRuntime.TryOverrideMatchMode(ref selectedGameModeType);
    }
}

[HarmonyPatch(typeof(BeforeSelectionState), nameof(BeforeSelectionState.Tick))]
internal static class BeforeSelectionStateTickPatch
{
    private static bool Prefix(BeforeSelectionState __instance, MatchStateMachine stateMachine)
    {
        PortalModeSelectorRuntime.ApplyActiveMode(__instance._gameState);
        if (PortalModeSelectorRuntime.TryRedirectBerekSelection(stateMachine))
        {
            return false;
        }

        return true;
    }
}

[HarmonyPatch(typeof(MatchStateMachine), nameof(MatchStateMachine.FixedUpdateNetwork))]
internal static class MatchStateMachineFixedUpdateNetworkPatch
{
    [HarmonyPrefix]
    private static void Prefix(MatchStateMachine __instance)
    {
        PortalModeSelectorRuntime.ApplyActiveModeForMatchTick(__instance._gameState);
    }
}

[HarmonyPatch(typeof(ShouldStartState), nameof(ShouldStartState.Tick))]
internal static class ShouldStartStateTickPatch
{
    private static void Prefix(ShouldStartState __instance)
    {
        PortalModeSelectorRuntime.ApplyActiveMode(__instance._gameState);
    }
}

[HarmonyPatch(typeof(GameStartController), "PrepareVictims")]
internal static class GameStartControllerPrepareVictimsPatch
{
    private static bool Prefix(GameStartController __instance, CharacterType seekerCharacterType)
    {
        PortalModeSelectorRuntime.ApplyActiveMode(__instance._gameState);
        PortalModeSelectorRuntime.WireAllBerekComponents();
        return !PortalModeSelectorRuntime.TryStartBerekMode(__instance, seekerCharacterType);
    }
}

[HarmonyPatch(typeof(MatchStateHelper), nameof(MatchStateHelper.KinguinverseStartMatch))]
internal static class MatchStateHelperKinguinverseStartMatchPatch
{
    private static void Prefix(MatchStateHelper __instance)
    {
        PortalModeSelectorRuntime.ApplyActiveMode(__instance._gameState);
    }
}

[HarmonyPatch(typeof(SpookedNetworkPlayer), nameof(SpookedNetworkPlayer.AssignComponents))]
internal static class SpookedNetworkPlayerAssignComponentsPatch
{
    [HarmonyPriority(Priority.First)]
    private static void Postfix(SpookedNetworkPlayer __instance)
    {
        PortalModeSelectorRuntime.ApplyActiveModeFromPlayer(__instance);
        PortalModeSelectorRuntime.WirePlayerBerekComponent(__instance);
    }
}

[HarmonyPatch(typeof(SpookedNetworkPlayer), nameof(SpookedNetworkPlayer.Init))]
internal static class SpookedNetworkPlayerInitPatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.First)]
    private static void Postfix(SpookedNetworkPlayer __instance)
    {
        // Init is the first match-scene lifecycle callback that is proven to run on 1.1.10 for
        // both the local player and the managed test bot. The scene's DI GameState is available
        // here, before selection starts, so restore the Photon mode on that exact instance.
        PortalModeSelectorRuntime.ApplyActiveModeFromPlayer(__instance);
        PortalModeSelectorRuntime.WirePlayerBerekComponent(__instance);
    }
}
