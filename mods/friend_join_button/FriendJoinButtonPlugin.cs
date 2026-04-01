using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace SneakOut.FriendJoinButton;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class FriendJoinButtonPlugin : BasePlugin
{
    public const string PluginGuid = "chelokot.sneakout.friend-join-button";
    public const string PluginName = "Friend Join Button";
    public const string PluginVersion = "0.1.0";

    public override void Load()
    {
        var configuration = FriendJoinButtonConfig.Bind(Config);
        FriendJoinButtonRuntime.Initialize(Log, configuration);
        Log.LogInfo($"{PluginName} {PluginVersion} loaded");
    }
}
