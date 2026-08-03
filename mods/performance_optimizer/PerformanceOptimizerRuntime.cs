using System.Diagnostics;
using System.Globalization;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using Fusion;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Networking.Photon;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;

namespace SneakOut.PerformanceOptimizer;

internal static class PerformanceOptimizerRuntime
{
    private static readonly Stopwatch SessionClock = Stopwatch.StartNew();
    private static readonly FrameTimeAccumulator FrameTimes = new();
    private static readonly FrameTimingAccumulator EngineFrameTimings = new();
    private static readonly List<RecorderMetric> RecorderMetrics = new();
    private static readonly object ReportGate = new();

    private static ManualLogSource? _logger;
    private static PerformanceOptimizerConfig? _configuration;
    private static Harmony? _harmony;
    private static string? _reportPath;
    private static NetworkRunner? _networkRunner;
    private static double _nextNetworkSampleSeconds;
    private static double _lastRttMilliseconds;
    private static int _fusionTickRate;
    private static PhotonPingServer? _photonPingServer;
    private static int _photonRegionPingMilliseconds;
    private static double _nextReportSeconds;
    private static double _nextFramePacingCheckSeconds;
    private static int _lastGeneration0Collections;
    private static int _lastGeneration1Collections;
    private static int _lastGeneration2Collections;
    private static bool _initialized;
    private static bool _telemetryInitializationAttempted;
    private static bool _telemetryInitialized;
    private static bool _reportHeaderWritten;
    private static int _lastObservedFrame = -1;
    private static bool _frameWatcherAttached;
    private static TimeSpan _lastProcessCpuTime;
    private static double _lastProcessSampleSeconds;
    private static bool _networkSampleFailureLogged;
    private static bool _adaptiveShadowFallbackApplied;
    private static bool _adaptiveVSyncDisabled;
    private static string _activeSceneName = string.Empty;
    private static double _activeSceneStartedSeconds;
    private static int _removedNullRoomLights;
    private static bool _resolutionSelectorRecoveryReported;
    private static bool _missingEndMatchRecordReported;
    private static Il2CppStructArray<FrameTiming>? _frameTimingBuffer;
    private static bool _sceneCensusComplete;
    private static int _zeroEngineTimingSamples;

    public static void Initialize(ManualLogSource logger, PerformanceOptimizerConfig configuration)
    {
        _logger = logger;
        _configuration = configuration;
        if (!configuration.EnableMod.Value || _initialized)
        {
            return;
        }

        _initialized = true;
        if (configuration.QualityLevel.Value >= 0)
        {
            QualitySettings.SetQualityLevel(
                Math.Clamp(configuration.QualityLevel.Value, 0, Math.Max(0, QualitySettings.count - 1)),
                true);
        }
        ApplyPreset(ResolvePreset(configuration.Preset.Value));
        ApplyPipelineOverrides();
        if (configuration.VSync.Value != VSyncMode.Preserve)
        {
            QualitySettings.vSyncCount = (int)configuration.VSync.Value;
        }

        if (configuration.MaxQueuedFrames.Value >= 1)
        {
            QualitySettings.maxQueuedFrames = Math.Clamp(configuration.MaxQueuedFrames.Value, 1, 4);
        }

        if (configuration.TargetFrameRate.Value > 0)
        {
            Application.targetFrameRate = Math.Clamp(configuration.TargetFrameRate.Value, 30, 360);
        }

        _logger?.LogInfo(
            $"Frame pacing: target={Application.targetFrameRate}, vSync={QualitySettings.vSyncCount}, "
            + $"maxQueuedFrames={QualitySettings.maxQueuedFrames}");

        Application.add_quitting(new Action(WriteFinalReport));
        _harmony = new Harmony(PerformanceOptimizerPlugin.PluginGuid);
        _harmony.PatchAll();
    }

    public static void AttachFrameWatcher()
    {
        if (_frameWatcherAttached)
        {
            return;
        }

        ClassInjector.RegisterTypeInIl2Cpp<PerformanceFrameWatcher>();
        var watcherObject = new GameObject("PerformanceOptimizerFrameWatcher");
        UnityEngine.Object.DontDestroyOnLoad(watcherObject);
        watcherObject.hideFlags = HideFlags.HideAndDontSave;
        watcherObject.AddComponent<PerformanceFrameWatcher>();
        _frameWatcherAttached = true;
    }

    public static void CapturePhotonPingServer(PhotonPingServer photonPingServer)
    {
        _photonPingServer = photonPingServer;
    }

    public static void ReportSanitizedRoomLights(int removedCount)
    {
        if (removedCount <= 0)
        {
            return;
        }

        _removedNullRoomLights += removedCount;
        _logger?.LogInfo(
            $"Removed {removedCount} null serialized light reference(s) before Room.OnAwake "
            + $"({_removedNullRoomLights} total); room light caching can continue normally");
    }

