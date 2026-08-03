using Gameplay.Player.Components;
using Gameplay.Skills;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using Types;

namespace SneakOut.LobbySkillSandbox;

/// <summary>
/// Supplies the small piece of map infrastructure which the lobby scene does not contain.
/// The source objects are existing lobby scenery; no map scene or synthetic gameplay room is loaded.
/// </summary>
internal static class LobbyPropPool
{
    private const string RootName = "LobbySkillSandbox.PropPool";

    private static readonly (PlayerPropType Type, string SourceName)[] PropSources =
    [
        (PlayerPropType.Chair, "TavernTableSet_a_chair_prefab (19)"),
        (PlayerPropType.LibraryChair, "TavernTableSet_a_chair_prefab (20)"),
        (PlayerPropType.Scroll1, "Scroll_b_01_prefab"),
        (PlayerPropType.Scroll2, "Scroll_b_02_prefab"),
        (PlayerPropType.Book1, "Book_c_01_prefab"),
        (PlayerPropType.PotCactus, "PotSet_a_Pot_j_prefab"),
        (PlayerPropType.Pot0, "PotSet_a_Pot_d_prefab"),
        (PlayerPropType.Pot1, "PotSet_a_Pot_j_prefab"),
        (PlayerPropType.Pot2, "PotSet_a_Pot_d_prefab"),
        (PlayerPropType.ToyTeddyBear, "ToySet_a_sittingteddy_prefab"),
        (PlayerPropType.ToyDragon, "ToySet_a_dragon_prefab"),
        (PlayerPropType.ToyRubikCube, "ToySet_a_rubikcube_prefab"),
        (PlayerPropType.GreenBag, "ItemSet_b_paperbag_prefab"),
        (PlayerPropType.RedBag, "Bag_b_wPaper_Prefab"),
        (PlayerPropType.BlueBag, "ItemSet_b_paperbag_prefab"),
    ];

    private static GameObject? _root;
    private static PlayerPropType[] _availableTypes = [];

    public static bool EnsureInitialized(EntitySkillsComponent skills)
    {
        var propPool = skills._propPool;
        if (IsInitialized(propPool))
        {
            CacheInitializedTypes(propPool!._propPoolInitialization);
            return _availableTypes.Length > 0;
        }

        Dispose();

        try
        {
            var sources = FindLobbySources();
            if (sources.Count == 0)
            {
                return false;
            }

            var root = new GameObject(RootName);
            _root = root;
            root.hideFlags = HideFlags.HideAndDontSave;
            root.SetActive(false);

            var initialization = root.AddComponent<PropPoolInitialization>();
            initialization.SkipDependencyResolution = true;
            initialization._propPool = propPool;

            var propPrefabs = new Il2CppReferenceArray<Prop>(sources.Count);
            var availableTypes = new PlayerPropType[sources.Count];
            for (var index = 0; index < sources.Count; index++)
            {
                var source = sources[index];
                propPrefabs[index] = new Prop(source.Object, source.Type, null);
                availableTypes[index] = source.Type;
            }

            initialization._propPrefabs = propPrefabs;
            _availableTypes = availableTypes;

            // PropPoolInitialization.OnAwake performs the game's normal PropPool.Init call,
            // but only after every dependency and prefab entry has been assigned above.
            root.SetActive(true);
            if (IsInitialized(propPool))
            {
                return true;
            }
        }
        catch (Exception exception)
        {
            LobbySkillSandboxRuntime.Warn($"LobbyPropPool initialization failed: {exception.GetType().Name}");
        }

        Dispose();
        return false;
    }

    public static PlayerPropType ChooseRandomType()
    {
        if (_availableTypes.Length == 0)
        {
            return PlayerPropType.None;
        }

        return _availableTypes[UnityEngine.Random.Range(0, _availableTypes.Length)];
    }

    public static void Dispose()
    {
        _availableTypes = [];
        if (_root is null)
        {
            return;
        }

        UnityEngine.Object.Destroy(_root);
        _root = null;
    }

    private static bool IsInitialized(PropPool? propPool)
    {
        return propPool is not null
            && propPool._propPoolInitialization is not null
            && propPool._pool is not null
            && propPool._poolTransform is not null;
    }

    private static void CacheInitializedTypes(PropPoolInitialization? initialization)
    {
        if (_availableTypes.Length > 0 || initialization?._propPrefabs is null)
        {
            return;
        }

        var types = new List<PlayerPropType>();
        foreach (var prop in initialization._propPrefabs)
        {
            if (prop is not null && prop._playerPropType != PlayerPropType.None)
            {
                types.Add(prop._playerPropType);
            }
        }

        _availableTypes = types.ToArray();
    }

    private static List<LobbyPropSource> FindLobbySources()
    {
        var lobbyObjects = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        foreach (var candidate in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (candidate is null
                || candidate.name is null
                || !candidate.scene.IsValid()
                || !string.Equals(candidate.scene.name, "Lobby", StringComparison.Ordinal))
            {
                continue;
            }

            if (!lobbyObjects.ContainsKey(candidate.name))
            {
                lobbyObjects[candidate.name] = candidate;
            }
        }

        var sources = new List<LobbyPropSource>(PropSources.Length);
        foreach (var (type, sourceName) in PropSources)
        {
            if (lobbyObjects.TryGetValue(sourceName, out var sourceObject))
            {
                sources.Add(new LobbyPropSource(type, sourceObject));
            }
        }

        return sources;
    }

    private readonly record struct LobbyPropSource(PlayerPropType Type, GameObject Object);
}
