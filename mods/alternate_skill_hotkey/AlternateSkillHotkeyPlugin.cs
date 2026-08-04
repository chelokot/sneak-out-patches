using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace SneakOut.AlternateSkillHotkey;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class AlternateSkillHotkeyPlugin : BasePlugin
{
    public const string PluginGuid = "chelokot.sneakout.alternate-skill-hotkey";
    public const string PluginName = "Alternate Skill Hotkey";
    public const string PluginVersion = "0.1.0";

    public override void Load()
    {
        AlternateSkillHotkeyRuntime.Initialize(Log, AlternateSkillHotkeyConfig.Bind(Config));
        Log.LogInfo($"{PluginName} {PluginVersion} loaded");
    }
}
