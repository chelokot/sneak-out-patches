using SneakOut.ChairWallThrowFix;
using SneakOut.MagicWardrobeHookFix;
using SneakOut.NetworkHostSelector;
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

var wardrobeHook = new MagicWardrobeHookPolicy();
Require(!wardrobeHook.RecordHook(7, 1f, 4f), "hook armed without an active magic-wardrobe entry");
Require(!wardrobeHook.BeginStep(7, true, 2f), "ordinary magic-wardrobe entry was cancelled");
Require(wardrobeHook.RecordHook(7, 2.1f, 4f), "active magic-wardrobe entry did not accept a hook interruption");
Require(wardrobeHook.BeginStep(7, true, 2.2f), "hooked magic-wardrobe entry was not cancelled");
Require(!wardrobeHook.BeginStep(7, true, 2.3f), "one hook interruption cancelled more than one coroutine step");
wardrobeHook.End(7);
Require(!wardrobeHook.RecordHook(7, 3f, 4f), "completed magic-wardrobe entry remained armed");
Require(!wardrobeHook.BeginStep(8, false, 3f), "magic-wardrobe exit was treated as an entry");
Require(!wardrobeHook.RecordHook(8, 3.1f, 4f), "magic-wardrobe exit armed the entry hook fix");
Require(!wardrobeHook.BeginStep(9, true, 3f), "ordinary entry was cancelled before a hook");
Require(wardrobeHook.RecordHook(9, 3.1f, 1f), "active entry did not record expiring hook marker");
Require(!wardrobeHook.BeginStep(9, true, 4.2f), "expired hook marker cancelled a later wardrobe step");

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

foreach (var expected in new[]
         {
             new HostSelectionMessage(HostSelectionMessageType.Hello, 0, 0),
             new HostSelectionMessage(HostSelectionMessageType.SelectRequest, 17, 4),
             new HostSelectionMessage(HostSelectionMessageType.ProposalAck, 23, 0),
         })
{
    var encoded = HostSelectionProtocol.Encode(expected.Type, expected.Revision, expected.TargetPlayerRaw);
    Require(encoded.Length == HostSelectionProtocol.PayloadLength, "host selection protocol payload length changed");
    Require(HostSelectionProtocol.TryDecode(encoded, out var decoded), "valid host selection packet was rejected");
    Require(decoded == expected, "host selection packet did not round-trip");
}
var invalidHostPacket = HostSelectionProtocol.Encode(HostSelectionMessageType.Hello);
invalidHostPacket[4]++;
Require(!HostSelectionProtocol.TryDecode(invalidHostPacket, out _), "mismatched host selector protocol version was accepted");
Require(
    !HostSelectionProtocol.TryDecode(invalidHostPacket[..^1], out _),
    "truncated host selector packet was accepted");
var signatureA = HostSelectionProtocol.ComputeMembershipSignature(new[]
{
    (3, "steam-c"),
    (1, "steam-a"),
    (2, "steam-b"),
});
var signatureB = HostSelectionProtocol.ComputeMembershipSignature(new[]
{
    (2, "steam-b"),
    (3, "steam-c"),
    (1, "steam-a"),
});
var signatureDifferent = HostSelectionProtocol.ComputeMembershipSignature(new[]
{
    (1, "steam-a"),
    (2, "steam-b"),
    (3, "steam-other"),
});
Require(signatureA == signatureB, "host selector membership signature depends on enumeration order");
Require(signatureA != signatureDifferent, "host selector membership signature ignored a changed identity");

Console.WriteLine("Gameplay fixes and synchronized host-selection protocol tests passed.");
