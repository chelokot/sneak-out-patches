namespace SneakOut.ProximityVoiceChat;

internal static class VoiceAudibilityPolicy
{
    public static bool CanHear(bool localPlayerIsDead, bool remotePlayerIsDead)
    {
        // Ghosts hear both channels. Living players hear only the living channel.
        return localPlayerIsDead || !remotePlayerIsDead;
    }
}
