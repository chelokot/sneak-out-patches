using SneakOut.ChairWallThrowFix;
using SneakOut.PumpkinRadiusIndicatorFix;
using SneakOut.RipperCornerBlinkFix;

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void RequireClose(float actual, float expected, string message)
{
    Require(MathF.Abs(actual - expected) <= 0.0001f, $"{message}: expected {expected}, got {actual}");
}

var releaseCandidates = ChairReleasePolicy.CandidateDistances(0.25f).ToArray();
Require(releaseCandidates.Length == 3, "chair release search did not include its bounded final candidate");
RequireClose(releaseCandidates[0], 0.1f, "chair release first step changed");
RequireClose(releaseCandidates[1], 0.2f, "chair release second step changed");
RequireClose(releaseCandidates[2], 0.25f, "chair release maximum was exceeded");
Require(!ChairReleasePolicy.CandidateDistances(float.NaN).Any(), "chair release accepted a non-finite maximum");

Require(
    PumpkinIndicatorScalePolicy.TryCalculate(3f, new Scale3(0.8f, 0.8f, 0.8f), out var pumpkinScale),
    "pumpkin scale compensation rejected the shipped prefab scale");
RequireClose(pumpkinScale.X, 3.75f, "pumpkin X scale was not parent-compensated");
RequireClose(pumpkinScale.Y, 3.75f, "pumpkin Y scale was not parent-compensated");
RequireClose(pumpkinScale.Z, 3.75f, "pumpkin Z scale was not parent-compensated");
RequireClose(pumpkinScale.X * 0.8f, 3f, "pumpkin world radius does not match kill radius");
Require(
    !PumpkinIndicatorScalePolicy.TryCalculate(3f, new Scale3(0f, 0.8f, 0.8f), out _),
    "pumpkin scale compensation divided by a zero parent axis");

const int intersectionLayer = 15;
Require(
    !SharedCornerPolicy.ShouldBypass(
        false,
        5f,
        intersectionLayer,
        new[] { new PathBlocker(2f, intersectionLayer) }),
    "room-junction traversal was enabled without the through-wall perk");
Require(
    SharedCornerPolicy.ShouldBypass(
        true,
        5f,
        intersectionLayer,
        new[] { new PathBlocker(2f, intersectionLayer) }),
    "the equipped through-wall perk did not bypass a room-junction strip");
Require(
    SharedCornerPolicy.ShouldBypass(
        true,
        5f,
        intersectionLayer,
        new[]
        {
            new PathBlocker(2f, intersectionLayer),
            new PathBlocker(4.98f, 8)
        }),
    "the destination floor was treated as a path blocker");
Require(
    !SharedCornerPolicy.ShouldBypass(
        true,
        5f,
        intersectionLayer,
        new[] { new PathBlocker(2f, 14) }),
    "an ordinary Wall-layer blocker was bypassed");
Require(
    !SharedCornerPolicy.ShouldBypass(
        true,
        5f,
        intersectionLayer,
        new[]
        {
            new PathBlocker(2f, intersectionLayer),
            new PathBlocker(3f, 20)
        }),
    "HardEnvironment behind a room junction was bypassed");
Require(
    !SharedCornerPolicy.ShouldBypass(true, 5f, intersectionLayer, Array.Empty<PathBlocker>()),
    "a clear path incorrectly took the custom RPC path");

Console.WriteLine("Chair release, pumpkin indicator, and perk-gated room-junction blink policy tests passed.");
