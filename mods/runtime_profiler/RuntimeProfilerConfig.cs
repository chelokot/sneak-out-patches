using BepInEx.Configuration;

namespace SneakOut.RuntimeProfiler;

internal sealed class RuntimeProfilerConfig
{
    private RuntimeProfilerConfig(
        ConfigEntry<bool> enableMod,
        ConfigEntry<bool> enableLogging,
        ConfigEntry<string> targetAssemblies,
        ConfigEntry<string> includeNamespacePrefixes,
        ConfigEntry<string> excludeNamespacePrefixes,
        ConfigEntry<bool> includePropertyAccessors,
        ConfigEntry<bool> includeConstructors,
        ConfigEntry<bool> includeCompilerGenerated,
        ConfigEntry<int> maxPatchedMethods,
        ConfigEntry<int> topMethodCount,
        ConfigEntry<int> topEdgeCount)
    {
        EnableMod = enableMod;
        EnableLogging = enableLogging;
        TargetAssemblies = targetAssemblies;
        IncludeNamespacePrefixes = includeNamespacePrefixes;
        ExcludeNamespacePrefixes = excludeNamespacePrefixes;
        IncludePropertyAccessors = includePropertyAccessors;
        IncludeConstructors = includeConstructors;
        IncludeCompilerGenerated = includeCompilerGenerated;
        MaxPatchedMethods = maxPatchedMethods;
        TopMethodCount = topMethodCount;
        TopEdgeCount = topEdgeCount;
    }

    public ConfigEntry<bool> EnableMod { get; }

    public ConfigEntry<bool> EnableLogging { get; }

    public ConfigEntry<string> TargetAssemblies { get; }

    public ConfigEntry<string> IncludeNamespacePrefixes { get; }

    public ConfigEntry<string> ExcludeNamespacePrefixes { get; }

    public ConfigEntry<bool> IncludePropertyAccessors { get; }

    public ConfigEntry<bool> IncludeConstructors { get; }

    public ConfigEntry<bool> IncludeCompilerGenerated { get; }

    public ConfigEntry<int> MaxPatchedMethods { get; }

    public ConfigEntry<int> TopMethodCount { get; }

    public ConfigEntry<int> TopEdgeCount { get; }

    public static RuntimeProfilerConfig Bind(ConfigFile configFile)
    {
        var enableMod = configFile.Bind(
            "general",
            "EnableMod",
            true,
            "Enable the runtime method profiler.");
        var enableLogging = configFile.Bind(
            "general",
            "EnableLogging",
            false,
            "Write profiler setup details to the BepInEx log.");
        var targetAssemblies = configFile.Bind(
            "targeting",
            "TargetAssemblies",
            "Assembly-CSharp;Kinguinverse",
            "Semicolon-separated assembly names to scan for methods.");
        var includeNamespacePrefixes = configFile.Bind(
            "targeting",
            "IncludeNamespacePrefixes",
            "Gameplay.Match.;Networking.PGOS.;UI.Views.",
            "Semicolon-separated full type-name prefixes to include.");
        var excludeNamespacePrefixes = configFile.Bind(
            "targeting",
            "ExcludeNamespacePrefixes",
            "Gameplay.Match.MatchState.;UI.Views.BattlepassView;UI.Views.DailyQuestsView",
            "Semicolon-separated full type-name prefixes to exclude.");
        var includePropertyAccessors = configFile.Bind(
            "targeting",
            "IncludePropertyAccessors",
            false,
            "Patch property/event accessor methods.");
        var includeConstructors = configFile.Bind(
            "targeting",
            "IncludeConstructors",
            false,
            "Patch constructors.");
        var includeCompilerGenerated = configFile.Bind(
            "targeting",
            "IncludeCompilerGenerated",
            false,
            "Patch compiler-generated methods and closure/state-machine types.");
        var maxPatchedMethods = configFile.Bind(
            "targeting",
            "MaxPatchedMethods",
            300,
            "Maximum number of methods to patch after filtering.");
        var topMethodCount = configFile.Bind(
            "report",
            "TopMethodCount",
            200,
            "Maximum number of methods to write into the report.");
        var topEdgeCount = configFile.Bind(
            "report",
            "TopEdgeCount",
            100,
            "Maximum number of caller->callee edges to write into the report.");

        return new RuntimeProfilerConfig(
            enableMod,
            enableLogging,
            targetAssemblies,
            includeNamespacePrefixes,
            excludeNamespacePrefixes,
            includePropertyAccessors,
            includeConstructors,
            includeCompilerGenerated,
            maxPatchedMethods,
            topMethodCount,
            topEdgeCount);
    }
}
