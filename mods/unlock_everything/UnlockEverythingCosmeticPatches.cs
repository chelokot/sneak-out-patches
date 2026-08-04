using Collections;
using Events;
using HarmonyLib;
using Kinguinverse.WebServiceProvider.Types_v2;
using UI;
using UI.Buttons;
using UI.Views;
using Gameplay.Spawn;
using Types.Structs;
using ClientCharacterType = Types.CharacterType;
using Il2CppTasks = Il2CppSystem.Threading.Tasks;

namespace SneakOut.UnlockEverything;

[HarmonyPatch(typeof(SpookedShopNewMeta), nameof(SpookedShopNewMeta.GetAllSkinTypeProducts))]
internal static class SpookedShopNewMetaGetAllSkinTypeProductsPatch
{
    private static void Prefix(SpookedShopNewMeta __instance)
    {
        if (UnlockEverythingRuntime.UseProfileOverlay)
        {
            UnlockEverythingStub.ApplySkinProductsToShop(__instance);
        }
    }
}

[HarmonyPatch(typeof(SceneSpawner), "GetCharacterData")]
internal static class SceneSpawnerGetCharacterDataPatch
{
    private static void Postfix(ref CharacterData __result)
    {
        UnlockEverythingSelections.NormalizeCharacterData(ref __result);
        UnlockEverythingRuntime.LogSkillUiEvent(
            "SceneSpawner.GetCharacterData:normalized",
            $"head={__result.HeadType}, torso={__result.TorsoType}, arms={__result.ArmsType}, legs={__result.LegsType}, back={__result.BackType}, whole={__result.WholeType}");
    }
}

[HarmonyPatch(typeof(AvatarAndFrameView), "SetProducts")]
internal static class AvatarAndFrameViewSetProductsPatch
{
    private static void Postfix(AvatarAndFrameView __instance)
    {
        if (__instance?._titleRecordButtons is null)
        {
            return;
        }

        try
        {
            foreach (var button in __instance._titleRecordButtons)
            {
                if (button?._titleText is null)
                {
                    continue;
                }

                var currentText = button._titleText.text;
                button._titleText.text = AvatarSelectionPolicy.GetTitleDisplayText(
                    currentText,
                    currentText);
            }
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Failed to apply title display fallbacks", exception);
        }
    }
}

[HarmonyPatch(typeof(AvatarAndFrameView), "BuyProduct")]
internal static class AvatarAndFrameViewBuyProductPatch
{
    private static void Prefix(AvatarAndFrameView __instance)
    {
        if (__instance is not null && UnlockEverythingSelections.TryParseAvatarProductFromView(__instance, out var avatarType, out var avatarFrameType, out var descriptionType))
        {
            UnlockEverythingRuntime.LogSkillUiEvent("AvatarAndFrameView.BuyProduct", $"avatar={avatarType}, frame={avatarFrameType}, title={descriptionType}");
        }
        else
        {
            UnlockEverythingRuntime.LogSkillUiEvent("AvatarAndFrameView.BuyProduct", "selectionUnavailable");
        }
    }
}

[HarmonyPatch(typeof(AvatarModyfiRecordButton), "PassChosenCostume")]
internal static class AvatarModyfiRecordButtonPassChosenCostumePatch
{
    private static void Prefix(AvatarModyfiRecordButton __instance)
    {
        if (__instance?.StoredProduct is null)
        {
            UnlockEverythingRuntime.LogSkillUiEvent("AvatarModyfiRecordButton.PassChosenCostume", "storedProductNull");
            return;
        }

        UnlockEverythingRuntime.LogSkillUiEvent("AvatarModyfiRecordButton.PassChosenCostume", "storedProductPresent");
        UnlockEverythingSelections.RememberPendingAvatarSelection(__instance.StoredProduct);
    }
}

[HarmonyPatch(typeof(TitleRecordButton), "PassChosenCostume")]
internal static class TitleRecordButtonPassChosenCostumePatch
{
    private static void Prefix(TitleRecordButton __instance)
    {
        if (__instance?.StoredProduct is null)
        {
            UnlockEverythingRuntime.LogSkillUiEvent("TitleRecordButton.PassChosenCostume", "storedProductNull");
            return;
        }

        UnlockEverythingRuntime.LogSkillUiEvent("TitleRecordButton.PassChosenCostume", "storedProductPresent");
        UnlockEverythingSelections.RememberPendingAvatarSelection(__instance.StoredProduct);
    }
}

[HarmonyPatch(typeof(PlayerNewMetaInventory), nameof(PlayerNewMetaInventory.OnAvatarModyficationChange))]
internal static class PlayerNewMetaInventoryOnAvatarModyficationChangePatch
{
    private static bool Prefix(PlayerNewMetaInventory __instance, Il2CppSystem.Enum productType, ClientCharacterType characterType, ref Il2CppTasks.Task<bool> __result)
    {
        UnlockEverythingSelections.RememberInventory(__instance);
        UnlockEverythingRuntime.LogSkillUiEvent("PlayerNewMetaInventory.OnAvatarModyficationChange:entered", $"characterType={characterType}");
        if (!UnlockEverythingRuntime.UsePersistentSelections)
        {
            return true;
        }

        var handledLocally = UnlockEverythingSelections.ApplyAvatarModificationSelection(productType, characterType);
        UnlockEverythingRuntime.LogAvatarModificationSelection("PlayerNewMetaInventory.OnAvatarModyficationChange:prefix", productType, characterType, handledLocally);
        if (!handledLocally)
        {
            return true;
        }

        __result = Il2CppTasks.Task.FromResult(true);
        return false;
    }

