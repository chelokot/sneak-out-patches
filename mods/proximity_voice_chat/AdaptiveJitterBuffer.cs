namespace SneakOut.ProximityVoiceChat;

internal readonly record struct EncodedVoiceFrame(
    uint Sequence,
    uint CaptureTimestampMilliseconds,
    float ArrivalTimeSeconds,
    byte[] Payload);

/// <summary>
/// Small adaptive packet buffer. It absorbs Steam relay jitter without turning every short
/// network wobble into an audible gap, while keeping the configured delay as a hard baseline.
/// </summary>
internal sealed class AdaptiveJitterBuffer
{
    private const int MaximumFrames = 192;
    private const int MaximumBufferedBytes = 1024 * 1024;

    private readonly Dictionary<uint, EncodedVoiceFrame> _frames = new();
    private readonly float _baseDelaySeconds;
    private readonly float _maximumDelaySeconds;
    private uint _nextSequence;
    private uint _lastSequence;
    private uint _lastCaptureTimestamp;
    private float _lastArrivalTime;
    private float _firstArrivalTime;
    private float _missingSince;
    private float _estimatedJitterSeconds;
    private int _bufferedBytes;
    private bool _initialized;
    private bool _playoutStarted;

    public AdaptiveJitterBuffer(float baseDelayMilliseconds, float maximumDelayMilliseconds)
    {
        _baseDelaySeconds = Math.Max(0.02f, baseDelayMilliseconds / 1000f);
        _maximumDelaySeconds = Math.Max(_baseDelaySeconds, maximumDelayMilliseconds / 1000f);
    }

    public float TargetDelaySeconds => Math.Clamp(
        _baseDelaySeconds + 4f * _estimatedJitterSeconds,
        _baseDelaySeconds,
        _maximumDelaySeconds);

    public void Enqueue(in EncodedVoiceFrame frame)
    {
        if (frame.Payload.Length == 0 || frame.Payload.Length > VoiceProtocol.MaximumPayloadLength)
        {
            return;
        }

        if (!_initialized)
        {
            _initialized = true;
            _nextSequence = frame.Sequence;
            _lastSequence = frame.Sequence;
            _lastCaptureTimestamp = frame.CaptureTimestampMilliseconds;
            _lastArrivalTime = frame.ArrivalTimeSeconds;
            _firstArrivalTime = frame.ArrivalTimeSeconds;
        }
        else
        {
            if (IsOlder(frame.Sequence, _nextSequence) || _frames.ContainsKey(frame.Sequence))
            {
                return;
            }
            if (_frames.Count == 0 && _playoutStarted)
            {
                _playoutStarted = false;
                _firstArrivalTime = frame.ArrivalTimeSeconds;
            }

            if (IsNewer(frame.Sequence, _lastSequence))
            {
                var captureDelta = unchecked(frame.CaptureTimestampMilliseconds - _lastCaptureTimestamp) / 1000f;
                var arrivalDelta = Math.Max(0f, frame.ArrivalTimeSeconds - _lastArrivalTime);
                var deviation = Math.Abs(arrivalDelta - captureDelta);
                _estimatedJitterSeconds += (deviation - _estimatedJitterSeconds) / 16f;
                _lastSequence = frame.Sequence;
                _lastCaptureTimestamp = frame.CaptureTimestampMilliseconds;
                _lastArrivalTime = frame.ArrivalTimeSeconds;
            }
        }

        _frames.Add(frame.Sequence, frame);
        _bufferedBytes += frame.Payload.Length;
        TrimExcess();
    }

    public bool TryDequeue(float nowSeconds, out EncodedVoiceFrame frame)
    {
        frame = default;
        if (!_initialized || _frames.Count == 0)
        {
            return false;
        }

        if (!_playoutStarted)
        {
            if (nowSeconds - _firstArrivalTime < TargetDelaySeconds)
            {
                return false;
            }
            _playoutStarted = true;
        }

        if (_frames.Remove(_nextSequence, out frame))
        {
            _bufferedBytes -= frame.Payload.Length;
            _nextSequence++;
            _missingSince = 0f;
            return true;
        }

        _missingSince = _missingSince <= 0f ? nowSeconds : _missingSince;
        var lossWait = Math.Clamp(TargetDelaySeconds * 0.35f, 0.025f, 0.12f);
        if (nowSeconds - _missingSince < lossWait)
        {
            return false;
        }

        // Jump to the nearest packet ahead of the loss rather than walking an attacker-controlled
        // sequence gap one number per rendered frame.
        _nextSequence = FindNearestSequence(_nextSequence);
        _missingSince = nowSeconds;
        return false;
    }

    public void Reset()
    {
        _frames.Clear();
        _bufferedBytes = 0;
        _initialized = false;
        _playoutStarted = false;
        _missingSince = 0f;
        _estimatedJitterSeconds = 0f;
    }

    private void TrimExcess()
    {
        while (_frames.Count > MaximumFrames || _bufferedBytes > MaximumBufferedBytes)
        {
            _nextSequence = FindNearestSequence(_nextSequence);
            if (!_frames.Remove(_nextSequence, out var discarded))
            {
                break;
            }
            _bufferedBytes -= discarded.Payload.Length;
            _nextSequence++;
        }
    }

    private uint FindNearestSequence(uint reference)
    {
        var nearest = reference;
        var nearestDistance = uint.MaxValue;
        foreach (var sequence in _frames.Keys)
        {
            var distance = unchecked(sequence - reference);
            if (distance < nearestDistance)
            {
                nearest = sequence;
                nearestDistance = distance;
            }
        }
        return nearest;
    }

    private static bool IsNewer(uint candidate, uint reference)
    {
        return unchecked((int)(candidate - reference)) > 0;
    }

    private static bool IsOlder(uint candidate, uint reference)
    {
        return unchecked((int)(candidate - reference)) < 0;
    }
}
