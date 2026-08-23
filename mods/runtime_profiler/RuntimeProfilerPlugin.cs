using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace SneakOut.RuntimeProfiler;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class RuntimeProfilerPlugin : BasePlugin
{
    public const string PluginGuid = "chelokot.sneakout.runtime-profiler";
    public const string PluginName = "Runtime Event Logger";
    public const string PluginVersion = "1.0.0";

    public override void Load()
    {
        var configuration = RuntimeProfilerConfig.Bind(Config);
        RuntimeProfilerRuntime.Initialize(Log, configuration);
        Log.LogInfo($"{PluginName} {PluginVersion} loaded");
    }
}
