using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Steamworks;

namespace SneakOut.ProximityVoiceChat;

internal sealed class SteamVoiceDecoder
{
    private const int MaximumCompressedBytes = VoiceProtocol.MaximumPayloadLength;
    private const int MaximumPcmBytes = 128 * 1024;

    private readonly Il2CppStructArray<byte> _compressedBuffer = new(MaximumCompressedBytes);
    private readonly Il2CppStructArray<byte> _pcmBuffer = new(MaximumPcmBytes);
    private readonly float[] _samples = new float[MaximumPcmBytes / 2];

    public SteamVoiceDecoder(uint sampleRate)
    {
        SampleRate = sampleRate;
    }

    public uint SampleRate { get; }

    public bool TryDecode(byte[] encoded, out float[] samples, out int sampleCount)
    {
        samples = _samples;
        sampleCount = 0;
        if (encoded.Length == 0 || encoded.Length > _compressedBuffer.Length)
        {
            return false;
        }

        for (var index = 0; index < encoded.Length; index++)
        {
            _compressedBuffer[index] = encoded[index];
        }

        var result = SteamUser.DecompressVoice(
            _compressedBuffer,
            (uint)encoded.Length,
            _pcmBuffer,
            (uint)_pcmBuffer.Length,
            out var writtenBytes,
            SampleRate);
        if (result != EVoiceResult.k_EVoiceResultOK || writtenBytes < 2)
        {
            return false;
        }

        sampleCount = checked((int)(writtenBytes / 2));
        for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            var byteIndex = sampleIndex * 2;
            var pcm = (short)(_pcmBuffer[byteIndex] | (_pcmBuffer[byteIndex + 1] << 8));
            samples[sampleIndex] = pcm / 32768f;
        }
        return true;
    }
}
