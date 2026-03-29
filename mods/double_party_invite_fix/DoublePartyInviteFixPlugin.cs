using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace SneakOut.DoublePartyInviteFix;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class DoublePartyInviteFixPlugin : BasePlugin
{
    public const string PluginGuid = "chelokot.sneakout.double-party-invite-fix";
    public const string PluginName = "Double Party Invite Fix";
    public const string PluginVersion = "0.1.0";

    public override void Load()
    {
        var configuration = DoublePartyInviteFixConfig.Bind(Config);
        DoublePartyInviteFixRuntime.Initialize(Log, configuration);
        Log.LogInfo($"{PluginName} {PluginVersion} loaded");
    }
}
