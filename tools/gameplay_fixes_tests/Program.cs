using SneakOut.ChairWallThrowFix;
using SneakOut.KeyboardLayoutFix;
using SneakOut.LockerStunFix;
using SneakOut.MagicWardrobeHookFix;
using SneakOut.NetworkHostSelector;
using SneakOut.PumpkinRadiusIndicatorFix;
using SneakOut.RipperCornerBlinkFix;
using SneakOut.UnlockEverything;

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

Require(
    AvatarSelectionPolicy.PreserveOwnedProductId(731, 100_004) == 731,
    "avatar overlay replaced a real backend product id");
Require(
    AvatarSelectionPolicy.PreserveOwnedProductId(0, 100_004) == 100_004,
    "avatar overlay did not assign an id to a synthetic product");
Require(
    AvatarSelectionPolicy.GetTitleDisplayText("TITLE_CHAIR_DESTROYER", "chair_destroyer") == "Chair Destroyer",
    "missing title translation was not humanized");
Require(
    AvatarSelectionPolicy.GetTitleDisplayText("Developer", "developer") == "Developer",
    "existing title translation was replaced");
Require(
    AvatarSelectionPolicy.GetTitleDisplayText("TITLE_ARISTOCRATE", "aristocrate") == "Aristocrat",
    "known misspelled title enum leaked into the UI");
Require(
    AvatarSelectionPolicy.GetTitleDisplayText("TITLE_CHAIR_DESTROYER", "TITLE_CHAIR_DESTROYER") == "Chair Destroyer",
    "title localization key fallback still depended on the boxed IL2CPP enum");
Require(LocalSkinEconomy.DisplayedGold(5_000, 3) == 2_000, "local skin ledger did not reduce displayed Gold");
Require(LocalSkinEconomy.DisplayedGold(500, 1) == 0, "local skin ledger allowed a negative displayed balance");
Require(LocalSkinEconomy.DisplayedGold(5_000, 0) == 5_000, "empty local skin ledger changed server Gold");
Require(LocalSkinEconomy.DisplayedGold(int.MaxValue, int.MaxValue) == 0, "local skin ledger overflowed");
Require(LocalSkinEconomy.CanPurchase(1_000), "exactly 1000 Gold could not buy a skin part");
Require(!LocalSkinEconomy.CanPurchase(999), "skin purchase accepted less than 1000 Gold");
var initialGoldOverlay = LocalSkinEconomy.ResolveOverlay(1_322, 0, null, 0);
Require(initialGoldOverlay.DisplayedGold == 1_322, "empty local ledger changed initial server Gold");
var stockDebitedGoldOverlay = LocalSkinEconomy.ResolveOverlay(
    322,
    1,
    initialGoldOverlay.DisplayedGold,
    initialGoldOverlay.ChargedPurchaseCount);
Require(
    stockDebitedGoldOverlay.DisplayedGold == 322,
    "stock shop continuation was charged a second time by the local ledger");
var earlyRefreshGoldOverlay = LocalSkinEconomy.ResolveOverlay(
    1_322,
    1,
    initialGoldOverlay.DisplayedGold,
    initialGoldOverlay.ChargedPurchaseCount);
Require(
    earlyRefreshGoldOverlay.DisplayedGold == 322,
    "local ledger did not charge a refresh that preceded the stock continuation");
var serverRefreshGoldOverlay = LocalSkinEconomy.ResolveOverlay(
    1_322,
    1,
    stockDebitedGoldOverlay.DisplayedGold,
    stockDebitedGoldOverlay.ChargedPurchaseCount);
Require(
    serverRefreshGoldOverlay.DisplayedGold == 322,
    "authoritative server refresh did not preserve the local-only purchase debit");
var completeSkinCatalog = SkinPartCatalogPolicy.AllConcreteEnumValues(TestSkinPart.None);
Require(
    completeSkinCatalog.SequenceEqual(new[]
    {
        TestSkinPart.VisibleHead,
        TestSkinPart.HiddenWhole,
        TestSkinPart.RenderableBack,
        TestSkinPart.EnumOnlyWithoutAsset
    }),
    "skin product catalog omitted a hidden enum item without a client-side asset");
Require(
    SkinPartCatalogPolicy.IsLocallyPurchasable(TestSkinPart.EnumOnlyWithoutAsset, TestSkinPart.None),
    "hidden skin part was rejected by the local wardrobe purchase policy");
Require(
    !SkinPartCatalogPolicy.IsLocallyPurchasable(TestSkinPart.None, TestSkinPart.None),
    "empty skin sentinel was exposed as a purchasable product");

