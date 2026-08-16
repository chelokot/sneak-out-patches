using Gameplay.Interactions;
using UnityEngine;
using UnityEngine.Rendering;

namespace SneakOut.LockerStunFix;

internal static class LockerStunZoneIndicator
{
    private const int SegmentCount = 72;
    private const float LineWidth = 0.055f;
    private const float DirectionLineWidth = 0.035f;
    private static readonly Color ZoneColor = new(0.2f, 0.95f, 1f, 0.92f);
    private static readonly Color DirectionColor = new(0.75f, 1f, 1f, 0.8f);

    private static Material? _lineMaterial;

    public static bool TryShow(Locker locker, out string failure)
    {
        failure = string.Empty;
        try
        {
            var lockerTransform = locker.transform;
            if (lockerTransform is null)
            {
                failure = "locker transform is unavailable";
                return false;
            }

            var origin = lockerTransform.position;
            var forward = lockerTransform.forward;
            if (!LockerStunZonePolicy.TryResolveCenter(
                    new LockerStunZonePoint(origin.x, origin.y, origin.z),
                    new LockerStunZonePoint(forward.x, forward.y, forward.z),
                    out var resolvedCenter))
            {
                failure = "locker transform produced a non-finite stun center";
                return false;
            }

            var material = GetOrCreateMaterial();
            if (material is null)
            {
                failure = "no compatible unlit line shader is available";
                return false;
            }

            var center = new Vector3(resolvedCenter.X, resolvedCenter.Y, resolvedCenter.Z);
            var indicatorObject = new GameObject("LockerBooStunZone")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            var line = indicatorObject.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.loop = true;
            line.positionCount = SegmentCount;
            line.startWidth = LineWidth;
            line.endWidth = LineWidth;
            line.numCornerVertices = 2;
            line.startColor = ZoneColor;
            line.endColor = ZoneColor;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sortingOrder = 100;

            for (var index = 0; index < SegmentCount; index++)
            {
                var angle = 2f * MathF.PI * index / SegmentCount;
                line.SetPosition(
                    index,
                    center + new Vector3(
                        MathF.Cos(angle) * LockerStunZonePolicy.Radius,
                        0f,
                        MathF.Sin(angle) * LockerStunZonePolicy.Radius));
            }

            AddDirectionMarker(indicatorObject, origin, forward, center, material);

            UnityEngine.Object.Destroy(indicatorObject, LockerStunZonePolicy.IndicatorDurationSeconds);
            return true;
        }
        catch (Exception exception)
        {
            failure = $"{exception.GetType().Name}: {exception.Message}";
            return false;
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

        var shaft = AddLine(indicatorObject, "LockerBooDirectionShaft", material, 2);
        shaft.SetPosition(0, markerOrigin);
        shaft.SetPosition(1, tip);

        var arrow = AddLine(indicatorObject, "LockerBooDirectionArrow", material, 3);
        arrow.SetPosition(0, tip - forward * 0.2f + side * 0.13f);
        arrow.SetPosition(1, tip);
        arrow.SetPosition(2, tip - forward * 0.2f - side * 0.13f);
    }

    private static LineRenderer AddLine(
        GameObject parent,
        string name,
        Material material,
        int positionCount)
    {
        var lineObject = new GameObject(name)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        lineObject.transform.SetParent(parent.transform, false);
        var line = lineObject.AddComponent<LineRenderer>();
        line.sharedMaterial = material;
        line.useWorldSpace = true;
        line.loop = false;
        line.positionCount = positionCount;
        line.startWidth = DirectionLineWidth;
        line.endWidth = DirectionLineWidth;
        line.numCornerVertices = 2;
        line.startColor = DirectionColor;
        line.endColor = DirectionColor;
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
