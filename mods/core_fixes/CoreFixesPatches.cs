using Events;
using Gameplay.Match.MatchState;
using HarmonyLib;
using Il2CppSystem;
using Networking.PGOS;
using Types;
using UI.Views;

namespace SneakOut.CoreFixes;

[HarmonyPatch(typeof(PgosLobby), "OnJoinLobbyEvent")]
internal static class PgosLobbyOnJoinLobbyEventPatch
{
    private static bool Prefix(PgosLobby __instance, Il2CppSystem.Object? sender, Il2CppSystem.EventArgs? args)
    {
        return !CoreFixesRuntime.TryHandleJoinLobbyEvent(__instance, args);
    }
}

[HarmonyPatch(typeof(ShouldStartState), "GetRandomSeeker")]
internal static class ShouldStartStateGetRandomSeekerPatch
{
    private static bool Prefix(ShouldStartState __instance, ref int __result)
    {
        return !CoreFixesRuntime.TryHandleUniformHunterRandom(__instance, ref __result);
    }
}

[HarmonyPatch(typeof(BattlepassView), "OnOnWebplayerRefreshEvent")]
internal static class BattlepassViewOnOnWebplayerRefreshEventPatch
{
    private static bool Prefix()
    {
        if (!CoreFixesRuntime.ShouldDisableCrashyBattlepassRefreshHandler)
        {
            return true;
        }

        CoreFixesRuntime.LogBattlepassRefreshSuppressed();
        return false;
    }
}

[HarmonyPatch(typeof(BattlepassView), "SetEndTime")]
internal static class BattlepassViewSetEndTimePatch
{
    private static bool Prefix()
    {
        if (!CoreFixesRuntime.ShouldDisableCrashyBattlepassRefreshHandler)
        {
            return true;
        }

        CoreFixesRuntime.LogLoopSuppressed("BattlepassView.SetEndTime");
        return false;
    }
}

[HarmonyPatch(typeof(DailyQuestsView), "Refresh")]
internal static class DailyQuestsViewRefreshPatch
{
    private static bool Prefix()
    {
        if (!CoreFixesRuntime.ShouldDisableCrashyBattlepassRefreshHandler)
        {
            return true;
        }

        CoreFixesRuntime.LogLoopSuppressed("DailyQuestsView.Refresh");
        return false;
    }
}
