using Fusion;
using Gameplay.Interactions;
using Gameplay.Player.Components;
using HarmonyLib;
using UI.Interactions;

namespace SneakOut.CommunityDiscord;

[HarmonyPatch(typeof(Interactable), nameof(Interactable.Spawned))]
internal static class InteractableSpawnedPatch
{
    [HarmonyPostfix]
    private static void Postfix(Interactable __instance)
    {
        CommunityDiscordRuntime.EnsurePortalSideStatue(__instance);
    }
}

[HarmonyPatch(typeof(EntityInteractiveComponent), "FindInteractables")]
internal static class EntityInteractiveComponentFindInteractablesPatch
{
    [HarmonyPostfix]
    private static void Postfix(EntityInteractiveComponent __instance)
    {
        CommunityDiscordRuntime.EnsurePortalStatueIsDiscoverable(__instance);
    }
}

[HarmonyPatch(typeof(EntityInteractiveComponent), "ResolveSelectedInteractiveComponent")]
internal static class EntityInteractiveComponentResolveSelectedPatch
{
    [HarmonyPostfix]
    private static void Postfix(EntityInteractiveComponent __instance)
    {
        CommunityDiscordRuntime.ResolvePortalStatueSelection(__instance);
    }
}

[HarmonyPatch(typeof(Interactable), nameof(Interactable.NetworkObjectId), MethodType.Getter)]
internal static class InteractableNetworkObjectIdPatch
{
    [HarmonyPrefix]
    private static bool Prefix(Interactable __instance, ref NetworkId __result)
    {
        if (!CommunityDiscordRuntime.TryGetPortalInteractionId(__instance, out var interactionId))
        {
            return true;
        }

        __result = interactionId;
        return false;
    }
}

[HarmonyPatch(typeof(ActionCircle), nameof(ActionCircle.GetNewTarget))]
internal static class ActionCircleGetNewTargetPatch
{
    [HarmonyPostfix]
    private static void Postfix(ref Interactable? __result)
    {
        if (CommunityDiscordRuntime.TryGetSelectedPortalStatue(out var portalStatue))
        {
            __result = portalStatue;
        }
    }
}

[HarmonyPatch(typeof(ActionCircle), "LateUpdate")]
internal static class ActionCircleLateUpdatePatch
{
    [HarmonyPostfix]
    private static void Postfix(ActionCircle __instance)
    {
        CommunityDiscordRuntime.AnchorPortalActionCircleView(__instance);
    }
}
