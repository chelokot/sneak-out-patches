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
        IEnumerable<int> participantPlayerRaws,
        int leaderPlayerRaw,
        string leaderHostId,
        out LeaderHostTarget target)
    {
        target = default;
        if (string.IsNullOrWhiteSpace(leaderHostId))
        {
            return false;
        }

        foreach (var playerRaw in participantPlayerRaws)
        {
            if (playerRaw != leaderPlayerRaw)
            {
                continue;
            }

            target = new LeaderHostTarget(playerRaw, leaderHostId);
            return true;
        }

        return false;
    }
}
