using BepInEx.Logging;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Photon.Voice;
using Photon.Voice.Unity;
using UnityEngine;

namespace SneakOut.ProximityVoiceChat;

internal sealed class LocalVoiceTestPlayback : IDisposable
{
    private const float FrameDurationSeconds =
        OpusVoiceCapture.FrameSamples / (float)OpusVoiceCapture.SampleRate;

    private readonly ManualLogSource _logger;
    private readonly Queue<byte[]> _encodedFrames;
    private readonly VoiceGainProcessor _gainProcessor = new();
    private readonly Photon.Voice.Unity.Logger _photonLogger;
    private readonly UnityAudioOut _audioOutput;
    private readonly OpusVoiceDecoder _decoder;
    private readonly GameObject _host;
    private readonly float _drainDelaySeconds;
    private float _requestedVolume = 1f;
    private float _nextDecodeAt = -1f;
    private float _drainedAt = -1f;
    private float _maximumInputPeak;
    private float _maximumOutputPeak;
    private long _decodedFrames;
    private long _decodeFailures;
    private bool _disposed;

    public LocalVoiceTestPlayback(
        IReadOnlyCollection<byte[]> encodedFrames,
        ProximityVoiceChatConfig configuration,
        ManualLogSource logger)
    {
        _logger = logger;
        _encodedFrames = new Queue<byte[]>(encodedFrames);

        _host = new GameObject("ProximityVoice-MicrophoneTest");
        _host.hideFlags = HideFlags.HideAndDontSave;
        var audioSource = _host.AddComponent<AudioSource>();
        audioSource.loop = true;
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
        audioSource.dopplerLevel = 0f;
        audioSource.volume = 1f;

        var maximumDelayMilliseconds = Mathf.RoundToInt(Math.Max(
            configuration.JitterBufferMilliseconds.Value,
            configuration.MaximumJitterMilliseconds.Value));
        var targetDelayMilliseconds = Mathf.RoundToInt(Math.Clamp(
            configuration.JitterBufferMilliseconds.Value,
            40,
            maximumDelayMilliseconds));
        var playDelay = new AudioOutDelayControl.PlayDelayConfig
        {
            Low = targetDelayMilliseconds,
            High = Math.Min(maximumDelayMilliseconds, targetDelayMilliseconds + 40),
            Max = maximumDelayMilliseconds,
            SpeedUpPerc = 5,
        };
        _drainDelaySeconds = maximumDelayMilliseconds / 1000f + 0.5f;
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

    public bool Tick(float nowSeconds, float requestedVolume)
    {
        if (_disposed)
        {
            return true;
        }

        _requestedVolume = Math.Clamp(
            requestedVolume,
            VoicePlayerVolumePolicy.MinimumVolume,
            VoicePlayerVolumePolicy.MaximumVolume);
        _audioOutput.Service();
        if (_nextDecodeAt < 0f)
        {
            _nextDecodeAt = nowSeconds;
        }

        var decodeBudget = 8;
        while (decodeBudget-- > 0
               && _encodedFrames.TryPeek(out var encodedFrame)
               && nowSeconds + 0.001f >= _nextDecodeAt)
        {
            _encodedFrames.Dequeue();
            if (!_decoder.TryDecode(encodedFrame, missingFramesBefore: 0))
            {
                _decodeFailures++;
            }
            _nextDecodeAt += FrameDurationSeconds;
        }

        if (_encodedFrames.Count != 0)
        {
            return false;
        }
        if (_drainedAt < 0f)
        {
            _drainedAt = nowSeconds;
            _logger.LogInfo(
                $"Proximity voice microphone test playback metrics: decodedFrames={_decodedFrames}, "
                + $"decodeFailures={_decodeFailures}, inputPeak={_maximumInputPeak:F4}, "
                + $"outputPeak={_maximumOutputPeak:F4}, volume={_requestedVolume:F2}");
        }
        return nowSeconds - _drainedAt >= _drainDelaySeconds;
    }

    private void OnDecodedFrame(Il2CppArrayBase<float> samples)
    {
        if (_disposed || samples.Length == 0)
        {
            return;
        }

        _gainProcessor.Process(samples, _requestedVolume);
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
}