var releaseCandidates = ChairReleasePolicy.CandidateDistances(0.25f).ToArray();
Require(releaseCandidates.Length == 3, "chair release search did not include its bounded final candidate");
RequireClose(releaseCandidates[0], 0.1f, "chair release first step changed");
RequireClose(releaseCandidates[1], 0.2f, "chair release second step changed");
RequireClose(releaseCandidates[2], 0.25f, "chair release maximum was exceeded");
Require(!ChairReleasePolicy.CandidateDistances(float.NaN).Any(), "chair release accepted a non-finite maximum");
RequireClose(
    ChairReleasePolicy.SafeCenterDistance(1.25f, 0.2f, 0.03f),
    1.02f,
    "chair sweep did not stop before the first wall contact");
RequireClose(
    ChairReleasePolicy.SafeCenterDistance(0.01f, 0.2f, 0.03f),
    0f,
    "chair sweep moved through a wall closer than its clearance");
RequireClose(
    ChairReleasePolicy.SafeCenterDistance(float.NaN, 0.2f, 0.03f),
    0f,
    "chair sweep accepted a non-finite hit distance");
RequireClose(
    ChairReleasePolicy.PlayerSideCenterDistance(0.08f, 0.476811f, 0.03f),
    -0.426811f,
    "chair that could not fit between player and wall was not moved behind the player");
RequireClose(
    ChairReleasePolicy.PlayerSideCenterDistance(float.NaN, 0.2f, 0.03f),
    0f,
    "signed chair correction accepted a non-finite wall hit");
Require(
    !ChairReleasePolicy.ShouldMoveTowardPlayer(0.08f, 0.487456f, false),
    "chair correction moved toward a player who was closer to the wall");
Require(
    ChairReleasePolicy.ShouldMoveTowardPlayer(0.48f, 0.04f, false),
    "chair correction moved away from a player when the chair was closer to the wall");
Require(
    ChairReleasePolicy.ShouldMoveTowardPlayer(0.08f, 0.42f, true),
    "chair already beyond an intervening wall was pushed farther through it");
Require(
    ChairReleasePolicy.ShouldMoveTowardPlayer(float.NaN, 0.42f, false),
    "invalid obstacle distance did not use the conservative toward-player fallback");
Require(
    ChairReleasePolicy.ShouldOverrideBlockedRelease(true, true, 7, 7, false, true),
    "held chair remained blocked by the forward detector on release");
Require(
    !ChairReleasePolicy.ShouldOverrideBlockedRelease(false, true, 7, 7, false, true),
    "valid stock throw was unnecessarily overridden");
Require(
    !ChairReleasePolicy.ShouldOverrideBlockedRelease(true, false, 7, 7, false, true),
    "non-release input was converted into a throw");
Require(
    !ChairReleasePolicy.ShouldOverrideBlockedRelease(true, true, 8, 7, false, true),
    "another player's held item was converted into a local throw");
Require(
    !ChairReleasePolicy.ShouldOverrideBlockedRelease(true, true, 7, 7, true, true),
    "possessed chair action was replaced with a throw");
Require(
    !ChairReleasePolicy.ShouldOverrideBlockedRelease(true, true, 7, 7, false, false),
    "unrelated stock None result was treated as a forward-detector block");

var lockerPolicy = new LockerBooPolicy<int>();
Require(
    lockerPolicy.ObserveOpen(1, 10, 10, false, false, "Open") == LockerOpenObservation.IgnoredOccupant,
    "a penguin opening their own locker was misclassified as an external opener");
Require(
    lockerPolicy.ConsumeForExit(1, 10, out _) == LockerBooDecision.AllowVanillaNoExternalOpen,
    "a self-opened locker suppressed vanilla Boo");
Require(
    lockerPolicy.ObserveOpen(2, 20, 10, false, false, "Open") == LockerOpenObservation.RecordedExternalOpener,
    "a hunter opening an occupied locker was not recorded");
Require(
    lockerPolicy.ConsumeForExit(2, 10, out var forcedOpen) == LockerBooDecision.SuppressExternalOpen
    && forcedOpen.OpenerPlayerId == 20 && forcedOpen.OccupantPlayerId == 10,
    "a hunter-opened locker still allowed Boo");
Require(
    lockerPolicy.ConsumeForExit(2, 10, out _) == LockerBooDecision.AllowVanillaNoExternalOpen,
    "one hunter open suppressed more than one exit");
Require(
    lockerPolicy.ObserveOpen(3, 20, 10, true, false, "Open") == LockerOpenObservation.IgnoredUnavailable,
    "an already-open locker created a stale external-open marker");
Require(
    lockerPolicy.ObserveOpen(4, 20, 10, false, true, "Open") == LockerOpenObservation.IgnoredUnavailable,
    "a rejected in-progress open created a false marker");
lockerPolicy.ObserveOpen(5, 20, 10, false, false, "TryToOpen");
Require(
    lockerPolicy.ConsumeForExit(5, 11, out _) == LockerBooDecision.AllowVanillaDifferentOccupant,
    "an external-open marker suppressed a different occupant");
Require(lockerPolicy.Clear(5), "hide/close boundary did not clear the locker cycle");

var russianForward = NativeMovementPolicy.Resolve(true, true, false, false, false, false);
Require(
    russianForward.ShouldOverride && russianForward.OwnsMovement && russianForward.Vertical == 1f,
    "Russian physical W did not produce forward movement");
