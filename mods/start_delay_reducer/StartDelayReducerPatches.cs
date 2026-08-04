using Gameplay.Match.MatchState;
using HarmonyLib;
using UI.Views.Lobby;

namespace SneakOut.StartDelayReducer;

[HarmonyPatch(typeof(BeforeStartState), nameof(BeforeStartState.CalculateStateEndTick))]
internal static class BeforeStartStateCalculateStateEndTickPatch
{
    private static void Postfix(MatchStateMachine stateMachine, ref int __result)
    {
        StartDelayReducerRuntime.ApplyRequestedSkip(stateMachine, ref __result);
    }
}

[HarmonyPatch(typeof(CountingToStartState), nameof(CountingToStartState.CalculateStateEndTick))]
internal static class CountingToStartStateCalculateStateEndTickPatch
{
    private static void Postfix(MatchStateMachine stateMachine, ref int __result)
    {
        StartDelayReducerRuntime.ApplyRequestedSkip(stateMachine, ref __result);
    }
}

[HarmonyPatch(typeof(MatchStateMachine), nameof(MatchStateMachine.FixedUpdateNetwork))]
internal static class MatchStateMachineCapturePatch
{
    private static void Prefix(MatchStateMachine __instance)
    {
        StartDelayReducerRuntime.CaptureStateMachine(__instance);
    }
}

[HarmonyPatch(typeof(PortalPlayView), "OnAwake")]
internal static class PortalPlayViewStyleCapturePatch
{
    private static void Postfix(PortalPlayView __instance)
    {
        StartDelayReducerRuntime.CaptureStockButtonStyle(__instance);
    }
}
