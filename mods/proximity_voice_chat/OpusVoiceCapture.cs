using BepInEx.Logging;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using POpusCodec;
using POpusCodec.Enums;
using Photon.Voice;
using UnityEngine;

namespace SneakOut.ProximityVoiceChat;

internal readonly record struct CapturedVoiceFrame(byte[] EncodedAudio, float RootMeanSquare);

internal sealed class OpusVoiceCapture : IDisposable
{
    public const int SampleRate = 24000;
    public const int FrameSamples = SampleRate / 50;

    private const int CaptureBufferSeconds = 2;
    private const int Bitrate = 30000;
    private const int MaximumFramesPerPoll = 8;
    private const int StartRetryIntervalMilliseconds = 5000;

    private readonly ManualLogSource _logger;
    private readonly bool _loggingEnabled;
    private readonly OpusEncoder _encoder;
    private readonly Il2CppStructArray<float> _monoFrame = new(FrameSamples);
    private Il2CppStructArray<float>? _microphoneFrame;
    private AudioClip? _microphoneClip;
    private string _microphoneDevice = string.Empty;
    private byte[]? _encodedFrame;
    private int _readPosition;
    private int _microphoneChannels;
    private int _framesReadThisPoll;
    private long _recordingStartedAtMilliseconds;
    private long _nextDiagnosticAtMilliseconds;
    private long _nextStartAttemptAtMilliseconds;
    private long _pcmFrames;
    private long _capturedFrames;
    private long _capturedBytes;
    private long _encoderFailures;
    private double _rootMeanSquareTotal;
    private float _peakSample;
    private bool _recording;
    private bool _loggedFirstFrame;
    private bool _warnedNoDevices;
    private bool _warnedCaptureStall;
    private bool _warnedEncoderStall;

    public OpusVoiceCapture(ManualLogSource logger, bool loggingEnabled)
    {
        _logger = logger;
        _loggingEnabled = loggingEnabled;
        _encoder = new OpusEncoder(
            SamplingRate.Sampling24000,
            Channels.Mono,
            Bitrate,
            OpusApplicationType.Voip,
            Delay.Delay20ms)
        {
            UseInbandFEC = true,
            PacketLossPercentage = 30,
            DtxEnabled = false,
            Output = (Il2CppSystem.Action<Il2CppSystem.ArraySegment<byte>, FrameFlags>)OnEncodedFrame,
        };
    }

    public void SetRecording(bool shouldRecord)
    {
        if (shouldRecord == _recording)
        {
            return;
        }

        if (shouldRecord)
        {
            StartRecording();
            return;
        }

        StopRecording();
    }

    public bool TryCapture(bool analyzeLevel, out CapturedVoiceFrame frame)
    {
        frame = default;
        if (!_recording || _microphoneClip is null)
        {
            return false;
        }

        if (_framesReadThisPoll >= MaximumFramesPerPoll)
        {
            _framesReadThisPoll = 0;
            return false;
        }

        var microphonePosition = Microphone.GetPosition(_microphoneDevice);
        if (microphonePosition < 0)
        {
            ReportCaptureStall("position-unavailable");
            return false;
        }

        var availableSamples = microphonePosition >= _readPosition
            ? microphonePosition - _readPosition
            : _microphoneClip.samples - _readPosition + microphonePosition;
        if (availableSamples < FrameSamples)
        {
            ReportCaptureStall($"waiting-for-frame position={microphonePosition}, available={availableSamples}");
            _framesReadThisPoll = 0;
            return false;
        }

        if (availableSamples > SampleRate)
        {
            var droppedSamples = availableSamples - FrameSamples;
            _readPosition = PositiveModulo(microphonePosition - FrameSamples, _microphoneClip.samples);
            availableSamples = FrameSamples;
            _logger.LogWarning(
                $"Proximity voice microphone overrun recovered: position={microphonePosition}, "
                + $"droppedSamples={droppedSamples}");
        }

        _microphoneClip.GetData(_microphoneFrame!, _readPosition);
        _readPosition = (_readPosition + FrameSamples) % _microphoneClip.samples;
        _framesReadThisPoll++;

        double sumSquares = 0;
        var peak = 0f;
        for (var sampleIndex = 0; sampleIndex < FrameSamples; sampleIndex++)
        {
            var channelOffset = sampleIndex * _microphoneChannels;
            var monoSample = 0f;
            for (var channelIndex = 0; channelIndex < _microphoneChannels; channelIndex++)
            {
                monoSample += _microphoneFrame![channelOffset + channelIndex];
            }
            monoSample /= _microphoneChannels;
            _monoFrame[sampleIndex] = monoSample;
            sumSquares += monoSample * monoSample;
            peak = Math.Max(peak, Math.Abs(monoSample));
        }

        var rootMeanSquare = (float)Math.Sqrt(sumSquares / FrameSamples);
        _pcmFrames++;
        _encodedFrame = null;
        _encoder.Encode(_monoFrame);
        if (_encodedFrame is null)
        {
            _encoderFailures++;
            if (!_warnedEncoderStall
                && Environment.TickCount64 - _recordingStartedAtMilliseconds >= 3000)
            {
                _warnedEncoderStall = true;
                _logger.LogWarning(
                    $"Proximity voice Opus encoder produced no packet: pcmFrames={_pcmFrames}, "
                    + $"failures={_encoderFailures}, frameSamples={FrameSamples}");
            }
            return false;
        }

        _capturedFrames++;
        _capturedBytes += _encodedFrame.Length;
        _rootMeanSquareTotal += rootMeanSquare;
        _peakSample = Math.Max(_peakSample, peak);
        ReportDiagnostics();

        frame = new CapturedVoiceFrame(
            _encodedFrame,
            analyzeLevel ? rootMeanSquare : 1f);
        if (!_loggedFirstFrame)
        {
            _loggedFirstFrame = true;
            _logger.LogInfo(
                $"Proximity voice captured first Opus frame: bytes={_encodedFrame.Length}, "
                + $"rms={rootMeanSquare:F4}, peak={peak:F4}");
        }
        return true;
    }

