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

namespace SneakOut.PortalModeSelector;

[HarmonyPatch(typeof(GameUIManager), "OnAwake")]
internal static class GameUiManagerOnAwakePatch
{
    private static void Postfix(GameUIManager __instance)
    {
        PortalModeSelectorRuntime.BindPortalManager(__instance);
    }
}

[HarmonyPatch(typeof(SceneSpawner), nameof(SceneSpawner.Spawn))]
internal static class SceneSpawnerSpawnPatch
{
    [HarmonyPrefix]
    private static void Prefix(SceneSpawner __instance)
    {
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
    private static void Prefix(BeforeSelectionState __instance)
    {
        PortalModeSelectorRuntime.ApplyActiveMode(__instance._gameState);
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
    private static void Prefix(GameStartController __instance)
    {
        PortalModeSelectorRuntime.ApplyActiveMode(__instance._gameState);
        PortalModeSelectorRuntime.WireAllBerekComponents();
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
    private static void Postfix(SpookedNetworkPlayer __instance)
    {
        PortalModeSelectorRuntime.WirePlayerBerekComponent(__instance);
    }
}
