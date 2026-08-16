using BepInEx.Logging;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Photon.Voice;
using Photon.Voice.Unity;
using UnityEngine;

namespace SneakOut.ProximityVoiceChat;

internal sealed class LocalVoiceTestPlayback : IDisposable
{
    public const float PlaybackDelaySeconds = 1f;

    private const float FrameDurationSeconds =
        OpusVoiceCapture.FrameSamples / (float)OpusVoiceCapture.SampleRate;
    private const int MaximumBufferedFrames = 300;
    private const int TestOutputDelayMilliseconds = 40;
    private const int MaximumTestOutputDelayMilliseconds = 120;

    private readonly ManualLogSource _logger;
    private readonly Queue<ScheduledVoiceFrame> _encodedFrames = new();
    private readonly VoiceGainProcessor _gainProcessor = new(limitPeaks: false);
    private readonly Photon.Voice.Unity.Logger _photonLogger;
    private readonly UnityAudioOut _audioOutput;
    private readonly OpusVoiceDecoder _decoder;
    private readonly GameObject _host;
    private float _lastScheduledPlayAt = -1f;
    private float _drainedAt = -1f;
    private float _maximumInputPeak;
    private float _maximumOutputPeak;
    private long _decodedFrames;
    private long _decodeFailures;
    private bool _captureCompleted;
    private bool _disposed;

    public LocalVoiceTestPlayback(ProximityVoiceChatConfig configuration, ManualLogSource logger)
    {
        _logger = logger;
        _host = new GameObject("ProximityVoice-MicrophoneTest");
        _host.hideFlags = HideFlags.HideAndDontSave;
        var audioSource = _host.AddComponent<AudioSource>();
        audioSource.loop = true;
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
        audioSource.dopplerLevel = 0f;
        audioSource.volume = 1f;

        var playDelay = new AudioOutDelayControl.PlayDelayConfig
        {
            Low = TestOutputDelayMilliseconds,
            High = TestOutputDelayMilliseconds + 20,
            Max = MaximumTestOutputDelayMilliseconds,
            SpeedUpPerc = 5,
        };
        _photonLogger = new Photon.Voice.Unity.Logger(
            configuration.EnableLogging.Value
                ? Photon.Voice.LogLevel.Info
                : Photon.Voice.LogLevel.Warning);
        _audioOutput = new UnityAudioOut(
            audioSource,
            playDelay,
            new Photon.Voice.ILogger(_photonLogger.Pointer),
            "[ProximityVoice:MicrophoneTest]",
            configuration.EnableLogging.Value);
        _audioOutput.Start(OpusVoiceCapture.SampleRate, 1, OpusVoiceCapture.FrameSamples);
        _decoder = new OpusVoiceDecoder(OnDecodedFrame);
    }

    public void Enqueue(byte[] encodedFrame, float capturedAt)
    {
        if (_disposed || _captureCompleted || encodedFrame.Length == 0)
        {
            return;
        }
        if (_encodedFrames.Count >= MaximumBufferedFrames)
        {
            _encodedFrames.Dequeue();
            _logger.LogWarning("Proximity voice microphone test dropped its oldest delayed frame");
        }
        var delayedCaptureTime = capturedAt + PlaybackDelaySeconds;
        var playAt = delayedCaptureTime;
        if (_lastScheduledPlayAt >= 0f)
        {
            var continuousPlayAt = _lastScheduledPlayAt + FrameDurationSeconds;
            playAt = delayedCaptureTime - continuousPlayAt > 0.25f
                ? delayedCaptureTime
                : continuousPlayAt;
        }
        _lastScheduledPlayAt = playAt;
        _encodedFrames.Enqueue(new ScheduledVoiceFrame(encodedFrame, playAt));
    }

    public void CompleteCapture()
    {
        _captureCompleted = true;
    }

    public bool Tick(float nowSeconds)
    {
        if (_disposed)
        {
            return true;
        }

        _audioOutput.Service();
        var decodeBudget = 8;
        while (decodeBudget-- > 0
               && _encodedFrames.TryPeek(out var frame)
               && nowSeconds + 0.001f >= frame.PlayAt)
        {
            _encodedFrames.Dequeue();
            if (!_decoder.TryDecode(frame.EncodedAudio, missingFramesBefore: 0))
            {
                _decodeFailures++;
            }
        }

        if (!_captureCompleted || _encodedFrames.Count != 0)
        {
            return false;
        }
        if (_drainedAt < 0f)
        {
            _drainedAt = nowSeconds;
            _logger.LogInfo(
                $"Proximity voice microphone test playback metrics: decodedFrames={_decodedFrames}, "
                + $"decodeFailures={_decodeFailures}, inputPeak={_maximumInputPeak:F4}, "
                + $"outputPeak={_maximumOutputPeak:F4}, receiveVolume=1.00");
        }
        return nowSeconds - _drainedAt >= MaximumTestOutputDelayMilliseconds / 1000f + 0.5f;
    }

    private void OnDecodedFrame(Il2CppArrayBase<float> samples)
    {
        if (_disposed || samples.Length == 0)
        {
            return;
        }

        _gainProcessor.Process(samples, requestedGain: 1f);
        _maximumInputPeak = Math.Max(_maximumInputPeak, _gainProcessor.LastInputPeak);
        var outputPeak = 0f;
        for (var index = 0; index < samples.Length; index++)
        {
            outputPeak = Math.Max(outputPeak, Math.Abs(samples[index]));
        }
        _maximumOutputPeak = Math.Max(_maximumOutputPeak, outputPeak);
        _audioOutput.Push(samples);
        _decodedFrames++;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var playbackClip = _audioOutput.clip;
        _audioOutput.Stop();
        _decoder.Dispose();
        if (playbackClip is not null && playbackClip.Pointer != IntPtr.Zero)
        {
            UnityEngine.Object.Destroy(playbackClip);
        }
        UnityEngine.Object.Destroy(_host);
        _encodedFrames.Clear();
    }

    private readonly record struct ScheduledVoiceFrame(byte[] EncodedAudio, float PlayAt);
}
