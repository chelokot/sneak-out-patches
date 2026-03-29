using HarmonyLib;
using UI.Views;

namespace SneakOut.UICrashGuards;

[HarmonyPatch(typeof(BattlepassView), "OnOnWebplayerRefreshEvent")]
internal static class BattlepassViewOnOnWebplayerRefreshEventPatch
{
    private static bool Prefix()
    {
        if (!UICrashGuardsRuntime.Enabled)
        {
            return true;
        }

        UICrashGuardsRuntime.LogLoopSuppressed("BattlepassView.OnOnWebplayerRefreshEvent");
        return false;
    }
}

[HarmonyPatch(typeof(BattlepassView), "SetEndTime")]
internal static class BattlepassViewSetEndTimePatch
{
    private static bool Prefix()
    {
        if (!UICrashGuardsRuntime.Enabled)
        {
            return true;
        }

        UICrashGuardsRuntime.LogLoopSuppressed("BattlepassView.SetEndTime");
        return false;
    }
}

[HarmonyPatch(typeof(DailyQuestsView), "Refresh")]
internal static class DailyQuestsViewRefreshPatch
{
    private static bool Prefix()
    {
        if (!UICrashGuardsRuntime.Enabled)
        {
            return true;
        }

        UICrashGuardsRuntime.LogLoopSuppressed("DailyQuestsView.Refresh");
        return false;
    }
}
