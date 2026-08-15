namespace SneakOut.ProximityVoiceChat;

internal static class VoiceGainPolicy
{
    private const float NominalVoiceGain = 3.5f;
    private const float PeakHeadroom = 0.95f;

    public static float CalculatePeakLimitedGain(float peak, float requestedGain)
    {
        var safeRequestedGain = Math.Max(0f, requestedGain) * NominalVoiceGain;
        return peak <= 0f
            ? safeRequestedGain
            : Math.Min(safeRequestedGain, PeakHeadroom / peak);
    }
}
