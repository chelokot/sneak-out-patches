namespace SneakOut.ProximityVoiceChat;

internal sealed class VoicePeerAdmission
{
    private readonly HashSet<ulong> _candidates = new();
    private readonly HashSet<ulong> _accepted = new();

    public IEnumerable<ulong> KnownPeers => _candidates.Concat(_accepted).Distinct();

    public void Allow(ulong steamId)
    {
        if (steamId != 0)
        {
            _candidates.Add(steamId);
        }
    }

    public bool CanAcceptRequest(ulong steamId, bool currentlyAllowed)
    {
        return currentlyAllowed && _candidates.Contains(steamId);
    }

    public bool IsAccepted(ulong steamId) => _accepted.Contains(steamId);

    public void MarkAccepted(ulong steamId) => _accepted.Add(steamId);

    public void MarkDisconnected(ulong steamId) => _accepted.Remove(steamId);

    public bool Forget(ulong steamId)
    {
        return _candidates.Remove(steamId) | _accepted.Remove(steamId);
    }

    public void Clear()
    {
        _candidates.Clear();
        _accepted.Clear();
    }
}
