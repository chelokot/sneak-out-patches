using System.Reflection;
using Fusion;
using Gameplay.ArrowIndicators;
using Gameplay.Match;
using Gameplay.Player;
using Gameplay.Player.Components;
using Gameplay.Player.Customization;
using Gameplay.Spawn;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Networking;
using Networking.Matchmaking;
using Networking.Matchmaking.Match;
using Networking.Party;
using Types;
using UI;
using UnityEngine;

namespace SneakOut.LobbyTestBot;

[HarmonyPatch(typeof(PlayersPositionIndicator), nameof(PlayersPositionIndicator.ManagerAwake))]
internal static class PlayersPositionIndicatorManagerAwakePatch
{
    [HarmonyPrefix]
    private static bool Prefix(PlayersPositionIndicator __instance)
    {
        if (!LobbyTestBotRuntime.PreparePlayerIndicators(__instance))
        {
            return false;
        }

        var indicators = __instance._playerIndicators;
        if (indicators is null)
        {
            __instance._playerIndicators = new Il2CppReferenceArray<PlayerIndicator>(0);
            return true;
        }

        var validIndicators = indicators
            .Where(indicator => indicator is not null && indicator.Pointer != IntPtr.Zero)
            .ToArray();
        if (validIndicators.Length == indicators.Length)
        {
            return true;
        }

        var compacted = new Il2CppReferenceArray<PlayerIndicator>(validIndicators.Length);
        for (var index = 0; index < validIndicators.Length; index++)
        {
            compacted[index] = validIndicators[index];
        }
        __instance._playerIndicators = compacted;
        return true;
    }
}

[HarmonyPatch(typeof(PlayersPositionIndicator), "OnAfterMyCharacterChangedEvent")]
internal static class PlayersPositionIndicatorCharacterChangedPatch
{
    [HarmonyPrefix]
    private static bool Prefix(PlayersPositionIndicator __instance)
    {
        return LobbyTestBotRuntime.PlayerIndicatorsHaveDependencies(__instance);
    }
}

[HarmonyPatch(typeof(GameUIManager), "OnAwake")]
internal static class GameUiManagerOnAwakePatch
{
    [HarmonyPostfix]
    private static void Postfix(GameUIManager __instance)
    {
        LobbyTestBotRuntime.BindPortalManager(__instance);
    }
}

[HarmonyPatch(typeof(SceneSpawner), nameof(SceneSpawner.Spawn))]
internal static class SceneSpawnerSpawnCachePatch
{
    [HarmonyPostfix]
    private static void Postfix(SceneSpawner __instance)
    {
        LobbyTestBotRuntime.RememberSceneSpawner(__instance);
    }
}

[HarmonyPatch(typeof(SpookedNetworkPlayer), nameof(SpookedNetworkPlayer.Despawned))]
internal static class SpookedNetworkPlayerDespawnedPatch
{
    [HarmonyPostfix]
    private static void Postfix(SpookedNetworkPlayer __instance)
    {
        LobbyTestBotRuntime.ObservePlayerDespawned(__instance);
    }
}

[HarmonyPatch(typeof(PgosLobby), nameof(PgosLobby.TeamCount), MethodType.Getter)]
internal static class PgosLobbyTeamCountPatch
{
    [HarmonyPostfix]
    private static void Postfix(ref int __result)
    {
        LobbyTestBotRuntime.IncludeManagedBotInPartyCount(ref __result);
    }
}

[HarmonyPatch(typeof(Gameplay.Match.MatchState.ShouldStartState), "GetRandomSeeker")]
[HarmonyBefore("chelokot.sneakout.uniform-seeker-random")]
internal static class ManagedBotHunterPriorityPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(
        Gameplay.Match.MatchState.ShouldStartState __instance,
        ref int __result)
    {
        return !LobbyTestBotRuntime.TryPrioritizeManagedBotAsSeeker(__instance, ref __result);
    }
}

