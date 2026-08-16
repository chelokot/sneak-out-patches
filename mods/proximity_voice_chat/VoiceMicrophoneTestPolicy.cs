namespace SneakOut.ProximityVoiceChat;

internal enum VoiceMicrophoneTestState
{
    Idle,
    Capturing,
    Draining,
}

internal static class VoiceMicrophoneTestPolicy
{
    public static bool IsActive(bool testRequested, VoiceMicrophoneTestState state)
    {
        return testRequested || state != VoiceMicrophoneTestState.Idle;
    }

    public static bool ShouldCancelForSettingsState(bool controlsAvailable, bool audioTabActive)
    {
        return !controlsAvailable || !audioTabActive;
    }
}
