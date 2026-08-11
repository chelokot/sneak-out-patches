using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace SneakOut.CommunityDiscord;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class CommunityDiscordPlugin : BasePlugin
{
    public const string PluginGuid = "chelokot.sneakout.community-discord";
    public const string PluginName = "Community Discord";
    public const string PluginVersion = "0.2.12";

    public override void Load()
    {
        var configuration = CommunityDiscordConfig.Bind(Config);
        CommunityDiscordRuntime.Initialize(Log, configuration);
        Log.LogInfo($"{PluginName} {PluginVersion} loaded");
    }
}
