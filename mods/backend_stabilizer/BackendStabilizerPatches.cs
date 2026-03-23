using Base;
using Collections;
using HarmonyLib;
using Kinguinverse.WebServiceProvider;
using Kinguinverse.WebServiceProvider.Requests;
using Kinguinverse.WebServiceProvider.Responses;
using Kinguinverse.WebServiceProvider.Responses.V2;
using Kinguinverse.WebServiceProvider.Types.Games;
using Kinguinverse.WebServiceProvider.Types_v2;
using Kinguinverse.WebServiceProvider.Types_v2.Products;
using UI.Views;
using Il2CppCollections = Il2CppSystem.Collections.Generic;
using Il2CppTasks = Il2CppSystem.Threading.Tasks;
using Tasks = System.Threading.Tasks;

namespace SneakOut.BackendStabilizer;

internal static class BackendStabilizerOverlay
{
    public static void EnsureClientCache(ClientCache clientCache)
    {
        if (BackendStabilizerRuntime.UseLocalStub)
        {
            BackendStabilizerStub.PopulateClientCache(clientCache);
            return;
        }

        if (BackendStabilizerRuntime.UseProfileOverlay)
        {
            BackendStabilizerStub.ApplyProfileOverlay(clientCache);
        }
    }
}

[HarmonyPatch(typeof(ClientCache), nameof(ClientCache.OnClientConfirmed))]
internal static class ClientCacheOnClientConfirmedPatch
{
    private static void Postfix(ClientCache __instance)
    {
        try
        {
            BackendStabilizerOverlay.EnsureClientCache(__instance);

            BackendStabilizerRuntime.LogClientCacheState("ClientCache.OnClientConfirmed", __instance);
        }
        catch (Exception exception)
        {
            BackendStabilizerRuntime.LogError("Backend stabilizer ClientCache.OnClientConfirmed postfix failed", exception);
        }
    }
}

[HarmonyPatch(typeof(PlayerNewMetaInventory), nameof(PlayerNewMetaInventory.GetSkillCard))]
internal static class PlayerNewMetaInventoryGetSkillCardPatch
{
    private static void Postfix(SkillType skillType, ref SkillCard __result)
    {
        if (!BackendStabilizerRuntime.UseProfileOverlay && !BackendStabilizerRuntime.UseLocalStub)
        {
            return;
        }

        if (skillType == SkillType.None || __result is not null)
        {
            return;
        }

        try
        {
            __result = BackendStabilizerStub.CreateMaxSkillCard(skillType);
        }
        catch (Exception exception)
        {
            BackendStabilizerRuntime.LogError("Backend stabilizer PlayerNewMetaInventory.GetSkillCard postfix failed", exception);
        }
    }
}

[HarmonyPatch(typeof(ClientCache), nameof(ClientCache.RefreshPlayer))]
internal static class ClientCacheRefreshPlayerPatch
{
    private static void Prefix(ClientCache __instance)
    {
        try
        {
            if (BackendStabilizerRuntime.UseLocalStub)
            {
                BackendStabilizerStub.PopulateClientCache(__instance);
            }

            BackendStabilizerRuntime.LogClientCacheState("ClientCache.RefreshPlayer", __instance);
        }
        catch (Exception exception)
        {
            BackendStabilizerRuntime.LogError("Backend stabilizer ClientCache.RefreshPlayer prefix failed", exception);
        }
    }

