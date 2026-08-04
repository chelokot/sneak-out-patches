using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace SneakOut.ChairWallThrowFix;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class ChairWallThrowFixPlugin : BasePlugin
{
    public const string PluginGuid = "chelokot.sneakout.chair-wall-throw-fix";
    public const string PluginName = "Chair Wall Throw Fix";
    public const string PluginVersion = "0.1.5";

    public override void Load()
    {
        var configuration = ChairWallThrowFixConfig.Bind(Config);
        ChairWallThrowFixRuntime.Initialize(Log, configuration);
        Log.LogInfo($"{PluginName} {PluginVersion} loaded");
    }
}
