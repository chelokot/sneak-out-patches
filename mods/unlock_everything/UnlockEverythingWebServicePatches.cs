using HarmonyLib;
using Kinguinverse.WebServiceProvider;
using Kinguinverse.WebServiceProvider.Requests;
using Kinguinverse.WebServiceProvider.Responses;
using Kinguinverse.WebServiceProvider.Responses.V2;
using Kinguinverse.WebServiceProvider.Types.Games;
using Kinguinverse.WebServiceProvider.Types_v2;
using Kinguinverse.WebServiceProvider.Types_v2.Products;
using Il2CppCollections = Il2CppSystem.Collections.Generic;
using Il2CppTasks = Il2CppSystem.Threading.Tasks;

namespace SneakOut.UnlockEverything;

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.PutOnCharacterSkin))]
internal static class KinguinverseWebServicePutOnCharacterSkinPatch
{
    private static bool Prefix(int characterId, int characterSkinTypeId, ref Il2CppTasks.Task<Result<bool>> __result)
    {
        var handledLocally = UnlockEverythingRuntime.UsePersistentSelections && UnlockEverythingSelections.ApplyCharacterSkinSelection(characterId, characterSkinTypeId);
        UnlockEverythingRuntime.LogSkinWebCall("KinguinverseWebService.PutOnCharacterSkin:prefix", characterId, characterSkinTypeId, handledLocally);
        if (!handledLocally)
        {
            UnlockEverythingSelections.SaveCharacterSkinSelection(characterId, characterSkinTypeId);
            return true;
        }

        __result = UnlockEverythingStub.SuccessBoolean();
        return false;
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.PutOnAvatar))]
internal static class KinguinverseWebServicePutOnAvatarPatch
{
    private static bool Prefix(int characterId, int avatarId, ref Il2CppTasks.Task<Result<bool>> __result)
    {
        var handledLocally = UnlockEverythingRuntime.UsePersistentSelections && UnlockEverythingSelections.ApplyAvatarSelection(characterId, avatarId);
        if (!handledLocally)
        {
            UnlockEverythingRuntime.LogSkillUiEvent("KinguinverseWebService.PutOnAvatar:prefix", $"characterId={characterId}, avatarId={avatarId}, handledLocally=False");
            return true;
        }

        UnlockEverythingRuntime.LogSkillUiEvent("KinguinverseWebService.PutOnAvatar:prefix", $"characterId={characterId}, avatarId={avatarId}, handledLocally=True");
        __result = UnlockEverythingStub.SuccessBoolean();
        return false;
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.PutOnAvatarFrame))]
internal static class KinguinverseWebServicePutOnAvatarFramePatch
{
    private static bool Prefix(int characterId, int avatarFrameId, ref Il2CppTasks.Task<Result<bool>> __result)
    {
        var handledLocally = UnlockEverythingRuntime.UsePersistentSelections && UnlockEverythingSelections.ApplyAvatarFrameSelection(characterId, avatarFrameId);
        if (!handledLocally)
        {
            UnlockEverythingRuntime.LogSkillUiEvent("KinguinverseWebService.PutOnAvatarFrame:prefix", $"characterId={characterId}, avatarFrameId={avatarFrameId}, handledLocally=False");
            return true;
        }

        UnlockEverythingRuntime.LogSkillUiEvent("KinguinverseWebService.PutOnAvatarFrame:prefix", $"characterId={characterId}, avatarFrameId={avatarFrameId}, handledLocally=True");
        __result = UnlockEverythingStub.SuccessBoolean();
        return false;
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.PutOnCharacterDescription))]
internal static class KinguinverseWebServicePutOnCharacterDescriptionPatch
{
    private static bool Prefix(int characterId, DescriptionType descriptionType, ref Il2CppTasks.Task<Result<bool>> __result)
    {
        var handledLocally = UnlockEverythingRuntime.UsePersistentSelections && UnlockEverythingSelections.ApplyTitleSelection(characterId, descriptionType);
        if (!handledLocally)
        {
            UnlockEverythingRuntime.LogSkillUiEvent("KinguinverseWebService.PutOnCharacterDescription:prefix", $"characterId={characterId}, descriptionType={descriptionType}, handledLocally=False");
            return true;
        }

        UnlockEverythingRuntime.LogSkillUiEvent("KinguinverseWebService.PutOnCharacterDescription:prefix", $"characterId={characterId}, descriptionType={descriptionType}, handledLocally=True");
        __result = UnlockEverythingStub.SuccessBoolean();
        return false;
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.PutOnCharacterFart))]
internal static class KinguinverseWebServicePutOnCharacterFartPatch
{
    private static bool Prefix(int characterId, EmoteType emoteFart, ref Il2CppTasks.Task<Result<bool>> __result)
    {
        if (!UnlockEverythingRuntime.UsePersistentSelections || !UnlockEverythingSelections.ApplyFartSelection(characterId, emoteFart))
        {
            return true;
        }

        __result = UnlockEverythingStub.SuccessBoolean();
        return false;
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.PutOnCharacterDance))]
internal static class KinguinverseWebServicePutOnCharacterDancePatch
{
    private static bool Prefix(int characterId, EmoteType emoteFart, ref Il2CppTasks.Task<Result<bool>> __result)
    {
        if (!UnlockEverythingRuntime.UsePersistentSelections || !UnlockEverythingSelections.ApplyDanceSelection(characterId, emoteFart))
        {
            return true;
        }

        __result = UnlockEverythingStub.SuccessBoolean();
        return false;
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.PutOnEmotion))]
internal static class KinguinverseWebServicePutOnEmotionPatch
{
    private static bool Prefix(int characterId, EmoteType emoteType, int wheelSlotId, ref Il2CppTasks.Task<Result<bool>> __result)
    {
        if (!UnlockEverythingRuntime.UsePersistentSelections
            || !UnlockEverythingSelections.ApplyEmotionSelection(characterId, emoteType, wheelSlotId))
        {
            return true;
        }

        __result = UnlockEverythingStub.SuccessBoolean();
        return false;
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.PutOnSkinPart))]
internal static class KinguinverseWebServicePutOnSkinPartPatch
{
    private static bool Prefix(int characterId, int skinPartId, ref Il2CppTasks.Task<Result<bool>> __result)
    {
        var handledLocally = UnlockEverythingRuntime.UsePersistentSelections && UnlockEverythingSelections.ApplySkinPartSelection(characterId, skinPartId);
        UnlockEverythingRuntime.LogSkinWebCall("KinguinverseWebService.PutOnSkinPart:prefix", characterId, skinPartId, handledLocally);
        if (!handledLocally)
        {
            return true;
        }

        __result = UnlockEverythingStub.SuccessBoolean();
        return false;
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.BuySkinPartProduct))]
internal static class KinguinverseWebServiceBuySkinPartProductPatch
{
    private static bool Prefix(int id, bool shards, ref Il2CppTasks.Task<Result<bool>> __result)
    {
        if (!UnlockEverythingRuntime.UseProfileOverlay)
        {
            return true;
        }

        if (!UnlockEverythingStub.HasSkinPartProduct(id))
        {
            return true;
        }

        // Every exposed skin product is normalized to a single 1000 Gold price. The purchase is
        // deliberately local because synthetic catalog entries do not exist on the backend.
        var purchased = UnlockEverythingStub.TryPurchaseSkinPartProduct(id);
        __result = UnlockEverythingStub.SuccessBoolean(purchased);
        return false;
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.PutOnSkillCard))]
internal static class KinguinverseWebServicePutOnSkillCardPatch
{
    private static bool Prefix(int characterId, int skillCardSlot, int skillCardId, ref Il2CppTasks.Task<Result<bool>> __result)
    {
        var handledLocally = UnlockEverythingRuntime.UsePersistentSelections && UnlockEverythingSelections.ApplySkillCardSelection(characterId, skillCardSlot, skillCardId);
        UnlockEverythingRuntime.LogSkillUiEvent("KinguinverseWebService.PutOnSkillCard:prefix", $"characterId={characterId}, slot={skillCardSlot}, skillCardId={skillCardId}, handledLocally={handledLocally}");
        if (!handledLocally)
        {
            return true;
        }

        __result = UnlockEverythingStub.SuccessBoolean();
        return false;
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.PutOffSkillCard))]
internal static class KinguinverseWebServicePutOffSkillCardPatch
{
    private static bool Prefix(int characterId, int skillCardSlot, ref Il2CppTasks.Task<Result<bool>> __result)
    {
        var handledLocally = UnlockEverythingRuntime.UsePersistentSelections && UnlockEverythingSelections.RemoveSkillCardSelection(characterId, skillCardSlot);
        UnlockEverythingRuntime.LogSkillUiEvent("KinguinverseWebService.PutOffSkillCard:prefix", $"characterId={characterId}, slot={skillCardSlot}, handledLocally={handledLocally}");
        if (!handledLocally)
        {
            return true;
        }

        __result = UnlockEverythingStub.SuccessBoolean();
        return false;
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.PutOffSkinPart))]
internal static class KinguinverseWebServicePutOffSkinPartPatch
{
    private static bool Prefix(int characterId, SkinType skinType, ref Il2CppTasks.Task<Result<bool>> __result)
    {
        var handledLocally = UnlockEverythingRuntime.UsePersistentSelections && UnlockEverythingSelections.RemoveSkinPartSelection(characterId, skinType);
        UnlockEverythingRuntime.LogSkinRemove("KinguinverseWebService.PutOffSkinPart:prefix", characterId, skinType, handledLocally);
        if (!handledLocally)
        {
            return true;
        }

        __result = UnlockEverythingStub.SuccessBoolean();
        return false;
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.RefreshPlayer))]
internal static class KinguinverseWebServiceRefreshPlayerPatch
{
    private static bool Prefix(ref Il2CppTasks.Task<Result<RefreshLobbyPlayerResponse>> __result)
    {
        if (!UnlockEverythingRuntime.UseLocalStub)
        {
            return true;
        }

        try
        {
            __result = UnlockEverythingStub.RefreshPlayer();
            return false;
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend local stub RefreshPlayer failed", exception);
            return true;
        }
    }