    private void StartRecording()
    {
        var nowMilliseconds = Environment.TickCount64;
        if (nowMilliseconds < _nextStartAttemptAtMilliseconds)
        {
            return;
        }
        _nextStartAttemptAtMilliseconds = nowMilliseconds + StartRetryIntervalMilliseconds;

        var devices = Microphone.devices;
        if (devices is null || devices.Length == 0)
        {
            if (!_warnedNoDevices)
            {
                _warnedNoDevices = true;
                _logger.LogWarning("Proximity voice found no Unity microphone devices");
            }
            return;
        }

        _microphoneDevice = devices[0];
        Microphone.GetDeviceCaps(_microphoneDevice, out var minimumFrequency, out var maximumFrequency);
        _microphoneClip = Microphone.Start(
            _microphoneDevice,
            true,
            CaptureBufferSeconds,
            SampleRate);
        if (_microphoneClip is null || _microphoneClip.Pointer == IntPtr.Zero)
        {
            _microphoneClip = null;
            _logger.LogWarning(
                $"Proximity voice could not start Unity microphone capture: device={_microphoneDevice}");
            return;
        }

        _microphoneChannels = _microphoneClip.channels;
        if (_microphoneChannels <= 0 || _microphoneClip.frequency != SampleRate)
        {
            var actualChannels = _microphoneChannels;
            var actualFrequency = _microphoneClip.frequency;
            Microphone.End(_microphoneDevice);
            UnityEngine.Object.Destroy(_microphoneClip);
            _microphoneClip = null;
            _logger.LogWarning(
                $"Proximity voice microphone format is unsupported: requested={SampleRate}Hz mono, "
                + $"actual={actualFrequency}Hz channels={actualChannels}");
            return;
        }

        _microphoneFrame = new Il2CppStructArray<float>(FrameSamples * _microphoneChannels);
        _readPosition = 0;
        _framesReadThisPoll = 0;
        _recordingStartedAtMilliseconds = Environment.TickCount64;
        _nextDiagnosticAtMilliseconds = _recordingStartedAtMilliseconds + 10000;
        _warnedCaptureStall = false;
        _warnedEncoderStall = false;
        _recording = true;
        _logger.LogInfo(
            $"Proximity voice Unity microphone started: device={_microphoneDevice}, "
            + $"requestedRate={SampleRate}, actualRate={_microphoneClip.frequency}, "
            + $"channels={_microphoneChannels}, caps={minimumFrequency}-{maximumFrequency}, "
            + $"codec=Opus, frameMs=20, bitrate={Bitrate}");
    }

    private void StopRecording()
    {
        if (_recording)
        {
            Microphone.End(_microphoneDevice);
        }
        if (_microphoneClip is not null)
        {
            UnityEngine.Object.Destroy(_microphoneClip);
        }
        _microphoneClip = null;
        _microphoneFrame = null;
        _recording = false;
        _framesReadThisPoll = 0;
        if (_loggingEnabled)
        {
            _logger.LogInfo("Proximity voice Unity microphone stopped");
        }
    }

    private void OnEncodedFrame(Il2CppSystem.ArraySegment<byte> encoded, FrameFlags flags)
    {
        if (encoded.Count <= 0 || flags != 0)
        {
            return;
        }

        var result = new byte[encoded.Count];
        var source = encoded.Array;
        for (var index = 0; index < result.Length; index++)
        {
            result[index] = source[encoded.Offset + index];
        }
        _encodedFrame = result;
    }

    private void ReportCaptureStall(string state)
    {
        if (_warnedCaptureStall
            || Environment.TickCount64 - _recordingStartedAtMilliseconds < 3000)
        {
            return;
        }
        _warnedCaptureStall = true;
        _logger.LogWarning(
            $"Proximity voice Unity microphone produced no complete PCM frame after 3s: "
            + $"device={_microphoneDevice}, state={state}, recording={Microphone.IsRecording(_microphoneDevice)}");
    }

    private void ReportDiagnostics()
    {
        if (!_loggingEnabled || Environment.TickCount64 < _nextDiagnosticAtMilliseconds)
        {
            return;
        }
        var averageRootMeanSquare = _capturedFrames == 0
            ? 0
            : _rootMeanSquareTotal / _capturedFrames;
        var averageEncodedBytes = _capturedFrames == 0
            ? 0
            : _capturedBytes / _capturedFrames;
        _logger.LogInfo(
            $"Proximity voice capture metrics: pcmFrames={_pcmFrames}, encodedFrames={_capturedFrames}, "
            + $"encoderFailures={_encoderFailures}, "
            + $"averageBytes={averageEncodedBytes}, averageRms={averageRootMeanSquare:F4}, "
            + $"peak={_peakSample:F4}, microphonePosition={Microphone.GetPosition(_microphoneDevice)}");
        _nextDiagnosticAtMilliseconds = Environment.TickCount64 + 10000;
    }

    private static int PositiveModulo(int value, int modulus)
    {
        var result = value % modulus;
        return result < 0 ? result + modulus : result;
    }

    public void Dispose()
    {
        StopRecording();
        _encoder.Dispose();
    }
}
