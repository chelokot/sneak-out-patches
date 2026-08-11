using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace SneakOut.PropBuff;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class PropBuffPlugin : BasePlugin
{
    public const string PluginGuid = "chelokot.sneakout.prop-buff";
    public const string PluginName = "Prop Buff";
    public const string PluginVersion = "0.1.5";

    public override void Load()
    {
        PropBuffRuntime.Initialize(Log, PropBuffConfig.Bind(Config));
        Log.LogInfo($"{PluginName} {PluginVersion} loaded");
    }
}