    public static void ReportResolutionSelectorRecovery(
        int currentWidth,
        int currentHeight,
        int fallbackWidth,
        int fallbackHeight)
    {
        if (_resolutionSelectorRecoveryReported)
        {
            return;
        }

        _resolutionSelectorRecoveryReported = true;
        _logger?.LogWarning(
            $"Video settings did not contain the active {currentWidth}x{currentHeight} mode; "
            + $"showing the nearest {fallbackWidth}x{fallbackHeight} option without changing the display mode");
    }

    public static void ReportMissingEndMatchRecord(int localInternalId, int recordCount)
    {
        if (_missingEndMatchRecordReported)
        {
            return;
        }

        _missingEndMatchRecordReported = true;
        _logger?.LogWarning(
            $"Skipped optional battlepass end-screen progress because local player {localInternalId} "
            + $"was absent from {recordCount} server result record(s)");
    }

    private static PerformancePreset ResolvePreset(PerformancePreset configuredPreset)
    {
        if (configuredPreset != PerformancePreset.Auto)
        {
            return configuredPreset;
        }

        return SystemInfo.graphicsMemorySize > 0 && SystemInfo.graphicsMemorySize <= 3072
            || SystemInfo.systemMemorySize > 0 && SystemInfo.systemMemorySize <= 8192
            || SystemInfo.processorCount <= 4
                ? PerformancePreset.LowSpec
                : PerformancePreset.Balanced;
    }

    private static void ApplyPreset(PerformancePreset preset)
    {
        if (_configuration is null || preset == PerformancePreset.ObserveOnly)
        {
            _logger?.LogInfo("Performance preset is ObserveOnly; Unity settings are unchanged");
            return;
        }

        Application.backgroundLoadingPriority = UnityEngine.ThreadPriority.BelowNormal;
        QualitySettings.asyncUploadPersistentBuffer = true;
        QualitySettings.streamingMipmapsMaxFileIORequests = Math.Clamp(
            QualitySettings.streamingMipmapsMaxFileIORequests,
            2,
            8);

        if (preset == PerformancePreset.LowSpec)
        {
            if (_configuration.QualityLevel.Value < 0 && QualitySettings.GetQualityLevel() != 0)
            {
                QualitySettings.SetQualityLevel(0, true);
            }

            if (_configuration.EnableMipStreaming.Value)
            {
                QualitySettings.streamingMipmapsActive = true;
                QualitySettings.streamingMipmapsMemoryBudget = Math.Clamp(
                    _configuration.MipStreamingBudgetMb.Value,
                    256,
                    2048);
                QualitySettings.streamingMipmapsRenderersPerFrame = Math.Clamp(
                    QualitySettings.streamingMipmapsRenderersPerFrame,
                    32,
                    128);
                QualitySettings.streamingMipmapsMaxLevelReduction = Math.Max(
                    QualitySettings.streamingMipmapsMaxLevelReduction,
                    2);
            }

            QualitySettings.shadowDistance = Math.Min(QualitySettings.shadowDistance, 45f);
            QualitySettings.shadowCascades = Math.Min(QualitySettings.shadowCascades, 2);
            QualitySettings.lodBias = Math.Min(QualitySettings.lodBias, 1.25f);
            QualitySettings.realtimeReflectionProbes = false;
            QualitySettings.softParticles = false;
            QualitySettings.particleRaycastBudget = Math.Min(QualitySettings.particleRaycastBudget, 128);
        }

        _logger?.LogInfo(
            $"Applied {preset} performance preset: gpu={SystemInfo.graphicsDeviceName}, "
            + $"vramMb={SystemInfo.graphicsMemorySize}, ramMb={SystemInfo.systemMemorySize}, "
            + $"cpu={SystemInfo.processorType}, cores={SystemInfo.processorCount}");
    }