    private static void Postfix(ClientCache __instance, Il2CppTasks.Task __result)
    {
        if (!BackendStabilizerRuntime.UseProfileOverlay || BackendStabilizerRuntime.UseLocalStub)
        {
            return;
        }

        try
        {
            if (__result is null)
            {
                return;
            }

            if (__result.IsCompletedSuccessfully)
            {
                BackendStabilizerStub.ApplyProfileOverlay(__instance);
                BackendStabilizerRuntime.LogClientCacheState("ClientCache.RefreshPlayer:completed", __instance);
                return;
            }

            _ = Tasks.Task.Run(
                async () =>
                {
                    while (!__result.IsCompleted)
                    {
                        await Tasks.Task.Delay(50).ConfigureAwait(false);
                    }

                    if (!__result.IsCompletedSuccessfully)
                    {
                        return;
                    }

                    try
                    {
                        BackendStabilizerStub.ApplyProfileOverlay(__instance);
                        BackendStabilizerRuntime.LogClientCacheState("ClientCache.RefreshPlayer:completed", __instance);
                    }
                    catch (Exception exception)
                    {
                        BackendStabilizerRuntime.LogError("Backend stabilizer ClientCache.RefreshPlayer completion overlay failed", exception);
                    }
                });
        }
        catch (Exception exception)
        {
            BackendStabilizerRuntime.LogError("Backend stabilizer ClientCache.RefreshPlayer completion overlay failed", exception);
        }
    }
}

[HarmonyPatch(typeof(DailyQuestsView), "Refresh")]
internal static class DailyQuestsViewRefreshPatch
{
    private static bool Prefix(DailyQuestsView __instance)
    {
        if (!BackendStabilizerRuntime.UseLocalStub && !BackendStabilizerRuntime.UseProfileOverlay)
        {
            return true;
        }

        try
        {
            if (__instance._clientCache.PlayerDailyQuests is null)
            {
                BackendStabilizerOverlay.EnsureClientCache(__instance._clientCache);
            }

            BackendStabilizerRuntime.LogSuppressedLoop("DailyQuestsView.Refresh");
            return false;
        }
        catch (Exception exception)
        {
            BackendStabilizerRuntime.LogError("Backend stabilizer DailyQuestsView.Refresh prefix failed", exception);
            return false;
        }
    }
}

[HarmonyPatch(typeof(BattlepassView), "OnOnWebplayerRefreshEvent")]
internal static class BattlepassViewOnWebplayerRefreshPatch
{
    private static bool Prefix(BattlepassView __instance)
    {
        if (!BackendStabilizerRuntime.UseLocalStub && !BackendStabilizerRuntime.UseProfileOverlay)
        {
            return true;
        }

        try
        {
            if (__instance._clientCache.SeasonPass is null || __instance._clientCache.CurrentSeasonPassProgression is null)
            {
                BackendStabilizerOverlay.EnsureClientCache(__instance._clientCache);
            }

            BackendStabilizerRuntime.LogSuppressedLoop("BattlepassView.OnOnWebplayerRefreshEvent");
            return false;
        }
        catch (Exception exception)
        {
            BackendStabilizerRuntime.LogError("Backend stabilizer BattlepassView.OnOnWebplayerRefreshEvent prefix failed", exception);
            return false;
        }
    }
}

[HarmonyPatch(typeof(BattlepassView), "SetEndTime")]
internal static class BattlepassViewSetEndTimePatch
{
    private static bool Prefix(BattlepassView __instance)
    {
        if (!BackendStabilizerRuntime.UseLocalStub && !BackendStabilizerRuntime.UseProfileOverlay)
        {
            return true;
        }

        try
        {
            if (__instance._clientCache.SeasonPass is null)
            {
                BackendStabilizerOverlay.EnsureClientCache(__instance._clientCache);
            }

            BackendStabilizerRuntime.LogSuppressedLoop("BattlepassView.SetEndTime");
            return false;
        }
        catch (Exception exception)
        {
            BackendStabilizerRuntime.LogError("Backend stabilizer BattlepassView.SetEndTime prefix failed", exception);
            return false;
        }
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.RefreshPlayer))]
internal static class KinguinverseWebServiceRefreshPlayerPatch
{
    private static bool Prefix(ref Il2CppTasks.Task<Result<RefreshLobbyPlayerResponse>> __result)
    {
        if (!BackendStabilizerRuntime.UseLocalStub)
        {
            return true;
        }

        try
        {
            __result = BackendStabilizerStub.RefreshPlayer();
            return false;
        }
        catch (Exception exception)
        {
            BackendStabilizerRuntime.LogError("Backend local stub RefreshPlayer failed", exception);
            return true;
        }
    }

