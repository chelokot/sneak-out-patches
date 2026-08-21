namespace SneakOut.LockerStunFix;

internal readonly record struct LockerStunZonePoint(float X, float Y, float Z);

internal static class LockerStunZonePolicy
{
    public const float OpeningDistance = 1f;
    public const float BalanceMargin = 0.2f;
    public const float StunDistance = OpeningDistance + BalanceMargin;
    public const float PlayerInteractionHeight = 0.5f;

    public static bool IsWithinOpeningDistance(
        LockerStunZonePoint playerInteractionPoint,
        LockerStunZonePoint closestLockerPoint)
    {
        return IsWithinDistance(
            playerInteractionPoint,
            closestLockerPoint,
            OpeningDistance);
    }

    public static bool IsWithinStunDistance(
        LockerStunZonePoint playerInteractionPoint,
        LockerStunZonePoint closestLockerPoint)
    {
        return IsWithinDistance(
            playerInteractionPoint,
            closestLockerPoint,
            StunDistance);
    }

    private static bool IsWithinDistance(
        LockerStunZonePoint playerInteractionPoint,
        LockerStunZonePoint closestLockerPoint,
        float distance)
    {
        if (!IsFinite(playerInteractionPoint) || !IsFinite(closestLockerPoint))
        {
            return false;
        }

        var deltaX = playerInteractionPoint.X - closestLockerPoint.X;
        var deltaY = playerInteractionPoint.Y - closestLockerPoint.Y;
        var deltaZ = playerInteractionPoint.Z - closestLockerPoint.Z;
        return deltaX * deltaX + deltaY * deltaY + deltaZ * deltaZ
            <= distance * distance;
    }

    public static bool TryResolveBroadPhaseRadius(
        LockerStunZonePoint boundsExtents,
        out float radius)
    {
        radius = 0f;
        if (!IsFinite(boundsExtents)
            || boundsExtents.X < 0f
            || boundsExtents.Y < 0f
            || boundsExtents.Z < 0f)
        {
            return false;
        }

        radius = MathF.Sqrt(
            boundsExtents.X * boundsExtents.X
            + boundsExtents.Y * boundsExtents.Y
            + boundsExtents.Z * boundsExtents.Z) + StunDistance;
        return float.IsFinite(radius) && radius > 0f;
    }

    private static bool IsFinite(LockerStunZonePoint point)
    {
        return float.IsFinite(point.X)
            && float.IsFinite(point.Y)
            && float.IsFinite(point.Z);
    }
}