    private static void InitializeTelemetry()
    {
        var reportDirectory = Path.Combine(Paths.BepInExRootPath, "performance-reports");
        Directory.CreateDirectory(reportDirectory);
        _reportPath = Path.Combine(
            reportDirectory,
            $"performance-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv");
        _nextReportSeconds = Math.Max(2, _configuration!.ReportIntervalSeconds.Value);
        _lastGeneration0Collections = GC.CollectionCount(0);
        _lastGeneration1Collections = GC.CollectionCount(1);
        _lastGeneration2Collections = GC.CollectionCount(2);
        try
        {
            _lastProcessCpuTime = Process.GetCurrentProcess().TotalProcessorTime;
        }
        catch
        {
            _lastProcessCpuTime = TimeSpan.Zero;
        }

        if (_configuration.EnableExperimentalUnityRecorders.Value)
        {
            AddRecorder("main_thread_ns", ProfilerCategory.Internal, "Main Thread");
            AddRecorder("render_thread_ns", ProfilerCategory.Render, "Render Thread");
            AddRecorder("present_wait_ns", ProfilerCategory.Render, "Gfx.WaitForPresentOnGfxThread");
            AddRecorder("player_loop_ns", ProfilerCategory.Internal, "PlayerLoop");
            AddRecorder("behaviour_update_ns", ProfilerCategory.Scripts, "BehaviourUpdate");
            AddRecorder("gc_alloc_bytes", ProfilerCategory.Memory, "GC.Alloc");
            AddRecorder("gc_collect_ns", ProfilerCategory.Memory, "GC.Collect");
            AddRecorder("system_used_memory_bytes", ProfilerCategory.Memory, "System Used Memory");
            AddRecorder("total_used_memory_bytes", ProfilerCategory.Memory, "Total Used Memory");
            AddRecorder("total_reserved_memory_bytes", ProfilerCategory.Memory, "Total Reserved Memory");
            AddRecorder("gc_used_memory_bytes", ProfilerCategory.Memory, "GC Used Memory");
            AddRecorder("texture_memory_bytes", ProfilerCategory.Memory, "Texture Memory");
            AddRecorder("mesh_memory_bytes", ProfilerCategory.Memory, "Mesh Memory");
            AddRecorder("audio_memory_bytes", ProfilerCategory.Memory, "Audio Used Memory");
            AddRecorder("draw_calls", ProfilerCategory.Render, "Draw Calls Count");
            AddRecorder("batches", ProfilerCategory.Render, "Batches Count");
            AddRecorder("setpass_calls", ProfilerCategory.Render, "SetPass Calls Count");
            AddRecorder("triangles", ProfilerCategory.Render, "Triangles Count");
            AddRecorder("vertices", ProfilerCategory.Render, "Vertices Count");
            AddRecorder("physics_ns", ProfilerCategory.Physics, "Physics.Processing");
            AddRecorder("file_read_bytes", ProfilerCategory.FileIO, "File.Read");
        }

        try
        {
            if (FrameTimingManager.IsFeatureEnabled())
            {
                _frameTimingBuffer = new Il2CppStructArray<FrameTiming>(1);
                _logger?.LogInfo("Native CPU/GPU frame timing telemetry is available");
            }
        }
        catch (Exception exception)
        {
            _logger?.LogDebug($"Native frame timing telemetry unavailable: {exception.Message}");
        }

        _logger?.LogInfo(
            $"Performance telemetry initialized with {RecorderMetrics.Count(metric => metric.Valid)} Unity recorders; "
            + $"report={_reportPath}");
        _telemetryInitialized = true;
    }

    private static void AddRecorder(string columnName, ProfilerCategory category, string markerName)
    {
        try
        {
            var recorder = ProfilerRecorder.StartNew(
                category,
                markerName,
                1,
                ProfilerRecorderOptions.StartImmediately | ProfilerRecorderOptions.SumAllSamplesInFrame);
            RecorderMetrics.Add(new RecorderMetric(columnName, markerName, recorder));
        }
        catch (Exception exception)
        {
            _logger?.LogDebug($"Profiler recorder unavailable for {markerName}: {exception.Message}");
        }
    }

    public static void ObserveFrame()
    {
        if (_configuration is null || !_configuration.EnableTelemetry.Value)
        {
            return;
        }

        if (!_telemetryInitializationAttempted)
        {
            _telemetryInitializationAttempted = true;
            try
            {
                InitializeTelemetry();
            }
            catch (Exception exception)
            {
                _logger?.LogError($"Performance telemetry initialization failed: {exception}");
            }
        }

        if (!_telemetryInitialized)
        {
            return;
        }

        var frame = Time.frameCount;
        if (frame == _lastObservedFrame)
        {
            return;
        }
        _lastObservedFrame = frame;

        var elapsedSeconds = SessionClock.Elapsed.TotalSeconds;
        var activeSceneName = SceneManager.GetActiveScene().name;
        if (!string.Equals(activeSceneName, _activeSceneName, StringComparison.Ordinal))
        {
            FrameTimes.SnapshotAndReset();
            EngineFrameTimings.SnapshotAndReset();
            _activeSceneName = activeSceneName;
            _activeSceneStartedSeconds = elapsedSeconds;
            _sceneCensusComplete = false;
        }

        FrameTimes.Record(Time.unscaledDeltaTime * 1000d);
        CaptureEngineFrameTiming();
        if (_configuration.EnableSceneCensus.Value
            && !_sceneCensusComplete
            && elapsedSeconds - _activeSceneStartedSeconds >= 12d)
        {
            CaptureSceneCensus();
            _sceneCensusComplete = true;
        }
        if (elapsedSeconds >= _nextFramePacingCheckSeconds)
        {
            MaintainFramePacingOverrides();
            _nextFramePacingCheckSeconds = elapsedSeconds + 2d;
        }

        if (elapsedSeconds >= _nextNetworkSampleSeconds)
        {
            SampleNetwork();
            _nextNetworkSampleSeconds = elapsedSeconds + 1d;
        }

        if (elapsedSeconds >= _nextReportSeconds)
        {
            WriteIntervalReport();
            _nextReportSeconds = elapsedSeconds + Math.Max(2, _configuration.ReportIntervalSeconds.Value);
        }
    }

