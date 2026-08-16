namespace SneakOut.ProximityVoiceChat;

internal static class VoiceGainPolicy
{
    public const float NominalVoiceGain = 21f;
    private const float PeakHeadroom = 0.95f;

    public static float CalculatePeakLimitedGain(float peak, float requestedGain)
    {
        return CalculatePeakLimitedGain(peak, requestedGain, NominalVoiceGain);
    }

    public static float CalculatePeakLimitedGain(
        float peak,
        float requestedGain,
        float nominalGain)
    {
        var safeRequestedGain = Math.Max(0f, requestedGain) * Math.Max(0f, nominalGain);
        return peak <= 0f
            ? safeRequestedGain
            : Math.Min(safeRequestedGain, PeakHeadroom / peak);
    }
}
