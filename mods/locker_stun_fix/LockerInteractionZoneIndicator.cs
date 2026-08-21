using Gameplay.Interactions;
using Gameplay.Player;
using Gameplay.Player.Components;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace SneakOut.LockerStunFix;

internal static class LockerInteractionZoneIndicator
{
    private const float TargetCellSize = 0.15f;
    private const float FloorClearance = 0.012f;
    private const float PlayerInteractionHeight = 0.5f;
    private const float RaycastExtension = 0.06f;
    private const int MaximumCellsPerAxis = 64;
    private static readonly Color InteractionColor = new(1f, 0.68f, 0.12f, 0.28f);
    private static readonly Dictionary<IntPtr, GameObject> IndicatorsByLocker = new();

    private static Material? _material;

    public static bool TryEnsureVisible(Locker locker, out bool created, out string failure)
    {
        created = false;
        failure = string.Empty;
        try
        {
            if (locker.Pointer == IntPtr.Zero)
            {
                failure = "locker pointer is unavailable";
                return false;
            }

            if (IndicatorsByLocker.TryGetValue(locker.Pointer, out var existing))
            {
                if (existing)
                {
                    existing.SetActive(true);
                    return true;
                }

                IndicatorsByLocker.Remove(locker.Pointer);
            }

            var queryTransform = locker.Transform;
            var lockerCollider = locker._collider;
            var gameplaySettings = locker.Settings?.Gameplay;
            if (queryTransform is null || !queryTransform)
            {
                failure = "locker interaction transform is unavailable";
                return false;
            }

            if (lockerCollider is null || !lockerCollider)
            {
                failure = "locker interaction collider is unavailable";
                return false;
            }

            if (gameplaySettings is null || gameplaySettings.Pointer == IntPtr.Zero)
            {
                failure = "gameplay interaction settings are unavailable";
                return false;
            }

            var maximumDistance = gameplaySettings.LocalDistanceToInteract;
            if (!float.IsFinite(maximumDistance) || maximumDistance <= 0f || maximumDistance > 20f)
            {
                failure = $"invalid local interaction distance {maximumDistance}";
                return false;
            }

            var closeRange = gameplaySettings.InteractDistanceWithoutRaycast;
            if (!float.IsFinite(closeRange) || closeRange < 0f || closeRange > maximumDistance)
            {
                failure = $"invalid interaction no-raycast distance {closeRange}";
                return false;
            }

            if (!TryResolveLocalInteractionLayerMask(out var interactionLayerMask))
            {
                failure = "local player interaction component is unavailable";
                return false;
            }

            var colliderObject = lockerCollider.gameObject;
            if (colliderObject is null
                || (interactionLayerMask & (1 << colliderObject.layer)) == 0)
            {
                failure = "locker collider is excluded by the local interaction layer mask";
                return false;
            }

            var material = GetOrCreateMaterial();
            if (material is null)
            {
                failure = "no compatible unlit interaction-area shader is available";
                return false;
            }

            var bounds = lockerCollider.bounds;
            var minimumX = bounds.min.x - maximumDistance;
            var maximumX = bounds.max.x + maximumDistance;
            var minimumZ = bounds.min.z - maximumDistance;
            var maximumZ = bounds.max.z + maximumDistance;
            var cellsX = ResolveCellCount(maximumX - minimumX);
            var cellsZ = ResolveCellCount(maximumZ - minimumZ);
            var cellSizeX = (maximumX - minimumX) / cellsX;
            var cellSizeZ = (maximumZ - minimumZ) / cellsZ;
            var sampleHeight = queryTransform.position.y;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            for (var zIndex = 0; zIndex < cellsZ; zIndex++)
            {
                var cellMinimumZ = minimumZ + zIndex * cellSizeZ;
                var cellMaximumZ = cellMinimumZ + cellSizeZ;
                for (var xIndex = 0; xIndex < cellsX; xIndex++)
                {
                    var cellMinimumX = minimumX + xIndex * cellSizeX;
                    var cellMaximumX = cellMinimumX + cellSizeX;
                    var samplePosition = new Vector3(
                        (cellMinimumX + cellMaximumX) * 0.5f,
                        sampleHeight,
                        (cellMinimumZ + cellMaximumZ) * 0.5f);
                    if (!IsLocallySelectable(
                            locker,
                            lockerCollider,
                            samplePosition,
                            maximumDistance,
                            closeRange,
                            interactionLayerMask))
                    {
                        continue;
                    }

                    AddCell(
                        queryTransform,
                        vertices,
                        triangles,
                        cellMinimumX,
                        cellMaximumX,
                        cellMinimumZ,
                        cellMaximumZ,
                        sampleHeight + FloorClearance);
                }
            }

            if (vertices.Count == 0)
            {
                failure = "local interaction resolver accepted no sampled floor positions";
                return false;
            }

            var indicatorObject = new GameObject("LockerInteractionArea")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            indicatorObject.transform.SetParent(queryTransform, false);
            var mesh = new Mesh
            {
                name = "LockerInteractionAreaMesh",
                hideFlags = HideFlags.HideAndDontSave,
                vertices = ToIl2CppArray(vertices),
                triangles = ToIl2CppArray(triangles)
            };
            mesh.RecalculateBounds();
            var meshFilter = indicatorObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = mesh;
            var meshRenderer = indicatorObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.sortingOrder = 90;

            IndicatorsByLocker[locker.Pointer] = indicatorObject;
            created = true;
            return true;
        }
        catch (Exception exception)
        {
            failure = $"{exception.GetType().Name}: {exception.Message}";
            return false;
        }
    }