    private static void Postfix(Il2CppTasks.Task<Result<RefreshLobbyPlayerResponse>> __result)
    {
        if (UnlockEverythingRuntime.UseLocalStub || __result is null)
        {
            return;
        }

        try
        {
            if (__result.IsCompletedSuccessfully)
            {
                ApplyRefreshPlayerOverlay(__result);
                return;
            }

            UnlockEverythingRuntime.ContinueOnMainThread(
                __result,
                _ => ApplyRefreshPlayerOverlay(__result),
                "Unlock Everything RefreshPlayer completion overlay failed");
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend research RefreshPlayer postfix failed", exception);
        }
    }

    private static void ApplyRefreshPlayerOverlay(Il2CppTasks.Task<Result<RefreshLobbyPlayerResponse>> task)
    {
        var result = task.Result;
        if (result is null || !result.IsSuccessful || result.Value is null)
        {
            return;
        }

        if (UnlockEverythingRuntime.UseProfileOverlay)
        {
            UnlockEverythingStub.ApplyRefreshPlayerOverlay(result.Value);
        }

        UnlockEverythingRuntime.LogRefreshPlayerResponse("KinguinverseWebService.RefreshPlayer", result.Value);
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.GetProducts))]
internal static class KinguinverseWebServiceGetProductsPatch
{
    private static bool Prefix(ref Il2CppTasks.Task<Result<Il2CppCollections.List<Kinguinverse.WebServiceProvider.Types.Products.ProductDto>>> __result)
    {
        if (!UnlockEverythingRuntime.UseLocalStub)
        {
            return true;
        }

        try
        {
            __result = UnlockEverythingStub.GetProducts();
            return false;
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend local stub GetProducts failed", exception);
            return true;
        }
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.GetProductsV2))]
internal static class KinguinverseWebServiceGetProductsV2Patch
{
    private static bool Prefix(ref Il2CppTasks.Task<Result<Products>> __result)
    {
        if (!UnlockEverythingRuntime.UseLocalStub)
        {
            return true;
        }

        try
        {
            __result = UnlockEverythingStub.GetProductsV2();
            return false;
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend local stub GetProductsV2 failed", exception);
            return true;
        }
    }

