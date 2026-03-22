using Base;
using HarmonyLib;
using Kinguinverse.WebServiceProvider;
using Kinguinverse.WebServiceProvider.Requests;
using Kinguinverse.WebServiceProvider.Responses;
using Kinguinverse.WebServiceProvider.Responses.V2;
using Kinguinverse.WebServiceProvider.Types.Games;
using Kinguinverse.WebServiceProvider.Types_v2;
using Kinguinverse.WebServiceProvider.Types_v2.Products;
using UI.Views;
using Il2CppGuid = Il2CppSystem.Guid;
using Il2CppCollections = Il2CppSystem.Collections.Generic;
using Il2CppTasks = Il2CppSystem.Threading.Tasks;

namespace SneakOut.BackendRedirector;

[HarmonyPatch(typeof(KinguinverseWebServiceV2), MethodType.Constructor, typeof(KinguinverseWebServiceSettings))]
internal static class KinguinverseWebServiceV2CtorSettingsPatch
{
    private static void Postfix(KinguinverseWebServiceV2 __instance, KinguinverseWebServiceSettings settings)
    {
        try
        {
            BackendRedirectorRuntime.ApplyRedirect("KinguinverseWebServiceV2..ctor(settings)", __instance);
            BackendRedirectorRuntime.LogWebServiceSettings("KinguinverseWebServiceV2..ctor(settings)", settings);
            BackendRedirectorRuntime.LogWebServiceV2("KinguinverseWebServiceV2..ctor(settings)", __instance, settings);
        }
        catch (Exception exception)
        {
            BackendRedirectorRuntime.LogError("Backend redirector settings constructor postfix failed", exception);
        }
    }
}

[HarmonyPatch(typeof(KinguinverseWebServiceV2), MethodType.Constructor, typeof(KinguinverseWebServiceSettings), typeof(string))]
internal static class KinguinverseWebServiceV2CtorSettingsAuthorizationPatch
{
    private static void Postfix(KinguinverseWebServiceV2 __instance, KinguinverseWebServiceSettings settings, string authorizationToken)
    {
        try
        {
            BackendRedirectorRuntime.ApplyRedirect("KinguinverseWebServiceV2..ctor(settings, authorizationToken)", __instance);
            BackendRedirectorRuntime.LogWebServiceSettings("KinguinverseWebServiceV2..ctor(settings, authorizationToken)", settings);
            BackendRedirectorRuntime.LogWebServiceV2("KinguinverseWebServiceV2..ctor(settings, authorizationToken)", __instance, settings);
        }
        catch (Exception exception)
        {
            BackendRedirectorRuntime.LogError("Backend redirector authorization constructor postfix failed", exception);
        }
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), MethodType.Constructor, typeof(KinguinverseWebServiceSettings))]
internal static class KinguinverseWebServiceSettingsConstructorPatch
{
    private static void Postfix(KinguinverseWebService __instance)
    {
        try
        {
            BackendRedirectorRuntime.ApplyRedirect("KinguinverseWebService..ctor(settings)", __instance);
            BackendRedirectorRuntime.LogWebService("KinguinverseWebService..ctor(settings)", __instance);
        }
        catch (Exception exception)
        {
            BackendRedirectorRuntime.LogError("Backend redirector legacy settings constructor postfix failed", exception);
        }
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), MethodType.Constructor, typeof(KinguinverseWebServiceSettings), typeof(string))]
internal static class KinguinverseWebServiceSettingsAuthorizationConstructorPatch
{
    private static void Postfix(KinguinverseWebService __instance)
    {
        try
        {
            BackendRedirectorRuntime.ApplyRedirect("KinguinverseWebService..ctor(settings, authorizationToken)", __instance);
            BackendRedirectorRuntime.LogWebService("KinguinverseWebService..ctor(settings, authorizationToken)", __instance);
        }
        catch (Exception exception)
        {
            BackendRedirectorRuntime.LogError("Backend redirector legacy settings/authorization constructor postfix failed", exception);
        }
    }
}

