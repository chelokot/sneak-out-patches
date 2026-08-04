using BepInEx.Configuration;

namespace SneakOut.AlternateSkillHotkey;

internal sealed class AlternateSkillHotkeyConfig
{
    private AlternateSkillHotkeyConfig(ConfigEntry<bool> enableMod)
    {
        EnableMod = enableMod;
    }

    public ConfigEntry<bool> EnableMod { get; }

    public static AlternateSkillHotkeyConfig Bind(ConfigFile configFile)
    {
        return new AlternateSkillHotkeyConfig(configFile.Bind(
            "general",
            "EnableMod",
            true,
            "Use Left Alt to activate the character's unequipped alternate active perk."));
    }
}
