using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace SneakOut.UnlockEverything;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class UnlockEverythingPlugin : BasePlugin
{
    public const string PluginGuid = "chelokot.sneakout.unlock-everything";
    public const string PluginName = "Unlock Everything";
    public const string PluginVersion = "0.1.0";

    public override void Load()
    {
        var configuration = UnlockEverythingConfig.Bind(Config);
        UnlockEverythingRuntime.Initialize(Log, configuration);
        Log.LogInfo($"{PluginName} {PluginVersion} loaded");
    }
}
