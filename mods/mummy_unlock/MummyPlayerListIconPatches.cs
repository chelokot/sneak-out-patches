using HarmonyLib;
using UI;

namespace SneakOut.MummyUnlock;

[HarmonyPatch(typeof(PlayerInGameRecord), nameof(PlayerInGameRecord.Refresh))]
internal static class MummyPlayerInGameRecordRefreshPatch
{
    private static void Postfix(PlayerInGameRecord __instance, int playerId)
    {
        try
        {
            MummyAbilityIconRuntime.ApplyToPlayerList(__instance, playerId);
        }
        catch (Exception exception)
        {
            MummyUnlockRuntime.LogError("Applying Mummy player-list icon failed", exception);
        }
    }
}
