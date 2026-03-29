using Gameplay.Match.MatchState;
using HarmonyLib;

namespace SneakOut.UniformSeekerRandom;

[HarmonyPatch(typeof(ShouldStartState), "GetRandomSeeker")]
internal static class ShouldStartStateGetRandomSeekerPatch
{
    private static bool Prefix(ShouldStartState __instance, ref int __result)
    {
        return !UniformSeekerRandomRuntime.TryHandleUniformHunterRandom(__instance, ref __result);
    }
}
