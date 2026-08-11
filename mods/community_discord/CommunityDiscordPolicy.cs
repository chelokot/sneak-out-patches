namespace SneakOut.CommunityDiscord;

internal readonly record struct StatuePoint(float X, float Y, float Z);

internal static class CommunityDiscordPolicy
{
    public static StatuePoint MoveOnFloor(StatuePoint origin, float xOffset, float zOffset)
    {
        if (!IsFinite(origin)
            || !float.IsFinite(xOffset)
            || !float.IsFinite(zOffset))
        {
            return origin;
        }

        return new StatuePoint(
            origin.X + xOffset,
            origin.Y,
            origin.Z + zOffset);
    }

    public static bool ShouldOfferInteraction(
        float portalDistance,
        float nativeInteractionDistance,
        float minimumInteractionDistance)
    {
        if (!float.IsFinite(portalDistance) || portalDistance < 0f)
        {
            return false;
        }

        var interactionDistance = ResolveInteractionDistance(
            nativeInteractionDistance,
            minimumInteractionDistance);
        return portalDistance <= interactionDistance;
    }

    public static bool ShouldPreferPortal(
        float portalDistance,
        float nearestOtherDistance,
        float nativeInteractionDistance,
        float minimumInteractionDistance,
        float selectionTolerance)
    {
        if (!ShouldOfferInteraction(
                portalDistance,
                nativeInteractionDistance,
                minimumInteractionDistance)
            || !float.IsFinite(selectionTolerance)
            || selectionTolerance < 0f)
        {
            return false;
        }

        return float.IsPositiveInfinity(nearestOtherDistance)
            || (float.IsFinite(nearestOtherDistance)
                && nearestOtherDistance >= 0f
                && portalDistance <= nearestOtherDistance + selectionTolerance);
    }

    private static float ResolveInteractionDistance(float nativeDistance, float minimumDistance)
    {
        var validNativeDistance = float.IsFinite(nativeDistance) && nativeDistance > 0f
            ? nativeDistance
            : 0f;
        var validMinimumDistance = float.IsFinite(minimumDistance) && minimumDistance > 0f
            ? minimumDistance
            : 0f;
        return MathF.Max(validNativeDistance, validMinimumDistance);
    }

    private static bool IsFinite(StatuePoint point)
    {
        return float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z);
    }
}