[HarmonyPatch(typeof(UnityHttp), MethodType.Constructor)]
internal static class UnityHttpConstructorPatch
{
    private static void Postfix(UnityHttp __instance)
    {
        try
        {
            BackendRedirectorRuntime.ApplyRedirect("UnityHttp..ctor", __instance);
            BackendRedirectorRuntime.LogUnityHttp("UnityHttp..ctor", __instance);
        }
        catch (Exception exception)
        {
            BackendRedirectorRuntime.LogError("Backend redirector UnityHttp constructor postfix failed", exception);
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
            if (BackendRedirectorRuntime.UseLocalStub)
            {
                BackendRedirectorStub.PopulateClientCache(__instance);
            }

            BackendRedirectorRuntime.LogClientCacheState("ClientCache.OnClientConfirmed", __instance);
        }
        catch (Exception exception)
        {
            BackendRedirectorRuntime.LogError("Backend redirector ClientCache.OnClientConfirmed postfix failed", exception);
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
            if (BackendRedirectorRuntime.UseLocalStub)
            {
                BackendRedirectorStub.PopulateClientCache(__instance);
            }

            BackendRedirectorRuntime.LogClientCacheState("ClientCache.RefreshPlayer", __instance);
        }
        catch (Exception exception)
        {
            BackendRedirectorRuntime.LogError("Backend redirector ClientCache.RefreshPlayer prefix failed", exception);
        }
    }
}

