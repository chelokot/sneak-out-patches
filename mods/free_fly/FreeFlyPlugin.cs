using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace SneakOut.FreeFly;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class FreeFlyPlugin : BasePlugin
{
    public const string PluginGuid = "chelokot.sneakout.free-fly";
    public const string PluginName = "Free Fly";
    public const string PluginVersion = "0.1.0";

    public override void Load()
    {
        var configuration = FreeFlyConfig.Bind(Config);
        FreeFlyRuntime.Initialize(Log, configuration);
        Log.LogInfo($"{PluginName} {PluginVersion} loaded");
    }
}