    private static void Postfix(Il2CppTasks.Task<Result<RefreshLobbyPlayerResponse>> __result)
    {
        if (BackendStabilizerRuntime.UseLocalStub || __result is null)
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

            _ = Tasks.Task.Run(
                async () =>
                {
                    while (!__result.IsCompleted)
                    {
                        await Tasks.Task.Delay(50).ConfigureAwait(false);
                    }

                    if (!__result.IsCompletedSuccessfully)
                    {
                        return;
                    }

                    try
                    {
                        ApplyRefreshPlayerOverlay(__result);
                    }
                    catch (Exception exception)
                    {
                        BackendStabilizerRuntime.LogError("Backend research RefreshPlayer completion overlay failed", exception);
                    }
                });
        }
        catch (Exception exception)
        {
            BackendStabilizerRuntime.LogError("Backend research RefreshPlayer postfix failed", exception);
        }
    }

    private static void ApplyRefreshPlayerOverlay(Il2CppTasks.Task<Result<RefreshLobbyPlayerResponse>> task)
    {
        var result = task.Result;
        if (result is null || !result.IsSuccessful || result.Value is null)
        {
            return;
        }

        if (BackendStabilizerRuntime.UseProfileOverlay)
        {
            BackendStabilizerStub.ApplyRefreshPlayerOverlay(result.Value);
        }

        BackendStabilizerRuntime.LogRefreshPlayerResponse("KinguinverseWebService.RefreshPlayer", result.Value);
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.GetProducts))]
internal static class KinguinverseWebServiceGetProductsPatch
{
    private static bool Prefix(ref Il2CppTasks.Task<Result<Il2CppCollections.List<Kinguinverse.WebServiceProvider.Types.Products.ProductDto>>> __result)
    {
        if (!BackendStabilizerRuntime.UseLocalStub)
        {
            return true;
        }

        try
        {
            __result = BackendStabilizerStub.GetProducts();
            return false;
        }
        catch (Exception exception)
        {
            BackendStabilizerRuntime.LogError("Backend local stub GetProducts failed", exception);
            return true;
        }
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.GetProductsV2))]
internal static class KinguinverseWebServiceGetProductsV2Patch
{
    private static bool Prefix(ref Il2CppTasks.Task<Result<Products>> __result)
    {
        if (!BackendStabilizerRuntime.UseLocalStub)
        {
            return true;
        }

        try
        {
            __result = BackendStabilizerStub.GetProductsV2();
            return false;
        }
        catch (Exception exception)
        {
            BackendStabilizerRuntime.LogError("Backend local stub GetProductsV2 failed", exception);
            return true;
        }
    }

