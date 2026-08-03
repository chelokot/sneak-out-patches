using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Steamworks;

namespace SneakOut.ProximityVoiceChat;

internal readonly record struct CapturedVoiceFrame(byte[] EncodedAudio, float RootMeanSquare);

internal sealed class SteamVoiceCapture : IDisposable
{
    private const int MaximumCompressedBytes = 32 * 1024;
    private const int MaximumPcmBytes = 96 * 1024;

    private readonly Il2CppStructArray<byte> _compressedBuffer = new(MaximumCompressedBytes);
    private readonly Il2CppStructArray<byte> _pcmBuffer = new(MaximumPcmBytes);
    private Il2CppStructArray<byte>? _discardBuffer;
    private bool _recording;

    public SteamVoiceCapture()
    {
        var requestedRate = SteamUser.GetVoiceOptimalSampleRate();
        SampleRate = requestedRate is 11025 or 22050 or 44100 or 48000
            ? requestedRate
            : 48000;
    }

    public uint SampleRate { get; }

    public void SetRecording(bool shouldRecord)
    {
        if (shouldRecord == _recording)
        {
            return;
        }

        if (shouldRecord)
        {
            SteamUser.StartVoiceRecording();
        }
        else
        {
            SteamUser.StopVoiceRecording();
            DiscardPendingVoice();
        }
        _recording = shouldRecord;
    }

    public bool TryCapture(bool analyzeLevel, out CapturedVoiceFrame frame)
    {
        frame = default;
        if (!_recording)
        {
            return false;
        }

        var availableResult = SteamUser.GetAvailableVoice(out var availableCompressedBytes);
        if (availableResult != EVoiceResult.k_EVoiceResultOK || availableCompressedBytes == 0)
        {
            return false;
        }
        if (availableCompressedBytes > MaximumCompressedBytes)
        {
            DrainOversizedCapture(availableCompressedBytes);
            return false;
        }

        var requestedBytes = Math.Min(availableCompressedBytes, (uint)_compressedBuffer.Length);
        var voiceResult = SteamUser.GetVoice(
            true,
            _compressedBuffer,
            requestedBytes,
            out var writtenCompressedBytes);
        if (voiceResult != EVoiceResult.k_EVoiceResultOK || writtenCompressedBytes == 0)
        {
            return false;
        }

        var encoded = new byte[checked((int)writtenCompressedBytes)];
        for (var index = 0; index < encoded.Length; index++)
        {
            encoded[index] = _compressedBuffer[index];
        }

        var rootMeanSquare = analyzeLevel ? CalculateRootMeanSquare(encoded) : 1f;
        frame = new CapturedVoiceFrame(encoded, rootMeanSquare);
        return true;
    }

    private float CalculateRootMeanSquare(byte[] encoded)
    {
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
            return 0f;
        }

        double sumSquares = 0;
        var sampleCount = (int)(writtenBytes / 2);
        for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            var byteIndex = sampleIndex * 2;
            var sample = (short)(_pcmBuffer[byteIndex] | (_pcmBuffer[byteIndex + 1] << 8));
            var normalized = sample / 32768f;
            sumSquares += normalized * normalized;
        }
        return (float)Math.Sqrt(sumSquares / sampleCount);
    }

    private void DrainOversizedCapture(uint availableBytes)
    {
        var required = checked((int)availableBytes);
        if (_discardBuffer is null || _discardBuffer.Length < required)
        {
            _discardBuffer = new Il2CppStructArray<byte>(required);
        }
        SteamUser.GetVoice(
            true,
            _discardBuffer,
            availableBytes,
            out _);
    }

    private void DiscardPendingVoice()
    {
        var result = SteamUser.GetAvailableVoice(out var availableBytes);
        if (result != EVoiceResult.k_EVoiceResultOK || availableBytes == 0)
        {
            return;
        }
        DrainOversizedCapture(availableBytes);
    }

    public void Dispose()
    {
        SetRecording(false);
    }
}
