using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace SneakOut.MagicWardrobeHookFix;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class MagicWardrobeHookFixPlugin : BasePlugin
{
    public const string PluginGuid = "chelokot.sneakout.magic-wardrobe-hook-fix";
    public const string PluginName = "Magic Wardrobe Hook Fix";
    public const string PluginVersion = "0.1.0";

    public override void Load()
    {
        var configuration = MagicWardrobeHookFixConfig.Bind(Config);
        MagicWardrobeHookFixRuntime.Initialize(Log, configuration);
        Log.LogInfo($"{PluginName} {PluginVersion} loaded");
    }
}
