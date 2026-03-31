using Collections;
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
        MummyUnlockRuntime.LogSeekerSelectionView(__instance);
    }
}

[HarmonyPatch(typeof(SeekerSelectionView), nameof(SeekerSelectionView.RefreshLabelText))]
internal static class SeekerSelectionViewRefreshLabelTextPatch
{
    private static void Postfix(SeekerSelectionView __instance)
    {
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

[HarmonyPatch(typeof(SarcophagusInitializer), "Start")]
internal static class SarcophagusInitializerStartPatch
{
    private static void Postfix(Sarcophagus[] ___sarcophagi)
    {
        MummySarcophagusVisualRuntime.ApplyToSarcophagi(___sarcophagi);
    }
}
