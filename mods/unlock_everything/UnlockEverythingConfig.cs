using BepInEx.Configuration;

namespace SneakOut.UnlockEverything;

internal sealed class UnlockEverythingConfig
{
    private UnlockEverythingConfig(
        ConfigEntry<bool> enableResearchLogging,
        ConfigEntry<bool> enableProfileOverlay,
        ConfigEntry<bool> enableLocalStub,
        ConfigEntry<bool> enablePersistentSelections)
    {
        EnableResearchLogging = enableResearchLogging;
        EnableProfileOverlay = enableProfileOverlay;
        EnableLocalStub = enableLocalStub;
        EnablePersistentSelections = enablePersistentSelections;
    }

    public ConfigEntry<bool> EnableResearchLogging { get; }
    public ConfigEntry<bool> EnableProfileOverlay { get; }

    public ConfigEntry<bool> EnableLocalStub { get; }
    public ConfigEntry<bool> EnablePersistentSelections { get; }

    public static UnlockEverythingConfig Bind(ConfigFile configFile)
    {
        var enableResearchLogging = configFile.Bind(
            "general",
            "EnableResearchLogging",
            false,
            "Enable backend stabilizer research logs.");
        var enableProfileOverlay = configFile.Bind(
            "general",
            "EnableProfileOverlay",
            true,
            "Apply a local max-profile overlay after the stock backend profile refresh has completed.");
        var enableLocalStub = configFile.Bind(
            "general",
            "EnableLocalStub",
            false,
            "Replace selected profile webservice requests with a local stub. Leave disabled for the normal stabilizer flow.");
        var enablePersistentSelections = configFile.Bind(
            "general",
            "EnablePersistentSelections",
            true,
            "Persist selected cosmetics and equipped cards locally and reapply them after profile refresh.");

        return new UnlockEverythingConfig(enableResearchLogging, enableProfileOverlay, enableLocalStub, enablePersistentSelections);
    }
}
