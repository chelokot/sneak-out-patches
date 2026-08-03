namespace SneakOut.MagicWardrobeHookFix;

internal sealed class MagicWardrobeHookPolicy
{
    private readonly HashSet<int> _activeEntries = new();
    private readonly Dictionary<int, float> _interruptedEntries = new();

    public bool BeginStep(int playerId, bool isWardrobeEntry, float now)
    {
        if (!isWardrobeEntry)
        {
            End(playerId);
            return false;
        }

        _activeEntries.Add(playerId);
        return _interruptedEntries.Remove(playerId, out var expiresAt) && now <= expiresAt;
    }

    public bool RecordHook(int playerId, float now, float lifetime)
    {
        if (!_activeEntries.Contains(playerId))
        {
            return false;
        }

        _interruptedEntries[playerId] = now + lifetime;
        return true;
    }

    public void End(int playerId)
    {
        _activeEntries.Remove(playerId);
        _interruptedEntries.Remove(playerId);
    }
}
