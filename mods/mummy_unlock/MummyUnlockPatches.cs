using Collections;
using Gameplay.Player.Components;
using Gameplay.Skills;
using HarmonyLib;
using Types;
using UI.Views;
using UI.Views.Lobby;

namespace SneakOut.MummyUnlock;

[HarmonyPatch(typeof(SeekerSelectionViewModel), nameof(SeekerSelectionViewModel.Init))]
internal static class SeekerSelectionViewModelInitPatch
{
    private static void Postfix(SeekerSelectionViewModel __instance)
    {
        MummyUnlockRuntime.EnsureAvailableSeekersContainMummy(__instance);
    }
}

[HarmonyPatch(typeof(SeekerSelectionViewModel), "OnSelectionChange")]
internal static class SeekerSelectionViewModelOnSelectionChangePatch
{
    private static void Postfix(SeekerSelectionViewModel __instance)
    {
    }
}

[HarmonyPatch(typeof(SeekerSelectionView), nameof(SeekerSelectionView.ManagerAwake))]
internal static class SeekerSelectionViewManagerAwakePatch
{
    private static void Postfix(SeekerSelectionView __instance)
    {
        MummyAbilityIconRuntime.ApplyToSeekerSelectionView(__instance);
        MummyUnlockRuntime.LogSeekerSelectionView(__instance);
    }
}

[HarmonyPatch(typeof(SeekerSelectionView), nameof(SeekerSelectionView.RefreshLabelText))]
internal static class SeekerSelectionViewRefreshLabelTextPatch
{
    private static void Postfix(SeekerSelectionView __instance)
    {
        MummyAbilityIconRuntime.ApplyToSeekerSelectionView(__instance);
        MummyUnlockRuntime.LogSeekerSelectionView(__instance);
    }
}

[HarmonyPatch(typeof(CharacterShopView), nameof(CharacterShopView.Open))]
internal static class CharacterShopViewOpenPatch
{
    private static void Prefix(CharacterShopView __instance)
    {
        MummyUnlockRuntime.PrepareCharacterShop(__instance);
    }

    private static void Postfix(CharacterShopView __instance)
    {
        MummyUnlockRuntime.LogCharacterShop(__instance);
        MummyUnlockRuntime.LogCharacterShopStep("Open", __instance);
    }
}

[HarmonyPatch(typeof(CharacterShopView), nameof(CharacterShopView.SetDescriptions))]
internal static class CharacterShopViewSetDescriptionsPatch
{
    private static bool Prefix(CharacterShopView __instance)
    {
        return !MummyUnlockRuntime.TryRenderCharacterShopDescription(__instance);
    }
}

[HarmonyPatch(typeof(CharacterShopView), "OnLeftArrow")]
internal static class CharacterShopViewOnLeftArrowPatch
{
    private static void Prefix(CharacterShopView __instance)
    {
        MummyUnlockRuntime.PrepareCharacterShopForShift("OnLeftArrow", __instance);
    }

    private static void Postfix(CharacterShopView __instance)
    {
        MummyUnlockRuntime.LogCharacterShopStep("OnLeftArrow", __instance);
    }
}

[HarmonyPatch(typeof(CharacterShopView), "OnRightArrow")]
internal static class CharacterShopViewOnRightArrowPatch
{
    private static void Prefix(CharacterShopView __instance)
    {
        MummyUnlockRuntime.PrepareCharacterShopForShift("OnRightArrow", __instance);
    }

    private static void Postfix(CharacterShopView __instance)
    {
        MummyUnlockRuntime.LogCharacterShopStep("OnRightArrow", __instance);
    }
}

[HarmonyPatch(typeof(CharacterShopView), "Shift")]
internal static class CharacterShopViewShiftPatch
{
    private static void Prefix(CharacterShopView __instance)
    {
        MummyUnlockRuntime.PrepareCharacterShopForShift("Shift", __instance);
    }

    private static void Postfix(CharacterShopView __instance)
    {
        MummyUnlockRuntime.LogCharacterShopStep("Shift", __instance);
    }
}

