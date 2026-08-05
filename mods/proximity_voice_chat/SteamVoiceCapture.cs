using Il2CppInterop.Runtime.InteropTypes.Arrays;
using BepInEx.Logging;
using Steamworks;

namespace SneakOut.ProximityVoiceChat;

internal readonly record struct CapturedVoiceFrame(byte[] EncodedAudio, float RootMeanSquare);

internal sealed class SteamVoiceCapture : IDisposable
{
    private const int MaximumCompressedBytes = 32 * 1024;
    private const int MaximumPcmBytes = 96 * 1024;

    private readonly Il2CppStructArray<byte> _compressedBuffer = new(MaximumCompressedBytes);
    private readonly Il2CppStructArray<byte> _pcmBuffer = new(MaximumPcmBytes);
    private readonly ManualLogSource _logger;
    private readonly bool _loggingEnabled;
    private Il2CppStructArray<byte>? _discardBuffer;
    private bool _recording;
    private bool _loggedFirstFrame;
    private bool _receivedFrameThisRecording;
    private EVoiceResult? _lastAvailabilityResult;
    private long _recordingStartedAtMilliseconds;
    private bool _warnedNoFrames;

    public SteamVoiceCapture(ManualLogSource logger, bool loggingEnabled)
    {
        _logger = logger;
        _loggingEnabled = loggingEnabled;
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
            _lastAvailabilityResult = null;
            _recordingStartedAtMilliseconds = Environment.TickCount64;
            _warnedNoFrames = false;
            _receivedFrameThisRecording = false;
            _logger.LogInfo($"Proximity voice microphone capture started: sampleRate={SampleRate}");
        }
        else
        {
            SteamUser.StopVoiceRecording();
            DiscardPendingVoice();
            if (_loggingEnabled)
            {
                _logger.LogInfo("Proximity voice microphone capture stopped");
            }
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
        ReportAvailabilityResult(availableResult, availableCompressedBytes);
        if (availableResult != EVoiceResult.k_EVoiceResultOK || availableCompressedBytes == 0)
        {
            ReportCaptureStall(availableResult, availableCompressedBytes);
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
        _receivedFrameThisRecording = true;
        if (!_loggedFirstFrame)
        {
            _loggedFirstFrame = true;
            _logger.LogInfo($"Proximity voice captured first encoded frame: bytes={encoded.Length}");
        }
        return true;
    }

    private void ReportCaptureStall(EVoiceResult result, uint availableBytes)
    {
        if (_warnedNoFrames
            || _receivedFrameThisRecording
            || Environment.TickCount64 - _recordingStartedAtMilliseconds < 3000)
        {
            return;
        }
        _warnedNoFrames = true;
        _logger.LogWarning(
            $"Proximity voice capture produced no encoded frames after 3s: "
            + $"result={result}, availableBytes={availableBytes}. Check the Steam microphone input.");
    }

    private void ReportAvailabilityResult(EVoiceResult result, uint availableBytes)
    {
        if (_lastAvailabilityResult == result)
        {
            return;
        }
        _lastAvailabilityResult = result;
        if (result is EVoiceResult.k_EVoiceResultOK or EVoiceResult.k_EVoiceResultNoData)
        {
            if (_loggingEnabled)
            {
                _logger.LogInfo(
                    $"Proximity voice capture state: result={result}, availableBytes={availableBytes}");
            }
            return;
        }
        _logger.LogWarning(
            $"Proximity voice capture unavailable: result={result}, availableBytes={availableBytes}");
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
