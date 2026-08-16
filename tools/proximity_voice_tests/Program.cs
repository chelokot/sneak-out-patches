using SneakOut.ProximityVoiceChat;

var playerVolumes = VoicePlayerVolumePolicy.Parse(
    "76561198000000002=0.5;invalid;76561198000000001:1.75;76561198000000003=99");
Require(
    playerVolumes.Count == 3
    && Math.Abs(playerVolumes[76561198000000001] - 1.75f) < 0.001f
    && Math.Abs(playerVolumes[76561198000000002] - 0.5f) < 0.001f
    && playerVolumes[76561198000000003] == VoicePlayerVolumePolicy.MaximumVolume,
    "per-player voice volumes were not parsed and clamped");
Require(
    VoicePlayerVolumePolicy.Serialize(playerVolumes)
        == "76561198000000001=1.75,76561198000000002=0.5,76561198000000003=2",
    "per-player voice volumes were not serialized deterministically");

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static VoicePacket Packet(
    uint sequence,
    ushort fragmentIndex,
    ushort fragmentCount,
    params byte[] payload)
{
    return new VoicePacket(
        VoicePacketKind.Audio,
        0x1020304050607080,
        76561198250587060,
        0x9988776655443322,
        17,
        sequence,
        123456,
        fragmentIndex,
        fragmentCount,
        payload);
}

var admission = new VoicePeerAdmission();
const ulong peerSteamId = 76561198000000001;
admission.Allow(peerSteamId);
Require(!admission.IsAccepted(peerSteamId), "allowing a voice peer prematurely marked its Steam session accepted");
Require(admission.CanAcceptRequest(peerSteamId, currentlyAllowed: true), "real Steam session request was rejected after peer discovery");
admission.MarkAccepted(peerSteamId);
Require(admission.IsAccepted(peerSteamId), "successful Steam session request was not recorded");
admission.MarkDisconnected(peerSteamId);
Require(!admission.IsAccepted(peerSteamId), "failed Steam session remained accepted");
Require(admission.CanAcceptRequest(peerSteamId, currentlyAllowed: true), "failed Steam session could not retry");
Require(
    VoiceTransportSendPolicy.CanInitiateConnection(VoiceTransportSendMode.Bootstrap),
    "voice bootstrap mode cannot initiate a fresh Steam P2P connection");
Require(
    VoiceTransportSendPolicy.CanInitiateConnection(VoiceTransportSendMode.Reliable),
    "reliable voice control mode cannot initiate a fresh Steam P2P connection");
Require(
    !VoiceTransportSendPolicy.CanInitiateConnection(VoiceTransportSendMode.Realtime),
    "realtime voice mode may buffer audio while establishing a connection");
Require(
    VoiceTransportSendPolicy.ForControlPacket(VoicePacketKind.Hello) == VoiceTransportSendMode.Bootstrap,
    "Hello regressed to a transport mode that cannot bootstrap Steam P2P");
Require(
    VoiceTransportSendPolicy.ForControlPacket(VoicePacketKind.Goodbye) == VoiceTransportSendMode.Reliable,
    "Goodbye is no longer delivered reliably");

var encodedPacket = Packet(42, 0, 1, 1, 2, 3, 4);
var encoded = VoiceProtocol.Encode(encodedPacket);
Require(VoiceProtocol.TryDecode(encoded, out var decoded), "valid voice packet did not decode");
Require(
    decoded.Kind == encodedPacket.Kind
    && decoded.SessionHash == encodedPacket.SessionHash
    && decoded.SenderSteamId == encodedPacket.SenderSteamId
    && decoded.SenderInstanceId == encodedPacket.SenderInstanceId
    && decoded.SenderInternalId == encodedPacket.SenderInternalId
    && decoded.Sequence == encodedPacket.Sequence
    && decoded.CaptureTimestampMilliseconds == encodedPacket.CaptureTimestampMilliseconds
    && decoded.FragmentIndex == encodedPacket.FragmentIndex
    && decoded.FragmentCount == encodedPacket.FragmentCount
    && decoded.Payload.SequenceEqual(encodedPacket.Payload),
    "voice packet did not round-trip exactly");