[HarmonyPatch(typeof(MummySarcophagusManager), nameof(MummySarcophagusManager.Init))]
internal static class MummySarcophagusManagerInitPatch
{
    private static void Postfix(Sarcophagus[] sarcophagi)
    {
        MummySarcophagusVisualRuntime.ApplyToSarcophagi(sarcophagi);
    }
}

[HarmonyPatch(typeof(SarcophagusInitializer), "Start")]
internal static class SarcophagusInitializerStartPatch
{
    private static void Postfix()
    {
        MummySarcophagusVisualRuntime.ApplyToSceneSarcophagi("SarcophagusInitializer.Start");
    }
}

[HarmonyPatch(typeof(LoadingScreenView), "AllowSceneActivation")]
internal static class LoadingScreenViewAllowSceneActivationSarcophagusPatch
{
    private static void Prefix(SceneType sceneType)
    {
        if (sceneType == SceneType.Game || sceneType == SceneType.Map01 || sceneType == SceneType.Map02 || sceneType == SceneType.Map03 || sceneType == SceneType.Map05_TagGame || sceneType == SceneType.Map_East01 || sceneType == SceneType.Map_East02 || sceneType == SceneType.Map_School02)
        {
            MummySarcophagusVisualRuntime.ApplyToSceneSarcophagi($"LoadingScreenView.AllowSceneActivation:{sceneType}");
        }
    }
}

[HarmonyPatch(typeof(PlayerActionsView), "SetFirstSkillSprite")]
internal static class PlayerActionsViewSetFirstSkillSpritePatch
{
    private static void Postfix(PlayerActionsView __instance, SpookedSkillType firstSkill)
    {
        MummyAbilityIconRuntime.ApplyToPlayerActionsView(__instance, firstSkill, false);
    }
}

[HarmonyPatch(typeof(PlayerActionsView), "SetSecondSkillSprite")]
internal static class PlayerActionsViewSetSecondSkillSpritePatch
{
    private static void Postfix(PlayerActionsView __instance, SpookedSkillType secondSkill)
    {
        MummyAbilityIconRuntime.ApplyToPlayerActionsView(__instance, secondSkill, true);
    }
}

[HarmonyPatch(typeof(EntityNetworkAnimatorComponent), nameof(EntityNetworkAnimatorComponent.HandleBuffAnimation))]
internal static class EntityNetworkAnimatorComponentHandleBuffAnimationPatch
{
    private static void Prefix(EntityNetworkAnimatorComponent __instance, SpookedBuffType buffType, float duration)
    {
        MummyAnimatorFallbackRuntime.TryApplyReactionController(__instance, buffType, duration);
    }
}

[HarmonyPatch(typeof(EntityNetworkAnimatorComponent), nameof(EntityNetworkAnimatorComponent.Render))]
internal static class EntityNetworkAnimatorComponentRenderPatch
{
    private static void Postfix(EntityNetworkAnimatorComponent __instance)
    {
        MummyAnimatorFallbackRuntime.RestoreIfDue(__instance);
    }
}

[HarmonyPatch]
internal static class SceneSpawnerOnPlayerLoadedSarcophagusPatch
{
    private static System.Reflection.MethodBase? TargetMethod()
    {
        var sceneSpawnerType = AccessTools.TypeByName("Gameplay.Spawn.SceneSpawner");
        return sceneSpawnerType is null ? null : AccessTools.Method(sceneSpawnerType, "OnPlayerLoaded");
    }

    private static void Postfix()
    {
        MummySarcophagusVisualRuntime.ApplyToSceneSarcophagi("SceneSpawner.OnPlayerLoaded");
    }
}

[HarmonyPatch(typeof(Sarcophagus), "OnAwake")]
internal static class SarcophagusOnAwakePatch
{
    private static void Postfix(Sarcophagus __instance)
    {
        MummySarcophagusVisualRuntime.ApplyToSarcophagus(__instance);
    }
}
