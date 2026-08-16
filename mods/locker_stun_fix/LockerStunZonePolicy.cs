namespace SneakOut.LockerStunFix;

internal readonly record struct LockerStunZonePoint(float X, float Y, float Z);

internal static class LockerStunZonePolicy
{
    // Locker.HandleBooSkill constructs this exact Physics.OverlapSphere query in
    // the supported client. Keep the visual tied to that query rather than to
    // the locker mesh, whose pivot and scale vary between map prefabs.
    public const float ForwardOffset = 1f;
    public const float HeightOffset = 0.75f;
    public const float Radius = 1.25f;
    public const float IndicatorDurationSeconds = 1.5f;

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

    private static bool IsFinite(LockerStunZonePoint point)
    {
        return float.IsFinite(point.X)
            && float.IsFinite(point.Y)
            && float.IsFinite(point.Z);
    }
}
