using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace SneakOut.StartDelayReducer;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class StartDelayReducerPlugin : BasePlugin
{
    public const string PluginGuid = "chelokot.sneakout.start-delay-reducer";
    public const string PluginName = "Start Delay Reducer";
    public const string PluginVersion = "0.1.0";

    public override void Load()
    {
        var configuration = StartDelayReducerConfig.Bind(Config);
        StartDelayReducerRuntime.Initialize(Log, configuration);
        Log.LogInfo($"{PluginName} {PluginVersion} loaded");
    }
}
