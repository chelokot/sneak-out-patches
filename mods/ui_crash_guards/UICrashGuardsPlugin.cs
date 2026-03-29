using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace SneakOut.UICrashGuards;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class UICrashGuardsPlugin : BasePlugin
{
    public const string PluginGuid = "chelokot.sneakout.ui-crash-guards";
    public const string PluginName = "UI Crash Guards";
    public const string PluginVersion = "0.1.0";

    public override void Load()
    {
        var configuration = UICrashGuardsConfig.Bind(Config);
        UICrashGuardsRuntime.Initialize(Log, configuration);
        Log.LogInfo($"{PluginName} {PluginVersion} loaded");
    }
}
