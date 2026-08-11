using SneakOut.PerformanceOptimizer;

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

var displayModes = new[]
{
    new ResolutionDimensions(1280, 720),
    new ResolutionDimensions(1920, 1080),
    new ResolutionDimensions(1920, 1080),
    new ResolutionDimensions(1920, 1200),
    new ResolutionDimensions(2560, 1440),
};
var selectedIndices = ResolutionOptionPolicy.GetUniqueDimensionIndices(displayModes);

Require(
    selectedIndices.SequenceEqual(new[] { 0, 1, 3, 4 }),
    "resolution options were not deduplicated by the complete width/height pair");
Require(
    displayModes[selectedIndices[2]] == new ResolutionDimensions(1920, 1200),
    "16:10 mode was collapsed into the 16:9 mode with the same width");

Console.WriteLine("performance optimizer tests passed");
