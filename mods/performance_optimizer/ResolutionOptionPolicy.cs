namespace SneakOut.PerformanceOptimizer;

internal readonly record struct ResolutionDimensions(int Width, int Height);

internal static class ResolutionOptionPolicy
{
    public static IReadOnlyList<int> GetUniqueDimensionIndices(
        IReadOnlyList<ResolutionDimensions> resolutions)
    {
        ArgumentNullException.ThrowIfNull(resolutions);

        var uniqueIndices = new List<int>(resolutions.Count);
        var seenDimensions = new HashSet<ResolutionDimensions>();
        for (var index = 0; index < resolutions.Count; index++)
        {
            if (seenDimensions.Add(resolutions[index]))
            {
                uniqueIndices.Add(index);
            }
        }

        return uniqueIndices;
    }
}
