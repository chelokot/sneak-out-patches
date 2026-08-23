using BepInEx.Configuration;

namespace SneakOut.RuntimeProfiler;

internal sealed class RuntimeProfilerConfig
{
    private RuntimeProfilerConfig(
        ConfigEntry<bool> enableMod,
        ConfigEntry<bool> logInteractions,
        ConfigEntry<bool> logItemActions,
        ConfigEntry<bool> logSkillsAndPerks,
        ConfigEntry<bool> logBuffsAndStuns,
        ConfigEntry<bool> logButtonPresses,
        ConfigEntry<int> freezeThresholdMilliseconds,
        ConfigEntry<int> hitchThresholdMilliseconds,
        ConfigEntry<int> watchdogPollMilliseconds,
        ConfigEntry<bool> detectWhileUnfocused)
    {
        EnableMod = enableMod;
        LogInteractions = logInteractions;
        LogItemActions = logItemActions;
        LogSkillsAndPerks = logSkillsAndPerks;
        LogBuffsAndStuns = logBuffsAndStuns;
        LogButtonPresses = logButtonPresses;
        FreezeThresholdMilliseconds = freezeThresholdMilliseconds;
        HitchThresholdMilliseconds = hitchThresholdMilliseconds;
        WatchdogPollMilliseconds = watchdogPollMilliseconds;
        DetectWhileUnfocused = detectWhileUnfocused;
    }

    public ConfigEntry<bool> EnableMod { get; }
    public ConfigEntry<bool> LogInteractions { get; }
    public ConfigEntry<bool> LogItemActions { get; }
    public ConfigEntry<bool> LogSkillsAndPerks { get; }
    public ConfigEntry<bool> LogBuffsAndStuns { get; }
    public ConfigEntry<bool> LogButtonPresses { get; }
    public ConfigEntry<int> FreezeThresholdMilliseconds { get; }
    public ConfigEntry<int> HitchThresholdMilliseconds { get; }
    public ConfigEntry<int> WatchdogPollMilliseconds { get; }
    public ConfigEntry<bool> DetectWhileUnfocused { get; }

    public static RuntimeProfilerConfig Bind(ConfigFile configFile)
    {
        var legacyProfilerConfig = HasConfigKey(configFile, "targeting", "TargetAssemblies")
                                   || HasConfigKey(configFile, "report", "TopMethodCount");
        if (legacyProfilerConfig)
        {
            BindLegacyProfilerSettings(configFile);
        }

        var enableMod = configFile.Bind(
            "general", "EnableMod", true,
            "Enable chronological gameplay event and freeze logging.");
        var logInteractions = configFile.Bind(
            "events", "LogInteractions", true,
            "Log object interactions and door state changes.");
        var logItemActions = configFile.Bind(
            "events", "LogItemActions", true,
            "Log item pickup, use, drop, and throw actions.");
        var logSkillsAndPerks = configFile.Bind(
            "events", "LogSkillsAndPerks", true,
            "Log skill/perk button attempts, validation, and successful use.");
        var logBuffsAndStuns = configFile.Bind(
            "events", "LogBuffsAndStuns", true,
            "Log buff application/removal, including the exact stun type and source player.");
        var logButtonPresses = configFile.Bind(
            "events", "LogButtonPresses", true,
            "Log every Unity UI button press with its hierarchy and enabled/selected state.");
        var freezeThresholdMilliseconds = configFile.Bind(
            "freeze-detection", "FreezeThresholdMilliseconds", 1000,
            "Main-thread heartbeat delay that is considered a freeze. Minimum: 250 ms.");
        var hitchThresholdMilliseconds = configFile.Bind(
            "freeze-detection", "HitchThresholdMilliseconds", 250,
            "Recovered frame delay that is logged as a hitch. Minimum: 50 ms.");
        var watchdogPollMilliseconds = configFile.Bind(
            "freeze-detection", "WatchdogPollMilliseconds", 100,
            "How often the background watchdog checks the main-thread heartbeat. Range: 50-1000 ms.");
        var detectWhileUnfocused = configFile.Bind(
            "freeze-detection", "DetectWhileUnfocused", false,
            "Report freezes while the game is unfocused or paused. Disabled by default to avoid false positives.");

        if (legacyProfilerConfig)
        {
            enableMod.Value = true;
            RemoveLegacyProfilerSettings(configFile);
            configFile.Save();
        }

        return new RuntimeProfilerConfig(
            enableMod,
            logInteractions,
            logItemActions,
            logSkillsAndPerks,
            logBuffsAndStuns,
            logButtonPresses,
            freezeThresholdMilliseconds,
            hitchThresholdMilliseconds,
            watchdogPollMilliseconds,
            detectWhileUnfocused);
    }

