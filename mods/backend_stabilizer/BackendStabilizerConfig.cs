using BepInEx.Configuration;

namespace SneakOut.BackendStabilizer;

internal sealed class BackendStabilizerConfig
{
    private BackendStabilizerConfig(
        ConfigEntry<bool> enableResearchLogging,
        ConfigEntry<bool> enableProfileOverlay,
        ConfigEntry<bool> enableLocalStub)
    {
        EnableResearchLogging = enableResearchLogging;
        EnableProfileOverlay = enableProfileOverlay;
        EnableLocalStub = enableLocalStub;
    }

    public ConfigEntry<bool> EnableResearchLogging { get; }
    public ConfigEntry<bool> EnableProfileOverlay { get; }

    public ConfigEntry<bool> EnableLocalStub { get; }

    public static BackendStabilizerConfig Bind(ConfigFile configFile)
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

        return new BackendStabilizerConfig(enableResearchLogging, enableProfileOverlay, enableLocalStub);
    }
}
