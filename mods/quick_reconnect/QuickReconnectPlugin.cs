using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace SneakOut.QuickReconnect;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class QuickReconnectPlugin : BasePlugin
{
    public const string PluginGuid = "chelokot.sneakout.quick-reconnect";
    public const string PluginName = "Quick Reconnect";
    public const string PluginVersion = "0.1.0";

    public override void Load()
    {
        QuickReconnectRuntime.Initialize(Log);
        Log.LogInfo($"{PluginName} {PluginVersion} loaded");
    }
}
