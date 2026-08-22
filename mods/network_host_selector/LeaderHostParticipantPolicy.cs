namespace SneakOut.NetworkHostSelector;

internal sealed record LeaderHostParticipant(
    int Raw,
    string Name,
    bool IsRealPlayer,
    bool IsBot);

internal static class LeaderHostParticipantPolicy
{
    public static IReadOnlyList<LeaderHostParticipant> CreateSnapshot(
        IEnumerable<LeaderHostParticipant> observedPlayers)
    {
        return observedPlayers
            .Where(player => player.Raw > 0 && player.IsRealPlayer && !player.IsBot)
            .GroupBy(player => player.Raw)
            .Select(group => group.First())
            .OrderBy(player => player.Raw)
            .ToArray();
    }

    public static bool IsComplete(int sessionPlayerCount, int observedRealPlayerCount)
    {
        return sessionPlayerCount > 0 && observedRealPlayerCount == sessionPlayerCount;
    }
}
