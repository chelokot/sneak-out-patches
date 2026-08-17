namespace SneakOut.LockerStunFix;

internal readonly record struct LockerStunZonePoint(float X, float Y, float Z);

internal static class LockerStunZonePolicy
{
    // Locker.HandleBooSkill constructs this exact Physics.OverlapSphere query
    // from the serialized Interactable.Transform in the supported client. Keep
    // the visual tied to that anchor rather than to the locker GameObject's
    // inherited transform; those transforms differ on some map prefabs.
    public const float ForwardOffset = 1f;
    public const float HeightOffset = 0.75f;
    public const float Radius = 1.25f;

    public static bool TryResolveCenter(
        LockerStunZonePoint origin,
        LockerStunZonePoint forward,
        out LockerStunZonePoint center)
    {
        center = default;
        if (!IsFinite(origin) || !IsFinite(forward))
        {
            return false;
        }

        center = new LockerStunZonePoint(
            origin.X + forward.X * ForwardOffset,
            origin.Y + forward.Y * ForwardOffset + HeightOffset,
            origin.Z + forward.Z * ForwardOffset);
        return IsFinite(center);
    }

    public static bool IsPointInsideQuery(
        LockerStunZonePoint origin,
        LockerStunZonePoint forward,
        LockerStunZonePoint point)
    {
        if (!TryResolveCenter(origin, forward, out var center) || !IsFinite(point))
        {
            return false;
        }

        var deltaX = point.X - center.X;
        var deltaY = point.Y - center.Y;
        var deltaZ = point.Z - center.Z;
        return deltaX * deltaX + deltaY * deltaY + deltaZ * deltaZ <= Radius * Radius;
    }

    public static bool TryResolveHorizontalCrossSection(
        LockerStunZonePoint origin,
        LockerStunZonePoint forward,
        float planeHeight,
        out LockerStunZonePoint center,
        out float radius)
    {
        center = default;
        radius = 0f;
        if (!float.IsFinite(planeHeight)
            || !TryResolveCenter(origin, forward, out var sphereCenter))
        {
            return false;
        }

        var heightFromCenter = planeHeight - sphereCenter.Y;
        var squaredRadius = Radius * Radius - heightFromCenter * heightFromCenter;
        if (!float.IsFinite(squaredRadius) || squaredRadius < 0f)
        {
            return false;
        }

        center = new LockerStunZonePoint(sphereCenter.X, planeHeight, sphereCenter.Z);
        radius = MathF.Sqrt(squaredRadius);
        return IsFinite(center) && float.IsFinite(radius);
    }

    private static bool IsFinite(LockerStunZonePoint point)
    {
        return float.IsFinite(point.X)
            && float.IsFinite(point.Y)
            && float.IsFinite(point.Z);
    }
}