    private static void Postfix(Il2CppTasks.Task<Result<Products>> __result)
    {
        if (BackendStabilizerRuntime.UseLocalStub || __result is null || !__result.IsCompletedSuccessfully)
        {
            return;
        }

        try
        {
            var result = __result.Result;
            if (result is not null && result.IsSuccessful && result.Value is not null)
            {
                BackendStabilizerRuntime.LogProductsResponse("KinguinverseWebService.GetProductsV2", result.Value);
            }
        }
        catch (Exception exception)
        {
            BackendStabilizerRuntime.LogError("Backend research GetProductsV2 postfix failed", exception);
        }
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.GetGameUserMetadata))]
internal static class KinguinverseWebServiceGetGameUserMetadataPatch
{
    private static bool Prefix(int userId, string key, ref Il2CppTasks.Task<Result<GetUserMetadataResponse>> __result)
    {
        if (!BackendStabilizerRuntime.UseLocalStub)
        {
            return true;
        }

        try
        {
            __result = BackendStabilizerStub.GetGameUserMetadata(userId, key);
            return false;
        }
        catch (Exception exception)
        {
            BackendStabilizerRuntime.LogError("Backend local stub GetGameUserMetadata failed", exception);
            return true;
        }
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.GetGameUserMetadatas))]
internal static class KinguinverseWebServiceGetGameUserMetadatasPatch
{
    private static bool Prefix(int userId, ref Il2CppTasks.Task<Result<Il2CppCollections.Dictionary<string, string>>> __result)
    {
        if (!BackendStabilizerRuntime.UseLocalStub)
        {
            return true;
        }

        try
        {
            __result = BackendStabilizerStub.GetGameUserMetadatas(userId);
            return false;
        }
        catch (Exception exception)
        {
            BackendStabilizerRuntime.LogError("Backend local stub GetGameUserMetadatas failed", exception);
            return true;
        }
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.SetGameUserMetadata))]
internal static class KinguinverseWebServiceSetGameUserMetadataPatch
{
    private static bool Prefix(int userId, string key, string value, ref Il2CppTasks.Task<Result> __result)
    {
        if (!BackendStabilizerRuntime.UseLocalStub)
        {
            return true;
        }

        try
        {
            __result = BackendStabilizerStub.SetGameUserMetadata(userId, key, value);
            return false;
        }
        catch (Exception exception)
        {
            BackendStabilizerRuntime.LogError("Backend local stub SetGameUserMetadata failed", exception);
            return true;
        }
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.SetGameUserMetadatas))]
internal static class KinguinverseWebServiceSetGameUserMetadatasPatch
{
    private static bool Prefix(int userId, SetUserMetadatasRequest request, ref Il2CppTasks.Task<Result> __result)
    {
        if (!BackendStabilizerRuntime.UseLocalStub)
        {
            return true;
        }

        try
        {
            __result = BackendStabilizerStub.SetGameUserMetadatas(userId, request);
            return false;
        }
        catch (Exception exception)
        {
            BackendStabilizerRuntime.LogError("Backend local stub SetGameUserMetadatas failed", exception);
            return true;
        }
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.GetPlayerMessages))]
internal static class KinguinverseWebServiceGetPlayerMessagesPatch
{
    private static bool Prefix(ref Il2CppTasks.Task<Result<Il2CppCollections.List<PlayerSystemMessage>>> __result)
    {
        if (!BackendStabilizerRuntime.UseLocalStub)
        {
            return true;
        }

        try
        {
            __result = BackendStabilizerStub.GetPlayerMessages();
            return false;
        }
        catch (Exception exception)
        {
            BackendStabilizerRuntime.LogError("Backend local stub GetPlayerMessages failed", exception);
            return true;
        }
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.GetPlayerResources))]
internal static class KinguinverseWebServiceGetPlayerResourcesPatch
{
    private static bool Prefix(ref Il2CppTasks.Task<Result<PlayerResources>> __result)
    {
        if (!BackendStabilizerRuntime.UseLocalStub)
        {
            return true;
        }

        try
        {
            __result = BackendStabilizerStub.GetPlayerResources();
            return false;
        }
        catch (Exception exception)
        {
            BackendStabilizerRuntime.LogError("Backend local stub GetPlayerResources failed", exception);
            return true;
        }
    }

    private static void Postfix(Il2CppTasks.Task<Result<PlayerResources>> __result)
    {
        if (BackendStabilizerRuntime.UseLocalStub || __result is null || !__result.IsCompletedSuccessfully)
        {
            return;
        }

        try
        {
            var result = __result.Result;
            if (result is not null && result.IsSuccessful && result.Value is not null)
            {
                BackendStabilizerRuntime.LogResourcesResponse("KinguinverseWebService.GetPlayerResources", result.Value);
            }
        }
        catch (Exception exception)
        {
            BackendStabilizerRuntime.LogError("Backend research GetPlayerResources postfix failed", exception);
        }
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.GetMyBoosters))]
internal static class KinguinverseWebServiceGetMyBoostersPatch
{
    private static bool Prefix(ref Il2CppTasks.Task<Result<PlayerBoosters>> __result)
    {
        if (!BackendStabilizerRuntime.UseLocalStub)
        {
            return true;
        }

        try
        {
            __result = BackendStabilizerStub.GetMyBoosters();
            return false;
        }
        catch (Exception exception)
        {
            BackendStabilizerRuntime.LogError("Backend local stub GetMyBoosters failed", exception);
            return true;
        }
    }
}
