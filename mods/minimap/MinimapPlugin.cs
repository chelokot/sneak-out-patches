using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace SneakOut.Minimap;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class MinimapPlugin : BasePlugin
{
    public const string PluginGuid = "chelokot.sneakout.minimap";
    public const string PluginName = "Minimap";
    public const string PluginVersion = "0.3.2";

    public override void Load()
    {
        var configuration = MinimapConfig.Bind(Config);
        MinimapSettingsUi.Initialize(configuration, Log);
        MinimapRuntime.Initialize(Log, configuration);
        Log.LogInfo($"{PluginName} {PluginVersion} loaded");
    }
}
