using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace SneakOut.KeyboardLayoutFix;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class KeyboardLayoutFixPlugin : BasePlugin
{
    public const string PluginGuid = "chelokot.sneakout.keyboard-layout-fix";
    public const string PluginName = "Keyboard Layout Fix";
    public const string PluginVersion = "0.4.1";

    public override void Load()
    {
        var configuration = KeyboardLayoutFixConfig.Bind(Config);
        KeyboardLayoutFixRuntime.Initialize(Log, configuration);
        Log.LogInfo($"{PluginName} {PluginVersion} loaded");
    }
}
