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
var missingTitleEntries = TitleLocalizationPolicy.MissingEntries.ToDictionary(entry => entry.Key, entry => entry.Value);
Require(missingTitleEntries.Count == 10, "missing title localization catalog changed unexpectedly");
Require(missingTitleEntries["TITLE_MAGNATE"] == "Magnate", "Magnate localization is missing");
Require(missingTitleEntries["TITLE_CHAIR_DESTROYER"] == "Chair Destroyer", "Chair Destroyer localization is missing");
Require(missingTitleEntries["TITLE_ARISTOCRATE"] == "Aristocrat", "Aristocrat localization preserved the game's typo");
Require(!missingTitleEntries.ContainsKey("TITLE_DEVELOPER"), "existing title localization would be overwritten");
Require(!missingTitleEntries.ContainsKey("TITLE_COMMUNITY_LEADER"), "existing Community Leader localization would be overwritten");
Require(!TitleAccessPolicy.ShouldShowInMenu(2, 4, false), "rarity 4 remained visible after a regular tab click");
Require(TitleAccessPolicy.ShouldShowInMenu(2, 4, true), "Shift-click did not reveal rarity 4");
Require(TitleAccessPolicy.ShouldShowInMenu(5, 3, false), "a regular title was hidden");
Require(!TitleAccessPolicy.ShouldShowInMenu(0, 0, true), "the empty title sentinel remained visible");
Require(!TitleAccessPolicy.ShouldShowInMenu(19, 0, true), "an unsupported rank title remained visible");
Require(
    PersistentSelectionPolicy.IsLegacyEmptyAppearance(0, 0, 0, 0, 0, 0, 0),
    "legacy all-None appearance snapshot was not recognized");
Require(
    !PersistentSelectionPolicy.IsLegacyEmptyAppearance(0, 0, 0, 17, 0, 0, 0),
    "valid persisted outfit was mistaken for a legacy empty snapshot");
Require(
    !PersistentSelectionPolicy.IsLegacyEmptyAppearance(null, null, null, null, null, null, null),
    "unset appearance fields were mistaken for a legacy empty snapshot");
Require(
    !PersistentSelectionPolicy.HasSkinPartSelection(null, null, null, null, null, null),
    "unset skin-part persistence would overwrite the server outfit");
Require(
    PersistentSelectionPolicy.HasSkinPartSelection(null, 12, null, null, null, null),
    "explicit skin-part persistence was ignored");
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

Require(
    LockerStunZonePolicy.IsWithinOpeningDistance(
        new LockerStunZonePoint(1f, 0f, 0f),
        new LockerStunZonePoint(0f, 0f, 0f)),
    "the opening-distance boundary was excluded");
Require(
    LockerStunZonePolicy.IsWithinStunDistance(
        new LockerStunZonePoint(1f, 0f, 0f),
        new LockerStunZonePoint(0f, 0f, 0f)),
    "a valid opening position was excluded from the balanced Boo zone");
Require(
    LockerStunZonePolicy.IsWithinStunDistance(
        new LockerStunZonePoint(-1.2f, 0f, 0f),
        new LockerStunZonePoint(0f, 0f, 0f)),
    "the balanced Boo margin was not symmetric beside the locker");
Require(
    !LockerStunZonePolicy.IsWithinStunDistance(
        new LockerStunZonePoint(1.201f, 0f, 0f),
        new LockerStunZonePoint(0f, 0f, 0f)),
    "the balanced Boo zone exceeded its 1.2 metre closest-point distance");
Require(
    LockerStunZonePolicy.IsWithinStunDistance(
        new LockerStunZonePoint(0.6f, 0f, 0.6f),
        new LockerStunZonePoint(0f, 0f, 0f)),
    "the rounded corner used a square distance test");
Require(
    LockerStunZonePolicy.TryResolveBroadPhaseRadius(
        new LockerStunZonePoint(3f, 4f, 0f),
        out var lockerBroadPhaseRadius),
    "finite locker bounds did not produce a broad-phase radius");
RequireClose(lockerBroadPhaseRadius, 6.2f, "locker broad phase could clip the balanced zone");
RequireClose(
    LockerStunZonePolicy.StunDistance - LockerStunZonePolicy.OpeningDistance,
    0.2f,
    "balanced Boo margin changed");
