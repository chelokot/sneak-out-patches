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

    public static bool IsStructuralName(string objectName)
    {
        return objectName.Contains("wall", StringComparison.OrdinalIgnoreCase)
            || objectName.Contains("door", StringComparison.OrdinalIgnoreCase)
            || objectName.Contains("labyrinth_collision", StringComparison.OrdinalIgnoreCase);
    }

    public static VoiceOcclusionProfile GetProfile(VoiceOcclusionKind kind)
    {
        return kind switch
        {
            VoiceOcclusionKind.Item => new VoiceOcclusionProfile(0.75f, 6500f),
            VoiceOcclusionKind.Wall => new VoiceOcclusionProfile(0.20f, 1100f),
            _ => new VoiceOcclusionProfile(1f, UnoccludedLowPassFrequency),
        };
    }
}
