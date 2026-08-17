using Gameplay.Interactions;
using UnityEngine;
using UnityEngine.Rendering;

namespace SneakOut.LockerStunFix;

internal static class LockerStunZoneIndicator
{
    private const int SegmentCount = 72;
    private const float FloorClearance = 0.025f;
    private const float ZoneLineWidth = 0.055f;
    private const float DirectionLineWidth = 0.035f;
    private static readonly Color ZoneColor = new(0.2f, 0.95f, 1f, 0.92f);
    private static readonly Color DirectionColor = new(0.75f, 1f, 1f, 0.8f);
    private static readonly Dictionary<IntPtr, GameObject> IndicatorsByLocker = new();

    private static Material? _lineMaterial;

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

            // Locker.HandleBooSkill reads Interactable.Transform, not the
            // MonoBehaviour's inherited transform. Some locker prefabs use a
            // distinct serialized anchor, so using locker.transform moves and
            // rotates the visualization away from the native physics query.
            var queryTransform = locker.Transform;
            if (queryTransform is null || !queryTransform)
            {
                failure = "locker stun-query transform is unavailable";
                return false;
            }

            var origin = queryTransform.position;
            var forward = queryTransform.forward;
            if (!LockerStunZonePolicy.TryResolveHorizontalCrossSection(
                    new LockerStunZonePoint(origin.x, origin.y, origin.z),
                    new LockerStunZonePoint(forward.x, forward.y, forward.z),
                    origin.y,
                    out var resolvedCenter,
                    out var resolvedRadius))
            {
                failure = "locker transform produced no finite floor-level stun cross-section";
                return false;
            }

            var material = GetOrCreateMaterial();
            if (material is null)
            {
                failure = "no compatible unlit line shader is available";
                return false;
            }

            // Render the native sphere's slice through the locker anchor plane,
            // not its widest circle 0.75 m in the air. The latter projects onto
            // the floor in the isometric camera and falsely suggests side reach.
            var center = new Vector3(
                resolvedCenter.X,
                resolvedCenter.Y + FloorClearance,
                resolvedCenter.Z);
            var indicatorObject = new GameObject("LockerBooStunZone")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            indicatorObject.transform.SetParent(queryTransform, false);
            AddCircle(
                indicatorObject,
                "LockerBooStunZoneFloorCrossSection",
                material,
                center,
                Vector3.right,
                Vector3.forward,
                resolvedRadius,
                ZoneLineWidth,
                ZoneColor);

            AddDirectionMarker(indicatorObject, origin, forward, center, material);
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

    private static void AddCircle(
        GameObject parent,
        string name,
        Material material,
        Vector3 center,
        Vector3 firstAxis,
        Vector3 secondAxis,
        float radius,
        float width,
        Color color)
    {
        var line = AddLine(parent, name, material, SegmentCount, width, color, loop: true);
        for (var index = 0; index < SegmentCount; index++)
        {
            var angle = 2f * MathF.PI * index / SegmentCount;
            line.SetPosition(
                index,
                center + (firstAxis * MathF.Cos(angle) + secondAxis * MathF.Sin(angle))
                * radius);
        }
    }

    private static void AddDirectionMarker(
        GameObject indicatorObject,
        Vector3 origin,
        Vector3 nativeForward,
        Vector3 center,
        Material material)
    {
        var planarLength = MathF.Sqrt(
            nativeForward.x * nativeForward.x + nativeForward.z * nativeForward.z);
        if (!float.IsFinite(planarLength) || planarLength < 0.0001f)
        {
            return;
        }

        var forward = new Vector3(
            nativeForward.x / planarLength,
            0f,
            nativeForward.z / planarLength);
        var side = new Vector3(-forward.z, 0f, forward.x);
        var markerOrigin = new Vector3(origin.x, center.y, origin.z) + forward * 0.12f;
        var tip = center + forward * 0.4f;

        var shaft = AddLine(
            indicatorObject,
            "LockerBooDirectionShaft",
            material,
            2,
            DirectionLineWidth,
            DirectionColor,
            loop: false);
        shaft.SetPosition(0, markerOrigin);
        shaft.SetPosition(1, tip);

        var arrow = AddLine(
            indicatorObject,
            "LockerBooDirectionArrow",
            material,
            3,
            DirectionLineWidth,
            DirectionColor,
            loop: false);
        arrow.SetPosition(0, tip - forward * 0.2f + side * 0.13f);
        arrow.SetPosition(1, tip);
        arrow.SetPosition(2, tip - forward * 0.2f - side * 0.13f);
    }

    private static LineRenderer AddLine(
        GameObject parent,
        string name,
        Material material,
        int positionCount,
        float width,
        Color color,
        bool loop)
    {
        var lineObject = new GameObject(name)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        lineObject.transform.SetParent(parent.transform, false);
        var line = lineObject.AddComponent<LineRenderer>();
        line.sharedMaterial = material;
        line.useWorldSpace = true;
        line.loop = loop;
        line.positionCount = positionCount;
        line.startWidth = width;
        line.endWidth = width;
        line.numCornerVertices = 2;
        line.startColor = color;
        line.endColor = color;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sortingOrder = 101;
        return line;
    }

    private static Material? GetOrCreateMaterial()
    {
        if (_lineMaterial is not null)
        {
            return _lineMaterial;
        }

        var shader = Shader.Find("Sprites/Default")
            ?? Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Hidden/Internal-Colored");
        if (shader is null)
        {
            return null;
        }

        _lineMaterial = new Material(shader)
        {
            name = "LockerBooStunZoneMaterial",
            hideFlags = HideFlags.HideAndDontSave,
            color = ZoneColor,
            renderQueue = 3000
        };
        _lineMaterial.SetInt("_SrcBlend", 5);
        _lineMaterial.SetInt("_DstBlend", 10);
        _lineMaterial.SetInt("_ZWrite", 0);
        return _lineMaterial;
    }
}
