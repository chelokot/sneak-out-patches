using UnityEngine;
using Types;

namespace SneakOut.LobbySkillSandbox;

/// <summary>
/// Creates a visual-only lobby prop from existing lobby scenery. It never initializes the
/// gameplay PropPool and never changes the player's collider, transform or registry state.
/// </summary>
internal static class LobbyPropPool
{
    private const float DesiredPropSize = 0.9f;

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

    private static readonly Dictionary<PlayerPropType, GameObject> Sources = [];

    public static PlayerPropType ChooseRandomType()
    {
        RefreshSources();
        if (Sources.Count == 0)
        {
            return PlayerPropType.None;
        }

        var types = Sources.Keys.ToArray();
        return types[UnityEngine.Random.Range(0, types.Length)];
    }

    public static GameObject? CreateVisual(PlayerPropType type, Transform owner)
    {
        RefreshSources();
        if (!Sources.TryGetValue(type, out var source) || !source)
        {
            return null;
        }

        var visual = UnityEngine.Object.Instantiate(source, owner, false);
        visual.name = $"LobbyPropVisual.{type}";
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = Vector3.one;

        foreach (var behaviour in visual.GetComponentsInChildren<MonoBehaviour>(true))
        {
            behaviour.enabled = false;
        }

        foreach (var collider in visual.GetComponentsInChildren<Collider>(true))
        {
            collider.enabled = false;
        }

        foreach (var body in visual.GetComponentsInChildren<Rigidbody>(true))
        {
            body.isKinematic = true;
            body.detectCollisions = false;
        }

        FitVisualToPlayer(visual, owner);
        return visual;
    }

    public static void Dispose()
    {
        Sources.Clear();
    }

    private static void RefreshSources()
    {
        if (Sources.Count > 0 && Sources.Values.All(source => source))
        {
            return;
        }

        Sources.Clear();
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

            lobbyObjects.TryAdd(candidate.name, candidate);
        }

        foreach (var (type, sourceName) in PropSources)
        {
            if (lobbyObjects.TryGetValue(sourceName, out var source))
            {
                Sources[type] = source;
            }
        }
    }

    private static void FitVisualToPlayer(GameObject visual, Transform owner)
    {
        var renderers = visual.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return;
        }

        var bounds = renderers[0].bounds;
        for (var index = 1; index < renderers.Length; index++)
        {
            bounds.Encapsulate(renderers[index].bounds);
        }

        var largestDimension = Math.Max(bounds.size.x, Math.Max(bounds.size.y, bounds.size.z));
        if (largestDimension > 0.001f)
        {
            var scale = Mathf.Clamp(DesiredPropSize / largestDimension, 0.05f, 8f);
            visual.transform.localScale *= scale;

            bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }
        }

        var desiredCenter = owner.position + Vector3.up * 0.45f;
        visual.transform.position += desiredCenter - bounds.center;
    }
}
