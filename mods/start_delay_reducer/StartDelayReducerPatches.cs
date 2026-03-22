using Gameplay.Match.MatchState;
using HarmonyLib;

namespace SneakOut.StartDelayReducer;

[HarmonyPatch(typeof(BeforeStartState), nameof(BeforeStartState.CalculateStateEndTick))]
internal static class BeforeStartStateCalculateStateEndTickPatch
{
    private static void Postfix(MatchStateMachine stateMachine, ref int __result)
    {
        StartDelayReducerRuntime.ReduceBeforeStartDelay(stateMachine, ref __result);
    }
}

[HarmonyPatch(typeof(CountingToStartState), nameof(CountingToStartState.CalculateStateEndTick))]
internal static class CountingToStartStateCalculateStateEndTickPatch
{
    private static void Postfix(MatchStateMachine stateMachine, ref int __result)
    {
        StartDelayReducerRuntime.ReduceCountingToStartDelay(stateMachine, ref __result);
    }
}
