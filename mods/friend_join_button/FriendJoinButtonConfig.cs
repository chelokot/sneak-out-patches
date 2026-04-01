using BepInEx.Configuration;

namespace SneakOut.FriendJoinButton;

internal sealed class FriendJoinButtonConfig
{
    private FriendJoinButtonConfig(
        ConfigEntry<bool> enableMod,
        ConfigEntry<bool> enableLogging)
    {
        EnableMod = enableMod;
        EnableLogging = enableLogging;
    }

    public ConfigEntry<bool> EnableMod { get; }

    public ConfigEntry<bool> EnableLogging { get; }

    public static FriendJoinButtonConfig Bind(ConfigFile configFile)
    {
        var enableMod = configFile.Bind(
            "general",
            "EnableMod",
            true,
            "Add a separate JOIN button to the friend popup when the game's friend refresh payload exposes a joinable party.");
        var enableLogging = configFile.Bind(
            "general",
            "EnableLogging",
            false,
            "Log friend snapshot payloads and join-button decisions.");

        return new FriendJoinButtonConfig(enableMod, enableLogging);
    }
}