Require(
    !LockerStunZonePolicy.TryResolveBroadPhaseRadius(
        new LockerStunZonePoint(float.NaN, 0f, 0f),
        out _),
    "locker broad phase accepted non-finite bounds");

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

Require(
    LeaderHostPolicy.TryResolve(
        new[] { (2, "steam-b"), (4, "steam-d") },
        4,
        "pgos-leader-d",
        out var leaderTarget),
    "party creator was not resolved as the automatic match host");
Require(
    leaderTarget == new LeaderHostTarget(4, "pgos-leader-d"),
    "automatic Leader Host target did not use the exact party-leader identity");
Require(
    !LeaderHostPolicy.TryResolve(new[] { (4, "steam-d") }, 4, string.Empty, out _),
    "Leader Host accepted an empty party-leader identity");
Require(
    !LeaderHostPolicy.ShouldOverrideAssignedHost(
        privateGame: false,
        assignedHostId: "public-host",
        leaderHostId: "party-creator"),
    "Leader Host replaced the backend-assigned host for public matchmaking");
Require(
    LeaderHostPolicy.ShouldOverrideAssignedHost(
        privateGame: true,
        assignedHostId: "private-peer",
        leaderHostId: "party-creator"),
    "Leader Host did not select the party creator for a private match");
Require(
    !LeaderHostPolicy.ShouldOverrideAssignedHost(
        privateGame: true,
        assignedHostId: "party-creator",
        leaderHostId: "party-creator"),
    "Leader Host rewrote an already-correct private match host");
Require(
    LeaderHostHudText.Compose("Lobby", "Party Creator") == "Lobby   Host: Party Creator",
    "Leader Host did not append the creator to the stock map value");
Require(
    LeaderHostHudText.Compose("Lobby   Host: Old Host", "New Host") == "Lobby   Host: New Host",
    "Leader Host duplicated its HUD suffix during repeated stock refreshes");
Require(
    LeaderHostHudText.Compose("Lobby   Host: Old Host", string.Empty) == "Lobby",
    "Leader Host did not restore the stock map value when the host became unavailable");
Require(
    LeaderHostHudText.Compose("Lobby", "Line One\nLine Two") == "Lobby   Host: Line One Line Two",
    "Leader Host allowed a player name to break the bottom HUD strip");
Require(
    HostSelectionProtocol.CreateHello(
        "AABBCCDD",
        "steam-a",
        HostSelectionProtocol.UniformSeekerRandomCapability)
        == "4|AABBCCDD|steam-a|1",
    "Leader Host hello token changed unexpectedly");
Require(
    HostSelectionProtocol.CreateAck(
        7,
        "AABBCCDD",
        4,
        "steam-d",
        HostSelectionProtocol.UniformSeekerRandomCapability)
        == "4|7|AABBCCDD|4|steam-d|1",
    "Leader Host acknowledgement token changed unexpectedly");
var expectedState = new HostSelectionState(
    7,
    4,
    "steam-d",
    "AABBCCDD",
    HostSelectionProtocol.UniformSeekerRandomCapability,
    true,
    false);
var encodedState = HostSelectionProtocol.CreateState(
    expectedState.Revision,
    expectedState.TargetPlayerRaw,
    expectedState.TargetUserId,
    expectedState.Membership,
    expectedState.CommonCapabilities,
    expectedState.Compatible,
    expectedState.Ready);
Require(
    HostSelectionProtocol.TryParseState(encodedState, out var decodedState),
    "valid compact host-selection state was rejected");
Require(decodedState == expectedState, "compact host-selection state did not round-trip");
Require(
    !HostSelectionProtocol.TryParseState("4|7|4||AABBCCDD|1|1|0", out _),
    "selected host state accepted an empty user id");
var peerRegistry = HostSelectionProtocol.UpsertPeer(
    string.Empty,
    2,
    "steam-b",
    "AABBCCDD",
    HostSelectionProtocol.UniformSeekerRandomCapability,
    -1);
peerRegistry = HostSelectionProtocol.UpsertPeer(
    peerRegistry,
    4,
    "steam-d",
    "AABBCCDD",
    0,
    7);
Require(
    HostSelectionProtocol.TryGetPeer(peerRegistry, 2, out var peerTwo)
    && peerTwo == new HostSelectionPeer(
        2,
        "steam-b",
        "AABBCCDD",
        HostSelectionProtocol.UniformSeekerRandomCapability,
        -1),
    "compact peer registry lost the first participant");
