using Collections;
using Events;
using HarmonyLib;
using Kinguinverse.WebServiceProvider.Types_v2;
using UI;
using UI.Buttons;
using UI.Views;
using Gameplay.Spawn;
using Scriptables;
using Types.Structs;
using UnityEngine;
using UnityEngine.InputSystem;
using ClientCharacterType = Types.CharacterType;
using Il2CppTasks = Il2CppSystem.Threading.Tasks;

namespace SneakOut.UnlockEverything;

[HarmonyPatch(typeof(AvatarAndFrameView), "OnTitlesCategory")]
internal static class AvatarAndFrameViewOnTitlesCategoryPatch
{
    private static void Prefix(AvatarAndFrameView __instance)
    {
        var keyboard = Keyboard.current;
        var revealRarityFour = keyboard?.leftShiftKey.isPressed == true
            || keyboard?.rightShiftKey.isPressed == true;
        TitleMenuVisibility.SetRevealRarityFour(__instance, revealRarityFour);
    }
}

internal static class TitleMenuVisibility
{
    private static readonly HashSet<IntPtr> RevealedViews = new();

    public static void SetRevealRarityFour(AvatarAndFrameView view, bool reveal)
    {
        if (view is null || view.Pointer == IntPtr.Zero)
        {
            return;
        }

        if (reveal)
        {
            RevealedViews.Add(view.Pointer);
        }
        else
        {
            RevealedViews.Remove(view.Pointer);
        }
    }

    public static bool ShouldRevealRarityFour(AvatarAndFrameView view)
    {
        return view is not null
            && view.Pointer != IntPtr.Zero
            && RevealedViews.Contains(view.Pointer);
    }
}

[HarmonyPatch(typeof(TypesUtils), nameof(TypesUtils.IsPurchasable), new[] { typeof(SkinPartType) })]
internal static class TypesUtilsSkinPartIsPurchasablePatch
{
    private static void Postfix(SkinPartType skinPartType, ref bool __result)
    {
        if (UnlockEverythingRuntime.UseProfileOverlay)
        {
            __result = SkinPartCatalogPolicy.IsLocallyPurchasable(skinPartType, SkinPartType.None);
        }
    }
}

[HarmonyPatch(typeof(SpookedSkinSprites), nameof(SpookedSkinSprites.GetSprite))]
internal static class SpookedSkinSpritesGetSpritePatch
{
    private static bool Prefix(SpookedSkinSprites __instance, SkinPartType skinPartyType, ref Sprite __result)
    {
        if (!UnlockEverythingRuntime.UseProfileOverlay
            || __instance?._skinReference is null)
        {
            return true;
        }

        foreach (var reference in __instance._skinReference)
        {
            if (reference is not null
                && reference.Pointer != IntPtr.Zero
                && reference.SkinPartType == skinPartyType
                && !string.IsNullOrWhiteSpace(reference.SpriteName))
            {
                return true;
            }
        }

        // Synthetic hidden products legitimately have no atlas entry in this client build.
        // The stock lookup logs an error and performs a costly failed atlas search for every
        // such card. Its eventual result is null, so return that result immediately while
        // keeping the product visible in the wardrobe.
        __result = null!;
        return false;
    }
}

[HarmonyPatch(typeof(CustomizeCharacterNewMetaView), "ShowCostume")]
internal static class CustomizeCharacterNewMetaViewShowCostumePatch
{
    private static void Prefix(CustomizeCharacterNewMetaView __instance)
    {
        var shop = __instance?._spookedShopNewMeta;
        if (UnlockEverythingRuntime.UseProfileOverlay && shop is not null)
        {
            UnlockEverythingStub.ApplySkinProductsToShop(shop);
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
internal static class AvatarAndFrameViewFilterTitleSlotsPatch
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
                if (button is null)
                {
                    continue;
                }

                var storedProduct = button.StoredProduct;
                var shouldShow = storedProduct is not null
                    && storedProduct.Pointer != IntPtr.Zero
                    && System.Enum.TryParse(storedProduct.ToString(), out DescriptionType descriptionType)
                    && TitleAccessPolicy.ShouldShowInMenu(
                        (int)descriptionType,
                        __instance._spookedSettings.Titles.GetTitleRarity(descriptionType),
                        TitleMenuVisibility.ShouldRevealRarityFour(__instance));
                button.gameObject.SetActive(shouldShow);
            }
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Failed to filter title slots", exception);
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
