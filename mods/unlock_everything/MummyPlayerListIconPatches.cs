using HarmonyLib;
using UI;

namespace SneakOut.UnlockEverything;

[HarmonyPatch(typeof(PlayerInGameRecord), nameof(PlayerInGameRecord.Refresh))]
internal static class MummyPlayerInGameRecordRefreshPatch
{
    private static void Postfix(PlayerInGameRecord __instance, int playerId)
    {
        try
        {
            MummyPerkShopRuntime.ApplyPlayerListIcon(__instance, playerId);
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Applying Mummy player-list icon failed", exception);
        }
    }
}
