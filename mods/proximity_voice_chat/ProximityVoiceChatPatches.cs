using Gameplay.Player.Components;
using HarmonyLib;
using UI.Views;

namespace SneakOut.ProximityVoiceChat;

[HarmonyPatch(typeof(SpookedNetworkPlayer), nameof(SpookedNetworkPlayer.Spawned))]
internal static class ProximityVoicePlayerSpawnedPatch
{
    [HarmonyPostfix]
    private static void Postfix(SpookedNetworkPlayer __instance)
    {
        ProximityVoiceChatRuntime.ObservePlayer(__instance);
    }
}

[HarmonyPatch(typeof(SpookedNetworkPlayer), nameof(SpookedNetworkPlayer.Init))]
internal static class ProximityVoicePlayerInitializedPatch
{
    [HarmonyPostfix]
    private static void Postfix(SpookedNetworkPlayer __instance)
    {
        ProximityVoiceChatRuntime.ObservePlayer(__instance);
    }
}

[HarmonyPatch(typeof(SpookedNetworkPlayer), nameof(SpookedNetworkPlayer.Despawned))]
internal static class ProximityVoicePlayerDespawnedPatch
{
    [HarmonyPostfix]
    private static void Postfix(SpookedNetworkPlayer __instance)
    {
        ProximityVoiceChatRuntime.ForgetPlayer(__instance);
    }
}

[HarmonyPatch(typeof(GameMenuView), "OnAwake")]
internal static class ProximityVoiceGameMenuAwakePatch
{
    [HarmonyPostfix]
    private static void Postfix(GameMenuView __instance)
    {
        ProximityVoiceChatRuntime.ObserveSettingsMenu(__instance);
    }
}