    private static void Postfix(Il2CppTasks.Task<Result<Products>> __result)
    {
        if (UnlockEverythingRuntime.UseLocalStub || __result is null)
        {
            return;
        }

        try
        {
            if (__result.IsCompletedSuccessfully)
            {
                ApplyAndLogProducts(__result.Result);
                return;
            }

            UnlockEverythingRuntime.ContinueOnMainThread(
                __result,
                ApplyAndLogProducts,
                "Backend research GetProductsV2 completion logging failed");
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend research GetProductsV2 postfix failed", exception);
        }
    }

    private static void ApplyAndLogProducts(Result<Products> result)
    {
        if (result is not null && result.IsSuccessful && result.Value is not null)
        {
            if (UnlockEverythingRuntime.UseProfileOverlay)
            {
                UnlockEverythingStub.ApplySkinProductsOverlay(result.Value);
            }

            UnlockEverythingRuntime.LogProductsResponse("KinguinverseWebService.GetProductsV2", result.Value);
        }
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.GetGameUserMetadata))]
internal static class KinguinverseWebServiceGetGameUserMetadataPatch
{
    private static bool Prefix(int userId, string key, ref Il2CppTasks.Task<Result<GetUserMetadataResponse>> __result)
    {
        if (!UnlockEverythingRuntime.UseLocalStub)
        {
            return true;
        }

        try
        {
            __result = UnlockEverythingStub.GetGameUserMetadata(userId, key);
            return false;
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend local stub GetGameUserMetadata failed", exception);
            return true;
        }
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.GetGameUserMetadatas))]
internal static class KinguinverseWebServiceGetGameUserMetadatasPatch
{
    private static bool Prefix(int userId, ref Il2CppTasks.Task<Result<Il2CppCollections.Dictionary<string, string>>> __result)
    {
        if (!UnlockEverythingRuntime.UseLocalStub)
        {
            return true;
        }

        try
        {
            __result = UnlockEverythingStub.GetGameUserMetadatas(userId);
            return false;
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend local stub GetGameUserMetadatas failed", exception);
            return true;
        }
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.SetGameUserMetadata))]
internal static class KinguinverseWebServiceSetGameUserMetadataPatch
{
    private static bool Prefix(int userId, string key, string value, ref Il2CppTasks.Task<Result> __result)
    {
        if (!UnlockEverythingRuntime.UseLocalStub)
        {
            return true;
        }

        try
        {
            __result = UnlockEverythingStub.SetGameUserMetadata(userId, key, value);
            return false;
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend local stub SetGameUserMetadata failed", exception);
            return true;
        }
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.SetGameUserMetadatas))]
internal static class KinguinverseWebServiceSetGameUserMetadatasPatch
{
    private static bool Prefix(int userId, SetUserMetadatasRequest request, ref Il2CppTasks.Task<Result> __result)
    {
        if (!UnlockEverythingRuntime.UseLocalStub)
        {
            return true;
        }

        try
        {
            __result = UnlockEverythingStub.SetGameUserMetadatas(userId, request);
            return false;
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend local stub SetGameUserMetadatas failed", exception);
            return true;
        }
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.GetPlayerMessages))]
internal static class KinguinverseWebServiceGetPlayerMessagesPatch
{
    private static bool Prefix(ref Il2CppTasks.Task<Result<Il2CppCollections.List<PlayerSystemMessage>>> __result)
    {
        if (!UnlockEverythingRuntime.UseLocalStub)
        {
            return true;
        }

        try
        {
            __result = UnlockEverythingStub.GetPlayerMessages();
            return false;
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend local stub GetPlayerMessages failed", exception);
            return true;
        }
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.GetPlayerResources))]
internal static class KinguinverseWebServiceGetPlayerResourcesPatch
{
    private static bool Prefix(ref Il2CppTasks.Task<Result<PlayerResources>> __result)
    {
        if (!UnlockEverythingRuntime.UseLocalStub)
        {
            return true;
        }

        try
        {
            __result = UnlockEverythingStub.GetPlayerResources();
            return false;
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend local stub GetPlayerResources failed", exception);
            return true;
        }
    }

