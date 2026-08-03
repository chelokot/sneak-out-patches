using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace SneakOut.ProximityVoiceChat;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class ProximityVoiceChatPlugin : BasePlugin
{
    public const string PluginGuid = "chelokot.sneakout.proximity-voice-chat";
    public const string PluginName = "Proximity Voice Chat";
    public const string PluginVersion = "0.1.0";

    public override void Load()
    {
        var configuration = ProximityVoiceChatConfig.Bind(Config);
        ProximityVoiceChatRuntime.Initialize(Log, configuration);
        Log.LogInfo($"{PluginName} {PluginVersion} loaded");
    }
}
