using Gameplay.Player.Components;
using HarmonyLib;
using UI.Views;
using UnityEngine;
using UnityEngine.UI;

namespace SneakOut.Minimap;

[HarmonyPatch(typeof(GameMenuView), "OnAwake")]
internal static class MinimapGameMenuAwakePatch
{
    [HarmonyPostfix]
    private static void Postfix(GameMenuView __instance)
    {
        MinimapSettingsUi.Attach(__instance);
    }
}

[HarmonyPatch(typeof(GameMenuView), "DeactivateAllPanels")]
internal static class MinimapGameMenuDeactivatePanelsPatch
{
    [HarmonyPostfix]
    private static void Postfix(GameMenuView __instance)
    {
        MinimapSettingsUi.OnStockPanelsDeactivated(__instance);
    }
}

[HarmonyPatch(typeof(GameMenuView), "GetPanel")]
internal static class MinimapGameMenuGetPanelPatch
{
    [HarmonyPostfix]
    private static void Postfix(GameMenuView __instance, ref Button target, ref GameObject __result)
    {
        var mapPanel = MinimapSettingsUi.GetPanelForButton(__instance, target);
        if (mapPanel is not null)
        {
            __result = mapPanel;
        }
    }
}

[HarmonyPatch(typeof(SpookedNetworkPlayer), nameof(SpookedNetworkPlayer.Spawned))]
internal static class MinimapPlayerSpawnedPatch
{
    [HarmonyPostfix]
    private static void Postfix(SpookedNetworkPlayer __instance)
    {
        MinimapRuntime.ObservePlayer(__instance);
    }
}

[HarmonyPatch(typeof(SpookedNetworkPlayer), nameof(SpookedNetworkPlayer.Init))]
internal static class MinimapPlayerInitializedPatch
{
    [HarmonyPostfix]
    private static void Postfix(SpookedNetworkPlayer __instance)
    {
        MinimapRuntime.ObservePlayer(__instance);
    }
}

[HarmonyPatch(typeof(SpookedNetworkPlayer), nameof(SpookedNetworkPlayer.Despawned))]
internal static class MinimapPlayerDespawnedPatch
{
    [HarmonyPrefix]
    private static void Prefix(SpookedNetworkPlayer __instance)
    {
        MinimapRuntime.ForgetPlayer(__instance);
    }
}
