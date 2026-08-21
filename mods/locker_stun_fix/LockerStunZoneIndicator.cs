using Gameplay.Interactions;
using UnityEngine;
using UnityEngine.Rendering;

namespace SneakOut.LockerStunFix;

internal static class LockerStunZoneIndicator
{
    private const int SegmentsPerCorner = 18;
    private const float FloorClearance = 0.025f;
    private const float ZoneLineWidth = 0.055f;
    private static readonly Color ZoneColor = new(0.2f, 0.95f, 1f, 0.92f);
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

            var queryTransform = locker.Transform;
            if (queryTransform is null || !queryTransform)
            {
                failure = "locker interaction transform is unavailable";
                return false;
            }

            var lockerCollider = locker._collider;
            if (lockerCollider is null || !lockerCollider)
            {
                failure = "locker interaction collider is unavailable";
                return false;
            }

            if (!TryResolveHorizontalBox(
                    lockerCollider,
                    queryTransform.position.y + FloorClearance,
                    out var center,
                    out var firstAxis,
                    out var secondAxis,
                    out var firstHalfExtent,
                    out var secondHalfExtent))
            {
                failure = "locker collider produced no finite horizontal outline";
                return false;
            }

            var material = GetOrCreateMaterial();
            if (material is null)
            {
                failure = "no compatible unlit line shader is available";
                return false;
            }

            var indicatorObject = new GameObject("LockerBooStunZone")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            indicatorObject.transform.SetParent(queryTransform, false);
            AddRoundedRectangle(
                indicatorObject,
                "LockerBooStunRoundedRectangle",
                material,
                center,
                firstAxis,
                secondAxis,
                firstHalfExtent,
                secondHalfExtent,
                LockerStunZonePolicy.StunDistance,
                ZoneLineWidth,
                ZoneColor);

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

    private static bool TryResolveHorizontalBox(
        Collider collider,
        float height,
        out Vector3 center,
        out Vector3 firstAxis,
        out Vector3 secondAxis,
        out float firstHalfExtent,
        out float secondHalfExtent)
    {
        center = default;
        firstAxis = Vector3.right;
        secondAxis = Vector3.forward;
        firstHalfExtent = 0f;
        secondHalfExtent = 0f;
        if (!float.IsFinite(height))
        {
            return false;
        }

        if (collider.TryCast<BoxCollider>() is { } boxCollider)
        {
            var boxTransform = boxCollider.transform;
            var scale = boxTransform.lossyScale;
            center = boxTransform.TransformPoint(boxCollider.center);
            center.y = height;
            firstAxis = Vector3.ProjectOnPlane(boxTransform.right, Vector3.up);
            secondAxis = Vector3.ProjectOnPlane(boxTransform.forward, Vector3.up);
            firstHalfExtent = boxCollider.size.x * 0.5f * Mathf.Abs(scale.x);
            secondHalfExtent = boxCollider.size.z * 0.5f * Mathf.Abs(scale.z);
        }
        else
        {
            var bounds = collider.bounds;
            center = new Vector3(bounds.center.x, height, bounds.center.z);
            firstHalfExtent = bounds.extents.x;
            secondHalfExtent = bounds.extents.z;
        }

        var firstLength = firstAxis.magnitude;
        var secondLength = secondAxis.magnitude;
        if (!IsFinite(center)
            || !float.IsFinite(firstHalfExtent)
            || !float.IsFinite(secondHalfExtent)
            || firstHalfExtent < 0f
            || secondHalfExtent < 0f
            || firstLength < 0.0001f
            || secondLength < 0.0001f)
        {
            return false;
        }

        firstAxis /= firstLength;
        secondAxis /= secondLength;
        return true;
    }

    private static void AddRoundedRectangle(
        GameObject parent,
        string name,
        Material material,
        Vector3 center,
        Vector3 firstAxis,
        Vector3 secondAxis,
        float firstHalfExtent,
        float secondHalfExtent,
        float radius,
        float width,
        Color color)
    {
        var positionCount = SegmentsPerCorner * 4;
        var line = AddLine(parent, name, material, positionCount, width, color, loop: true);
        for (var cornerIndex = 0; cornerIndex < 4; cornerIndex++)
        {
            var firstSign = cornerIndex is 0 or 3 ? 1f : -1f;
            var secondSign = cornerIndex is 0 or 1 ? 1f : -1f;
            var cornerCenter = center
                + firstAxis * (firstHalfExtent * firstSign)
                + secondAxis * (secondHalfExtent * secondSign);
            var startingAngle = cornerIndex * MathF.PI * 0.5f;
            for (var segmentIndex = 0; segmentIndex < SegmentsPerCorner; segmentIndex++)
            {
                var progress = segmentIndex / (SegmentsPerCorner - 1f);
                var angle = startingAngle + progress * MathF.PI * 0.5f;
                var position = cornerCenter
                    + firstAxis * (MathF.Cos(angle) * radius)
                    + secondAxis * (MathF.Sin(angle) * radius);
                line.SetPosition(cornerIndex * SegmentsPerCorner + segmentIndex, position);
            }
        }
    }

    private static bool IsFinite(Vector3 point)
    {
        return float.IsFinite(point.x)
            && float.IsFinite(point.y)
            && float.IsFinite(point.z);
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