[HarmonyPatch(typeof(Gameplay.Match.MatchState.SelectionState), nameof(Gameplay.Match.MatchState.SelectionState.Tick))]
internal static class ManagedBotHunterConfirmationPatch
{
    [HarmonyPostfix]
    private static void Postfix(Gameplay.Match.MatchState.SelectionState __instance)
    {
        LobbyTestBotRuntime.ConfirmManagedBotHunter(__instance);
    }
}

[HarmonyPatch(typeof(Matchmaker), "OnStartMatchmaking")]
internal static class MatchmakerOnStartMatchmakingPatch
{
    [HarmonyPrefix]
    private static bool Prefix(Matchmaker __instance, Il2CppSystem.EventArgs args, out bool __state)
    {
        __state = false;
        if (LobbyTestBotRuntime.ManagedMatchStartInProgress)
        {
            return false;
        }

        var startEvent = args.Cast<Events.StartMatchmakingEvent>();
        __state = LobbyTestBotRuntime.BeginManagedBotMatchStart(__instance, startEvent.GameModeType);
        return true;
    }

    [HarmonyPostfix]
    private static void Postfix(Matchmaker __instance, bool __state)
    {
        LobbyTestBotRuntime.FinishManagedBotMatchStart(__instance, __state);
    }

    [HarmonyFinalizer]
    private static void Finalizer()
    {
        LobbyTestBotRuntime.EndManagedBotMatchStartScope();
    }
}

[HarmonyPatch(typeof(LobbySessionMatchResolver), "Resolve")]
internal static class LobbySessionMatchResolverResolvePatch
{
    [HarmonyPrefix]
    private static void Prefix(LobbySessionMatchResolver __instance, out ManagedBotResolverState? __state)
    {
        __state = LobbyTestBotRuntime.ExcludeManagedBotFromHostResolution(__instance);
    }

    [HarmonyFinalizer]
    private static Exception? Finalizer(Exception? __exception, ManagedBotResolverState? __state)
    {
        LobbyTestBotRuntime.RestoreManagedBotAfterHostResolution(__state);
        return __exception;
    }
}

[HarmonyPatch(typeof(GameState), nameof(GameState.CurrentStateBlock))]
internal static class GameStateCurrentStateBlockPatch
{
    [HarmonyPostfix]
    private static void Postfix(ref bool __result)
    {
        LobbyTestBotRuntime.AllowManagedBotMatchStart(ref __result);
    }
}

[HarmonyPatch(typeof(PlayerDangerAudio), "HandleDangerAudio")]
internal static class PlayerDangerAudioHandleDangerAudioPatch
{
    [HarmonyPrefix]
    private static bool Prefix(PlayerDangerAudio __instance)
    {
        return LobbyTestBotRuntime.CanUpdateDangerAudio(__instance._networkPlayerRegistry);
    }
}

[HarmonyPatch(typeof(SpookedNetworkPlayer), nameof(SpookedNetworkPlayer.Init))]
internal static class SpookedNetworkPlayerInitPatch
{
    [HarmonyPostfix]
    private static void Postfix(SpookedNetworkPlayer __instance)
    {
        LobbyTestBotRuntime.ObservePlayerInitialized(__instance);
    }
}

[HarmonyPatch(typeof(PlayerInputController), "ResolveLocalInputs")]
internal static class DiagnosticPlayerMovementPatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerInputController __instance)
    {
        LobbyTestBotRuntime.ApplyDiagnosticMovement(__instance);
    }
}

[HarmonyPatch(typeof(UnderlyingPrefabComponent), nameof(UnderlyingPrefabComponent.PrefabSpawned))]
internal static class UnderlyingPrefabComponentPrefabSpawnedPatch
{
    [HarmonyPostfix]
    private static void Postfix(UnderlyingPrefabComponent __instance, GameObject prefabInstance)
    {
        LobbyTestBotRuntime.ObserveUnderlyingPrefabSpawned(__instance, prefabInstance);
    }
}

