using Gameplay.Match.MatchState;
using HarmonyLib;
using Networking.Lobby;

namespace SneakOut.UniformSeekerRandom;

[HarmonyPatch(typeof(PhotonLobby), nameof(PhotonLobby.JoinMatchSession))]
internal static class UniformSeekerRandomJoinMatchSessionPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.Last)]
    private static void Prefix(string hostId)
    {
        UniformSeekerRandomRuntime.CaptureLaunchHandshake(hostId);
    }
}

[HarmonyPatch(typeof(ShouldStartState), nameof(ShouldStartState.Tick))]
internal static class UniformSeekerRandomShouldStartStateTickPatch
{
    [HarmonyPrefix]
    private static void Prefix(ShouldStartState __instance, MatchStateMachine stateMachine)
    {
        UniformSeekerRandomRuntime.BeginShouldStartTick(__instance, stateMachine);
    }

    [HarmonyPostfix]
    private static void Postfix(ShouldStartState __instance, MatchStateMachine stateMachine)
    {
        UniformSeekerRandomRuntime.EndShouldStartTick(__instance, stateMachine);
    }
}

[HarmonyPatch(typeof(ShouldStartState), "GetRandomSeeker")]
internal static class ShouldStartStateGetRandomSeekerPatch
{
    private static bool Prefix(ShouldStartState __instance, ref int __result)
    {
        return !UniformSeekerRandomRuntime.TryHandleUniformHunterRandom(__instance, ref __result);
    }
}

[HarmonyPatch(typeof(MatchStateMachine), nameof(MatchStateMachine.FixedUpdateNetwork))]
internal static class UniformSeekerRandomMatchStateMachineFixedUpdatePatch
{
    [HarmonyPostfix]
    private static void Postfix(MatchStateMachine __instance)
    {
        UniformSeekerRandomRuntime.ObserveReplicatedSeeker(__instance);
    }
}
