using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace SneakOut.BackendStabilizer;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class BackendStabilizerPlugin : BasePlugin
{
    public const string PluginGuid = "chelokot.sneakout.backend-stabilizer";
    public const string PluginName = "Backend Stabilizer";
    public const string PluginVersion = "0.1.0";

    public override void Load()
    {
        var configuration = BackendStabilizerConfig.Bind(Config);
        BackendStabilizerRuntime.Initialize(Log, configuration);
        Log.LogInfo($"{PluginName} {PluginVersion} loaded");
    }
}
