using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace SneakOut.FirstPersonExperiment;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class FirstPersonExperimentPlugin : BasePlugin
{
    public const string PluginGuid = "chelokot.sneakout.first-person-experiment";
    public const string PluginName = "First Person Experiment";
    public const string PluginVersion = "0.16.13";

    public override void Load()
    {
        var configuration = FirstPersonExperimentConfig.Bind(Config);
        FirstPersonExperimentRuntime.Initialize(Log, configuration);
        Log.LogInfo($"{PluginName} {PluginVersion} loaded");
    }
}