var reservedBytePacket = encoded.ToArray();
reservedBytePacket[7] = 1;
Require(!VoiceProtocol.TryDecode(reservedBytePacket, out _), "reserved protocol byte was accepted");

var incompatibleCodecPacket = encoded.ToArray();
incompatibleCodecPacket[6] = 0;
Require(!VoiceProtocol.TryDecode(incompatibleCodecPacket, out _), "incompatible voice codec was accepted");

Require(
    Math.Abs(VoiceGainPolicy.CalculatePeakLimitedGain(0.01f, 1f) - 21f) < 0.001f,
    "100% receive volume did not apply six times the original nominal voice gain");
Require(
    Math.Abs(VoiceGainPolicy.CalculatePeakLimitedGain(0f, 2f) - 42f) < 0.001f,
    "200% receive volume did not apply twelve times the original nominal voice gain");
Require(
    Math.Abs(VoiceGainPolicy.CalculatePeakLimitedGain(0.5f, 2f) - 1.9f) < 0.001f,
    "loud speech gain was not limited below clipping");
Require(
    Math.Abs(VoiceGainPolicy.CalculatePeakLimitedGain(0.1f, 2f, 1f) - 2f) < 0.001f,
    "outgoing microphone volume did not use a unity base gain");
Require(
    VoiceGainPolicy.CalculatePeakLimitedGain(0.5f, 0f) == 0f,
    "muted voice received non-zero gain");
Require(
    VoicePacketLossPolicy.CountMissingFrames(42, 42) == 0,
    "in-order voice packet reported a loss");
Require(
    VoicePacketLossPolicy.CountMissingFrames(42, 44) == 2,
    "short voice packet gap was not measured");
Require(
    VoicePacketLossPolicy.CountMissingFrames(uint.MaxValue, 0) == 1,
    "wrapped voice packet sequence was not measured");
Require(
    VoicePacketLossPolicy.CountMissingFrames(44, 42) == 0,
    "older voice packet reported a forward loss");
Require(
    VoicePacketLossPolicy.CountMissingFrames(42, 4200)
        == VoicePacketLossPolicy.MaximumConcealedFramesPerPacket,
    "large voice packet gap was not bounded");

var assembler = new VoiceFragmentAssembler();
Require(!assembler.TryAdd(Packet(7, 2, 3, 7, 8), 1f, out _), "partial fragment assembly completed early");
Require(!assembler.TryAdd(Packet(7, 0, 3, 1, 2, 3), 1.01f, out _), "partial fragment assembly completed early");
Require(assembler.TryAdd(Packet(7, 1, 3, 4, 5, 6), 1.02f, out var assembled), "complete fragments did not assemble");
Require(assembled.Payload.SequenceEqual(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }), "fragment order was not restored");

var jitter = new AdaptiveJitterBuffer(100f, 300f);
jitter.Enqueue(new EncodedVoiceFrame(10, 1000, 0f, new byte[] { 10 }));
jitter.Enqueue(new EncodedVoiceFrame(12, 1040, 0.04f, new byte[] { 12 }));
Require(!jitter.TryDequeue(0.09f, out _), "jitter buffer ignored baseline delay");
Require(jitter.TryDequeue(0.11f, out var first) && first.Sequence == 10, "jitter buffer lost first packet");
Require(!jitter.TryDequeue(0.11f, out _), "jitter buffer skipped a missing packet immediately");
Require(!jitter.TryDequeue(0.24f, out _), "loss recovery should first advance to the nearest sequence");
Require(jitter.TryDequeue(0.25f, out var recovered) && recovered.Sequence == 12, "loss recovery did not resume at nearest packet");