Require(
    HostSelectionProtocol.TryGetPeer(peerRegistry, 4, out var peerFour)
    && peerFour == new HostSelectionPeer(4, "steam-d", "AABBCCDD", 0, 7),
    "compact peer registry did not preserve acknowledgement state");
Require(
    !HostSelectionProtocol.TryGetPeer("3,4,c3RlYW0tZA==,AABBCCDD,7", 4, out _),
    "Leader Host accepted a peer running the manual-selector protocol");
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
Require(signatureA == signatureB, "Leader Host membership signature depends on enumeration order");
Require(signatureA != signatureDifferent, "Leader Host membership signature ignored a changed identity");
var observedParticipantSnapshot = LeaderHostParticipantPolicy.CreateSnapshot(new[]
{
    new LeaderHostParticipant(4, "steam-d", "D", true, false),
    new LeaderHostParticipant(2, "steam-b", "B", true, false),
    new LeaderHostParticipant(4, "duplicate-d", "Duplicate D", true, false),
    new LeaderHostParticipant(6, "bot", "Test Bot", true, true),
    new LeaderHostParticipant(0, "none", "None", false, false),
});
Require(
    observedParticipantSnapshot.Select(participant => participant.Raw).SequenceEqual(new[] { 2, 4 }),
    "Leader Host participant snapshot did not deterministically filter bots, invalid refs, or duplicates");
Require(
    LeaderHostParticipantPolicy.IsComplete(2, observedParticipantSnapshot.Count),
    "Leader Host rejected a complete spawned-player snapshot");
Require(
    !LeaderHostParticipantPolicy.IsComplete(3, observedParticipantSnapshot.Count),
    "Leader Host accepted an incomplete snapshot while a peer was still joining");
Require(
    !LeaderHostParticipantPolicy.IsComplete(0, 0),
    "Leader Host treated an uninitialized Fusion session as a complete lobby");

var repositoryRoot = new DirectoryInfo(AppContext.BaseDirectory);
while (repositoryRoot is not null
       && !File.Exists(Path.Combine(repositoryRoot.FullName, "runtime_mods_manifest.json")))
{
    repositoryRoot = repositoryRoot.Parent;
}
Require(repositoryRoot is not null, "gameplay tests could not locate the repository root");
var communityDiscordRuntimeSource = File.ReadAllText(Path.Combine(
    repositoryRoot!.FullName,
    "mods",
    "community_discord",
    "CommunityDiscordRuntime.cs"));
Require(
    communityDiscordRuntimeSource.Contains("stockStatue._redirectURL = inviteUrl", StringComparison.Ordinal)
    && !communityDiscordRuntimeSource.Contains("Object.Instantiate", StringComparison.Ordinal)
    && !communityDiscordRuntimeSource.Contains("CommunityDiscordStatue", StringComparison.Ordinal),
    "Community Discord must replace the stock statue URL without creating another statue");
var unlockEverythingCosmeticPatchesSource = File.ReadAllText(Path.Combine(
    repositoryRoot.FullName,
    "mods",
    "unlock_everything",
    "UnlockEverythingCosmeticPatches.cs"));
var unlockEverythingProfilePatchesSource = File.ReadAllText(Path.Combine(
    repositoryRoot.FullName,
    "mods",
    "unlock_everything",
    "UnlockEverythingProfilePatches.cs"));
var unlockEverythingSkillPatchesSource = File.ReadAllText(Path.Combine(
    repositoryRoot.FullName,
    "mods",
    "unlock_everything",
    "UnlockEverythingSkillPatches.cs"));
var unlockEverythingSkillSelectionsSource = File.ReadAllText(Path.Combine(
    repositoryRoot.FullName,
    "mods",
    "unlock_everything",
    "UnlockEverythingSkillSelections.cs"));
var unlockEverythingStubSource = File.ReadAllText(Path.Combine(
    repositoryRoot.FullName,
    "mods",
    "unlock_everything",
    "UnlockEverythingStub.cs"));
var unlockEverythingDirectory = Path.Combine(repositoryRoot.FullName, "mods", "unlock_everything");
var unlockEverythingSources = string.Join(
    "\n",
    Directory.GetFiles(unlockEverythingDirectory, "*.cs")
        .OrderBy(path => path, StringComparer.Ordinal)
        .Select(File.ReadAllText));
var mummyUnlockPatchesSource = File.ReadAllText(Path.Combine(
    repositoryRoot.FullName,
    "mods",
    "mummy_unlock",
    "MummyUnlockPatches.cs"));
