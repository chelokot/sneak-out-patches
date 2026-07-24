using Gameplay.Match;
using Gameplay.Match.MatchState;
using Gameplay.Player.Components;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Networking.Matchmaking;
using Networking.Photon;
using Types;
using UI.Views.Lobby;

namespace SneakOut.PortalModeSelector;

[HarmonyPatch(typeof(PortalPlayView), nameof(PortalPlayView.Open))]
internal static class PortalPlayViewOpenPatch
{
    private static void Postfix(PortalPlayView __instance)
    {
        try
        {
            PortalModeSelectorRuntime.OpenPortal(__instance);
        }
        catch (Exception exception)
        {
            PortalModeSelectorRuntime.LogError("Portal selector Open postfix failed", exception);
        }
    }
}

[HarmonyPatch(typeof(PortalPlayView), nameof(PortalPlayView.Close))]
internal static class PortalPlayViewClosePatch
{
    private static void Postfix(PortalPlayView __instance)
    {
        PortalModeSelectorRuntime.ReleasePortalView(__instance);
    }
}

[HarmonyPatch(typeof(PortalPlayView), nameof(PortalPlayView.ManagerDispose))]
internal static class PortalPlayViewManagerDisposePatch
{
    private static void Postfix(PortalPlayView __instance)
    {
        PortalModeSelectorRuntime.ReleasePortalView(__instance);
    }
}

[HarmonyPatch(typeof(PortalPlayView), nameof(PortalPlayView.OnChangeRoleButton))]
internal static class PortalPlayViewOnChangeRoleButtonPatch
{
    private static bool Prefix(PortalPlayView __instance)
    {
        try
        {
            return !PortalModeSelectorRuntime.TryHandleModeToggle(__instance);
        }
        catch (Exception exception)
        {
            PortalModeSelectorRuntime.LogError("Portal selector role-button prefix failed", exception);
            return true;
        }
    }
}

[HarmonyPatch(typeof(PortalPlayView), nameof(PortalPlayView.OnPlay))]
internal static class PortalPlayViewOnPlayPatch
{
    private static void Prefix(PortalPlayView __instance)
    {
        PortalModeSelectorRuntime.ActivateSelection(__instance);
    }
}

[HarmonyPatch(typeof(Matchmaker), "OnStartMatchmaking")]
internal static class MatchmakerOnStartMatchmakingPatch
{
    private static void Prefix(Il2CppSystem.EventArgs? args)
    {
        PortalModeSelectorRuntime.TryOverrideStartMatchmakingArgs(args);
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

[HarmonyPatch(typeof(Matchmaker), "OnCancelMatchmakingEvent")]
internal static class MatchmakerOnCancelMatchmakingEventPatch
{
    private static void Postfix()
    {
        PortalModeSelectorRuntime.ClearActiveSelection();
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

[HarmonyPatch(typeof(PhotonPlayFabLobbyController), "OnHostChooseGameModeEvent")]
internal static class PhotonPlayFabLobbyControllerOnHostChooseGameModeEventPatch
{
    private static void Prefix(Il2CppSystem.EventArgs? args)
    {
        PortalModeSelectorRuntime.TryOverrideRequestChangeGameModeArgs(args);
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

[HarmonyPatch(typeof(PhotonPlayFabLobbyController), "OnBroadcastMatchSessionToOtherLobbyMembers")]
internal static class PhotonPlayFabLobbyControllerOnBroadcastMatchSessionPatch
{
    private static void Prefix(Il2CppSystem.EventArgs? args)
    {
        PortalModeSelectorRuntime.TryOverrideBroadcastMatchArgs(args);
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

[HarmonyPatch(typeof(MatchStateHelper), nameof(MatchStateHelper.FinishMatch))]
internal static class MatchStateHelperFinishMatchPatch
{
    private static void Postfix()
    {
        PortalModeSelectorRuntime.ClearActiveSelection();
    }
}

[HarmonyPatch(typeof(MatchStateHelper), nameof(MatchStateHelper.BerekFinishMatch))]
internal static class MatchStateHelperBerekFinishMatchPatch
{
    private static void Postfix()
    {
        PortalModeSelectorRuntime.ClearActiveSelection();
    }
}
