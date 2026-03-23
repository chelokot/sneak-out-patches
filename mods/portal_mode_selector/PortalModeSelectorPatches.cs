using HarmonyLib;
using Il2CppSystem.Collections;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Networking.PGOS;
using Types;
using UI;
using UI.Views.Lobby;

namespace SneakOut.PortalModeSelector;

[HarmonyPatch(typeof(PortalPlayView), nameof(PortalPlayView.Open))]
internal static class PortalPlayViewOpenPatch
{
    private static void Postfix(PortalPlayView __instance)
    {
        try
        {
            PortalModeSelectorRuntime.TryEnsureModeRow(__instance);
        }
        catch (Exception exception)
        {
            PortalModeSelectorRuntime.LogError("Portal selector Open postfix failed", exception);
        }
    }
}

[HarmonyPatch(typeof(PortalPlayView), nameof(PortalPlayView.OnChangeRoleButton))]
internal static class PortalPlayViewOnChangeRoleButtonPatch
{
    private static bool Prefix(PortalPlayView __instance)
    {
        try
        {
            if (PortalModeSelectorRuntime.TryHandleModeToggle(__instance))
            {
                return false;
            }
        }
        catch (Exception exception)
        {
            PortalModeSelectorRuntime.LogError("Portal selector role-button prefix failed", exception);
        }

        return true;
    }

    private static void Postfix(PortalPlayView __instance)
    {
        try
        {
            PortalModeSelectorRuntime.LogOriginalRoleState(__instance, "after");
        }
        catch (Exception exception)
        {
            PortalModeSelectorRuntime.LogError("Portal selector role-button postfix failed", exception);
        }
    }
}

[HarmonyPatch(typeof(PortalPlayView), nameof(PortalPlayView.OnPlay))]
internal static class PortalPlayViewOnPlayPatch
{
    private static bool Prefix(PortalPlayView __instance)
    {
        try
        {
            return !PortalModeSelectorRuntime.TryHandlePlay(__instance);
        }
        catch (Exception exception)
        {
            PortalModeSelectorRuntime.LogError("Portal selector play prefix failed", exception);
        }

        return true;
    }
}

[HarmonyPatch(typeof(Matchmaker), nameof(Matchmaker.PrepareMatch))]
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
    private static void Prefix(object? sender, Il2CppSystem.EventArgs? args)
    {
        PortalModeSelectorRuntime.TryOverrideStartMatchmakingArgs(args);
    }
}

[HarmonyPatch(typeof(Matchmaker), "GetMatchmakingQuery")]
internal static class MatchmakerGetMatchmakingQueryPatch
{
    private static void Prefix(ref GameModeType gameModeType)
    {
        PortalModeSelectorRuntime.TryOverrideMatchMode(ref gameModeType);
    }
}

[HarmonyPatch(typeof(SceneTypeExtension), nameof(SceneTypeExtension.GetRandomScene))]
internal static class SceneTypeExtensionGetRandomScenePatch
{
    private static bool Prefix(Il2CppStructArray<SceneType> mapsToPlayOn, GameModeType gameModeType, ref SceneType __result)
    {
        try
        {
            if (PortalModeSelectorRuntime.TryOverrideRandomScene(mapsToPlayOn, gameModeType, ref __result))
            {
                return false;
            }
        }
        catch (Exception exception)
        {
            PortalModeSelectorRuntime.LogError("Portal selector GetRandomScene prefix failed", exception);
        }

        return true;
    }
}

[HarmonyPatch]
internal static class PhotonPlayFabLobbyControllerOnHostChooseGameModeEventPatch
{
    private static System.Reflection.MethodBase? TargetMethod()
    {
        return PortalModeSelectorRuntime.FindPatchedMethod("Networking.Photon.PhotonPlayFabLobbyController", "OnHostChooseGameModeEvent");
    }

    private static void Prefix(object? sender, Il2CppSystem.EventArgs? args)
    {
        PortalModeSelectorRuntime.TryOverrideRequestChangeGameModeArgs(args);
    }
}

[HarmonyPatch]
internal static class PhotonPlayFabLobbyControllerSetHostChosenGameModePatch
{
    private static System.Reflection.MethodBase? TargetMethod()
    {
        return PortalModeSelectorRuntime.FindPatchedMethod("Networking.Photon.PhotonPlayFabLobbyController", "set_HostChosenGameMode");
    }

    private static void Prefix(ref GameModeType value)
    {
        PortalModeSelectorRuntime.TryOverrideMatchMode(ref value);
    }
}

