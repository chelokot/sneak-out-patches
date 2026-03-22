using Base;
using BepInEx.Logging;
using HarmonyLib;
using Kinguinverse.WebServiceProvider;
using System.Reflection;

namespace SneakOut.BackendRedirector;

internal static class BackendRedirectorRuntime
{
    private static ManualLogSource? _logger;
    private static Harmony? _harmony;
    private static BackendRedirectorConfig? _configuration;

    public static void Initialize(ManualLogSource logger, BackendRedirectorConfig configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _harmony ??= new Harmony(BackendRedirectorPlugin.PluginGuid);
        _harmony.PatchAll();
        BackendRedirectorStub.Initialize();
        LogInfo(
            $"Configured targetBaseUrl='{configuration.TargetBaseUrl.Value}', targetEnvironment='{configuration.TargetEnvironmentName.Value}', localStubEnabled={configuration.EnableLocalStub.Value}, redirectEnabled={configuration.EnableRedirect.Value}");
    }

    public static bool UseLocalStub => _configuration is not null && _configuration.EnableLocalStub.Value;

    public static void ApplyRedirect(string source, KinguinverseWebService webService)
    {
        var targetBaseUrl = GetTargetBaseUrl();
        if (targetBaseUrl is null)
        {
            LogObservedServiceUrl(source, webService.WebServiceUrl);
            return;
        }

        webService._WebServiceUrl_k__BackingField = targetBaseUrl;
        LogRedirectedServiceUrl(source, targetBaseUrl);
    }

    public static void ApplyRedirect(string source, KinguinverseWebServiceV2 webService)
    {
        var targetBaseUrl = GetTargetBaseUrl();
        if (targetBaseUrl is null)
        {
            LogObservedServiceUrl(source, webService.WebServiceUrl);
            return;
        }

        webService._WebServiceUrl_k__BackingField = targetBaseUrl;
        LogRedirectedServiceUrl(source, targetBaseUrl);
    }

    public static void ApplyRedirect(string source, UnityHttp unityHttp)
    {
        var targetBaseUrl = GetTargetBaseUrl();
        if (targetBaseUrl is null)
        {
            LogObservedServiceUrl(source, unityHttp._webServiceUrl);
            return;
        }

        unityHttp._webServiceUrl = targetBaseUrl;
        LogRedirectedServiceUrl(source, targetBaseUrl);
    }

    public static void LogWebServiceSettings(string source, KinguinverseWebServiceSettings settings)
    {
        if (!ShouldLog())
        {
            return;
        }

        LogInfo(
            $"{source}: settings.WebServiceEnvironment='{settings.WebServiceEnvironment}', targetBaseUrl='{_configuration!.TargetBaseUrl.Value}', redirectEnabled={_configuration.EnableRedirect.Value}");
    }

    public static void LogWebServiceV2(string source, KinguinverseWebServiceV2 webService, KinguinverseWebServiceSettings? settings)
    {
        if (!ShouldLog())
        {
            return;
        }

        var environmentName = settings is null ? "<null>" : settings.WebServiceEnvironment;
        LogInfo(
            $"{source}: webServiceUrl='{webService.WebServiceUrl}', settingsEnvironment='{environmentName}', redirectEnabled={_configuration!.EnableRedirect.Value}");
    }

    public static void LogWebService(string source, KinguinverseWebService webService)
    {
        if (!ShouldLog())
        {
            return;
        }

        LogInfo($"{source}: webServiceUrl='{webService.WebServiceUrl}', redirectEnabled={_configuration!.EnableRedirect.Value}");
    }

    public static void LogUnityHttp(string source, UnityHttp unityHttp)
    {
        if (!ShouldLog())
        {
            return;
        }

        LogInfo($"{source}: unityHttp._webServiceUrl='{unityHttp._webServiceUrl}', timeout={unityHttp._timeout}");
    }

    public static void LogEnvironmentChange(string source, EnvType envType)
    {
        if (!ShouldLog())
        {
            return;
        }

        LogInfo($"{source}: requested env='{envType}'");
    }

