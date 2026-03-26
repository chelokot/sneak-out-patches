using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace SneakOut.CoreFixes;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class CoreFixesPlugin : BasePlugin
{
    public const string PluginGuid = "chelokot.sneakout.core-fixes";
    public const string PluginName = "Core Fixes";
    public const string PluginVersion = "0.1.0";

    public override void Load()
    {
        var configuration = CoreFixesConfig.Bind(Config);
        CoreFixesRuntime.Initialize(Log, configuration);
        Log.LogInfo($"{PluginName} {PluginVersion} loaded");
    }
}
