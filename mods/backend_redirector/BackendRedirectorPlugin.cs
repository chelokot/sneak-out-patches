using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace SneakOut.BackendRedirector;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class BackendRedirectorPlugin : BasePlugin
{
    public const string PluginGuid = "chelokot.sneakout.backend-redirector";
    public const string PluginName = "Backend Redirector";
    public const string PluginVersion = "0.1.0";

    public override void Load()
    {
        var configuration = BackendRedirectorConfig.Bind(Config);
        BackendRedirectorRuntime.Initialize(Log, configuration);
        Log.LogInfo($"{PluginName} {PluginVersion} loaded");
    }
}
