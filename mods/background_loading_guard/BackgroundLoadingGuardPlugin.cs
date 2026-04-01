using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace SneakOut.BackgroundLoadingGuard;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class BackgroundLoadingGuardPlugin : BasePlugin
{
    public const string PluginGuid = "chelokot.sneakout.background-loading-guard";
    public const string PluginName = "Background Loading Guard";
    public const string PluginVersion = "0.1.0";

    public override void Load()
    {
        var configuration = BackgroundLoadingGuardConfig.Bind(Config);
        BackgroundLoadingGuardRuntime.Initialize(Log, configuration);
        Log.LogInfo($"{PluginName} {PluginVersion} loaded");
    }
}
