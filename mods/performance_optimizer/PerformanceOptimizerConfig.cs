using BepInEx.Configuration;

namespace SneakOut.PerformanceOptimizer;

internal enum PerformancePreset
{
    ObserveOnly,
    Auto,
    Balanced,
    LowSpec
}

internal enum VSyncMode
{
    Preserve = -1,
    Off = 0,
    EveryVBlank = 1,
    EverySecondVBlank = 2
}

internal sealed class PerformanceOptimizerConfig
{
    private PerformanceOptimizerConfig(
        ConfigEntry<bool> enableMod,
        ConfigEntry<PerformancePreset> preset,
        ConfigEntry<bool> adaptivePerformance,
        ConfigEntry<int> adaptiveMinimumFps,
        ConfigEntry<bool> enableTelemetry,
        ConfigEntry<bool> enableSceneCensus,
        ConfigEntry<bool> enableExperimentalUnityRecorders,
        ConfigEntry<int> reportIntervalSeconds,
        ConfigEntry<int> targetFrameRate,
        ConfigEntry<VSyncMode> vSync,
        ConfigEntry<int> maxQueuedFrames,
        ConfigEntry<int> qualityLevel,
        ConfigEntry<bool> disableAdditionalLights,
        ConfigEntry<bool> disableAdditionalLightShadows,
        ConfigEntry<bool> disableSoftShadows,
        ConfigEntry<int> additionalShadowAtlasSize,
        ConfigEntry<int> shadowDistance,
        ConfigEntry<int> renderScalePercent,
        ConfigEntry<bool> enableMipStreaming,
        ConfigEntry<int> mipStreamingBudgetMb)
    {
        EnableMod = enableMod;
        Preset = preset;
        AdaptivePerformance = adaptivePerformance;
        AdaptiveMinimumFps = adaptiveMinimumFps;
        EnableTelemetry = enableTelemetry;
        EnableSceneCensus = enableSceneCensus;
        EnableExperimentalUnityRecorders = enableExperimentalUnityRecorders;
        ReportIntervalSeconds = reportIntervalSeconds;
        TargetFrameRate = targetFrameRate;
        VSync = vSync;
        MaxQueuedFrames = maxQueuedFrames;
        QualityLevel = qualityLevel;
        DisableAdditionalLights = disableAdditionalLights;
        DisableAdditionalLightShadows = disableAdditionalLightShadows;
        DisableSoftShadows = disableSoftShadows;
        AdditionalShadowAtlasSize = additionalShadowAtlasSize;
        ShadowDistance = shadowDistance;
        RenderScalePercent = renderScalePercent;
        EnableMipStreaming = enableMipStreaming;
        MipStreamingBudgetMb = mipStreamingBudgetMb;
    }

    public ConfigEntry<bool> EnableMod { get; }
    public ConfigEntry<PerformancePreset> Preset { get; }
    public ConfigEntry<bool> AdaptivePerformance { get; }
    public ConfigEntry<int> AdaptiveMinimumFps { get; }
    public ConfigEntry<bool> EnableTelemetry { get; }
    public ConfigEntry<bool> EnableSceneCensus { get; }
    public ConfigEntry<bool> EnableExperimentalUnityRecorders { get; }
    public ConfigEntry<int> ReportIntervalSeconds { get; }
    public ConfigEntry<int> TargetFrameRate { get; }
    public ConfigEntry<VSyncMode> VSync { get; }
    public ConfigEntry<int> MaxQueuedFrames { get; }
    public ConfigEntry<int> QualityLevel { get; }
    public ConfigEntry<bool> DisableAdditionalLights { get; }
    public ConfigEntry<bool> DisableAdditionalLightShadows { get; }
    public ConfigEntry<bool> DisableSoftShadows { get; }
    public ConfigEntry<int> AdditionalShadowAtlasSize { get; }
    public ConfigEntry<int> ShadowDistance { get; }
    public ConfigEntry<int> RenderScalePercent { get; }
    public ConfigEntry<bool> EnableMipStreaming { get; }
    public ConfigEntry<int> MipStreamingBudgetMb { get; }

