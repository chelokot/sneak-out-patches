namespace SneakOut.ProximityVoiceChat;

internal static class VoicePacketLossPolicy
{
    public const int MaximumConcealedFramesPerPacket = 8;

    public static int CountMissingFrames(uint expectedSequence, uint actualSequence)
    {
        var distance = unchecked((int)(actualSequence - expectedSequence));
        return distance <= 0
            ? 0
            : Math.Min(distance, MaximumConcealedFramesPerPacket);
    }
}
