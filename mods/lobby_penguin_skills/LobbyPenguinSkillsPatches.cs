using Gameplay.Player.Components;
using HarmonyLib;
using UI;
using UI.Views;
using UnityEngine;

namespace SneakOut.LobbyPenguinSkills;

[HarmonyPatch(typeof(GameUIManager), "ActivateLobby")]
internal static class GameUIManagerActivateLobbyPatch
{
    private static void Postfix()
    {
        LobbyPenguinSkillsRuntime.SetLobbyUiActive(true);
    }
}

[HarmonyPatch(typeof(GameUIManager), "ActivateGame")]
internal static class GameUIManagerActivateGamePatch
{
    private static void Postfix()
    {
        LobbyPenguinSkillsRuntime.SetLobbyUiActive(false);
    }
}

[HarmonyPatch(typeof(GameUIManager), "ActivateTutorial")]
internal static class GameUIManagerActivateTutorialPatch
{
    private static void Postfix()
    {
        LobbyPenguinSkillsRuntime.SetLobbyUiActive(false);
    }
}

[HarmonyPatch(typeof(GameUIManager), "ActivateEndScreen")]
internal static class GameUIManagerActivateEndScreenPatch
{
    private static void Postfix()
    {
        LobbyPenguinSkillsRuntime.SetLobbyUiActive(false);
    }
}

[HarmonyPatch(typeof(EntitySkillsComponent), "HostValidateAndUseSkill")]
internal static class EntitySkillsComponentHostValidateAndUseSkillPatch
{
    private static bool Prefix(EntitySkillsComponent __instance, bool isSecondSkill)
    {
        return !LobbyPenguinSkillsRuntime.TryHandleLobbySkillUse(__instance, isSecondSkill);
    }
}

[HarmonyPatch]
internal static class PlayerInputControllerFirstSkillPatch
{
    private static System.Reflection.MethodBase? TargetMethod()
    {
        var type = AccessTools.TypeByName("Gameplay.Player.PlayerInputController");
        return type is null ? null : AccessTools.Method(type, "OnFirstSkillInput");
    }

    private static bool Prefix(Component __instance)
    {
        return !LobbyPenguinSkillsRuntime.TryHandleLobbySkillInput(__instance, false);
    }
}

[HarmonyPatch]
internal static class PlayerInputControllerSecondSkillPatch
{
    private static System.Reflection.MethodBase? TargetMethod()
    {
        var type = AccessTools.TypeByName("Gameplay.Player.PlayerInputController");
        return type is null ? null : AccessTools.Method(type, "OnSecondSkillInput");
    }

    private static bool Prefix(Component __instance)
    {
        return !LobbyPenguinSkillsRuntime.TryHandleLobbySkillInput(__instance, true);
    }
}