    public static void LogClientCacheState(string source, ClientCache clientCache)
    {
        if (!ShouldLog())
        {
            return;
        }

        var authorizationTokenLength = GetLength(clientCache.AuthorizationToken);
        var sessionTokenLength = GetLength(clientCache.UserSessionToken);
        var hasUserInfo = clientCache.UserInfo is not null;
        var hasUserWebPlayer = clientCache.UserWebPlayer is not null;
        LogInfo(
            $"{source}: authorizationTokenLength={authorizationTokenLength}, sessionTokenLength={sessionTokenLength}, hasUserInfo={hasUserInfo}, hasUserWebPlayer={hasUserWebPlayer}, banned={clientCache.Banned}, possibleFirewallBlocked={clientCache.PossibleFirewallBlocked}");
    }

    public static void LogLegacyRequestEntry(KinguinverseWebService webService, MethodBase originalMethod, object[] args)
    {
        var source = $"KinguinverseWebService.{originalMethod.Name}";
        ApplyRedirect(source, webService);
        if (!ShouldLog())
        {
            return;
        }

        var requestTarget = ResolveLegacyRequestTarget(originalMethod.Name, args);
        LogInfo($"{source}: webServiceUrl='{webService.WebServiceUrl}', requestTarget='{requestTarget}'");
    }

    public static void LogUnityHttpRequestEntry(UnityHttp unityHttp, MethodBase originalMethod, object[] args)
    {
        var source = $"UnityHttp.{originalMethod.Name}";
        ApplyRedirect(source, unityHttp);
        if (!ShouldLog())
        {
            return;
        }

        var requestTarget = ResolveUnityHttpRequestTarget(originalMethod.Name, args);
        LogInfo($"{source}: unityHttp._webServiceUrl='{unityHttp._webServiceUrl}', requestTarget='{requestTarget}', timeout={unityHttp._timeout}");
    }

    public static void LogError(string message, Exception exception)
    {
        _logger?.LogError($"{message}: {exception}");
    }

    private static string? GetTargetBaseUrl()
    {
        if (_configuration is null || !_configuration.EnableRedirect.Value)
        {
            return null;
        }

        var rawTargetBaseUrl = _configuration.TargetBaseUrl.Value.Trim();
        if (rawTargetBaseUrl.Length == 0)
        {
            return null;
        }

        return rawTargetBaseUrl.EndsWith("/") ? rawTargetBaseUrl : $"{rawTargetBaseUrl}/";
    }

    private static int GetLength(string? value)
    {
        return value is null ? 0 : value.Length;
    }

    private static string ResolveLegacyRequestTarget(string methodName, object[] args)
    {
        return methodName switch
        {
            "Get" or "Delete" => TryGetStringArgument(args, 0),
            "Post" or "Put" => TryGetStringArgument(args, 1),
            "SendCustomRequest" => TryDescribeCustomRequest(args),
            _ => "<high-level call>",
        };
    }

    private static string ResolveUnityHttpRequestTarget(string methodName, object[] args)
    {
        return methodName switch
        {
            "Get" or "Post" or "Put" => TryGetStringArgument(args, 0),
            _ => "<high-level call>",
        };
    }

    private static string TryGetStringArgument(object[] args, int index)
    {
        if (args.Length <= index || args[index] is not string value)
        {
            return "<missing>";
        }

        return value;
    }

    private static string TryDescribeCustomRequest(object[] args)
    {
        if (args.Length == 0 || args[0] is null)
        {
            return "<missing>";
        }

        return args[0].ToString() ?? args[0].GetType().FullName ?? "<custom-request>";
    }

    private static void LogObservedServiceUrl(string source, string currentUrl)
    {
        if (!ShouldLog())
        {
            return;
        }

        LogInfo($"{source}: observed serviceUrl='{currentUrl}'");
    }

    private static void LogRedirectedServiceUrl(string source, string targetBaseUrl)
    {
        LogInfo($"{source}: redirected serviceUrl='{targetBaseUrl}'");
    }

    private static bool ShouldLog()
    {
        return _configuration is not null && _configuration.EnableResearchLogging.Value;
    }

    private static void LogInfo(string message)
    {
        _logger?.LogInfo(message);
    }
}