[HarmonyPatch(typeof(DailyQuestsView), "Refresh")]
internal static class DailyQuestsViewRefreshPatch
{
    private static void Prefix(DailyQuestsView __instance)
    {
        if (!BackendRedirectorRuntime.UseLocalStub)
        {
            return;
        }

        try
        {
            BackendRedirectorStub.PopulateClientCache(__instance._clientCache);
        }
        catch (Exception exception)
        {
            BackendRedirectorRuntime.LogError("Backend redirector DailyQuestsView.Refresh prefix failed", exception);
        }
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.SteamLogInV2))]
internal static class KinguinverseWebServiceSteamLogInV2Patch
{
    private static bool Prefix(SteamLogInRequest request, ref Il2CppTasks.Task<Result<LogInResponseV2>> __result)
    {
        return true;
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.GetUserSession))]
internal static class KinguinverseWebServiceGetUserSessionPatch
{
    private static bool Prefix(Il2CppGuid sessionToken, ref Il2CppTasks.Task<Result<GetUserSessionResponse>> __result)
    {
        return true;
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.GetUserSessionV2))]
internal static class KinguinverseWebServiceGetUserSessionV2Patch
{
    private static bool Prefix(Il2CppGuid sessionToken, ref Il2CppTasks.Task<Result<GetUserSessionV2Response>> __result)
    {
        return true;
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.RefreshPlayer))]
internal static class KinguinverseWebServiceRefreshPlayerPatch
{
    private static bool Prefix(ref Il2CppTasks.Task<Result<RefreshLobbyPlayerResponse>> __result)
    {
        if (!BackendRedirectorRuntime.UseLocalStub)
        {
            return true;
        }

        try
        {
            __result = BackendRedirectorStub.RefreshPlayer();
            return false;
        }
        catch (Exception exception)
        {
            BackendRedirectorRuntime.LogError("Backend local stub RefreshPlayer failed", exception);
            return true;
        }
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.GetPlayer), typeof(string))]
internal static class KinguinverseWebServiceGetPlayerBySessionPatch
{
    private static bool Prefix(string sessionToken, ref Il2CppTasks.Task<Result<LogInResponseV2>> __result)
    {
        return true;
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.GetPlayer), typeof(int))]
internal static class KinguinverseWebServiceGetPlayerByUserIdPatch
{
    private static bool Prefix(int userId, ref Il2CppTasks.Task<Result<WebPlayersSimplified>> __result)
    {
        return true;
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.GetProducts))]
internal static class KinguinverseWebServiceGetProductsPatch
{
    private static bool Prefix(ref Il2CppTasks.Task<Result<Il2CppCollections.List<Kinguinverse.WebServiceProvider.Types.Products.ProductDto>>> __result)
    {
        if (!BackendRedirectorRuntime.UseLocalStub)
        {
            return true;
        }

        try
        {
            __result = BackendRedirectorStub.GetProducts();
            return false;
        }
        catch (Exception exception)
        {
            BackendRedirectorRuntime.LogError("Backend local stub GetProducts failed", exception);
            return true;
        }
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.GetProductsV2))]
internal static class KinguinverseWebServiceGetProductsV2Patch
{
    private static bool Prefix(ref Il2CppTasks.Task<Result<Products>> __result)
    {
        if (!BackendRedirectorRuntime.UseLocalStub)
        {
            return true;
        }

        try
        {
            __result = BackendRedirectorStub.GetProductsV2();
            return false;
        }
        catch (Exception exception)
        {
            BackendRedirectorRuntime.LogError("Backend local stub GetProductsV2 failed", exception);
            return true;
        }
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.GetGameUserMetadata))]
internal static class KinguinverseWebServiceGetGameUserMetadataPatch
{
    private static bool Prefix(int userId, string key, ref Il2CppTasks.Task<Result<GetUserMetadataResponse>> __result)
    {
        if (!BackendRedirectorRuntime.UseLocalStub)
        {
            return true;
        }

        try
        {
            __result = BackendRedirectorStub.GetGameUserMetadata(userId, key);
            return false;
        }
        catch (Exception exception)
        {
            BackendRedirectorRuntime.LogError("Backend local stub GetGameUserMetadata failed", exception);
            return true;
        }
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.GetGameUserMetadatas))]
internal static class KinguinverseWebServiceGetGameUserMetadatasPatch
{
    private static bool Prefix(int userId, ref Il2CppTasks.Task<Result<Il2CppCollections.Dictionary<string, string>>> __result)
    {
        if (!BackendRedirectorRuntime.UseLocalStub)
        {
            return true;
        }

        try
        {
            __result = BackendRedirectorStub.GetGameUserMetadatas(userId);
            return false;
        }
        catch (Exception exception)
        {
            BackendRedirectorRuntime.LogError("Backend local stub GetGameUserMetadatas failed", exception);
            return true;
        }
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.SetGameUserMetadata))]
internal static class KinguinverseWebServiceSetGameUserMetadataPatch
{
    private static bool Prefix(int userId, string key, string value, ref Il2CppTasks.Task<Result> __result)
    {
        if (!BackendRedirectorRuntime.UseLocalStub)
        {
            return true;
        }

        try
        {
            __result = BackendRedirectorStub.SetGameUserMetadata(userId, key, value);
            return false;
        }
        catch (Exception exception)
        {
            BackendRedirectorRuntime.LogError("Backend local stub SetGameUserMetadata failed", exception);
            return true;
        }
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.SetGameUserMetadatas))]
internal static class KinguinverseWebServiceSetGameUserMetadatasPatch
{
    private static bool Prefix(int userId, SetUserMetadatasRequest request, ref Il2CppTasks.Task<Result> __result)
    {
        if (!BackendRedirectorRuntime.UseLocalStub)
        {
            return true;
        }

        try
        {
            __result = BackendRedirectorStub.SetGameUserMetadatas(userId, request);
            return false;
        }
        catch (Exception exception)
        {
            BackendRedirectorRuntime.LogError("Backend local stub SetGameUserMetadatas failed", exception);
            return true;
        }
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.GetPlayerMessages))]
internal static class KinguinverseWebServiceGetPlayerMessagesPatch
{
    private static bool Prefix(ref Il2CppTasks.Task<Result<Il2CppCollections.List<PlayerSystemMessage>>> __result)
    {
        if (!BackendRedirectorRuntime.UseLocalStub)
        {
            return true;
        }

        try
        {
            __result = BackendRedirectorStub.GetPlayerMessages();
            return false;
        }
        catch (Exception exception)
        {
            BackendRedirectorRuntime.LogError("Backend local stub GetPlayerMessages failed", exception);
            return true;
        }
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.GetPlayerResources))]
internal static class KinguinverseWebServiceGetPlayerResourcesPatch
{
    private static bool Prefix(ref Il2CppTasks.Task<Result<PlayerResources>> __result)
    {
        if (!BackendRedirectorRuntime.UseLocalStub)
        {
            return true;
        }

        try
        {
            __result = BackendRedirectorStub.GetPlayerResources();
            return false;
        }
        catch (Exception exception)
        {
            BackendRedirectorRuntime.LogError("Backend local stub GetPlayerResources failed", exception);
            return true;
        }
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.GetMyBoosters))]
internal static class KinguinverseWebServiceGetMyBoostersPatch
{
    private static bool Prefix(ref Il2CppTasks.Task<Result<PlayerBoosters>> __result)
    {
        if (!BackendRedirectorRuntime.UseLocalStub)
        {
            return true;
        }

        try
        {
            __result = BackendRedirectorStub.GetMyBoosters();
            return false;
        }
        catch (Exception exception)
        {
            BackendRedirectorRuntime.LogError("Backend local stub GetMyBoosters failed", exception);
            return true;
        }
    }
}
