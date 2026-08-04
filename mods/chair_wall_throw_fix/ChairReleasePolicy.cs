namespace SneakOut.ChairWallThrowFix;

internal static class ChairReleasePolicy
{
    internal const float SearchStep = 0.1f;

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
