using BepInEx.Configuration;

namespace SneakOut.Minimap;

internal enum MinimapShape
{
    Circle,
    Rectangle,
}

internal sealed class MinimapConfig
{
    private MinimapConfig(
        ConfigEntry<bool> enableMod,
        ConfigEntry<bool> startVisible,
        ConfigEntry<bool> showWhileHolding,
        ConfigEntry<MinimapShape> mapShape,
        ConfigEntry<int> mapSize,
        ConfigEntry<int> zoom,
        ConfigEntry<int> topMargin,
        ConfigEntry<int> rightMargin,
        ConfigEntry<string> toggleBinding,
        ConfigEntry<bool> captureSettingsScreenshot,
        ConfigEntry<bool> enableLogging)
    {
        EnableMod = enableMod;
        StartVisible = startVisible;
        ShowWhileHolding = showWhileHolding;
        MapShape = mapShape;
        MapSize = mapSize;
        Zoom = zoom;
        TopMargin = topMargin;
        RightMargin = rightMargin;
        ToggleBinding = toggleBinding;
        CaptureSettingsScreenshot = captureSettingsScreenshot;
        EnableLogging = enableLogging;
    }

    public ConfigEntry<bool> EnableMod { get; }

    public ConfigEntry<bool> StartVisible { get; }

    public ConfigEntry<bool> ShowWhileHolding { get; }

    public ConfigEntry<MinimapShape> MapShape { get; }

    public ConfigEntry<int> MapSize { get; }

    public ConfigEntry<int> Zoom { get; }

    public ConfigEntry<int> TopMargin { get; }

    public ConfigEntry<int> RightMargin { get; }

    public ConfigEntry<string> ToggleBinding { get; }

    public ConfigEntry<bool> CaptureSettingsScreenshot { get; }

    public ConfigEntry<bool> EnableLogging { get; }

    public static MinimapConfig Bind(ConfigFile configFile)
    {
        var legacyMargin = ReadLegacyMargin(configFile);
        var migratedTopMargin = Math.Clamp(legacyMargin ?? 24, 0, 300);
        var migratedRightMargin = Math.Clamp(legacyMargin ?? 12, 0, 300);
        var legacyMarginDefinition = new ConfigDefinition("appearance", "ScreenMargin");
        var legacyRotateDefinition = new ConfigDefinition("appearance", "RotateMap");
        var hasLegacyRotateMap = HasConfigKey(configFile, "appearance", "RotateMap");
        if (legacyMargin.HasValue)
        {
            configFile.Bind(
                legacyMarginDefinition,
                migratedTopMargin,
                new ConfigDescription("Legacy combined margin; migrated to TopMargin and RightMargin."));
        }
        if (hasLegacyRotateMap)
        {
            configFile.Bind(
                legacyRotateDefinition,
                true,
                new ConfigDescription("Legacy player-up rotation setting; the map now uses the fixed game-camera angle."));
        }

        var enableMod = configFile.Bind(
            "general",
            "EnableMod",
            true,
            "Generate and display a runtime minimap in playable map scenes. Also editable in the in-game Map settings tab.");
        var startVisible = configFile.Bind(
            "general",
            "StartVisible",
            true,
            "Show the minimap when a playable map loads. Also editable in the in-game Map settings tab.");
        var showWhileHolding = configFile.Bind(
            "input",
            "ShowWhileHolding",
            false,
            "Show the minimap only while its configured key is held instead of toggling it on press. Also editable in the in-game Map settings tab.");
        var mapShape = configFile.Bind(
            "appearance",
            "MapShape",
            MinimapShape.Circle,
            "Visible minimap frame: Circle or Rectangle. Also editable in the in-game Map settings tab.");
        var mapSize = configFile.Bind(
            "appearance",
            "MapSize",
            260,
            "Minimap size in reference-resolution pixels. Values are clamped from 140 to 500 and editable in the in-game Map settings tab.");
        var zoom = configFile.Bind(
            "appearance",
            "Zoom",
            0,
            "Minimap zoom from 0 (entire map) to 100 (closest view). Also editable in the in-game Map settings tab.");
        var topMargin = configFile.Bind(
            "appearance",
            "TopMargin",
            migratedTopMargin,
            "Distance from the top screen edge in reference-resolution pixels. Also editable in the in-game Map settings tab.");
        var rightMargin = configFile.Bind(
            "appearance",
            "RightMargin",
            migratedRightMargin,
            "Distance from the right screen edge in reference-resolution pixels. Also editable in the in-game Map settings tab.");
        var toggleBinding = configFile.Bind(
            "input",
            "ToggleBinding",
            "<Keyboard>/tab",
            "Unity Input System binding used to show or hide the minimap. Record a key in the in-game Map settings tab.");
        var captureSettingsScreenshot = configFile.Bind(
            "diagnostics",
            "CaptureSettingsScreenshot",
            false,
            "Open the in-game Map settings once and capture it for unattended visual regression testing.");
        var enableLogging = configFile.Bind(
            "diagnostics",
            "EnableLogging",
            false,
            "Log the runtime room-volume geometry used to build the minimap.");

        if (legacyMargin.HasValue || hasLegacyRotateMap)
        {
            if (legacyMargin.HasValue)
            {
                configFile.Remove(legacyMarginDefinition);
            }
            if (hasLegacyRotateMap)
            {
                configFile.Remove(legacyRotateDefinition);
            }
            configFile.Save();
        }

        return new MinimapConfig(
            enableMod,
            startVisible,
            showWhileHolding,
            mapShape,
            mapSize,
            zoom,
            topMargin,
            rightMargin,
            toggleBinding,
            captureSettingsScreenshot,
            enableLogging);
    }

    private static int? ReadLegacyMargin(ConfigFile configFile)
    {
        try
        {
            var configPath = configFile.GetType()
                .GetProperty("ConfigFilePath")?
                .GetValue(configFile) as string;
            if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
            {
                return null;
            }

            var inAppearanceSection = false;
            foreach (var rawLine in File.ReadLines(configPath))
            {
                var line = rawLine.Trim();
                if (line.StartsWith("[", StringComparison.Ordinal)
                    && line.EndsWith("]", StringComparison.Ordinal))
                {
                    inAppearanceSection = string.Equals(
                        line,
                        "[appearance]",
                        StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (!inAppearanceSection || !line.StartsWith("ScreenMargin", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var separator = line.IndexOf('=');
                if (separator >= 0 && int.TryParse(line[(separator + 1)..].Trim(), out var value))
                {
                    return value;
                }
            }
        }
        catch
        {
            // A migration read must never prevent the plugin from starting. Fresh defaults
            // remain valid when the legacy file is unavailable.
        }
        return null;
    }

    private static bool HasConfigKey(ConfigFile configFile, string section, string key)
    {
        try
        {
            var configPath = configFile.GetType()
                .GetProperty("ConfigFilePath")?
                .GetValue(configFile) as string;
            if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
            {
                return false;
            }

            var inRequestedSection = false;
            foreach (var rawLine in File.ReadLines(configPath))
            {
                var line = rawLine.Trim();
                if (line.StartsWith("[", StringComparison.Ordinal)
                    && line.EndsWith("]", StringComparison.Ordinal))
                {
                    inRequestedSection = string.Equals(
                        line,
                        $"[{section}]",
                        StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (!inRequestedSection)
                {
                    continue;
                }

                var separator = line.IndexOf('=');
                if (separator >= 0
                    && string.Equals(line[..separator].Trim(), key, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch
        {
            // Deprecated settings are optional cleanup and cannot block plugin startup.
        }
        return false;
    }
}
