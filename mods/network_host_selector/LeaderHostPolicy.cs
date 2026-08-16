namespace SneakOut.NetworkHostSelector;

internal readonly record struct LeaderHostTarget(int PlayerRaw, string UserId);

internal static class LeaderHostPolicy
{
    public static bool ShouldOverrideAssignedHost(
        bool privateGame,
        string assignedHostId,
        string leaderHostId)
    {
        return privateGame
            && !string.IsNullOrWhiteSpace(assignedHostId)
            && !string.IsNullOrWhiteSpace(leaderHostId)
            && !string.Equals(assignedHostId, leaderHostId, StringComparison.Ordinal);
    }

    public static bool TryResolve(
        IEnumerable<(int PlayerRaw, string UserId)> participants,
        int leaderPlayerRaw,
        string leaderHostId,
        out LeaderHostTarget target)
    {
        target = default;
        if (string.IsNullOrWhiteSpace(leaderHostId))
        {
            return false;
        }

        foreach (var participant in participants)
        {
            if (participant.PlayerRaw != leaderPlayerRaw
                || string.IsNullOrWhiteSpace(participant.UserId))
            {
                continue;
            }

            target = new LeaderHostTarget(participant.PlayerRaw, leaderHostId);
            return true;
        }

        return false;
    }
}