Require(VoiceAudibilityPolicy.CanHear(localPlayerIsDead: false, remotePlayerIsDead: false), "living player cannot hear living player");
Require(!VoiceAudibilityPolicy.CanHear(localPlayerIsDead: false, remotePlayerIsDead: true), "living player can hear ghost");
Require(VoiceAudibilityPolicy.CanHear(localPlayerIsDead: true, remotePlayerIsDead: false), "ghost cannot hear living player");
Require(VoiceAudibilityPolicy.CanHear(localPlayerIsDead: true, remotePlayerIsDead: true), "ghost cannot hear ghost");
// Resurrection is the same transition the runtime observes: local dead -> alive. The remote ghost
// must become inaudible on the first policy evaluation after that replicated state changes.
Require(!VoiceAudibilityPolicy.CanHear(localPlayerIsDead: false, remotePlayerIsDead: true), "resurrection did not restore living-only channel");

Require(
    VoiceDistancePolicy.EvaluateVolume(0f) == 1f
    && VoiceDistancePolicy.EvaluateVolume(VoiceDistancePolicy.FullVolumeDistanceMetres) == 1f,
    "voice distance curve attenuates inside its full-volume radius");
Require(
    VoiceDistancePolicy.EvaluateVolume(VoiceDistancePolicy.MaximumAudibleDistanceMetres) == 0f
    && !VoiceDistancePolicy.IsAudible(VoiceDistancePolicy.MaximumAudibleDistanceMetres + 0.01f),
    "voice distance curve does not end at the fixed audible edge");
Require(
    VoiceDistancePolicy.EvaluateVolume(17f) < VoiceDistancePolicy.EvaluateVolume(15f)
    && VoiceDistancePolicy.EvaluateVolume(15f) < VoiceDistancePolicy.EvaluateVolume(8f),
    "longer routed voice paths are not progressively quieter");
Require(
    VoiceDistancePolicy.EvaluateVolume(float.NaN) == 0f
    && !VoiceDistancePolicy.IsAudible(float.PositiveInfinity),
    "invalid voice route distances remain audible");

var clearProfile = VoiceOcclusionPolicy.GetProfile(VoiceOcclusionKind.None);
var itemProfile = VoiceOcclusionPolicy.GetProfile(VoiceOcclusionKind.Item);
var wallProfile = VoiceOcclusionPolicy.GetProfile(VoiceOcclusionKind.Wall);
Require(
    clearProfile.VolumeMultiplier > itemProfile.VolumeMultiplier
    && itemProfile.VolumeMultiplier > wallProfile.VolumeMultiplier,
    "voice occlusion volume profiles are not ordered clear > item > wall");
Require(
    clearProfile.LowPassFrequency > itemProfile.LowPassFrequency
    && itemProfile.LowPassFrequency > wallProfile.LowPassFrequency,
    "voice occlusion low-pass profiles are not ordered clear > item > wall");
Require(
    VoiceOcclusionPolicy.Combine(VoiceOcclusionKind.Item, VoiceOcclusionKind.Wall) == VoiceOcclusionKind.Wall,
    "a wall did not take precedence over item occlusion");
Require(
    VoiceOcclusionPolicy.Combine(VoiceOcclusionKind.Wall, VoiceOcclusionKind.Item) == VoiceOcclusionKind.Wall,
    "item occlusion incorrectly weakened an existing wall blocker");
Require(
    VoiceOcclusionPolicy.IsStructuralName("Wall_wood_standard_a_4m_prefab")
    && VoiceOcclusionPolicy.IsStructuralName("Door_a")
    && VoiceOcclusionPolicy.IsStructuralName("labyrinth_collision_02"),
    "known structural collider names were not classified as walls");
Require(
    !VoiceOcclusionPolicy.IsStructuralName("EnvironmentCollider")
    && !VoiceOcclusionPolicy.IsStructuralName("BlackboardSet_a_BB_b_base")
    && !VoiceOcclusionPolicy.IsStructuralName("OpenableGlobe_a_prefab"),
    "ordinary environment items were classified as walls");

Console.WriteLine("Proximity voice protocol, gain, jitter, fragmentation, audibility, distance, and occlusion tests passed.");
