using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace SneakOut.LockerStunFix;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class LockerStunFixPlugin : BasePlugin
{
    public const string PluginGuid = "chelokot.sneakout.locker-stun-fix";
    public const string PluginName = "Locker Stun Fix";
    public const string PluginVersion = "0.5.2";

    public override void Load()
    {
        var configuration = LockerStunFixConfig.Bind(Config);
        LockerStunFixRuntime.Initialize(Log, configuration);
        Log.LogInfo($"{PluginName} {PluginVersion} loaded");
    }
}