var mummyAbilityIconRuntimeSource = File.ReadAllText(Path.Combine(
    repositoryRoot.FullName,
    "mods",
    "mummy_unlock",
    "MummyAbilityIconRuntime.cs"));
var mummyPerkShopRuntimeSource = File.ReadAllText(Path.Combine(
    repositoryRoot.FullName,
    "mods",
    "mummy_unlock",
    "MummyPerkShopRuntime.cs"));
var mummyPerkRuntimeSource = File.ReadAllText(Path.Combine(
    repositoryRoot.FullName,
    "mods",
    "mummy_unlock",
    "MummyPerkRuntime.cs"));
var mummyPerkStoreSource = File.ReadAllText(Path.Combine(
    repositoryRoot.FullName,
    "mods",
    "mummy_unlock",
    "MummyPerkStore.cs"));
var mummyPlayerListIconPatchesSource = File.ReadAllText(Path.Combine(
    repositoryRoot.FullName,
    "mods",
    "mummy_unlock",
    "MummyPlayerListIconPatches.cs"));
var mummySarcophagusTeleportRuntimeSource = File.ReadAllText(Path.Combine(
    repositoryRoot.FullName,
    "mods",
    "mummy_unlock",
    "MummySarcophagusTeleportRuntime.cs"));
var mummySarcophagusTeleportPatchesSource = File.ReadAllText(Path.Combine(
    repositoryRoot.FullName,
    "mods",
    "mummy_unlock",
    "MummySarcophagusTeleportPatches.cs"));
var localSelectionsStoreSource = File.ReadAllText(Path.Combine(
    repositoryRoot.FullName,
    "mods",
    "unlock_everything",
    "LocalSelectionsStore.cs"));
var mummySkillsRegistrySource = File.ReadAllText(Path.Combine(
    repositoryRoot.FullName,
    "mods",
    "mummy_unlock",
    "MummySkillsRegistry.cs"));
var titleLocalizationPatchesSource = File.ReadAllText(Path.Combine(
    repositoryRoot.FullName,
    "mods",
    "unlock_everything",
    "TitleLocalizationPatches.cs"));
Require(
    titleLocalizationPatchesSource.Contains(
        "HarmonyPatch(typeof(GameTranslator), nameof(GameTranslator.ReloadDictionary))",
        StringComparison.Ordinal)
    && titleLocalizationPatchesSource.Contains(
        "dictionary.Add(entry.Key, entry.Value)",
        StringComparison.Ordinal)
    && !unlockEverythingCosmeticPatchesSource.Contains(
        "AvatarTitleDisplay",
        StringComparison.Ordinal)
    && !unlockEverythingCosmeticPatchesSource.Contains(
        "GetTitleDisplayText",
        StringComparison.Ordinal)
    && !unlockEverythingCosmeticPatchesSource.Contains(
        "HarmonyPatch(typeof(AvatarAndFrameView), \"OnAvatarPicked\")",
        StringComparison.Ordinal),
    "title labels must come from the localization dictionary without UI rewriting or the unsafe IL2CPP callback");
Require(
    unlockEverythingCosmeticPatchesSource.Contains(
        "TitleAccessPolicy.ShouldShowInMenu",
        StringComparison.Ordinal)
    && unlockEverythingCosmeticPatchesSource.Contains(
        "HarmonyPatch(typeof(AvatarAndFrameView), \"OnTitlesCategory\")",
        StringComparison.Ordinal)
    && unlockEverythingCosmeticPatchesSource.Contains(
        "Keyboard.current",
        StringComparison.Ordinal)
    && unlockEverythingCosmeticPatchesSource.Contains(
        "GetTitleRarity(descriptionType)",
        StringComparison.Ordinal)
    && unlockEverythingCosmeticPatchesSource.Contains(
        "button.gameObject.SetActive(shouldShow)",
        StringComparison.Ordinal),
    "the title menu must reveal rarity 4 only on Shift-click and remove unbound badge slots");
var clientConfirmedPrefixIndex = unlockEverythingProfilePatchesSource.IndexOf(
    "private static void Prefix(ClientCache __instance)",
    StringComparison.Ordinal);
var clientConfirmedOverlayIndex = unlockEverythingProfilePatchesSource.IndexOf(
    "UnlockEverythingOverlay.EnsureClientCache(__instance);",
    StringComparison.Ordinal);
