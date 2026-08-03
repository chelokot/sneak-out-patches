using SneakOut.ProximityVoiceChat;

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

Console.WriteLine("Proximity voice protocol, jitter, fragmentation, and audibility tests passed.");
