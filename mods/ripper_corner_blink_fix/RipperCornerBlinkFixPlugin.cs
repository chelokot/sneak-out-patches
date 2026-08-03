using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace SneakOut.RipperCornerBlinkFix;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class RipperCornerBlinkFixPlugin : BasePlugin
{
    public const string PluginGuid = "chelokot.sneakout.ripper-corner-blink-fix";
    public const string PluginName = "Ripper Corner Blink Fix";
    public const string PluginVersion = "0.1.0";

    public override void Load()
    {
        var configuration = RipperCornerBlinkFixConfig.Bind(Config);
        RipperCornerBlinkFixRuntime.Initialize(Log, configuration);
        Log.LogInfo($"{PluginName} {PluginVersion} loaded");
    }
}