    private static void Postfix(Il2CppTasks.Task<Result<PlayerResources>> __result)
    {
        if (UnlockEverythingRuntime.UseLocalStub || __result is null)
        {
            return;
        }

        try
        {
            if (__result.IsCompletedSuccessfully)
            {
                LogResources(__result.Result);
                return;
            }

            UnlockEverythingRuntime.ContinueOnMainThread(
                __result,
                LogResources,
                "Backend research GetPlayerResources completion logging failed");
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend research GetPlayerResources postfix failed", exception);
        }
    }

    private static void LogResources(Result<PlayerResources> result)
    {
        if (result is not null && result.IsSuccessful && result.Value is not null)
        {
            UnlockEverythingRuntime.LogResourcesResponse("KinguinverseWebService.GetPlayerResources", result.Value);
        }
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.GetPlayer), typeof(int))]
internal static class KinguinverseWebServiceGetPlayerByUserIdPatch
{
    private static void Postfix(int userId, Il2CppTasks.Task<Result<WebPlayersSimplified>> __result)
    {
        if (__result is null || !UnlockEverythingRuntime.UseProfileOverlay)
        {
            return;
        }

        try
        {
            if (__result.IsCompletedSuccessfully)
            {
                ApplyOverlay(userId, __result.Result);
                return;
            }

            UnlockEverythingRuntime.ContinueOnMainThread(
                __result,
                result => ApplyOverlay(userId, result),
                "Backend stabilizer GetPlayer(int) completion overlay failed");
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend stabilizer GetPlayer(int) postfix failed", exception);
        }
    }

    private static void ApplyOverlay(int userId, Result<WebPlayersSimplified> result)
    {
        if (result is not { IsSuccessful: true, Value: not null })
        {
            return;
        }

        UnlockEverythingStub.ApplyWebPlayerSimplifiedOverlay(result.Value);
        UnlockEverythingRuntime.LogSkillUiEvent(
            "KinguinverseWebService.GetPlayer:overlayApplied",
            $"userId={userId}, characters={result.Value.Characters?.Count ?? 0}");
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.GetMyBoosters))]
internal static class KinguinverseWebServiceGetMyBoostersPatch
{
    private static bool Prefix(ref Il2CppTasks.Task<Result<PlayerBoosters>> __result)
    {
        if (!UnlockEverythingRuntime.UseLocalStub)
        {
            return true;
        }

        try
        {
            __result = UnlockEverythingStub.GetMyBoosters();
            return false;
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend local stub GetMyBoosters failed", exception);
            return true;
        }
    }
}
