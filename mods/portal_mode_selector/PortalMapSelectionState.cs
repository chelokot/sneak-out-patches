using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Types;

namespace SneakOut.PortalModeSelector;

internal sealed class PortalMapSelectionState
{
    private readonly HashSet<SceneType> _displayedClassicMaps = new();
    private readonly HashSet<SceneType> _displayedCrownMaps = new();
    private readonly HashSet<SceneType> _selectedClassicMaps = new();
    private readonly HashSet<SceneType> _selectedCrownMaps = new();

    public void Synchronize(Il2CppStructArray<SceneType> maps)
    {
        Synchronize(maps.ToArray());
    }

    public void SynchronizeDefaults()
    {
        AddDefaultMap(SceneType.Map01, _displayedClassicMaps, _selectedClassicMaps);
        AddDefaultMap(SceneType.Map02, _displayedClassicMaps, _selectedClassicMaps);
        AddDefaultMap(SceneType.Map03, _displayedClassicMaps, _selectedClassicMaps);
        AddDefaultMap(SceneType.Map04, _displayedClassicMaps, _selectedClassicMaps);
        AddDefaultMap(SceneType.Map_East01, _displayedClassicMaps, _selectedClassicMaps);
        AddDefaultMap(SceneType.Map_East02, _displayedClassicMaps, _selectedClassicMaps);
        AddDefaultMap(SceneType.Map05_TagGame, _displayedCrownMaps, _selectedCrownMaps);
        AddDefaultMap(SceneType.Map_School01, _displayedCrownMaps, _selectedCrownMaps);
        AddDefaultMap(SceneType.Map_School02, _displayedCrownMaps, _selectedCrownMaps);
    }

    public IReadOnlyCollection<SceneType> GetDisplayedMaps(GameModeType gameModeType)
    {
        return gameModeType == GameModeType.Berek ? _displayedCrownMaps : _displayedClassicMaps;
    }

    public HashSet<SceneType> GetSelectedMaps(GameModeType gameModeType)
    {
        return gameModeType == GameModeType.Berek ? _selectedCrownMaps : _selectedClassicMaps;
    }

    public static bool IsSelectable(SceneType sceneType)
    {
        // Keep disabled maps visible in the party configurator without allowing
        // them into a matchmaking map pool.
        return sceneType is not SceneType.Map04
            and not SceneType.Map_East01;
    }

    public PortalMapSelectionState Snapshot()
    {
        var snapshot = new PortalMapSelectionState();
        snapshot._displayedClassicMaps.UnionWith(_displayedClassicMaps);
        snapshot._displayedCrownMaps.UnionWith(_displayedCrownMaps);
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
            availableMaps.Where(sceneType => !IsBerekMap(sceneType)),
            _displayedClassicMaps,
            _selectedClassicMaps
        );
        SynchronizeMode(
            availableMaps.Where(IsBerekMap),
            _displayedCrownMaps,
            _selectedCrownMaps
        );
    }

    private static void SynchronizeMode(
        IEnumerable<SceneType> maps,
        HashSet<SceneType> displayedMaps,
        HashSet<SceneType> selectedMaps
    )
    {
        var newDisplayedMaps = maps.ToArray();
        var mapsAdded = newDisplayedMaps.Where(sceneType => !displayedMaps.Contains(sceneType)).ToArray();

        displayedMaps.Clear();
        displayedMaps.UnionWith(newDisplayedMaps);
        selectedMaps.IntersectWith(displayedMaps);
        selectedMaps.RemoveWhere(sceneType => !IsSelectable(sceneType));
        selectedMaps.UnionWith(mapsAdded.Where(IsSelectable));
    }

    private static void AddDefaultMap(
        SceneType sceneType,
        HashSet<SceneType> displayedMaps,
        HashSet<SceneType> selectedMaps)
    {
        if (displayedMaps.Add(sceneType) && IsSelectable(sceneType))
        {
            selectedMaps.Add(sceneType);
        }
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

    private static bool IsBerekMap(SceneType sceneType)
    {
        return sceneType is SceneType.Map05_TagGame
            or SceneType.Map_School01
            or SceneType.Map_School02;
    }
}
