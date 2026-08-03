namespace SneakOut.ChairWallThrowFix;

internal static class ChairReleasePolicy
{
    internal const float SearchStep = 0.1f;

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
