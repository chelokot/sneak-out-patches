using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace SneakOut.ProximityVoiceChat;

internal sealed class VoiceGainProcessor
{
    private const float ReleasePerFrame = 0.08f;

    private readonly float _nominalGain;
    private readonly bool _limitPeaks;
    private float _gain = 1f;
    private bool _initialized;

    public VoiceGainProcessor(
        float nominalGain = VoiceGainPolicy.NominalVoiceGain,
        bool limitPeaks = true)
    {
        _nominalGain = Math.Max(0f, nominalGain);
        _limitPeaks = limitPeaks;
    }

    public float LastInputPeak { get; private set; }

    public float CurrentGain => _gain;

    public void Process(Il2CppArrayBase<float> samples, float requestedGain)
    {
        var peak = 0f;
        for (var index = 0; index < samples.Length; index++)
        {
            peak = Math.Max(peak, Math.Abs(samples[index]));
        }

        LastInputPeak = peak;
        var targetGain = _limitPeaks
            ? VoiceGainPolicy.CalculatePeakLimitedGain(peak, requestedGain, _nominalGain)
            : VoiceGainPolicy.CalculateLinearGain(requestedGain, _nominalGain);
        _gain = !_limitPeaks || !_initialized || targetGain < _gain
            ? targetGain
            : Math.Min(targetGain, _gain + ReleasePerFrame);
        _initialized = true;

        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] *= _gain;
        }
    }
}