    private static void CaptureEngineFrameTiming()
    {
        if (_frameTimingBuffer is null)
        {
            return;
        }

        try
        {
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, _frameTimingBuffer) > 0)
            {
                var timing = _frameTimingBuffer[0];
                if (timing.cpuFrameTime > 0d || timing.gpuFrameTime > 0d)
                {
                    EngineFrameTimings.Record(timing);
                    _zeroEngineTimingSamples = 0;
                }
                else if (++_zeroEngineTimingSamples >= 120)
                {
                    _frameTimingBuffer = null;
                    _logger?.LogInfo("Native frame timings return no data on this graphics backend; sampling disabled");
                }
            }
        }
        catch (Exception exception)
        {
            _frameTimingBuffer = null;
            _logger?.LogDebug($"Native frame timing telemetry stopped: {exception.Message}");
        }
    }

    private static void CaptureSceneCensus()
    {
        try
        {
            var renderers = UnityEngine.Object.FindObjectsOfType<Renderer>();
            var materials = new Dictionary<IntPtr, Material>();
            var materialSlots = 0;
            var shadowCasters = 0;
            var staticBatched = 0;
            var visibleRenderers = 0;
            var meshRenderers = 0;
            var skinnedRenderers = 0;
            var particleRenderers = 0;
            var skinnedUpdatingOffscreen = 0;
            var rendererLayers = new Dictionary<int, int>();
            foreach (var renderer in renderers)
            {
                if (renderer is null || renderer.Pointer == IntPtr.Zero || !renderer.enabled)
                {
                    continue;
                }

                if (renderer.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off)
                {
                    shadowCasters++;
                }

                if (renderer.isVisible)
                {
                    visibleRenderers++;
                }

                if (renderer.isPartOfStaticBatch)
                {
                    staticBatched++;
                }

                if (renderer.TryCast<MeshRenderer>() is not null)
                {
                    meshRenderers++;
                }
                else if (renderer.TryCast<SkinnedMeshRenderer>() is { } skinnedRenderer)
                {
                    skinnedRenderers++;
                    if (skinnedRenderer.updateWhenOffscreen)
                    {
                        skinnedUpdatingOffscreen++;
                    }
                }
                else if (renderer.TryCast<ParticleSystemRenderer>() is not null)
                {
                    particleRenderers++;
                }

                var layer = renderer.gameObject.layer;
                rendererLayers[layer] = rendererLayers.TryGetValue(layer, out var layerCount)
                    ? layerCount + 1
                    : 1;

                foreach (var material in renderer.sharedMaterials)
                {
                    if (material is null || material.Pointer == IntPtr.Zero)
                    {
                        continue;
                    }

                    materialSlots++;
                    materials.TryAdd(material.Pointer, material);
                }
            }

            var instancingEnabled = 0;
            foreach (var material in materials.Values)
            {
                if (material.enableInstancing)
                {
                    instancingEnabled++;
                }
            }

            var lights = UnityEngine.Object.FindObjectsOfType<Light>();
            var colliders = UnityEngine.Object.FindObjectsOfType<Collider>();
            var animators = UnityEngine.Object.FindObjectsOfType<Animator>();
            var alwaysAnimating = 0;
            foreach (var animator in animators)
            {
                if (animator.cullingMode == AnimatorCullingMode.AlwaysAnimate)
                {
                    alwaysAnimating++;
                }
            }
            var particles = UnityEngine.Object.FindObjectsOfType<ParticleSystem>();
            var lodGroups = UnityEngine.Object.FindObjectsOfType<LODGroup>();
            var cameras = UnityEngine.Object.FindObjectsOfType<Camera>();
            var layerSummary = string.Join(
                ';',
                rendererLayers.OrderByDescending(entry => entry.Value)
                    .Take(8)
                    .Select(entry => $"{LayerMask.LayerToName(entry.Key)}({entry.Key})={entry.Value}"));
            _logger?.LogInfo(
                $"Scene census {_activeSceneName}: renderers={renderers.Length}, materialSlots={materialSlots}, "
                + $"uniqueMaterials={materials.Count}, instancedMaterials={instancingEnabled}, "
                + $"visible={visibleRenderers}, staticBatched={staticBatched}, mesh={meshRenderers}, "
                + $"skinned={skinnedRenderers}, skinnedUpdatingOffscreen={skinnedUpdatingOffscreen}, "
                + $"particleRenderers={particleRenderers}, shadowCasters={shadowCasters}, lights={lights.Length}, "
                + $"colliders={colliders.Length}, animators={animators.Length}, alwaysAnimating={alwaysAnimating}, "
                + $"particles={particles.Length}, "
                + $"lodGroups={lodGroups.Length}, cameras={cameras.Length}, layers=[{layerSummary}]");
            foreach (var camera in cameras)
            {
                _logger?.LogInfo(
                    $"Camera census {camera.name}: enabled={camera.enabled}, depth={camera.depth:F1}, "
                    + $"occlusion={camera.useOcclusionCulling}, cullingMask=0x{camera.cullingMask:X8}");
            }

            var sceneCameraManager = UnityEngine.Object.FindObjectOfType<Gameplay.Camera.SceneCameraManager>();
            var currentRoom = sceneCameraManager?.CurrentRoom;
            if (currentRoom is not null)
            {
                _logger?.LogInfo(
                    $"Current camera room: name={currentRoom.name}, type={currentRoom.RoomType}, "
                    + $"layer={LayerMask.LayerToName(currentRoom.gameObject.layer)}({currentRoom.gameObject.layer})");
            }
        }
        catch (Exception exception)
        {
            _logger?.LogDebug($"Scene census unavailable: {exception.Message}");
        }
    }

    private static void MaintainFramePacingOverrides()
    {
        if (_configuration is null)
        {
            return;
        }

        if (_configuration.VSync.Value != VSyncMode.Preserve || _adaptiveVSyncDisabled)
        {
            var expectedVSync = _adaptiveVSyncDisabled ? 0 : (int)_configuration.VSync.Value;
            if (QualitySettings.vSyncCount != expectedVSync)
            {
                QualitySettings.vSyncCount = expectedVSync;
            }
        }

        if (_configuration.MaxQueuedFrames.Value >= 1)
        {
            var expectedQueuedFrames = Math.Clamp(_configuration.MaxQueuedFrames.Value, 1, 4);
            if (QualitySettings.maxQueuedFrames != expectedQueuedFrames)
            {
                QualitySettings.maxQueuedFrames = expectedQueuedFrames;
            }
        }

        if (_configuration.TargetFrameRate.Value > 0)
        {
            var expectedTargetFrameRate = Math.Clamp(_configuration.TargetFrameRate.Value, 30, 360);
            if (Application.targetFrameRate != expectedTargetFrameRate)
            {
                Application.targetFrameRate = expectedTargetFrameRate;
            }
        }

        if (_configuration.QualityLevel.Value >= 0)
        {
            var expectedQualityLevel = Math.Clamp(
                _configuration.QualityLevel.Value,
                0,
                Math.Max(0, QualitySettings.count - 1));
            if (QualitySettings.GetQualityLevel() != expectedQualityLevel)
            {
                QualitySettings.SetQualityLevel(expectedQualityLevel, true);
                ApplyPreset(ResolvePreset(_configuration.Preset.Value));
            }
        }

        ApplyPipelineOverrides();
    }

    private static void ApplyPipelineOverrides()
    {
        if (_configuration is null)
        {
            return;
        }

        try
        {
            var universalPipeline = QualitySettings.renderPipeline?.TryCast<UniversalRenderPipelineAsset>();
            if (universalPipeline is null)
            {
                return;
            }

            if ((_configuration.DisableAdditionalLightShadows.Value || _adaptiveShadowFallbackApplied)
                && universalPipeline.supportsAdditionalLightShadows)
            {
                universalPipeline.supportsAdditionalLightShadows = false;
            }

            if (_configuration.DisableAdditionalLights.Value
                && universalPipeline.additionalLightsRenderingMode != LightRenderingMode.Disabled)
            {
                universalPipeline.additionalLightsRenderingMode = LightRenderingMode.Disabled;
            }

            if (_configuration.DisableSoftShadows.Value && universalPipeline.supportsSoftShadows)
            {
                universalPipeline.supportsSoftShadows = false;
            }

            if (_configuration.AdditionalShadowAtlasSize.Value > 0)
            {
                var requestedSize = _configuration.AdditionalShadowAtlasSize.Value;
                var atlasSize = requestedSize <= 256 ? 256
                    : requestedSize <= 512 ? 512
                    : requestedSize <= 1024 ? 1024
                    : requestedSize <= 2048 ? 2048
                    : 4096;
                if (universalPipeline.additionalLightsShadowmapResolution != atlasSize)
                {
                    universalPipeline.additionalLightsShadowmapResolution = atlasSize;
                }
            }

            if (_configuration.ShadowDistance.Value > 0)
            {
                var shadowDistance = Math.Clamp(_configuration.ShadowDistance.Value, 5, 100);
                if (Math.Abs(universalPipeline.shadowDistance - shadowDistance) > 0.01f)
                {
                    universalPipeline.shadowDistance = shadowDistance;
                }
            }

            if (_configuration.RenderScalePercent.Value > 0)
            {
                var renderScale = Math.Clamp(_configuration.RenderScalePercent.Value, 50, 100) / 100f;
                if (Math.Abs(universalPipeline.renderScale - renderScale) > 0.001f)
                {
                    universalPipeline.renderScale = renderScale;
                }
            }
        }
        catch (Exception exception)
        {
            _logger?.LogDebug($"URP override unavailable: {exception.Message}");
        }
    }

    private static void SampleNetwork()
    {
        SamplePhotonRegionPing();
        try
        {
            if (_networkRunner is null
                || _networkRunner.Pointer == IntPtr.Zero
                || !_networkRunner.IsRunning
                || !_networkRunner.IsConnectedToServer)
            {
                _networkRunner = null;
                foreach (var runner in UnityEngine.Object.FindObjectsOfType<NetworkRunner>())
                {
                    if (runner is not null
                        && runner.Pointer != IntPtr.Zero
                        && runner.IsRunning
                        && runner.IsConnectedToServer)
                    {
                        _networkRunner = runner;
                        break;
                    }
                }
            }

            if (_networkRunner is null)
            {
                _lastRttMilliseconds = 0d;
                _fusionTickRate = 0;
                return;
            }

            var simulation = _networkRunner.Simulation;
            _lastRttMilliseconds = Math.Max(
                0d,
                (simulation is not null
                    ? simulation.GetPlayerRtt(_networkRunner.LocalPlayer)
                    : _networkRunner.GetPlayerRtt(_networkRunner.LocalPlayer)) * 1000d);
            _fusionTickRate = simulation?.TickRate ?? 0;
            _networkSampleFailureLogged = false;
        }
        catch (Exception exception)
        {
            _networkRunner = null;
            _lastRttMilliseconds = 0d;
            _fusionTickRate = 0;
            if (!_networkSampleFailureLogged)
            {
                _logger?.LogWarning($"Fusion telemetry unavailable: {exception.Message}");
                _networkSampleFailureLogged = true;
            }
        }
    }

    private static void SamplePhotonRegionPing()
    {
        try
        {
            if (_photonPingServer is null
                || _photonPingServer.Pointer == IntPtr.Zero
                || !_photonPingServer.HasMeasurements)
            {
                _photonRegionPingMilliseconds = 0;
                return;
            }

            var bestRegion = _photonPingServer.BestRegion;
            _photonRegionPingMilliseconds = string.IsNullOrWhiteSpace(bestRegion)
                ? 0
                : Math.Max(0, _photonPingServer.GetPingToPhotonRegion(bestRegion));
        }
        catch
        {
            _photonPingServer = null;
            _photonRegionPingMilliseconds = 0;
        }
    }

    private static void WriteIntervalReport()
    {
        lock (ReportGate)
        {
            if (_reportPath is null)
            {
                return;
            }

            try
            {
                if (!_reportHeaderWritten)
                {
                    File.AppendAllText(_reportPath, BuildHeader(), Encoding.UTF8);
                    _reportHeaderWritten = true;
                }

                var row = BuildRow(out var frameSnapshot);
                File.AppendAllText(_reportPath, row, Encoding.UTF8);
                EvaluateAdaptiveTuning(frameSnapshot);
            }
            catch (Exception exception)
            {
                _logger?.LogError($"Failed to append performance report: {exception.Message}");
            }
        }
    }

    private static string BuildHeader()
    {
        var columns = new List<string>
        {
            "elapsed_s", "scene", "frames", "avg_fps", "p50_ms", "p95_ms", "p99_ms", "max_ms",
            "engine_cpu_ms", "engine_main_ms", "engine_present_wait_ms", "engine_render_thread_ms", "engine_gpu_ms",
            "frames_over_33ms", "frames_over_100ms", "gc0", "gc1", "gc2", "managed_mb", "working_set_mb",
            "private_mb", "process_cpu_one_core_100", "process_threads", "target_fps", "vsync_count", "quality_level",
            "screen_width", "screen_height", "fullscreen", "run_in_background", "photon_region_ping_ms",
            "fusion_rtt_ms", "fusion_tick_rate", "pipeline", "render_scale", "hdr", "depth_texture",
            "opaque_texture", "main_shadows", "additional_lights", "additional_shadows", "soft_shadows",
            "pipeline_shadow_distance", "pipeline_shadow_cascades", "additional_shadow_atlas", "srp_batcher", "dynamic_batching",
            "render_graph", "mip_streaming", "mip_budget_mb", "queued_frames"
        };
        columns.AddRange(RecorderMetrics.Select(metric => metric.ColumnName));
        return string.Join(',', columns) + Environment.NewLine;
    }

    private static string BuildRow(out FrameTimeSnapshot frameSnapshot)
    {
        frameSnapshot = FrameTimes.SnapshotAndReset();
        var engineFrameSnapshot = EngineFrameTimings.SnapshotAndReset();
        var generation0 = GC.CollectionCount(0);
        var generation1 = GC.CollectionCount(1);
        var generation2 = GC.CollectionCount(2);
        var processSnapshot = GetProcessSnapshot();
        var pipelineSnapshot = GetPipelineSnapshot();
        var columns = new List<string>
        {
            Format(SessionClock.Elapsed.TotalSeconds),
            EscapeCsv(SceneManager.GetActiveScene().name),
            frameSnapshot.Frames.ToString(CultureInfo.InvariantCulture),
            Format(frameSnapshot.AverageFps),
            Format(frameSnapshot.P50Milliseconds),
            Format(frameSnapshot.P95Milliseconds),
            Format(frameSnapshot.P99Milliseconds),
            Format(frameSnapshot.MaxMilliseconds),
            Format(engineFrameSnapshot.CpuMilliseconds),
            Format(engineFrameSnapshot.MainThreadMilliseconds),
            Format(engineFrameSnapshot.PresentWaitMilliseconds),
            Format(engineFrameSnapshot.RenderThreadMilliseconds),
            Format(engineFrameSnapshot.GpuMilliseconds),
            frameSnapshot.OverBudgetFrames.ToString(CultureInfo.InvariantCulture),
            frameSnapshot.SevereStutters.ToString(CultureInfo.InvariantCulture),
            (generation0 - _lastGeneration0Collections).ToString(CultureInfo.InvariantCulture),
            (generation1 - _lastGeneration1Collections).ToString(CultureInfo.InvariantCulture),
            (generation2 - _lastGeneration2Collections).ToString(CultureInfo.InvariantCulture),
            Format(GC.GetTotalMemory(false) / 1048576d),
            Format(processSnapshot.WorkingSetMegabytes),
            Format(processSnapshot.PrivateMegabytes),
            Format(processSnapshot.CpuPercent),
            processSnapshot.ThreadCount.ToString(CultureInfo.InvariantCulture),
            Application.targetFrameRate.ToString(CultureInfo.InvariantCulture),
            QualitySettings.vSyncCount.ToString(CultureInfo.InvariantCulture),
            QualitySettings.GetQualityLevel().ToString(CultureInfo.InvariantCulture),
            Screen.width.ToString(CultureInfo.InvariantCulture),
            Screen.height.ToString(CultureInfo.InvariantCulture),
            Screen.fullScreen ? "1" : "0",
            Application.runInBackground ? "1" : "0",
            _photonRegionPingMilliseconds.ToString(CultureInfo.InvariantCulture),
            Format(_lastRttMilliseconds),
            _fusionTickRate.ToString(CultureInfo.InvariantCulture),
            EscapeCsv(pipelineSnapshot.Name),
            Format(pipelineSnapshot.RenderScale),
            pipelineSnapshot.SupportsHdr ? "1" : "0",
            pipelineSnapshot.RequiresDepthTexture ? "1" : "0",
            pipelineSnapshot.RequiresOpaqueTexture ? "1" : "0",
            pipelineSnapshot.SupportsMainLightShadows ? "1" : "0",
            pipelineSnapshot.AdditionalLightsMode.ToString(CultureInfo.InvariantCulture),
            pipelineSnapshot.SupportsAdditionalLightShadows ? "1" : "0",
            pipelineSnapshot.SupportsSoftShadows ? "1" : "0",
            Format(pipelineSnapshot.ShadowDistance),
            pipelineSnapshot.ShadowCascadeCount.ToString(CultureInfo.InvariantCulture),
            pipelineSnapshot.AdditionalShadowAtlasSize.ToString(CultureInfo.InvariantCulture),
            pipelineSnapshot.UseSrpBatcher ? "1" : "0",
            pipelineSnapshot.SupportsDynamicBatching ? "1" : "0",
            pipelineSnapshot.EnableRenderGraph ? "1" : "0",
            QualitySettings.streamingMipmapsActive ? "1" : "0",
            Format(QualitySettings.streamingMipmapsMemoryBudget),
            QualitySettings.maxQueuedFrames.ToString(CultureInfo.InvariantCulture)
        };
        _lastGeneration0Collections = generation0;
        _lastGeneration1Collections = generation1;
        _lastGeneration2Collections = generation2;
        columns.AddRange(RecorderMetrics.Select(metric => metric.ReadLastValue()));
        return string.Join(',', columns) + Environment.NewLine;
    }

    private static void EvaluateAdaptiveTuning(FrameTimeSnapshot frameSnapshot)
    {
        if (_configuration is null
            || _configuration.Preset.Value != PerformancePreset.Auto
            || !_configuration.AdaptivePerformance.Value
            || _adaptiveShadowFallbackApplied
            || SessionClock.Elapsed.TotalSeconds < 35d
            || SessionClock.Elapsed.TotalSeconds - _activeSceneStartedSeconds < 15d
            || string.IsNullOrWhiteSpace(SceneManager.GetActiveScene().name)
            || SceneManager.GetActiveScene().name == "Initialization"
            || frameSnapshot.Frames < 100
            || frameSnapshot.AverageFps >= Math.Clamp(_configuration.AdaptiveMinimumFps.Value, 30, 120))
        {
            return;
        }

        var universalPipeline = QualitySettings.renderPipeline?.TryCast<UniversalRenderPipelineAsset>();
        if (universalPipeline is null || !universalPipeline.supportsAdditionalLightShadows)
        {
            return;
        }

        _adaptiveShadowFallbackApplied = true;
        _adaptiveVSyncDisabled = _configuration.VSync.Value == VSyncMode.Preserve
            && QualitySettings.vSyncCount > 0;
        ApplyPipelineOverrides();
        MaintainFramePacingOverrides();
        _logger?.LogWarning(
            $"Adaptive tuning applied after {frameSnapshot.AverageFps:F1} FPS: "
            + "disabled additional-light shadows"
            + (_adaptiveVSyncDisabled ? " and vSync" : string.Empty));
    }

    private static PipelineSnapshot GetPipelineSnapshot()
    {
        try
        {
            var pipeline = QualitySettings.renderPipeline;
            var universalPipeline = pipeline?.TryCast<UniversalRenderPipelineAsset>();
            if (universalPipeline is null)
            {
                return new PipelineSnapshot(pipeline?.name ?? string.Empty);
            }

            return new PipelineSnapshot(
                universalPipeline.name,
                universalPipeline.renderScale,
                universalPipeline.supportsHDR,
                universalPipeline.supportsCameraDepthTexture,
                universalPipeline.supportsCameraOpaqueTexture,
                universalPipeline.supportsMainLightShadows,
                (int)universalPipeline.additionalLightsRenderingMode,
                universalPipeline.supportsAdditionalLightShadows,
                universalPipeline.supportsSoftShadows,
                universalPipeline.shadowDistance,
                universalPipeline.shadowCascadeCount,
                universalPipeline.additionalLightsShadowmapResolution,
                universalPipeline.useSRPBatcher,
                universalPipeline.supportsDynamicBatching,
                universalPipeline.enableRenderGraph);
        }
        catch
        {
            return default;
        }
    }

    private static ProcessSnapshot GetProcessSnapshot()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            var elapsedSeconds = SessionClock.Elapsed.TotalSeconds;
            var cpuTime = process.TotalProcessorTime;
            var elapsedDelta = elapsedSeconds - _lastProcessSampleSeconds;
            var cpuPercent = elapsedDelta > 0d
                ? (cpuTime - _lastProcessCpuTime).TotalSeconds
                    / elapsedDelta * 100d
                : 0d;
            _lastProcessCpuTime = cpuTime;
            _lastProcessSampleSeconds = elapsedSeconds;
            return new ProcessSnapshot(
                process.WorkingSet64 / 1048576d,
                process.PrivateMemorySize64 / 1048576d,
                Math.Max(0d, cpuPercent),
                process.Threads.Count);
        }
        catch
        {
            return default;
        }
    }

    private static void WriteFinalReport()
    {
        if (_configuration is null || !_configuration.EnableTelemetry.Value || !_telemetryInitialized)
        {
            return;
        }

        WriteIntervalReport();
        foreach (var metric in RecorderMetrics)
        {
            metric.Dispose();
        }
    }

    private static string Format(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string EscapeCsv(string value)
    {
        return value.Contains(',') || value.Contains('"')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }

    private sealed class RecorderMetric
    {
        private ProfilerRecorder _recorder;

        public RecorderMetric(string columnName, string markerName, ProfilerRecorder recorder)
        {
            ColumnName = columnName;
            MarkerName = markerName;
            _recorder = recorder;
        }

        public string ColumnName { get; }
        public string MarkerName { get; }
        public bool Valid => _recorder.Valid;

        public string ReadLastValue()
        {
            try
            {
                return _recorder.Valid
                    ? _recorder.LastValue.ToString(CultureInfo.InvariantCulture)
                    : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        public void Dispose()
        {
            if (_recorder.Valid)
            {
                _recorder.Dispose();
            }
        }
    }

    private sealed class FrameTimingAccumulator
    {
        private int _samples;
        private double _cpuMilliseconds;
        private double _mainThreadMilliseconds;
        private double _presentWaitMilliseconds;
        private double _renderThreadMilliseconds;
        private double _gpuMilliseconds;

        public void Record(FrameTiming timing)
        {
            _samples++;
            _cpuMilliseconds += timing.cpuFrameTime;
            _mainThreadMilliseconds += timing.cpuMainThreadFrameTime;
            _presentWaitMilliseconds += timing.cpuMainThreadPresentWaitTime;
            _renderThreadMilliseconds += timing.cpuRenderThreadFrameTime;
            _gpuMilliseconds += timing.gpuFrameTime;
        }

        public EngineFrameTimingSnapshot SnapshotAndReset()
        {
            var divisor = Math.Max(1, _samples);
            var snapshot = new EngineFrameTimingSnapshot(
                _cpuMilliseconds / divisor,
                _mainThreadMilliseconds / divisor,
                _presentWaitMilliseconds / divisor,
                _renderThreadMilliseconds / divisor,
                _gpuMilliseconds / divisor);
            _samples = 0;
            _cpuMilliseconds = 0d;
            _mainThreadMilliseconds = 0d;
            _presentWaitMilliseconds = 0d;
            _renderThreadMilliseconds = 0d;
            _gpuMilliseconds = 0d;
            return snapshot;
        }
    }

    private readonly record struct EngineFrameTimingSnapshot(
        double CpuMilliseconds,
        double MainThreadMilliseconds,
        double PresentWaitMilliseconds,
        double RenderThreadMilliseconds,
        double GpuMilliseconds);

    private readonly record struct ProcessSnapshot(
        double WorkingSetMegabytes,
        double PrivateMegabytes,
        double CpuPercent,
        int ThreadCount);

    private readonly record struct PipelineSnapshot(
        string Name,
        double RenderScale = 0d,
        bool SupportsHdr = false,
        bool RequiresDepthTexture = false,
        bool RequiresOpaqueTexture = false,
        bool SupportsMainLightShadows = false,
        int AdditionalLightsMode = 0,
        bool SupportsAdditionalLightShadows = false,
        bool SupportsSoftShadows = false,
        double ShadowDistance = 0d,
        int ShadowCascadeCount = 0,
        int AdditionalShadowAtlasSize = 0,
        bool UseSrpBatcher = false,
        bool SupportsDynamicBatching = false,
        bool EnableRenderGraph = false);

}
