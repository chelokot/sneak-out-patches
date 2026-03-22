using BepInEx.Configuration;

namespace SneakOut.BackendRedirector;

internal sealed class BackendRedirectorConfig
{
    private BackendRedirectorConfig(
        ConfigEntry<bool> enableResearchLogging,
        ConfigEntry<bool> enableLocalStub,
        ConfigEntry<bool> enableRedirect,
        ConfigEntry<string> targetBaseUrl,
        ConfigEntry<string> targetEnvironmentName)
    {
        EnableResearchLogging = enableResearchLogging;
        EnableLocalStub = enableLocalStub;
        EnableRedirect = enableRedirect;
        TargetBaseUrl = targetBaseUrl;
        TargetEnvironmentName = targetEnvironmentName;
    }

    public ConfigEntry<bool> EnableResearchLogging { get; }

    public ConfigEntry<bool> EnableLocalStub { get; }

    public ConfigEntry<bool> EnableRedirect { get; }

    public ConfigEntry<string> TargetBaseUrl { get; }

    public ConfigEntry<string> TargetEnvironmentName { get; }

    public static BackendRedirectorConfig Bind(ConfigFile configFile)
    {
        var enableResearchLogging = configFile.Bind(
            "general",
            "EnableResearchLogging",
            false,
            "Enable backend construction and environment-change logs.");
        var enableLocalStub = configFile.Bind(
            "general",
            "EnableLocalStub",
            false,
            "Serve maxed community account responses directly from the runtime mod.");
        var enableRedirect = configFile.Bind(
            "general",
            "EnableRedirect",
            false,
            "Enable live backend redirection.");
        var targetBaseUrl = configFile.Bind(
            "general",
            "TargetBaseUrl",
            "http://127.0.0.1:8080",
            "Community-backend base URL.");
        var targetEnvironmentName = configFile.Bind(
            "general",
            "TargetEnvironmentName",
            "CommunityLocal",
            "Logical environment label used in redirect logs.");

        return new BackendRedirectorConfig(
            enableResearchLogging,
            enableLocalStub,
            enableRedirect,
            targetBaseUrl,
            targetEnvironmentName);
    }
}
