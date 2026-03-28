using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace SneakOut.LobbyPenguinSkills;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class LobbyPenguinSkillsPlugin : BasePlugin
{
    public const string PluginGuid = "chelokot.sneakout.lobby-penguin-skills";
    public const string PluginName = "Lobby Penguin Skills";
    public const string PluginVersion = "0.1.0";

    public override void Load()
    {
        var configuration = LobbyPenguinSkillsConfig.Bind(Config);
        LobbyPenguinSkillsRuntime.Initialize(Log, configuration);
        Log.LogInfo($"{PluginName} {PluginVersion} loaded");
    }
}
