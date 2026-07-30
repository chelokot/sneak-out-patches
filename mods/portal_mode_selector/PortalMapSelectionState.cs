using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Types;

namespace SneakOut.PortalModeSelector;

internal sealed class PortalMapSelectionState
{
    private readonly HashSet<SceneType> _availableClassicMaps = new();
    private readonly HashSet<SceneType> _availableCrownMaps = new();
    private readonly HashSet<SceneType> _selectedClassicMaps = new();
    private readonly HashSet<SceneType> _selectedCrownMaps = new();

    public void Synchronize(Il2CppStructArray<SceneType> maps)
    {
        Synchronize(maps.ToArray());
    }

    public IReadOnlyCollection<SceneType> GetAvailableMaps(GameModeType gameModeType)
    {
        return gameModeType == GameModeType.Berek ? _availableCrownMaps : _availableClassicMaps;
    }

    public HashSet<SceneType> GetSelectedMaps(GameModeType gameModeType)
    {
        return gameModeType == GameModeType.Berek ? _selectedCrownMaps : _selectedClassicMaps;
    }

    public PortalMapSelectionState Snapshot()
    {
        var snapshot = new PortalMapSelectionState();
        snapshot._availableClassicMaps.UnionWith(_availableClassicMaps);
        snapshot._availableCrownMaps.UnionWith(_availableCrownMaps);
        snapshot._selectedClassicMaps.UnionWith(_selectedClassicMaps);
        snapshot._selectedCrownMaps.UnionWith(_selectedCrownMaps);
        return snapshot;
    }

    private void Synchronize(IEnumerable<SceneType> maps)
    {
        var availableMaps = maps
            .Where(IsPlayableMap)
            .Distinct()
            .ToArray();

        SynchronizeMode(
            availableMaps.Where(sceneType => !SceneTypeExtension.IsBerekMap(sceneType)),
            _availableClassicMaps,
            _selectedClassicMaps
        );
        SynchronizeMode(
            availableMaps.Where(SceneTypeExtension.IsBerekMap),
            _availableCrownMaps,
            _selectedCrownMaps
        );
    }

    private static void SynchronizeMode(
        IEnumerable<SceneType> maps,
        HashSet<SceneType> availableMaps,
        HashSet<SceneType> selectedMaps
    )
    {
        var newAvailableMaps = maps.ToArray();
        var mapsAdded = newAvailableMaps.Where(sceneType => !availableMaps.Contains(sceneType)).ToArray();

        availableMaps.Clear();
        availableMaps.UnionWith(newAvailableMaps);
        selectedMaps.IntersectWith(availableMaps);
        selectedMaps.UnionWith(mapsAdded);
    }

    private static bool IsPlayableMap(SceneType sceneType)
    {
        return sceneType is not SceneType.None
            and not SceneType.Initialization
            and not SceneType.Menu
            and not SceneType.Lobby
            and not SceneType.Tutorial
            and not SceneType.LoadingScreen
            and not SceneType.Game
            and not SceneType.MatchSummary
            and not SceneType.EndMatchScene
            and not SceneType.GameSceneTest;
    }
}
