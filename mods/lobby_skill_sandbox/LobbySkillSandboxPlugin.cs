using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace SneakOut.LobbySkillSandbox;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class LobbySkillSandboxPlugin : BasePlugin
{
    public const string PluginGuid = "chelokot.sneakout.lobby-skill-sandbox";
    public const string PluginName = "Lobby Skill Sandbox";
    public const string PluginVersion = "0.1.0";

    public override void Load()
    {
        var configuration = LobbySkillSandboxConfig.Bind(Config);
        LobbySkillSandboxRuntime.Initialize(Log, configuration);
        Log.LogInfo($"{PluginName} {PluginVersion} loaded");
    }
}
