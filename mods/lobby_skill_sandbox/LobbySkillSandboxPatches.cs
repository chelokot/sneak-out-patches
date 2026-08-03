using Gameplay.Player;
using Gameplay.Player.Components;
using HarmonyLib;
using Types;
using UI;

namespace SneakOut.LobbySkillSandbox;

[HarmonyPatch(typeof(GameUIManager), "ActivateLobby")]
internal static class GameUIManagerActivateLobbyPatch
{
    private static void Postfix(GameUIManager __instance)
    {
        LobbySkillSandboxRuntime.SetLobbyUiActive(true);
        LobbySkillSandboxRuntime.EnableLobbySkillView(__instance);
    }
}

[HarmonyPatch(typeof(GameUIManager), "ActivateGame")]
internal static class GameUIManagerActivateGamePatch
{
    private static void Postfix()
    {
        LobbySkillSandboxRuntime.SetLobbyUiActive(false);
    }
}

[HarmonyPatch(typeof(GameUIManager), "ActivateTutorial")]
internal static class GameUIManagerActivateTutorialPatch
{
    private static void Postfix()
    {
        LobbySkillSandboxRuntime.SetLobbyUiActive(false);
    }
}

[HarmonyPatch(typeof(GameUIManager), "ActivateEndScreen")]
internal static class GameUIManagerActivateEndScreenPatch
{
    private static void Postfix()
    {
        LobbySkillSandboxRuntime.SetLobbyUiActive(false);
    }
}

[HarmonyPatch(typeof(EntitySkillsComponent), "HostValidateAndUseSkill")]
internal static class EntitySkillsComponentHostValidateAndUseSkillPatch
{
    private static bool Prefix(EntitySkillsComponent __instance, bool isSecondSkill)
    {
        return !LobbySkillSandboxRuntime.TryHandleLobbySkillUse(__instance, isSecondSkill);
    }
}

[HarmonyPatch(typeof(EntitySkillsComponent), "ChangeToProp")]
internal static class EntitySkillsComponentLobbyChangeToPropPatch
{
    private static bool Prefix(EntitySkillsComponent __instance, PlayerPropType playerPropTypeToChangeInto)
    {
        return !LobbySkillSandboxRuntime.TryApplyLobbyPropVisual(__instance, playerPropTypeToChangeInto);
    }
}

[HarmonyPatch(typeof(EntitySkillsComponent), "ChangeFromProp")]
internal static class EntitySkillsComponentLobbyChangeFromPropPatch
{
    private static bool Prefix(EntitySkillsComponent __instance)
    {
        return !LobbySkillSandboxRuntime.TryRestoreLobbyPropVisual(__instance);
    }
}

[HarmonyPatch(typeof(SpookedNetworkPlayer), nameof(SpookedNetworkPlayer.Spawned))]
internal static class SpookedNetworkPlayerSpawnedPatch
{
    private static void Postfix(SpookedNetworkPlayer __instance)
    {
        LobbySkillSandboxRuntime.TryEnableLobbySkillViewAfterSpawn(__instance);
    }
}

[HarmonyPatch(typeof(PlayerInputController), "OnFirstSkillInput")]
internal static class PlayerInputControllerFirstSkillPatch
{
    private static bool Prefix(PlayerInputController __instance)
    {
        return !LobbySkillSandboxRuntime.TryHandleLobbySkillInput(__instance, false);
    }
}

[HarmonyPatch(typeof(PlayerInputController), "OnSecondSkillInput")]
internal static class PlayerInputControllerSecondSkillPatch
{
    private static bool Prefix(PlayerInputController __instance)
    {
        return !LobbySkillSandboxRuntime.TryHandleLobbySkillInput(__instance, true);
    }
}