    public static PerformanceOptimizerConfig Bind(ConfigFile configFile)
    {
        var enableMod = configFile.Bind(
            "general",
            "EnableMod",
            true,
            "Enable the performance optimizer and its low-overhead telemetry.");
        var preset = configFile.Bind(
            "general",
            "Preset",
            PerformancePreset.Auto,
            "ObserveOnly changes no Unity quality settings. Auto selects Balanced or LowSpec from detected hardware.");
        var adaptivePerformance = configFile.Bind(
            "general",
            "AdaptivePerformance",
            true,
            "In Auto mode, disable the measured URP shadow/vSync bottlenecks only after sustained low frame rate.");
        var adaptiveMinimumFps = configFile.Bind(
            "general",
            "AdaptiveMinimumFps",
            45,
            "Auto mode tuning threshold after the active lobby or match scene has settled.");
        var enableTelemetry = configFile.Bind(
            "telemetry",
            "EnableTelemetry",
            true,
            "Collect frame pacing, memory, render, loading, GC, and Fusion RTT statistics.");
        var reportIntervalSeconds = configFile.Bind(
            "telemetry",
            "ReportIntervalSeconds",
            10,
            "Append one aggregate telemetry row after this many seconds. No per-frame disk writes are performed.");
        var enableSceneCensus = configFile.Bind(
            "telemetry",
            "EnableSceneCensus",
            false,
            "Run one heavyweight renderer/light/collider census per scene for diagnostics. Keep disabled during normal play.");
        var enableExperimentalUnityRecorders = configFile.Bind(
            "telemetry",
            "EnableExperimentalUnityRecorders",
            false,
            "Use Unity ProfilerRecorder markers. Unsafe on the current retail IL2CPP build under Wine; keep disabled.");
        var targetFrameRate = configFile.Bind(
            "frame-pacing",
            "TargetFrameRate",
            0,
            "Optional frame cap. Zero preserves the game's current setting.");
        var vSync = configFile.Bind(
            "frame-pacing",
            "VSync",
            VSyncMode.Preserve,
            "VSync override. Preserve keeps the game setting; Off can avoid half-refresh stalls under Wine.");
        var maxQueuedFrames = configFile.Bind(
            "frame-pacing",
            "MaxQueuedFrames",
            -1,
            "Maximum render frames queued by the graphics driver. -1 preserves the game setting; 1-4 overrides it.");
        var qualityLevel = configFile.Bind(
            "graphics",
            "QualityLevel",
            -1,
            "Unity quality level override. -1 preserves the game setting; 0 is the lowest available level.");
        var disableAdditionalLights = configFile.Bind(
            "graphics",
            "DisableAdditionalLights",
            false,
            "Disable URP additional lights. This is a strong visual tradeoff intended only for very weak PCs.");
        var disableAdditionalLightShadows = configFile.Bind(
            "graphics",
            "DisableAdditionalLightShadows",
            false,
            "Disable expensive real-time shadows cast by additional lights while keeping the selected render pipeline.");
        var disableSoftShadows = configFile.Bind(
            "graphics",
            "DisableSoftShadows",
            false,
            "Disable soft-shadow filtering while preserving other shadow settings.");
        var additionalShadowAtlasSize = configFile.Bind(
            "graphics",
            "AdditionalShadowAtlasSize",
            0,
            "Additional-light shadow atlas size: 256, 512, 1024, 2048, or 4096. Zero preserves the pipeline asset.");
        var shadowDistance = configFile.Bind(
            "graphics",
            "ShadowDistance",
            0,
            "Maximum URP real-time shadow distance in world units. Zero preserves the pipeline asset.");
        var renderScalePercent = configFile.Bind(
            "graphics",
            "RenderScalePercent",
            0,
            "Internal URP render scale from 50 to 100 percent. Zero preserves the selected pipeline asset.");
        var enableMipStreaming = configFile.Bind(
            "memory",
            "EnableMipStreaming",
            false,
            "Allow the LowSpec preset to enable texture mip streaming. Keep disabled unless assets are authored for streaming.");
        var mipStreamingBudgetMb = configFile.Bind(
            "memory",
            "MipStreamingBudgetMb",
            768,
            "Texture streaming budget used by the LowSpec preset.");

        return new PerformanceOptimizerConfig(
            enableMod,
            preset,
            adaptivePerformance,
            adaptiveMinimumFps,
            enableTelemetry,
            enableSceneCensus,
            enableExperimentalUnityRecorders,
            reportIntervalSeconds,
            targetFrameRate,
            vSync,
            maxQueuedFrames,
            qualityLevel,
            disableAdditionalLights,
            disableAdditionalLightShadows,
            disableSoftShadows,
            additionalShadowAtlasSize,
            shadowDistance,
            renderScalePercent,
            enableMipStreaming,
            mipStreamingBudgetMb);
    }
}