    private static bool TryResolveLocalInteractionLayerMask(out int layerMask)
    {
        layerMask = 0;
        foreach (var player in Resources.FindObjectsOfTypeAll<SpookedNetworkPlayer>())
        {
            if (player is null || player.Pointer == IntPtr.Zero || !player.HasInputAuthority)
            {
                continue;
            }

            var interactiveComponent = player.EntityInteractiveComponent;
            if (interactiveComponent is null || interactiveComponent.Pointer == IntPtr.Zero)
            {
                continue;
            }

            layerMask = ~(int)interactiveComponent._ignoreLayerMask;
            return true;
        }

        return false;
    }

    private static bool IsLocallySelectable(
        Locker locker,
        Collider lockerCollider,
        Vector3 floorPosition,
        float localDistance,
        float closeRange,
        int layerMask)
    {
        // ResolveSelectedInteractiveComponent performs this closest-point test
        // from the player's interaction height after FindInteractables has
        // admitted colliders inside LocalDistanceToInteract.
        var rayOrigin = floorPosition + Vector3.up * PlayerInteractionHeight;
        var closestPoint = lockerCollider.ClosestPoint(rayOrigin);
        var toLocker = closestPoint - rayOrigin;
        var distance = toLocker.magnitude;
        if (distance > localDistance)
        {
            return false;
        }

        if (distance < closeRange || distance <= 0.0001f)
        {
            return true;
        }

        if (!Physics.Raycast(
                rayOrigin,
                toLocker / distance,
                out var hit,
                distance + RaycastExtension,
                layerMask,
                QueryTriggerInteraction.UseGlobal))
        {
            return false;
        }

        var hitObject = hit.collider?.gameObject;
        var lockerObject = locker.gameObject;
        return hitObject is not null
            && lockerObject is not null
            && hitObject == lockerObject;
    }

    private static int ResolveCellCount(float length)
    {
        return Math.Clamp((int)MathF.Ceiling(length / TargetCellSize), 1, MaximumCellsPerAxis);
    }

    private static void AddCell(
        Transform parent,
        List<Vector3> vertices,
        List<int> triangles,
        float minimumX,
        float maximumX,
        float minimumZ,
        float maximumZ,
        float height)
    {
        var firstVertex = vertices.Count;
        vertices.Add(parent.InverseTransformPoint(new Vector3(minimumX, height, minimumZ)));
        vertices.Add(parent.InverseTransformPoint(new Vector3(maximumX, height, minimumZ)));
        vertices.Add(parent.InverseTransformPoint(new Vector3(maximumX, height, maximumZ)));
        vertices.Add(parent.InverseTransformPoint(new Vector3(minimumX, height, maximumZ)));
        triangles.Add(firstVertex);
        triangles.Add(firstVertex + 2);
        triangles.Add(firstVertex + 1);
        triangles.Add(firstVertex);
        triangles.Add(firstVertex + 3);
        triangles.Add(firstVertex + 2);
    }

    private static Il2CppStructArray<T> ToIl2CppArray<T>(IReadOnlyList<T> values) where T : unmanaged
    {
        var result = new Il2CppStructArray<T>(values.Count);
        for (var index = 0; index < values.Count; index++)
        {
            result[index] = values[index];
        }

        return result;
    }

    private static Material? GetOrCreateMaterial()
    {
        if (_material is not null)
        {
            return _material;
        }

        var shader = Shader.Find("Sprites/Default")
            ?? Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Hidden/Internal-Colored");
        if (shader is null)
        {
            return null;
        }

        _material = new Material(shader)
        {
            name = "LockerInteractionAreaMaterial",
            hideFlags = HideFlags.HideAndDontSave,
            color = InteractionColor,
            renderQueue = 2990
        };
        _material.SetInt("_SrcBlend", 5);
        _material.SetInt("_DstBlend", 10);
        _material.SetInt("_ZWrite", 0);
        _material.SetInt("_Cull", 0);
        return _material;
    }
}
