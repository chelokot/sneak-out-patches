using System.Reflection;
using Fusion;
using Gameplay.Player.Components;
using Gameplay.Spawn;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Networking;
using Networking.Matchmaking;
using Networking.Party;
using UI;

namespace SneakOut.LobbyTestBot;

[HarmonyPatch(typeof(GameUIManager), "OnAwake")]
internal static class GameUiManagerOnAwakePatch
{
    [HarmonyPostfix]
    private static void Postfix(GameUIManager __instance)
    {
        LobbyTestBotRuntime.BindPortalManager(__instance);
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

[HarmonyPatch(typeof(Matchmaker), "OnStartMatchmaking")]
internal static class MatchmakerOnStartMatchmakingPatch
{
    [HarmonyPrefix]
    private static void Prefix(Matchmaker __instance, Il2CppSystem.EventArgs args, out bool __state)
    {
        var startEvent = args.Cast<Events.StartMatchmakingEvent>();
        __state = LobbyTestBotRuntime.BeginManagedBotMatchStart(__instance, startEvent.GameModeType);
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

[HarmonyPatch(typeof(GameState), nameof(GameState.CurrentStateBlock))]
internal static class GameStateCurrentStateBlockPatch
{
    [HarmonyPostfix]
    private static void Postfix(ref bool __result)
    {
        LobbyTestBotRuntime.AllowManagedBotMatchStart(ref __result);
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
