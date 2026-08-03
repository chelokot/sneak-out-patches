namespace SneakOut.RipperCornerBlinkFix;

internal readonly record struct PathBlocker(float Distance, int Layer);

internal static class SharedCornerPolicy
{
    internal const float EndpointTolerance = 0.05f;

    public static bool ShouldBypass(
        bool hasWallBlinkPerk,
        float pathDistance,
        int intersectionLayer,
        IReadOnlyList<PathBlocker> blockers)
    {
        if (!hasWallBlinkPerk
            || !float.IsFinite(pathDistance)
            || pathDistance <= EndpointTolerance
            || intersectionLayer < 0)
        {
            return false;
        }

        var foundIntersection = false;
        for (var index = 0; index < blockers.Count; index++)
        {
            var blocker = blockers[index];
            if (!float.IsFinite(blocker.Distance) || blocker.Distance < 0f)
            {
                return false;
            }

            // A downward path terminates on the sampled floor. RaycastAll can report that
            // surface at the exact endpoint; it is a destination, not a path blocker.
            if (blocker.Distance >= pathDistance - EndpointTolerance)
            {
                continue;
            }

            // ReaperHelloThere already defines which ordinary geometry its wall blink may cross.
            // This patch only removes the dedicated Intersections junction strip; another
            // RaycastAll hit must not make the custom preflight stricter than the perk itself.
            foundIntersection |= blocker.Layer == intersectionLayer;
        }

        return foundIntersection;
    }
}