var clientConfirmedPostfixIndex = unlockEverythingProfilePatchesSource.IndexOf(
    "private static void Postfix(ClientCache __instance)",
    StringComparison.Ordinal);
Require(
    clientConfirmedPrefixIndex >= 0
    && clientConfirmedOverlayIndex > clientConfirmedPrefixIndex
    && clientConfirmedPostfixIndex > clientConfirmedOverlayIndex,
    "the profile overlay must run before ClientCache.OnClientConfirmed notifies inventory subscribers");
Require(
    !unlockEverythingProfilePatchesSource.Contains(
        "HarmonyPatch(typeof(PlayerNewMetaInventory), nameof(PlayerNewMetaInventory.LoadOwnedSeekers))",
        StringComparison.Ordinal)
    && !unlockEverythingSources.Contains("Mummy", StringComparison.OrdinalIgnoreCase)
    && !unlockEverythingSources.Contains("OwnedSeekers", StringComparison.Ordinal)
    && !unlockEverythingStubSource.Contains("System.Enum.GetValues<CharacterType>()", StringComparison.Ordinal)
    && unlockEverythingStubSource.Contains(
        "new[] { CharacterType.Penguin, CharacterType.Reaper }",
        StringComparison.Ordinal)
    && unlockEverythingStubSource.Contains(
        "products.CharacterProducts = new Il2CppCollections.List<CharacterProduct>()",
        StringComparison.Ordinal),
    "Unlock Everything must preserve real hunter ownership and synthesize only the base fallback pair when no profile exists");
Require(
    mummyUnlockPatchesSource.Contains(
        "HarmonyPatch(typeof(PlayerNewMetaInventory), nameof(PlayerNewMetaInventory.LoadOwnedSeekers))",
        StringComparison.Ordinal)
    && mummyUnlockPatchesSource.Contains(
        "HarmonyPatch(typeof(SeekerSelectionViewModel), nameof(SeekerSelectionViewModel.Init))",
        StringComparison.Ordinal)
    && mummyUnlockPatchesSource.Contains(
        "HarmonyPatch(typeof(SeekerSelectionView), nameof(SeekerSelectionView.ManagerAwake))",
        StringComparison.Ordinal)
    && mummyPerkShopRuntimeSource.Contains("SkillTree = reaperPassiveTree.SkillTree", StringComparison.Ordinal)
    && mummyPerkShopRuntimeSource.Contains("emptyTree.Rows = Array.Empty<ListWrapper>()", StringComparison.Ordinal)
    && mummyPerkShopRuntimeSource.Contains("ApplyCarouselIcons", StringComparison.Ordinal)
    && mummyAbilityIconRuntimeSource.Contains("MummyCharacterIcon", StringComparison.Ordinal)
    && mummyUnlockPatchesSource.Contains(
        "HarmonyPatch(typeof(CharactersSkillsRuntime), \"GetSkillsForCharacterType\")",
        StringComparison.Ordinal)
    && mummyUnlockPatchesSource.Contains(
        "HarmonyPatch(typeof(CharactersSkillsRuntime), \"SaveSkillsForCharacterType\")",
        StringComparison.Ordinal)
    && mummyUnlockPatchesSource.Contains(
        "HarmonyPatch(typeof(MainBoostersView), nameof(MainBoostersView.GetDescriptionParams))",
        StringComparison.Ordinal)
    && mummyUnlockPatchesSource.Contains(
        "HarmonyPatch(typeof(SpookedSkillSettings), nameof(SpookedSkillSettings.GetTitle))",
        StringComparison.Ordinal)
    && mummyUnlockPatchesSource.Contains(
        "HarmonyPatch(typeof(SpookedSkillSettings), nameof(SpookedSkillSettings.GetDescriptionKey))",
        StringComparison.Ordinal)
    && mummyUnlockPatchesSource.Contains(
        "HarmonyPatch(typeof(SpookedSkillSettings), nameof(SpookedSkillSettings.GetAllModifiers))",
        StringComparison.Ordinal)
    && mummySkillsRegistrySource.Contains("GetDefinitionCharacter", StringComparison.Ordinal)
    && mummySkillsRegistrySource.Contains("return MummyPerkShopRuntime.ReaperCharacterType", StringComparison.Ordinal)
    && mummyUnlockPatchesSource.Contains("MainBoostersView._ShiftCharactersPanel_d__115", StringComparison.Ordinal)
    && !mummyPerkShopRuntimeSource.Contains("BeginEquippedSkillsAlias", StringComparison.Ordinal)
    && !mummyPerkShopRuntimeSource.Contains("ApplyEquippedPassiveSkills", StringComparison.Ordinal)
    && !mummyUnlockPatchesSource.Contains(
        "HarmonyPatch(typeof(MainBoostersView), nameof(MainBoostersView.SetSkillInfo))",
        StringComparison.Ordinal)
    && mummySkillsRegistrySource.Contains("TryGetSkills", StringComparison.Ordinal)
    && mummySkillsRegistrySource.Contains("TrySaveSkills", StringComparison.Ordinal)
    && mummySkillsRegistrySource.Contains("skills.ActiveSkill = default", StringComparison.Ordinal)
    && mummySkillsRegistrySource.Contains("skills.PassiveSkill4 = default", StringComparison.Ordinal)
    && mummyPerkStoreSource.Contains("chelokot.sneakout.mummy-unlock.json", StringComparison.Ordinal)
    && mummyPerkStoreSource.Contains("chelokot.sneakout.persistent-selections.json", StringComparison.Ordinal)
    && mummyPerkStoreSource.Contains("LegacyMummyCharacterKey = \"runtime:12\"", StringComparison.Ordinal)
    && mummyPerkRuntimeSource.Contains("GetSyntheticCard", StringComparison.Ordinal)
    && mummyPerkRuntimeSource.Contains("TryHaveSkillEquipped", StringComparison.Ordinal)
    && mummyPerkRuntimeSource.Contains("GetModifierDirectly", StringComparison.Ordinal)
    && mummyUnlockPatchesSource.Contains("HarmonyPriority(Priority.First)", StringComparison.Ordinal)
    && mummyUnlockPatchesSource.Contains(
        "HarmonyPatch(typeof(MainBoostersViewModel), \"TreeSkillEquipped\")",
        StringComparison.Ordinal)
    && !localSelectionsStoreSource.Contains("SaveRuntimeCharacterSkills", StringComparison.Ordinal)
    && !localSelectionsStoreSource.Contains("TryGetRuntimeCharacterSkills", StringComparison.Ordinal)
    && !unlockEverythingSkillSelectionsSource.Contains("Mummy", StringComparison.OrdinalIgnoreCase)
    && !unlockEverythingSkillPatchesSource.Contains("Mummy", StringComparison.OrdinalIgnoreCase),
    "Mummy Unlock must own the borrowed Reaper catalog, independent registry, selection persistence, and gameplay modifiers without Unlock Everything coupling");
