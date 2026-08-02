using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace SneakOut.LobbyTestBot;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class LobbyTestBotPlugin : BasePlugin
{
    public const string PluginGuid = "chelokot.sneakout.lobby-test-bot";
    public const string PluginName = "Lobby Test Bot";
    public const string PluginVersion = "0.6.0";

    public override void Load()
    {
        var configuration = LobbyTestBotConfig.Bind(Config);
        LobbyTestBotRuntime.Initialize(Log, configuration);
        Log.LogInfo($"{PluginName} {PluginVersion} loaded");
    }
}
