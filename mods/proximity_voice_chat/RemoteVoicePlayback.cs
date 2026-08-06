using BepInEx.Logging;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace SneakOut.ProximityVoiceChat;

internal sealed class RemoteVoicePlayback : IDisposable
{
    private const float OcclusionProbeIntervalSeconds = 0.12f;
    private const float UnoccludedLowPassFrequency = 22000f;
    private const float FullVolumeDistanceMetres = 2.5f;
    private const float MaximumAudibleDistanceMetres = 10f;
    private const int OcclusionHitCapacity = 16;

    private readonly ProximityVoiceChatConfig _configuration;
    private readonly ManualLogSource _logger;
    private readonly string _peerLabel;
    private readonly AdaptiveJitterBuffer _jitterBuffer;
    private readonly SteamVoiceDecoder _decoder;
    private readonly GameObject _host;
    private readonly AudioSource _audioSource;
    private readonly AudioLowPassFilter _lowPassFilter;
    private readonly AudioClip _clip;
    private readonly int _clipSamples;
    private readonly int _sampleRate;
    private readonly int _startThresholdSamples;
    private readonly Il2CppStructArray<float> _silence;
    private readonly Il2CppStructArray<RaycastHit> _occlusionHits = new(OcclusionHitCapacity);
    private readonly Dictionary<int, Il2CppStructArray<float>> _uploadBuffers = new();
    private Transform _anchor;
    private int _writePosition;
    private int _lastPlaybackPosition;
    private int _queuedSamples;
    private float _nextOcclusionProbe;
    private float _occlusionAmount;
    private bool _isOccluded;
    private float _lastPacketTime;
    private float _lastTickTime;
    private bool _started;
    private bool _outOfRange;
    private bool _suppressedByPlayerState;
    private bool _disposed;

    public RemoteVoicePlayback(
        Transform anchor,
        uint sampleRate,
        ProximityVoiceChatConfig configuration,
        ManualLogSource logger,
        string peerLabel)
    {
        _configuration = configuration;
        _logger = logger;
        _peerLabel = peerLabel;
        _anchor = anchor;
        _jitterBuffer = new AdaptiveJitterBuffer(
            configuration.JitterBufferMilliseconds.Value,
            configuration.MaximumJitterMilliseconds.Value);
        _decoder = new SteamVoiceDecoder(sampleRate);

        _host = new GameObject($"ProximityVoice-{peerLabel}");
        _host.hideFlags = HideFlags.HideAndDontSave;
        _host.transform.SetParent(anchor, false);
        _host.transform.localPosition = Vector3.up * 1.25f;
        _audioSource = _host.AddComponent<AudioSource>();
        _lowPassFilter = _host.AddComponent<AudioLowPassFilter>();
        _sampleRate = checked((int)sampleRate);
        _clipSamples = _sampleRate * 3;
        // The packet jitter buffer already owns network delay. The PCM ring only needs one normal
        // Steam voice frame before starting, otherwise the same latency would be paid twice.
        _startThresholdSamples = Math.Max(1, (int)(sampleRate * 0.02f));
        _clip = AudioClip.Create($"ProximityVoice-{peerLabel}", _clipSamples, 1, _sampleRate, false);
        _silence = new Il2CppStructArray<float>(_clipSamples);
        _clip.SetData(_silence, 0);

        _audioSource.clip = _clip;
        _audioSource.loop = true;
        _audioSource.playOnAwake = false;
        _audioSource.spatialBlend = 1f;
        _audioSource.dopplerLevel = 0f;
        _audioSource.spread = 0f;
        _audioSource.rolloffMode = AudioRolloffMode.Custom;
        ConfigureDistanceCurve();
        _audioSource.volume = Mathf.Clamp01(configuration.MasterVolume.Value);
        _lowPassFilter.cutoffFrequency = UnoccludedLowPassFrequency;
    }

    public Transform Anchor => _anchor;

    public float LastPacketTime => _lastPacketTime;

    public void Rebind(Transform anchor)
    {
        _anchor = anchor;
        _host.transform.SetParent(anchor, false);
        _host.transform.localPosition = Vector3.up * 1.25f;
    }