    private static void BindLegacyProfilerSettings(ConfigFile configFile)
    {
        configFile.Bind("general", "EnableLogging", false, "Obsolete profiler setting.");
        configFile.Bind("targeting", "TargetAssemblies", string.Empty, "Obsolete profiler setting.");
        configFile.Bind("targeting", "IncludeNamespacePrefixes", string.Empty, "Obsolete profiler setting.");
        configFile.Bind("targeting", "TargetMethodPatterns", string.Empty, "Obsolete profiler setting.");
        configFile.Bind("targeting", "ExcludeNamespacePrefixes", string.Empty, "Obsolete profiler setting.");
        configFile.Bind("targeting", "IncludePropertyAccessors", false, "Obsolete profiler setting.");
        configFile.Bind("targeting", "IncludeConstructors", false, "Obsolete profiler setting.");
        configFile.Bind("targeting", "IncludeCompilerGenerated", false, "Obsolete profiler setting.");
        configFile.Bind("targeting", "MaxPatchedMethods", 0, "Obsolete profiler setting.");
        configFile.Bind("report", "TopMethodCount", 0, "Obsolete profiler setting.");
        configFile.Bind("report", "WarmupSeconds", 0, "Obsolete profiler setting.");
        configFile.Bind("report", "ReportAfterSeconds", 0, "Obsolete profiler setting.");
        configFile.Bind("report", "TopEdgeCount", 0, "Obsolete profiler setting.");
    }

    private static void RemoveLegacyProfilerSettings(ConfigFile configFile)
    {
        foreach (var definition in new[]
                 {
                     new ConfigDefinition("general", "EnableLogging"),
                     new ConfigDefinition("targeting", "TargetAssemblies"),
                     new ConfigDefinition("targeting", "IncludeNamespacePrefixes"),
                     new ConfigDefinition("targeting", "TargetMethodPatterns"),
                     new ConfigDefinition("targeting", "ExcludeNamespacePrefixes"),
                     new ConfigDefinition("targeting", "IncludePropertyAccessors"),
                     new ConfigDefinition("targeting", "IncludeConstructors"),
                     new ConfigDefinition("targeting", "IncludeCompilerGenerated"),
                     new ConfigDefinition("targeting", "MaxPatchedMethods"),
                     new ConfigDefinition("report", "TopMethodCount"),
                     new ConfigDefinition("report", "WarmupSeconds"),
                     new ConfigDefinition("report", "ReportAfterSeconds"),
                     new ConfigDefinition("report", "TopEdgeCount")
                 })
        {
            configFile.Remove(definition);
        }
    }

    private static bool HasConfigKey(ConfigFile configFile, string section, string key)
    {
        try
        {
            var configPath = configFile.GetType()
                .GetProperty("ConfigFilePath")?
                .GetValue(configFile) as string;
            if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
            {
                return false;
            }

            var currentSection = string.Empty;
            foreach (var rawLine in File.ReadLines(configPath))
            {
                var line = rawLine.Trim();
                if (line.StartsWith("[", StringComparison.Ordinal)
                    && line.EndsWith("]", StringComparison.Ordinal))
                {
                    currentSection = line[1..^1].Trim();
                    continue;
                }

                if (!string.Equals(currentSection, section, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var equalsIndex = line.IndexOf('=');
                if (equalsIndex > 0
                    && string.Equals(line[..equalsIndex].Trim(), key, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }
}
