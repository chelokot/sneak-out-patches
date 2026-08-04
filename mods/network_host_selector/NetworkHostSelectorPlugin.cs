using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace SneakOut.NetworkHostSelector;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class NetworkHostSelectorPlugin : BasePlugin
{
    public const string PluginGuid = "chelokot.sneakout.network-host-selector";
    public const string PluginName = "Network Host Selector";
    public const string PluginVersion = "0.1.4";

    public override void Load()
    {
        var configuration = NetworkHostSelectorConfig.Bind(Config);
        NetworkHostSelectorRuntime.Initialize(Log, configuration);
        Log.LogInfo($"{PluginName} {PluginVersion} loaded");
    }
}
