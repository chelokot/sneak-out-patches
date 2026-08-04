using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace SneakOut.PumpkinRadiusIndicatorFix;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class PumpkinRadiusIndicatorFixPlugin : BasePlugin
{
    public const string PluginGuid = "chelokot.sneakout.pumpkin-radius-indicator-fix";
    public const string PluginName = "Pumpkin Radius Indicator Fix";
    public const string PluginVersion = "0.2.1";

    public override void Load()
    {
        var configuration = PumpkinRadiusIndicatorFixConfig.Bind(Config);
        PumpkinRadiusIndicatorFixRuntime.Initialize(Log, configuration);
        Log.LogInfo($"{PluginName} {PluginVersion} loaded");
    }
}
