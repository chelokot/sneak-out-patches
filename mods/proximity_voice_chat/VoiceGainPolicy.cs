namespace SneakOut.ProximityVoiceChat;

internal static class VoiceGainPolicy
{
    public const float NominalVoiceGain = 3.5f;
    private const float PeakHeadroom = 0.95f;
    private const float GainRecoveryBlendPerFrame = 0.35f;

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

    public static float RecoverGain(float currentGain, float targetGain)
    {
        var safeCurrentGain = Math.Max(0f, currentGain);
        var safeTargetGain = Math.Max(0f, targetGain);
        if (safeTargetGain <= safeCurrentGain)
        {
            return safeTargetGain;
        }
        return safeCurrentGain
            + (safeTargetGain - safeCurrentGain) * GainRecoveryBlendPerFrame;
    }
}