var russianRelease = NativeMovementPolicy.Resolve(true, false, false, false, false, true);
Require(
    russianRelease.ShouldOverride && !russianRelease.OwnsMovement
    && russianRelease.Horizontal == 0f && russianRelease.Vertical == 0f,
    "Russian physical movement release did not emit an explicit zero");
Require(
    !NativeMovementPolicy.Resolve(false, true, false, false, false, true).ShouldOverride,
    "English layout movement was overridden");

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
Require(
    PumpkinIndicatorScalePolicy.TryResolveRadii(3f, 2f, out var pumpkinRadii),
    "pumpkin radii rejected valid live settings");
RequireClose(pumpkinRadii.Trigger, 3f, "pumpkin trigger radius diverged from the native query range");
RequireClose(pumpkinRadii.Kill, 3f, "pumpkin kill radius diverged from the native instant-kill range");
RequireClose(pumpkinRadii.Stun, 5f, "pumpkin stun radius did not include the outer stun extension");
RequireClose(PumpkinIndicatorScalePolicy.StunIndicatorOpacity, 0.2f, "pumpkin stun indicator opacity changed");
Require(
    !PumpkinIndicatorScalePolicy.TryResolveRadii(3f, -1f, out _),
    "pumpkin radii accepted a negative stun extension");

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
    SharedCornerPolicy.ShouldBypass(
        true,
        5f,
        intersectionLayer,
        new[]
        {
            new PathBlocker(2f, intersectionLayer),
            new PathBlocker(3f, 20)
        }),
    "an ordinary collider made the perk stricter at a confirmed room junction");
Require(
    !SharedCornerPolicy.ShouldBypass(true, 5f, intersectionLayer, Array.Empty<PathBlocker>()),
    "a clear path incorrectly took the custom RPC path");

var expectedRequest = new HostSelectionRequest("AABBCCDD", 17, 4, "steam-d");
var encodedRequest = HostSelectionProtocol.CreateRequest(
    expectedRequest.Membership,
    expectedRequest.Sequence,
    expectedRequest.TargetPlayerRaw,
    expectedRequest.TargetUserId);
Require(
    HostSelectionProtocol.TryParseRequest(encodedRequest, out var decodedRequest),
    "valid host selection request was rejected");
Require(decodedRequest == expectedRequest, "host selection request did not round-trip");
Require(
    !HostSelectionProtocol.TryParseRequest("999|AABBCCDD|17|4|steam-d", out _),
    "mismatched host selector protocol version was accepted");
Require(
    !HostSelectionProtocol.TryParseRequest("2|AABBCCDD|17|4", out _),
    "truncated host selector request was accepted");
Require(
    !HostSelectionProtocol.TryParseRequest("2|AABBCCDD|17|0|steam-d", out _),
    "automatic host request accepted a non-empty user id");
Require(
    HostSelectionProtocol.CreateHello("AABBCCDD", "steam-a") == "2|AABBCCDD|steam-a",
    "host selector hello token changed unexpectedly");
Require(
    HostSelectionProtocol.CreateAck(7, "AABBCCDD", 4, "steam-d")
        == "2|7|AABBCCDD|4|steam-d",
    "host selector acknowledgement token changed unexpectedly");
var expectedState = new HostSelectionState(7, 4, "steam-d", "AABBCCDD", true, false);
var encodedState = HostSelectionProtocol.CreateState(
    expectedState.Revision,
    expectedState.TargetPlayerRaw,
    expectedState.TargetUserId,
    expectedState.Membership,
    expectedState.Compatible,
    expectedState.Ready);
Require(
    HostSelectionProtocol.TryParseState(encodedState, out var decodedState),
    "valid compact host-selection state was rejected");
Require(decodedState == expectedState, "compact host-selection state did not round-trip");
Require(
    !HostSelectionProtocol.TryParseState("2|7|4||AABBCCDD|1|0", out _),
    "selected host state accepted an empty user id");
var peerRegistry = HostSelectionProtocol.UpsertPeer(string.Empty, 2, "steam-b", "AABBCCDD", -1);
peerRegistry = HostSelectionProtocol.UpsertPeer(peerRegistry, 4, "steam-d", "AABBCCDD", 7);
Require(
    HostSelectionProtocol.TryGetPeer(peerRegistry, 2, out var peerTwo)
    && peerTwo == new HostSelectionPeer(2, "steam-b", "AABBCCDD", -1),
    "compact peer registry lost the first participant");
Require(
    HostSelectionProtocol.TryGetPeer(peerRegistry, 4, out var peerFour)
    && peerFour == new HostSelectionPeer(4, "steam-d", "AABBCCDD", 7),
    "compact peer registry did not preserve acknowledgement state");
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

internal enum TestSkinPart
{
    None,
    VisibleHead,
    HiddenWhole,
    RenderableBack,
    EnumOnlyWithoutAsset
}