    public void Enqueue(in VoicePacket packet, float arrivalTime)
    {
        if (_disposed || packet.Kind != VoicePacketKind.Audio)
        {
            return;
        }

        _lastPacketTime = arrivalTime;
        _jitterBuffer.Enqueue(new EncodedVoiceFrame(
            packet.Sequence,
            packet.CaptureTimestampMilliseconds,
            arrivalTime,
            packet.Payload));
    }

    public void Tick(float nowSeconds, Transform? listener, bool audibleForPlayerState)
    {
        if (_disposed)
        {
            return;
        }

        if (_started
            && _lastTickTime > 0f
            && nowSeconds - _lastTickTime > Math.Max(0.25f, _queuedSamples / (float)_sampleRate + 0.05f))
        {
            ResetPlayback();
        }
        _lastTickTime = nowSeconds;
        UpdateConsumedSamples();
        if (!audibleForPlayerState)
        {
            _jitterBuffer.Reset();
            if (!_suppressedByPlayerState)
            {
                ResetPlayback();
                _suppressedByPlayerState = true;
            }
            return;
        }
        _suppressedByPlayerState = false;
        var isInAudibleRange = listener is null
            || Vector3.SqrMagnitude(listener.position - _host.transform.position)
            <= _audioSource.maxDistance * _audioSource.maxDistance;
        if (!isInAudibleRange)
        {
            DiscardBufferedAudio(nowSeconds);
            if (!_outOfRange)
            {
                ResetPlayback();
                _outOfRange = true;
            }
            return;
        }
        _outOfRange = false;

        var decodeBudget = 8;
        while (decodeBudget-- > 0 && _jitterBuffer.TryDequeue(nowSeconds, out var frame))
        {
            if (_decoder.TryDecode(frame.Payload, out var samples, out var sampleCount))
            {
                WriteSamples(samples, sampleCount);
            }
        }

        if (!_started && _queuedSamples >= _startThresholdSamples)
        {
            _audioSource.timeSamples = 0;
            _lastPlaybackPosition = 0;
            _audioSource.Play();
            _started = true;
        }
        else if (_started && _queuedSamples <= 0)
        {
            ResetPlayback();
        }

        UpdateOcclusion(nowSeconds, listener);
    }

