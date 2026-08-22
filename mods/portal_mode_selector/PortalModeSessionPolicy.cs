namespace SneakOut.PortalModeSelector;

internal static class PortalModeSessionPolicy
{
    public static bool ShouldPublishGameMode(string? lobbyType)
    {
        return string.Equals(lobbyType, "Game", StringComparison.Ordinal);
    }
}
