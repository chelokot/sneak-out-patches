using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace SneakOut.MummyUnlock;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class MummyUnlockPlugin : BasePlugin
{
    public const string PluginGuid = "chelokot.sneakout.mummy-unlock";
    public const string PluginName = "Mummy Unlock";
    public const string PluginVersion = "0.1.0";

    public override void Load()
    {
        MummyUnlockRuntime.Initialize(Log);
        Log.LogInfo($"{PluginName} {PluginVersion} loaded");
    }
}
