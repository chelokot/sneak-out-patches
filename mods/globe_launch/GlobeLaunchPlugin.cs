using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace SneakOut.GlobeLaunch;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class GlobeLaunchPlugin : BasePlugin
{
    public const string PluginGuid = "chelokot.sneakout.globe-launch";
    public const string PluginName = "Globe Launch";
    public const string PluginVersion = "0.1.15";

    public override void Load()
    {
        var configuration = GlobeLaunchConfig.Bind(Config);
        GlobeLaunchRuntime.Initialize(Log, configuration);
        Log.LogInfo($"{PluginName} {PluginVersion} loaded");
    }
}
