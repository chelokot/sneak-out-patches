using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace SneakOut.PerformanceOptimizer;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class PerformanceOptimizerPlugin : BasePlugin
{
    public const string PluginGuid = "chelokot.sneakout.performance-optimizer";
    public const string PluginName = "Performance Optimizer";
    public const string PluginVersion = "1.0.1";

    public override void Load()
    {
        var configuration = PerformanceOptimizerConfig.Bind(Config);
        PerformanceOptimizerRuntime.Initialize(Log, configuration);
        if (configuration.EnableMod.Value && configuration.EnableTelemetry.Value)
        {
            PerformanceOptimizerRuntime.AttachFrameWatcher();
        }
        Log.LogInfo($"{PluginName} {PluginVersion} loaded");
    }
}
