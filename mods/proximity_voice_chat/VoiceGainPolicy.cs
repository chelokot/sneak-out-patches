namespace SneakOut.ProximityVoiceChat;

internal static class VoiceGainPolicy
{
    public const float NominalVoiceGain = 21f;
    private const float PeakHeadroom = 0.95f;
    private const float SoftLimitThreshold = 0.8f;
    private const float SoftLimitCeiling = 0.98f;

    public static float CalculatePeakLimitedGain(float peak, float requestedGain)
    {
        return CalculatePeakLimitedGain(peak, requestedGain, NominalVoiceGain);
    }

    public static float CalculatePeakLimitedGain(
        float peak,
        float requestedGain,
        float nominalGain)
    {
        var safeRequestedGain = CalculateLinearGain(requestedGain, nominalGain);
        return peak <= 0f
            ? safeRequestedGain
            : Math.Min(safeRequestedGain, PeakHeadroom / peak);
    }

    public static float CalculateLinearGain(float requestedGain, float nominalGain)
    {
        return Math.Max(0f, requestedGain) * Math.Max(0f, nominalGain);
    }

    public static float ApplySoftLimit(float sample)
    {
        var magnitude = Math.Abs(sample);
        if (magnitude <= SoftLimitThreshold)
        {
            return sample;
        }
        if (!float.IsFinite(magnitude))
        {
            return sample < 0f ? -SoftLimitCeiling : SoftLimitCeiling;
        }

        var limitRange = SoftLimitCeiling - SoftLimitThreshold;
        var excess = magnitude - SoftLimitThreshold;
        var limitedMagnitude = SoftLimitThreshold
            + limitRange * excess / (limitRange + excess);
        return sample < 0f ? -limitedMagnitude : limitedMagnitude;
    }
}
