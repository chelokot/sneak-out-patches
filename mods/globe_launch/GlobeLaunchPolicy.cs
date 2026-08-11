namespace SneakOut.GlobeLaunch;

internal enum GlobeHitDecision
{
    WaitingForPlayers,
    FinalPlayerArmed,
    WaitingForFinalPlayer,
    Launch,
    AlreadyLaunched,
    Ignored,
}

internal readonly record struct GlobeHitOutcome(
    GlobeHitDecision Decision,
    int DistinctPlayerCount,
    int FinalPlayerId,
    int FinalPlayerHitCount);

internal sealed class GlobeLaunchPolicy<TKey> where TKey : notnull
{
    private sealed class GlobeState
    {
        public readonly HashSet<int> DistinctPlayers = new();
        public int FinalPlayerId = -1;
        public int FinalPlayerHitCount;
        public bool Launched;
    }

    private readonly Dictionary<TKey, GlobeState> _states = new();

    public GlobeHitOutcome ObserveHit(
        TKey globe,
        int playerId,
        int requiredDistinctPlayers,
        int requiredFinalPlayerHits)
    {
        if (playerId < 0 || requiredDistinctPlayers <= 0 || requiredFinalPlayerHits <= 0)
        {
            return new GlobeHitOutcome(GlobeHitDecision.Ignored, 0, -1, 0);
        }

        if (!_states.TryGetValue(globe, out var state))
        {
            state = new GlobeState();
            _states.Add(globe, state);
        }

        if (state.Launched)
        {
            return Outcome(state, GlobeHitDecision.AlreadyLaunched);
        }

        var isNewPlayer = state.DistinctPlayers.Add(playerId);
        if (state.FinalPlayerId < 0
            && isNewPlayer
            && state.DistinctPlayers.Count == requiredDistinctPlayers)
        {
            state.FinalPlayerId = playerId;
            state.FinalPlayerHitCount = 1;
            if (requiredFinalPlayerHits == 1)
            {
                state.Launched = true;
                return Outcome(state, GlobeHitDecision.Launch);
            }

            return Outcome(state, GlobeHitDecision.FinalPlayerArmed);
        }

        if (state.FinalPlayerId < 0)
        {
            return Outcome(state, GlobeHitDecision.WaitingForPlayers);
        }

        if (playerId != state.FinalPlayerId)
        {
            return Outcome(state, GlobeHitDecision.WaitingForFinalPlayer);
        }

        state.FinalPlayerHitCount++;
        if (state.FinalPlayerHitCount < requiredFinalPlayerHits)
        {
            return Outcome(state, GlobeHitDecision.WaitingForFinalPlayer);
        }

        state.Launched = true;
        return Outcome(state, GlobeHitDecision.Launch);
    }

    public void Reset(TKey globe)
    {
        _states.Remove(globe);
    }

    private static GlobeHitOutcome Outcome(GlobeState state, GlobeHitDecision decision)
    {
        return new GlobeHitOutcome(
            decision,
            state.DistinctPlayers.Count,
            state.FinalPlayerId,
            state.FinalPlayerHitCount);
    }
}
