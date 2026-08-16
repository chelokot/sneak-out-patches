using Gameplay.Interactions;
using HarmonyLib;

namespace SneakOut.CommunityDiscord;

[HarmonyPatch(typeof(Interactable), nameof(Interactable.Spawned))]
internal static class InteractableSpawnedPatch
{
    [HarmonyPostfix]
    private static void Postfix(Interactable __instance)
    {
        CommunityDiscordRuntime.ReplaceStockDiscordStatueUrl(__instance);
    }
}