    private void ConfigureDistanceCurve()
    {
        _audioSource.minDistance = FullVolumeDistanceMetres;
        _audioSource.maxDistance = MaximumAudibleDistanceMetres;
        // AudioSource custom-rolloff curves use normalized distance: 0 is minDistance and 1 is
        // maxDistance. World-space keys would leave this fixed audible range at full volume. The
        // final key is exactly zero, unlike Unity's logarithmic rolloff.
        _audioSource.SetCustomCurve(
            AudioSourceCurveType.CustomRolloff,
            new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(0.18f, 0.92f),
                new Keyframe(0.55f, 0.42f),
                new Keyframe(1f, 0f)));
    }

    private void DiscardBufferedAudio(float nowSeconds)
    {
        var discardBudget = 32;
        while (discardBudget-- > 0 && _jitterBuffer.TryDequeue(nowSeconds, out _))
        {
        }
    }

    private void WriteSamples(float[] samples, int sampleCount)
    {
        if (sampleCount <= 0 || sampleCount > samples.Length || sampleCount >= _clipSamples)
        {
            return;
        }

        // If decode gets more than one circular clip ahead of playback, stale speech is worse than
        // a clean resynchronization. Drop the old queue and restart with the newest utterance.
        if (_queuedSamples + sampleCount >= _clipSamples - _startThresholdSamples)
        {
            ResetPlayback();
        }

        var firstLength = Math.Min(sampleCount, _clipSamples - _writePosition);
        SetClipData(samples, 0, firstLength, _writePosition);
        var secondLength = sampleCount - firstLength;
        if (secondLength > 0)
        {
            SetClipData(samples, firstLength, secondLength, 0);
        }
        _writePosition = (_writePosition + sampleCount) % _clipSamples;
        _queuedSamples += sampleCount;
    }

    private void SetClipData(float[] source, int sourceOffset, int length, int clipOffset)
    {
        if (!_uploadBuffers.TryGetValue(length, out var data))
        {
            data = new Il2CppStructArray<float>(length);
            if (_uploadBuffers.Count < 32)
            {
                _uploadBuffers.Add(length, data);
            }
        }
        // Unity clamps AudioSource.volume to 0..1. Apply only the portion above 100% to PCM so the
        // 500% setting is an actual gain control rather than a cosmetic slider extension.
        var boost = Math.Max(1f, _configuration.MasterVolume.Value);
        for (var index = 0; index < length; index++)
        {
            data[index] = Mathf.Clamp(source[sourceOffset + index] * boost, -1f, 1f);
        }
        _clip.SetData(data, clipOffset);
    }

    private void UpdateConsumedSamples()
    {
        if (!_started || !_audioSource.isPlaying)
        {
            return;
        }

        var current = _audioSource.timeSamples;
        var consumed = current >= _lastPlaybackPosition
            ? current - _lastPlaybackPosition
            : _clipSamples - _lastPlaybackPosition + current;
        _lastPlaybackPosition = current;
        _queuedSamples = Math.Max(0, _queuedSamples - consumed);
    }

    private void UpdateOcclusion(float nowSeconds, Transform? listener)
    {
        if (listener is not null
            && nowSeconds >= _nextOcclusionProbe)
        {
            _nextOcclusionProbe = nowSeconds + OcclusionProbeIntervalSeconds;
            var wasOccluded = _isOccluded;
            var blockingColliderName = string.Empty;
            var from = listener.position;
            var offset = _host.transform.position - from;
            var distance = offset.magnitude;
            var hitCount = distance > 0.01f
                ? Physics.RaycastNonAlloc(
                    from,
                    offset / distance,
                    _occlusionHits,
                    distance,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore)
                : 0;
            _isOccluded = false;
            for (var index = 0; index < hitCount; index++)
            {
                var collider = _occlusionHits[index].collider;
                if (collider is null
                    || collider.Pointer == IntPtr.Zero
                    || BelongsToPlayer(collider.transform, listener)
                    || BelongsToPlayer(collider.transform, _anchor))
                {
                    continue;
                }

                _isOccluded = true;
                blockingColliderName = collider.gameObject.name;
                break;
            }
            if (wasOccluded != _isOccluded && _configuration.EnableLogging.Value)
            {
                _logger.LogInfo(
                    $"Proximity voice occlusion: peer={_peerLabel}, occluded={_isOccluded}, "
                    + $"distance={distance:F2}, blocker={blockingColliderName}");
            }
        }
        else if (listener is null)
        {
            _isOccluded = false;
        }

        var target = _isOccluded ? 1f : 0f;
        _occlusionAmount = Mathf.MoveTowards(_occlusionAmount, target, Time.unscaledDeltaTime * 7f);
        var unoccludedVolume = Mathf.Clamp01(_configuration.MasterVolume.Value);
        _audioSource.volume = unoccludedVolume * Mathf.Lerp(
            1f,
            _configuration.OccludedVolumeMultiplier.Value,
            _occlusionAmount);
        _lowPassFilter.cutoffFrequency = Mathf.Lerp(
            UnoccludedLowPassFrequency,
            _configuration.OccludedLowPassFrequency.Value,
            _occlusionAmount);
    }

    private static bool BelongsToPlayer(Transform? candidate, Transform playerTransform)
    {
        if (candidate is null || candidate.Pointer == IntPtr.Zero)
        {
            return false;
        }

        var candidateRoot = candidate.root;
        var playerRoot = playerTransform.root;
        return candidateRoot is not null
            && playerRoot is not null
            && candidateRoot.Pointer == playerRoot.Pointer;
    }

    private void ResetPlayback()
    {
        _audioSource.Stop();
        _clip.SetData(_silence, 0);
        _writePosition = 0;
        _lastPlaybackPosition = 0;
        _queuedSamples = 0;
        _started = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _audioSource.Stop();
        UnityEngine.Object.Destroy(_clip);
        UnityEngine.Object.Destroy(_host);
        _jitterBuffer.Reset();
    }
}
