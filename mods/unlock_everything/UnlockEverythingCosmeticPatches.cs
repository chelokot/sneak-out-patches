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
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace SneakOut.UnlockEverything;

[HarmonyPatch(typeof(PlayerNewMetaInventory), nameof(PlayerNewMetaInventory.GetOwnedSkinParts))]
internal static class PlayerNewMetaInventoryGetOwnedSkinPartsPatch
{
    private static void Postfix(ref Il2CppStructArray<SkinPartType> __result)
    {
        var supported = UnlockEverythingCosmeticCatalog.GetSupportedSkinParts();
        if (supported is null || __result is null)
        {
            return;
        }

        var filtered = __result.Where(supported.Contains).Distinct().ToArray();
        if (filtered.Length == __result.Length)
        {
            return;
        }

        var safeResult = new Il2CppStructArray<SkinPartType>(filtered.Length);
        for (var index = 0; index < filtered.Length; index++)
        {
            safeResult[index] = filtered[index];
        }

        __result = safeResult;
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

[HarmonyPatch(typeof(AvatarAndFrameView), "EquipModification")]
internal static class AvatarAndFrameViewEquipModificationPatch
{
    private static bool Prefix(AvatarAndFrameView __instance)
    {
        if (__instance is not null && UnlockEverythingSelections.TryParseAvatarProductFromView(__instance, out var avatarType, out var avatarFrameType, out var descriptionType))
        {
            UnlockEverythingRuntime.LogSkillUiEvent("AvatarAndFrameView.EquipModification", $"avatar={avatarType}, frame={avatarFrameType}, title={descriptionType}");
        }
        else
        {
            UnlockEverythingRuntime.LogSkillUiEvent("AvatarAndFrameView.EquipModification", "selectionUnavailable");
        }

        return true;
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
    private static SkinType? _pendingSkinType;
    private static SkinPartType? _pendingSkinPartType;

    public static bool TryConsumePendingSkinType(out SkinType skinType)
    {
        if (_pendingSkinType.HasValue)
        {
            skinType = _pendingSkinType.Value;
            _pendingSkinType = null;
            return true;
        }

        skinType = SkinType.None;
        return false;
    }

    public static bool TryConsumePendingSkinPartType(out SkinPartType skinPartType)
    {
        if (_pendingSkinPartType.HasValue)
        {
            skinPartType = _pendingSkinPartType.Value;
            _pendingSkinPartType = null;
            return true;
        }

        skinPartType = SkinPartType.None;
        return false;
    }

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

        _pendingSkinType = skinType;
        _pendingSkinPartType = partType;
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

[HarmonyPatch(typeof(CustomizeCharacterNewMetaView), "CostumeChange")]
internal static class CustomizeCharacterNewMetaViewCostumeChangePatch
{
    private static void Postfix(CustomizeCharacterNewMetaView __instance, Il2CppTasks.Task __result)
    {
        if (!UnlockEverythingRuntime.UsePersistentSelections || __result is null)
        {
            return;
        }

        try
        {
            if (__result.IsCompletedSuccessfully)
            {
                RefreshCostumeView(__instance);
                return;
            }

            UnlockEverythingRuntime.ContinueOnMainThread(
                __result,
                () => RefreshCostumeView(__instance),
                "Unlock Everything CustomizeCharacterNewMetaView.CostumeChange postfix failed");
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Unlock Everything CustomizeCharacterNewMetaView.CostumeChange postfix failed", exception);
        }
    }

    private static void RefreshCostumeView(CustomizeCharacterNewMetaView view)
    {
        try
        {
            var currentSkinType = PlayerNewMetaInventoryChangeSkinEquippedPatch.TryConsumePendingSkinType(out var pendingSkinType)
                ? pendingSkinType
                : view._currentSkinType;
            var currentSkinPartType = PlayerNewMetaInventoryChangeSkinEquippedPatch.TryConsumePendingSkinPartType(out var pendingSkinPartType)
                ? pendingSkinPartType
                : view._currentSkinPartType;
            UnlockEverythingRuntime.LogSkinPreview("CustomizeCharacterNewMetaView.CostumeChange:refresh", 0, currentSkinType, currentSkinPartType, true);
            if (currentSkinType == SkinType.None)
            {
                return;
            }

            view._currentSkinType = currentSkinType;
            view._currentSkinPartType = currentSkinPartType;
            view._currentCategorySelectedIndex = GetCategoryIndex(currentSkinType);
            InvokeCategoryView(view, currentSkinType);
            UnlockEverythingRuntime.LogCostumePieces("CustomizeCharacterNewMetaView.CostumeChange:afterShowCostume", view);
            view.CurrentCostumeSelectedSprite(currentSkinType);
            if (currentSkinPartType != SkinPartType.None)
            {
                view.ChangeSelectedSprite(currentSkinPartType);
            }

            UnlockEverythingRuntime.LogCostumePieces("CustomizeCharacterNewMetaView.CostumeChange:afterSelectionRefresh", view);
            UnlockEverythingSelections.SyncPreviewCharacterData(currentSkinType, currentSkinPartType);
            TryRefreshPreviewModel(currentSkinType, currentSkinPartType);
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend stabilizer CustomizeCharacterNewMetaView.CostumeChange refresh failed", exception);
        }
    }

    private static int GetCategoryIndex(SkinType skinType)
    {
        return skinType switch
        {
            SkinType.Head => 0,
            SkinType.Hands => 1,
            SkinType.Chest => 2,
            SkinType.Legs => 3,
            SkinType.Back => 4,
            SkinType.Whole => 5,
            _ => 0
        };
    }

    private static void InvokeCategoryView(CustomizeCharacterNewMetaView view, SkinType skinType)
    {
        switch (skinType)
        {
            case SkinType.Head:
                view.ShowHeadTypes();
                break;
            case SkinType.Hands:
                view.ShowArmsTypes();
                break;
            case SkinType.Chest:
                view.ShowTorsoTypes();
                break;
            case SkinType.Legs:
                view.ShowLegsTypes();
                break;
            case SkinType.Back:
                view.ShowBackTypes();
                break;
            case SkinType.Whole:
                view.ShowWholeTypes();
                break;
        }
    }

    private static void TryRefreshPreviewModel(SkinType skinType, SkinPartType skinPartType)
    {
        try
        {
            var previewViews = UnityEngine.Resources.FindObjectsOfTypeAll<PlayerCustomizationView>();
            UnlockEverythingRuntime.LogSkinPreview("CustomizeCharacterNewMetaView.CostumeChange:previewViews", previewViews.Length, skinType, skinPartType, true);

            foreach (var previewView in previewViews)
            {
                if (previewView is null)
                {
                    continue;
                }

                previewView.TryPreviewOutfit(skinPartType, skinType);
                UnlockEverythingRuntime.LogSkinPreview("CustomizeCharacterNewMetaView.CostumeChange:previewInvoked", 0, skinType, skinPartType, true);
            }
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend stabilizer CustomizeCharacterNewMetaView preview refresh failed", exception);
        }
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
