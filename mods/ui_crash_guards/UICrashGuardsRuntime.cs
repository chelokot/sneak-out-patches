using BepInEx.Logging;
using HarmonyLib;

namespace SneakOut.UICrashGuards;

internal static class UICrashGuardsRuntime
{
    private static ManualLogSource? _logger;
    private static Harmony? _harmony;
    private static UICrashGuardsConfig? _configuration;
    private static readonly HashSet<string> LoggedSuppressedSources = new();

    public static void Initialize(ManualLogSource logger, UICrashGuardsConfig configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _harmony ??= new Harmony(UICrashGuardsPlugin.PluginGuid);
        _harmony.PatchAll();
    }

    public static bool Enabled => _configuration is not null && _configuration.EnableMod.Value;

    public static void LogLoopSuppressed(string source)
    {
        if (_configuration is null || !_configuration.EnableLogging.Value)
        {
            return;
        }

        if (!LoggedSuppressedSources.Add(source))
        {
            return;
        }

        _logger?.LogInfo($"Suppressed {source}");
    }
}
