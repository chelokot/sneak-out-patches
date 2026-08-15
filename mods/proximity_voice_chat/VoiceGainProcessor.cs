using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace SneakOut.ProximityVoiceChat;

internal sealed class VoiceGainProcessor
{
    private const float ReleasePerFrame = 0.08f;

    private float _gain = 1f;
    private bool _initialized;

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
        var peakLimitedGain = VoiceGainPolicy.CalculatePeakLimitedGain(peak, requestedGain);
        _gain = !_initialized || peakLimitedGain < _gain
            ? peakLimitedGain
            : Math.Min(peakLimitedGain, _gain + ReleasePerFrame);
        _initialized = true;

        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] *= _gain;
        }
    }
}
