namespace SneakOut.ChairWallThrowFix;

internal static class ChairReleasePolicy
{
    internal const float SearchStep = 0.1f;

    public static float SafeCenterDistance(float hitDistance, float projectedRadius, float clearance)
    {
        if (!float.IsFinite(hitDistance)
            || !float.IsFinite(projectedRadius)
            || !float.IsFinite(clearance))
        {
            return 0f;
        }

        return MathF.Max(
            0f,
            hitDistance - MathF.Max(0f, projectedRadius) - MathF.Max(0f, clearance));
    }

    public static float PlayerSideCenterDistance(float hitDistance, float projectedRadius, float clearance)
    {
        if (!float.IsFinite(hitDistance)
            || !float.IsFinite(projectedRadius)
            || !float.IsFinite(clearance))
        {
            return 0f;
        }

        // This deliberately remains signed. If the obstacle is closer than
        // the chair's support radius, the only valid center on the player's
        // side lies behind the player-side anchor.
        return hitDistance - MathF.Max(0f, projectedRadius) - MathF.Max(0f, clearance);
    }

    public static bool ShouldMoveTowardPlayer(
        float playerDistanceToObstacle,
        float chairDistanceToObstacle,
        bool obstacleBetweenCenters)
    {
        if (obstacleBetweenCenters)
        {
            return true;
        }

        if (!float.IsFinite(playerDistanceToObstacle)
            || !float.IsFinite(chairDistanceToObstacle))
        {
            return true;
        }

        // When both centers remain on the same side, move the chair in the
        // centerline direction that increases its distance from the obstacle.
        // If the player is closer, "toward player" points into the obstacle.
        return chairDistanceToObstacle <= playerDistanceToObstacle;
    }

    public static bool ShouldOverrideBlockedRelease(
        bool stockReturnedNoInteraction,
        bool isReleaseInput,
        int playerCurrentlyUsing,
        int requestingPlayer,
        bool isPossessed,
        bool isSomethingInFrontOfPlayer)
    {
        return stockReturnedNoInteraction
            && isReleaseInput
            && playerCurrentlyUsing == requestingPlayer
            && !isPossessed
            && isSomethingInFrontOfPlayer;
    }

    public static IEnumerable<float> CandidateDistances(float maximumDistance)
    {
        if (!float.IsFinite(maximumDistance) || maximumDistance < SearchStep)
        {
            yield break;
        }

        var stepCount = (int)MathF.Ceiling(maximumDistance / SearchStep);
        for (var step = 1; step <= stepCount; step++)
        {
            yield return MathF.Min(step * SearchStep, maximumDistance);
        }
    }
}