    private static void Postfix(ClientCharacterType characterType, Il2CppTasks.Task<bool> __result)
    {
        UnlockEverythingSelections.SaveAfterCompletion(__result, characterType);
    }
}

[HarmonyPatch(typeof(PlayerNewMetaInventory), nameof(PlayerNewMetaInventory.EmoteChange))]
internal static class PlayerNewMetaInventoryEmoteChangePatch
{
    private static void Postfix(ClientCharacterType characterType, Il2CppTasks.Task<bool> __result)
    {
        UnlockEverythingSelections.SaveAfterCompletion(__result, characterType);
    }
}

[HarmonyPatch(typeof(PlayerNewMetaInventory), nameof(PlayerNewMetaInventory.ChangeSkinEquipped))]
internal static class PlayerNewMetaInventoryChangeSkinEquippedPatch
{
    private static bool Prefix(PlayerNewMetaInventory __instance, SkinPartType partType, SkinType skinType, ClientCharacterType characterType, ref Il2CppTasks.Task __result)
    {
        UnlockEverythingSelections.RememberInventory(__instance);
        var handledLocally = UnlockEverythingRuntime.UsePersistentSelections
            && UnlockEverythingSelections.ApplySkinPartSelection(characterType, skinType, partType);
        UnlockEverythingRuntime.LogSkinPath("PlayerNewMetaInventory.ChangeSkinEquipped:prefix", characterType, skinType, partType, handledLocally);
        if (!handledLocally)
        {
            return true;
        }

        __result = Il2CppTasks.Task.CompletedTask;
        return false;
    }

    private static void Postfix(SkinPartType partType, SkinType skinType, ClientCharacterType characterType, Il2CppTasks.Task __result)
    {
        UnlockEverythingSelections.SaveAfterCompletion(__result, characterType);
        UnlockEverythingSelections.SaveSkinPartAfterCompletion(__result, characterType, skinType, partType);
    }
}

[HarmonyPatch(typeof(PlayerNewMetaInventory), nameof(PlayerNewMetaInventory.GetMyCurrentSkinPartType))]
internal static class PlayerNewMetaInventoryGetMyCurrentSkinPartTypeLoggingPatch
{
    private static void Postfix(SkinType skinType, ClientCharacterType characterType, SkinPartType __result)
    {
        UnlockEverythingRuntime.LogSkinPath("PlayerNewMetaInventory.GetMyCurrentSkinPartType", characterType, skinType, __result, true);
    }
}

[HarmonyPatch(typeof(CustomizeCharacterNewMetaView), "OnCostumePiecked")]
internal static class CustomizeCharacterNewMetaViewOnCostumePieckedLoggingPatch
{
    private static void Postfix(CustomizeCharacterNewMetaView __instance, SkinPartType skinPartType, SkinType skinType)
    {
        UnlockEverythingRuntime.LogSkinPreview("CustomizeCharacterNewMetaView.OnCostumePiecked", 0, skinType, skinPartType, true);
        UnlockEverythingRuntime.LogCostumePieces("CustomizeCharacterNewMetaView.OnCostumePiecked:pieces", __instance);
    }
}

[HarmonyPatch(typeof(CustomizeCharacterNewMetaView), "OnEquipButton")]
internal static class CustomizeCharacterNewMetaViewOnEquipButtonLoggingPatch
{
    private static void Postfix()
    {
        UnlockEverythingRuntime.LogSkinRefreshEvent("CustomizeCharacterNewMetaView.OnEquipButton", 0);
    }
}

[HarmonyPatch(typeof(PlayerCustomizationView), "OnTryOnCharacterOutfitLocally")]
internal static class PlayerCustomizationViewTryOnCharacterOutfitLoggingPatch
{
    private static void Postfix(Il2CppSystem.Object sender, Il2CppSystem.EventArgs args)
    {
        if (args is not TryOnCharacterOutfitLocallyEvent outfitEvent)
        {
            return;
        }

        UnlockEverythingRuntime.LogSkinPreview(
            "PlayerCustomizationView.OnTryOnCharacterOutfitLocally",
            outfitEvent.InternalId,
            outfitEvent.SkinType,
            outfitEvent.CostumeType,
            true);
    }
}

[HarmonyPatch(typeof(PlayerCustomizationView), "OnRefreshCharacterOutfit")]
internal static class PlayerCustomizationViewRefreshCharacterOutfitLoggingPatch
{
    private static void Postfix(Il2CppSystem.Object sender, Il2CppSystem.EventArgs args)
    {
        if (args is not RefreshCharacterOutfit refreshEvent)
        {
            return;
        }

        UnlockEverythingRuntime.LogSkinRefreshEvent("PlayerCustomizationView.OnRefreshCharacterOutfit", refreshEvent.InternalId);
    }
}

[HarmonyPatch(typeof(PlayerCustomizationView), "TryPreviewOutfit")]
internal static class PlayerCustomizationViewTryPreviewOutfitLoggingPatch
{
    private static void Postfix(SkinPartType costumeType, SkinType skinType)
    {
        UnlockEverythingRuntime.LogSkinPreview("PlayerCustomizationView.TryPreviewOutfit", 0, skinType, costumeType, true);
    }
}