Require(
    mummyPlayerListIconPatchesSource.Contains(
        "HarmonyPatch(typeof(PlayerInGameRecord), nameof(PlayerInGameRecord.Refresh))",
        StringComparison.Ordinal)
    && mummyPlayerListIconPatchesSource.Contains("MummyAbilityIconRuntime.ApplyToPlayerList", StringComparison.Ordinal)
    && mummySarcophagusTeleportRuntimeSource.Contains(
        "Types.CharacterAnimations.WardrobeHide",
        StringComparison.Ordinal)
    && mummySarcophagusTeleportRuntimeSource.Contains(
        "Types.CharacterAnimations.LockerStepOut",
        StringComparison.Ordinal)
    && mummySarcophagusTeleportRuntimeSource.Contains(
        "MurdererButcherAnimationController",
        StringComparison.Ordinal)
    && mummySarcophagusTeleportRuntimeSource.Contains(
        "characterMovement.SetInputDirection(Vector3.zero, true)",
        StringComparison.Ordinal)
    && mummySarcophagusTeleportRuntimeSource.Contains(
        "characterMovement.SetLookRotation(rotation, true, false)",
        StringComparison.Ordinal)
    && mummySarcophagusTeleportPatchesSource.Contains(
        "HarmonyPatch(typeof(EntityNetworkAnimatorComponent), nameof(EntityNetworkAnimatorComponent.FixedUpdateNetwork))",
        StringComparison.Ordinal)
    && mummySarcophagusTeleportPatchesSource.Contains(
        "HarmonyPatch(typeof(EntityNetworkAnimatorComponent), nameof(EntityNetworkAnimatorComponent.Render))",
        StringComparison.Ordinal),
    "Mummy presentation must borrow a wardrobe-capable controller and control sarcophagus motion every simulation and render frame");
