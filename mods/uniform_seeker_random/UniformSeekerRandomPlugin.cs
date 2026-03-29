using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace SneakOut.UniformSeekerRandom;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class UniformSeekerRandomPlugin : BasePlugin
{
    public const string PluginGuid = "chelokot.sneakout.uniform-seeker-random";
    public const string PluginName = "Uniform Seeker Random";
    public const string PluginVersion = "0.1.0";

    public override void Load()
    {
        var configuration = UniformSeekerRandomConfig.Bind(Config);
        UniformSeekerRandomRuntime.Initialize(Log, configuration);
        Log.LogInfo($"{PluginName} {PluginVersion} loaded");
    }
}
