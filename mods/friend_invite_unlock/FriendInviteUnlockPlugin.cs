using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace SneakOut.FriendInviteUnlock;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class FriendInviteUnlockPlugin : BasePlugin
{
    public const string PluginGuid = "chelokot.sneakout.friend-invite-unlock";
    public const string PluginName = "Friend Invite Unlock";
    public const string PluginVersion = "0.1.0";

    public override void Load()
    {
        var configuration = FriendInviteUnlockConfig.Bind(Config);
        FriendInviteUnlockRuntime.Initialize(Log, configuration);
        Log.LogInfo($"{PluginName} {PluginVersion} loaded");
    }
}
