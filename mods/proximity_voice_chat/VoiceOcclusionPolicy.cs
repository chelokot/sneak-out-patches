namespace SneakOut.ProximityVoiceChat;

internal enum VoiceOcclusionKind
{
    None,
    Item,
    Wall,
}

internal readonly record struct VoiceOcclusionProfile(
    float VolumeMultiplier,
    float LowPassFrequency);

internal static class VoiceOcclusionPolicy
{
    public const float UnoccludedLowPassFrequency = 22000f;

    public static VoiceOcclusionKind Combine(VoiceOcclusionKind current, VoiceOcclusionKind candidate)
    {
        return candidate > current ? candidate : current;
    }

    public static VoiceOcclusionProfile GetProfile(VoiceOcclusionKind kind)
    {
        return kind switch
        {
            VoiceOcclusionKind.Item => new VoiceOcclusionProfile(0.82f, 9000f),
            VoiceOcclusionKind.Wall => new VoiceOcclusionProfile(0.20f, 1100f),
            _ => new VoiceOcclusionProfile(1f, UnoccludedLowPassFrequency),
        };
    }
}
