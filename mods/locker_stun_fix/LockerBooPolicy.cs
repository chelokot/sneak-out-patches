namespace SneakOut.LockerStunFix;

internal enum LockerOpenObservation
{
    IgnoredInvalid,
    IgnoredUnavailable,
    IgnoredEmpty,
    IgnoredOccupant,
    RecordedExternalOpener,
    RefreshedExternalOpener
}

internal enum LockerBooDecision
{
    AllowVanillaNoExternalOpen,
    AllowVanillaDifferentOccupant,
    SuppressExternalOpen
}

internal readonly record struct ExternalOpenState(int OpenerPlayerId, int OccupantPlayerId, string Source);

/// <summary>
/// Tracks the event that vanilla discards: who opened an occupied locker.
/// IsOpen cannot answer that question because ComeOut opens the door itself
/// before HandleBooSkill runs.
/// </summary>
internal sealed class LockerBooPolicy<TKey> where TKey : notnull
{
    private readonly Dictionary<TKey, ExternalOpenState> _externalOpens = new();

    public LockerOpenObservation ObserveOpen(
        TKey locker,
        int openerPlayerId,
        int occupantPlayerId,
        bool isOpen,
        bool duringInteraction,
        string source)
    {
        if (openerPlayerId < 0)
        {
            return LockerOpenObservation.IgnoredInvalid;
        }

        // Mirror the first two guards in the stock Open/TryToOpen methods. A
        // rejected duplicate call must not create a false forced-open marker.
        if (isOpen || duringInteraction)
        {
            return LockerOpenObservation.IgnoredUnavailable;
        }

        if (occupantPlayerId < 0)
        {
            return LockerOpenObservation.IgnoredEmpty;
        }

        // ComeOut calls Open with the occupant's own id. This is the normal
        // self-exit path and must retain vanilla Boo behavior.
        if (openerPlayerId == occupantPlayerId)
        {
            return LockerOpenObservation.IgnoredOccupant;
        }

        var next = new ExternalOpenState(openerPlayerId, occupantPlayerId, source);
        if (_externalOpens.TryGetValue(locker, out var current) && current == next)
        {
            return LockerOpenObservation.RefreshedExternalOpener;
        }

        _externalOpens[locker] = next;
        return LockerOpenObservation.RecordedExternalOpener;
    }

    public LockerBooDecision ConsumeForExit(
        TKey locker,
        int exitingPlayerId,
        out ExternalOpenState externalOpen)
    {
        if (!_externalOpens.TryGetValue(locker, out externalOpen))
        {
            return LockerBooDecision.AllowVanillaNoExternalOpen;
        }

        if (externalOpen.OccupantPlayerId != exitingPlayerId)
        {
            return LockerBooDecision.AllowVanillaDifferentOccupant;
        }

        _externalOpens.Remove(locker);
        return LockerBooDecision.SuppressExternalOpen;
    }

    public bool Clear(TKey locker)
    {
        return _externalOpens.Remove(locker);
    }
}
