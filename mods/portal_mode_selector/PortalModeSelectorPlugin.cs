using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace SneakOut.PortalModeSelector;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class PortalModeSelectorPlugin : BasePlugin
{
    public const string PluginGuid = "chelokot.sneakout.portal-mode-selector";
    public const string PluginName = "Portal Mode Selector";
    public const string PluginVersion = "0.2.0";

    public override void Load()
    {
        PortalModeSelectorRuntime.Initialize(Log);
        Log.LogInfo($"{PluginName} {PluginVersion} loaded");
    }
}