[HarmonyPatch]
internal static class SceneSpawnerBotSpawnCompletedPatch
{
    private static MethodBase TargetMethod()
    {
        var closureType = typeof(SceneSpawner).GetNestedType(
            "__c__DisplayClass25_0",
            BindingFlags.Public | BindingFlags.NonPublic);
        return AccessTools.DeclaredMethod(closureType, "_OnSpawnActorEvent_b__0");
    }

    [HarmonyPostfix]
    private static void Postfix(NetworkObject instance)
    {
        LobbyTestBotRuntime.ObserveBotSpawnCompleted(instance);
    }
}

[HarmonyPatch(typeof(EntityNetworkAnimatorComponent), nameof(EntityNetworkAnimatorComponent.FixedUpdateNetwork))]
internal static class ManagedBotNetworkAnimatorPatch
{
    [HarmonyPrefix]
    private static bool Prefix(EntityNetworkAnimatorComponent __instance)
    {
        // The managed bot deliberately has no SpookedInputs entry. The stock animator assumes
        // every network player has one and otherwise logs a Fusion exception every simulation tick.
        return LobbyTestBotRuntime.ShouldRunNetworkAnimator(__instance);
    }
}

[HarmonyPatch(typeof(EntityNetworkAnimatorComponent), nameof(EntityNetworkAnimatorComponent.AfterSpawned))]
internal static class ManagedBotNetworkAnimatorAfterSpawnedPatch
{
    [HarmonyPrefix]
    private static bool Prefix(EntityNetworkAnimatorComponent __instance)
    {
        // During the lobby-to-match carry the visual prefab is not guaranteed to exist when this
        // async callback fires. The stock callback dereferences that absent prefab and leaves the
        // dummy's animation component half initialized.
        return LobbyTestBotRuntime.ShouldRunNetworkAnimator(__instance);
    }
}

[HarmonyPatch]
internal static class ManagedBotNetworkAnimatorCommandPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        // A dummy has no authored character prefab, so the network animator component can exist
        // while its Unity Animator and animation table are intentionally absent. Keep all stock
        // match positioning/state logic, but make animation mutations inert for this one object.
        var mutationNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "SetBool",
            "SetFloat",
            "SetInt",
            "SetTrigger",
            "ResetTrigger",
        };

        return AccessTools.GetDeclaredMethods(typeof(EntityNetworkAnimatorComponent))
            .Where(method => mutationNames.Contains(method.Name));
    }

    [HarmonyPrefix]
    private static bool Prefix(EntityNetworkAnimatorComponent __instance)
    {
        return LobbyTestBotRuntime.ShouldRunNetworkAnimator(__instance);
    }
}

[HarmonyPatch(typeof(PlayerCharacterPrefabController), "OnAwake")]
internal static class CharacterPrefabControllerAwakePatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerCharacterPrefabController __instance)
    {
        LobbyTestBotRuntime.NormalizeCurrentCostumeRenderers(__instance);
    }
}

[HarmonyPatch(typeof(PlayerCharacterPrefabController), nameof(PlayerCharacterPrefabController.RefreshCharacter))]
internal static class CharacterPrefabControllerRefreshPatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerCharacterPrefabController __instance)
    {
        LobbyTestBotRuntime.NormalizeCurrentCostumeRenderers(__instance);
    }
}

[HarmonyPatch(typeof(PlayerCharacterPrefabController), nameof(PlayerCharacterPrefabController.RefreshCharacterPreview))]
internal static class CharacterPrefabControllerPreviewPatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerCharacterPrefabController __instance)
    {
        LobbyTestBotRuntime.NormalizeCurrentCostumeRenderers(__instance);
    }
}

[HarmonyPatch(typeof(SceneTypeExtension), nameof(SceneTypeExtension.GetRandomScene))]
internal static class DiagnosticMapSelectionPatch
{
    [HarmonyPostfix]
    private static void Postfix(ref SceneType __result)
    {
        LobbyTestBotRuntime.TryOverrideDiagnosticMap(ref __result);
    }
}
