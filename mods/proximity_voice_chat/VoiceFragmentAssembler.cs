namespace SneakOut.ProximityVoiceChat;

internal sealed class VoiceFragmentAssembler
{
    private const int MaximumConcurrentAssemblies = 24;
    private const float AssemblyLifetimeSeconds = 1.5f;

    private readonly Dictionary<uint, PendingAssembly> _pending = new();

    public bool TryAdd(in VoicePacket fragment, float nowSeconds, out VoicePacket completePacket)
    {
        completePacket = default;
        Prune(nowSeconds);
        if (fragment.Kind != VoicePacketKind.Audio)
        {
            completePacket = fragment;
            return true;
        }
        if (fragment.FragmentCount == 1)
        {
            completePacket = fragment;
            return true;
        }

        if (!_pending.TryGetValue(fragment.Sequence, out var assembly))
        {
            if (_pending.Count >= MaximumConcurrentAssemblies)
            {
                RemoveOldest();
            }
            assembly = new PendingAssembly(fragment, nowSeconds);
            _pending.Add(fragment.Sequence, assembly);
        }
        else if (!assembly.Matches(fragment))
        {
            _pending.Remove(fragment.Sequence);
            return false;
        }

        if (!assembly.Add(fragment.FragmentIndex, fragment.Payload) || !assembly.IsComplete)
        {
            return false;
        }

        _pending.Remove(fragment.Sequence);
        var payload = assembly.Combine();
        if (payload.Length == 0 || payload.Length > VoiceProtocol.MaximumPayloadLength)
        {
            return false;
        }
        completePacket = fragment with
        {
            FragmentIndex = 0,
            FragmentCount = 1,
            Payload = payload,
        };
        return true;
    }

    public void Reset()
    {
        _pending.Clear();
    }

    private void Prune(float nowSeconds)
    {
        foreach (var pair in _pending.ToArray())
        {
            if (nowSeconds - pair.Value.CreatedAt > AssemblyLifetimeSeconds)
            {
                _pending.Remove(pair.Key);
            }
        }
    }

    private void RemoveOldest()
    {
        var oldestSequence = 0u;
        var oldestTime = float.MaxValue;
        foreach (var pair in _pending)
        {
            if (pair.Value.CreatedAt < oldestTime)
            {
                oldestTime = pair.Value.CreatedAt;
                oldestSequence = pair.Key;
            }
        }
        _pending.Remove(oldestSequence);
    }

    private sealed class PendingAssembly
    {
        private readonly byte[]?[] _fragments;
        private readonly ulong _senderInstanceId;
        private readonly int _senderInternalId;
        private readonly uint _timestamp;
        private int _receivedCount;
        private int _totalBytes;

        public PendingAssembly(in VoicePacket first, float createdAt)
        {
            _fragments = new byte[first.FragmentCount][];
            _senderInstanceId = first.SenderInstanceId;
            _senderInternalId = first.SenderInternalId;
            _timestamp = first.CaptureTimestampMilliseconds;
            CreatedAt = createdAt;
        }

        public float CreatedAt { get; }

        public bool IsComplete => _receivedCount == _fragments.Length;

        public bool Matches(in VoicePacket packet)
        {
            return packet.FragmentCount == _fragments.Length
                && packet.SenderInstanceId == _senderInstanceId
                && packet.SenderInternalId == _senderInternalId
                && packet.CaptureTimestampMilliseconds == _timestamp;
        }

        public bool Add(ushort index, byte[] payload)
        {
            if (index >= _fragments.Length || _fragments[index] is not null)
            {
                return false;
            }
            if (_totalBytes + payload.Length > VoiceProtocol.MaximumPayloadLength)
            {
                return false;
            }
            _fragments[index] = payload;
            _receivedCount++;
            _totalBytes += payload.Length;
            return true;
        }

        public byte[] Combine()
        {
            var combined = new byte[_totalBytes];
            var offset = 0;
            foreach (var fragment in _fragments)
            {
                if (fragment is null)
                {
                    return Array.Empty<byte>();
                }
                Buffer.BlockCopy(fragment, 0, combined, offset, fragment.Length);
                offset += fragment.Length;
            }
            return combined;
        }
    }
}