var leaderHostRuntimeSource = File.ReadAllText(Path.Combine(
    repositoryRoot.FullName,
    "mods",
    "network_host_selector",
    "NetworkHostSelectorRuntime.cs"));
var leaderHostPatchesSource = File.ReadAllText(Path.Combine(
    repositoryRoot.FullName,
    "mods",
    "network_host_selector",
    "NetworkHostSelectorPatches.cs"));
var uniformSeekerRuntimeSource = File.ReadAllText(Path.Combine(
    repositoryRoot.FullName,
    "mods",
    "uniform_seeker_random",
    "UniformSeekerRandomRuntime.cs"));
var uniformSeekerPatchesSource = File.ReadAllText(Path.Combine(
    repositoryRoot.FullName,
    "mods",
    "uniform_seeker_random",
    "UniformSeekerRandomPatches.cs"));
Require(
    !leaderHostRuntimeSource.Contains("ActivePlayers", StringComparison.Ordinal)
    && !leaderHostRuntimeSource.Contains("Il2CppSystem.Collections.IEnumerator", StringComparison.Ordinal),
    "Leader Host reintroduced unsafe polling of Fusion's mutable IL2CPP player iterator");
Require(
    !leaderHostRuntimeSource.Contains("LeaderHostStatus", StringComparison.Ordinal)
    && !leaderHostRuntimeSource.Contains("Instantiate(view._playButton", StringComparison.Ordinal),
    "Leader Host reintroduced the cloned rounded portal button");
Require(
    !leaderHostPatchesSource.Contains("typeof(PortalPlayView), \"OnPlay\"", StringComparison.Ordinal)
    && !leaderHostRuntimeSource.Contains("AllowPortalPlay", StringComparison.Ordinal)
    && !leaderHostRuntimeSource.Contains("PLAY held", StringComparison.Ordinal),
    "Leader Host must fall back to stock matchmaking instead of swallowing PLAY");
Require(
    leaderHostRuntimeSource.Contains("gameState.PrivateGameCheckbox", StringComparison.Ordinal)
    && leaderHostRuntimeSource.Contains("ShouldOverrideAssignedHost", StringComparison.Ordinal),
    "Leader Host must preserve the backend-assigned host during public matchmaking");
Require(
    leaderHostRuntimeSource.Contains("UniformSeekerRandomCapability", StringComparison.Ordinal)
    && leaderHostRuntimeSource.Contains("peer.Capabilities", StringComparison.Ordinal)
    && leaderHostRuntimeSource.Contains("commonCapabilities &=", StringComparison.Ordinal),
    "Leader Host must publish Uniform Seeker Random only when every current peer advertises it");
Require(
    uniformSeekerRuntimeSource.Contains("stateMachine.HasStateAuthority", StringComparison.Ordinal)
    && uniformSeekerRuntimeSource.Contains("_launchQuorumReady", StringComparison.Ordinal)
    && uniformSeekerRuntimeSource.Contains("SELECTION final action=STOCK reason=not-state-authority", StringComparison.Ordinal),
    "Uniform Seeker Random must never override selection on a client or without launch quorum");
Require(
    uniformSeekerRuntimeSource.Contains("stateMachine.SeekerChosenRefId", StringComparison.Ordinal)
    && uniformSeekerRuntimeSource.Contains("transport=stock-seeker-replication", StringComparison.Ordinal)
    && !uniformSeekerRuntimeSource.Contains("UpdateCustomProperties", StringComparison.Ordinal),
    "Uniform Seeker Random must observe the stock replicated result instead of publishing a second result protocol");
Require(
    uniformSeekerPatchesSource.Contains("nameof(PhotonLobby.JoinMatchSession)", StringComparison.Ordinal)
    && uniformSeekerPatchesSource.Contains("HarmonyPriority(Priority.Last)", StringComparison.Ordinal)
    && uniformSeekerPatchesSource.Contains("nameof(MatchStateMachine.FixedUpdateNetwork)", StringComparison.Ordinal),
    "Uniform Seeker Random must capture the finalized launch host and observe replicated match state");

Console.WriteLine("Gameplay fixes, Community Discord, Leader Host, and authoritative seeker tests passed.");

internal enum TestSkinPart
{
    None,
    VisibleHead,
    HiddenWhole,
    RenderableBack,
    EnumOnlyWithoutAsset
}
