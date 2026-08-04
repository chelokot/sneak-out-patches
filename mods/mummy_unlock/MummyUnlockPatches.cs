using Collections;
using Gameplay.Player.Components;
using Gameplay.Skills;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Types;
using UI.Views;
using UI.Views.Lobby;
using AvatarType = Kinguinverse.WebServiceProvider.Types_v2.AvatarType;

namespace SneakOut.MummyUnlock;

[HarmonyPatch(typeof(SeekerSelectionViewModel), nameof(SeekerSelectionViewModel.Init))]
internal static class SeekerSelectionViewModelInitPatch
{
    private static void Postfix(SeekerSelectionViewModel __instance)
    {
        MummyUnlockRuntime.EnsureAvailableSeekersContainMummy(__instance);
    }
}

[HarmonyPatch(typeof(SpookedNetworkPlayer), nameof(SpookedNetworkPlayer.GetCurrentAvatar))]
internal static class SpookedNetworkPlayerGetCurrentAvatarPatch
{
    private static bool Prefix(SpookedNetworkPlayer __instance, ref AvatarType __result)
    {
        if (!MummyUnlockRuntime.TryGetMummyAvatar(__instance, out var avatarType))
        {
            return true;
        }

        __result = avatarType;
        return false;
    }
}

[HarmonyPatch(typeof(SeekerSelectionView), nameof(SeekerSelectionView.ManagerAwake))]
internal static class SeekerSelectionViewManagerAwakePatch
{
    private static void Postfix(SeekerSelectionView __instance)
    {
        MummyAbilityIconRuntime.ApplyToSeekerSelectionView(__instance);
    }
}

[HarmonyPatch(typeof(SeekerSelectionView), nameof(SeekerSelectionView.RefreshLabelText))]
internal static class SeekerSelectionViewRefreshLabelTextPatch
{
    private static void Postfix(SeekerSelectionView __instance)
    {
        MummyAbilityIconRuntime.ApplyToSeekerSelectionView(__instance);
    }
}

[HarmonyPatch(typeof(CharacterShopView), nameof(CharacterShopView.ManagerAwake))]
internal static class CharacterShopViewManagerAwakePatch
{
    private static void Prefix(CharacterShopView __instance)
    {
        MummyUnlockRuntime.PrepareCharacterShop(__instance);
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

[HarmonyPatch(typeof(MummySarcophagusManager), nameof(MummySarcophagusManager.Init))]
internal static class MummySarcophagusManagerInitPatch
{
    private static void Postfix(Il2CppReferenceArray<Sarcophagus> sarcophagi)
    {
        MummySarcophagusVisualRuntime.ApplyToSarcophagi(sarcophagi);
    }
}

[HarmonyPatch(typeof(SarcophagusInitializer), "Start")]
internal static class SarcophagusInitializerStartPatch
{
    private static void Postfix(SarcophagusInitializer __instance)
    {
        MummySarcophagusVisualRuntime.ApplyToSarcophagi(__instance._sarcophagi);
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
