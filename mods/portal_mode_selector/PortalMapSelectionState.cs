using Types;

namespace SneakOut.PortalModeSelector;

internal sealed class PortalMapSelectionState
{
    public HashSet<SceneType> SelectedClassicMaps { get; } = new(PortalModeSelectorRuntime.ClassicMapPool);

    public HashSet<SceneType> SelectedCrownMaps { get; } = new(PortalModeSelectorRuntime.CrownMapPool);

    public HashSet<SceneType> GetSelectedMaps(GameModeType gameModeType)
    {
        return gameModeType == GameModeType.Berek ? SelectedCrownMaps : SelectedClassicMaps;
    }
}