[HarmonyPatch]
internal static class PhotonPlayFabLobbyControllerRpcRequestChangeGameModePatch
{
    private static System.Reflection.MethodBase? TargetMethod()
    {
        return PortalModeSelectorRuntime.FindPatchedMethod("Networking.Photon.PhotonPlayFabLobbyController", "RPC_RequestChangeGameMode");
    }

    private static void Prefix(ref GameModeType gameMode)
    {
        PortalModeSelectorRuntime.TryOverrideMatchMode(ref gameMode);
    }
}

[HarmonyPatch]
internal static class PhotonPlayFabLobbyControllerOnSendMatchInfoToTeamEventPatch
{
    private static System.Reflection.MethodBase? TargetMethod()
    {
        return PortalModeSelectorRuntime.FindPatchedMethod("Networking.Photon.PhotonPlayFabLobbyController", "OnSendMatchInfoToTeamEvent");
    }

    private static void Prefix(object? sender, Il2CppSystem.EventArgs? args)
    {
        PortalModeSelectorRuntime.TryOverrideSendMatchInfoArgs(args);
    }
}

[HarmonyPatch]
internal static class PhotonPlayFabLobbyControllerRpcSendMatchInfoToTeamPatch
{
    private static System.Reflection.MethodBase? TargetMethod()
    {
        return PortalModeSelectorRuntime.FindPatchedMethod("Networking.Photon.PhotonPlayFabLobbyController", "RPC_SendMatchInfoToTeam");
    }

    private static void Prefix(string matchId, ref GameModeType selectedGameModeType)
    {
        PortalModeSelectorRuntime.TryOverrideMatchMode(ref selectedGameModeType);
    }
}

[HarmonyPatch]
internal static class GameStartControllerHandleDefaultModeStartPatch
{
    private static System.Reflection.MethodBase? TargetMethod()
    {
        return PortalModeSelectorRuntime.FindPatchedMethod("Gameplay.Match.GameStartController", "HandleDefaultModeStart");
    }

    private static bool Prefix(object __instance, CharacterType seekerCharacterType, ref IEnumerator __result)
    {
        return !PortalModeSelectorRuntime.TryRedirectDefaultModeStart(__instance, seekerCharacterType, ref __result);
    }
}

[HarmonyPatch]
internal static class GameStartControllerHandleBerekModeStartPatch
{
    private static System.Reflection.MethodBase? TargetMethod()
    {
        return PortalModeSelectorRuntime.FindPatchedMethod("Gameplay.Match.GameStartController", "HandleBerekModeStart");
    }

    private static void Prefix(ref CharacterType seekerCharacterType)
    {
        PortalModeSelectorRuntime.LogBerekModeStart(seekerCharacterType);
    }
}

[HarmonyPatch]
internal static class GameStartControllerHandleSeekerPatch
{
    private static System.Reflection.MethodBase? TargetMethod()
    {
        return PortalModeSelectorRuntime.FindPatchedMethod("Gameplay.Match.GameStartController", "HandleSeeker");
    }

    private static void Prefix(ref CharacterType seekerCharacterType)
    {
        PortalModeSelectorRuntime.LogHandleSeeker(seekerCharacterType);
    }
}

[HarmonyPatch]
internal static class GameStartControllerPrepareVictimsPatch
{
    private static System.Reflection.MethodBase? TargetMethod()
    {
        return PortalModeSelectorRuntime.FindPatchedMethod("Gameplay.Match.GameStartController", "PrepareVictims");
    }

    private static bool Prefix(object __instance, ref CharacterType seekerCharacterType)
    {
        return !PortalModeSelectorRuntime.TryStartBerekModeFromPrepareVictims(__instance, seekerCharacterType);
    }
}

[HarmonyPatch]
internal static class GameStartControllerOnConfirmSeekerCharacterEventPatch
{
    private static System.Reflection.MethodBase? TargetMethod()
    {
        return PortalModeSelectorRuntime.FindPatchedMethod("Gameplay.Match.GameStartController", "OnConfirmSeekerCharacterEvent");
    }

    private static void Prefix(object? sender, Il2CppSystem.EventArgs? args)
    {
        PortalModeSelectorRuntime.LogConfirmSeekerCharacterEvent(args);
    }
}

[HarmonyPatch]
internal static class GameStartControllerOnSeekerChosenRequestEventPatch
{
    private static System.Reflection.MethodBase? TargetMethod()
    {
        return PortalModeSelectorRuntime.FindPatchedMethod("Gameplay.Match.GameStartController", "OnSeekerChosenRequestEvent");
    }

    private static void Prefix(object? sender, Il2CppSystem.EventArgs? args)
    {
    }
}

[HarmonyPatch]
internal static class MatchStateHelperKinguinverseStartMatchPatch
{
    private static System.Reflection.MethodBase? TargetMethod()
    {
        return PortalModeSelectorRuntime.FindPatchedMethod("Gameplay.Match.MatchState.MatchStateHelper", "KinguinverseStartMatch");
    }

    private static void Prefix(object __instance)
    {
        PortalModeSelectorRuntime.LogKinguinverseStartMatch(__instance);
    }
}

[HarmonyPatch]
internal static class BeforeSelectionStateTickPatch
{
    private static System.Reflection.MethodBase? TargetMethod()
    {
        return PortalModeSelectorRuntime.FindPatchedMethod("Gameplay.Match.MatchState.BeforeSelectionState", "Tick");
    }

    private static bool Prefix(object __instance, object stateMachine)
    {
        return !PortalModeSelectorRuntime.TryRedirectBeforeSelectionState(__instance, stateMachine);
    }
}

[HarmonyPatch]
internal static class BerekSelectionStateTickPatch
{
    private static System.Reflection.MethodBase? TargetMethod()
    {
        return PortalModeSelectorRuntime.FindPatchedMethod("Gameplay.Match.MatchState.BerekSelectionState", "Tick");
    }

    private static void Prefix(object __instance, object stateMachine)
    {
        PortalModeSelectorRuntime.LogBerekSelectionStateTick(__instance, stateMachine);
    }
}

[HarmonyPatch]
internal static class SpookedNetworkPlayerAssignComponentsPatch
{
    private static System.Reflection.MethodBase? TargetMethod()
    {
        return PortalModeSelectorRuntime.FindPatchedMethod("Gameplay.Player.Components.SpookedNetworkPlayer", "AssignComponents");
    }

    private static void Postfix(object __instance)
    {
        PortalModeSelectorRuntime.WirePlayerBerekComponent(__instance, "AssignComponents");
    }
}

[HarmonyPatch]
internal static class GameStartControllerGivePlayerCrownPatch
{
    private static System.Reflection.MethodBase? TargetMethod()
    {
        return PortalModeSelectorRuntime.FindPatchedMethod("Gameplay.Match.GameStartController", "GivePlayerCrown");
    }

    private static void Prefix(object __instance)
    {
        PortalModeSelectorRuntime.LogGivePlayerCrown(__instance);
    }
}

[HarmonyPatch]
internal static class GameStartControllerInitializeBerekComponentsPatch
{
    private static System.Reflection.MethodBase? TargetMethod()
    {
        return PortalModeSelectorRuntime.FindPatchedMethod("Gameplay.Match.GameStartController", "InitializeBerekComponents");
    }

    private static void Prefix(object __instance)
    {
        PortalModeSelectorRuntime.LogInitializeBerekComponents(__instance);
    }
}

[HarmonyPatch]
internal static class EntityBerekComponentHandleCrownPatch
{
    private static System.Reflection.MethodBase? TargetMethod()
    {
        return PortalModeSelectorRuntime.FindPatchedMethod("Gameplay.Player.Components.EntityBerekComponent", "HandleCrown");
    }

    private static void Prefix(Gameplay.Player.Components.EntityBerekComponent __instance)
    {
        PortalModeSelectorRuntime.LogEntityBerekHandleCrown(__instance);
    }
}

[HarmonyPatch]
internal static class MatchStateHelperKinguinverseStartMatchClosurePatch
{
    private static System.Reflection.MethodBase? TargetMethod()
    {
        var outerType = PortalModeSelectorRuntime.FindPatchedType("Gameplay.Match.MatchState.MatchStateHelper");
        if (outerType is null)
        {
            return null;
        }

        var closureType = outerType.GetNestedTypes(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
            .FirstOrDefault(type => type.Name.Contains("DisplayClass9_0"));
        if (closureType is null)
        {
            return null;
        }

        return closureType.GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            .FirstOrDefault(method => method.Name.Contains("KinguinverseStartMatch") && method.Name.Contains("b__0"));
    }

    private static void Prefix(object __instance)
    {
        PortalModeSelectorRuntime.TryOverrideKinguinverseTag(__instance);
    }
}

[HarmonyPatch]
internal static class WebMatchSetTagPatch
{
    private static System.Reflection.MethodBase? TargetMethod()
    {
        return PortalModeSelectorRuntime.FindPatchedMethod("Kinguinverse.WebServiceProvider.Types_v2.WebMatch", "set_Tag");
    }

    private static void Prefix(ref bool value)
    {
        PortalModeSelectorRuntime.TryOverrideWebMatchTag(ref value);
    }
}
