namespace SneakOut.ProximityVoiceChat;

internal static class VoiceDistancePolicy
{
    public const float FullVolumeDistanceMetres = 2.5f;
    public const float MaximumAudibleDistanceMetres = 20f;

    public static bool IsAudible(float distanceMetres)
    {
        return float.IsFinite(distanceMetres)
            && distanceMetres <= MaximumAudibleDistanceMetres;
    }

    public static float EvaluateVolume(float distanceMetres)
    {
        if (!float.IsFinite(distanceMetres))
        {
            return 0f;
        }
        if (distanceMetres <= FullVolumeDistanceMetres)
        {
            return 1f;
        }
        if (distanceMetres >= MaximumAudibleDistanceMetres)
        {
            return 0f;
        }

        var normalized = (distanceMetres - FullVolumeDistanceMetres)
            / (MaximumAudibleDistanceMetres - FullVolumeDistanceMetres);
        if (normalized <= 0.18f)
        {
            return Interpolate(normalized, 0f, 1f, 0.18f, 0.92f);
        }
        if (normalized <= 0.55f)
        {
            return Interpolate(normalized, 0.18f, 0.92f, 0.55f, 0.42f);
        }
        return Interpolate(normalized, 0.55f, 0.42f, 1f, 0f);
    }

    private static float Interpolate(float value, float fromTime, float fromValue, float toTime, float toValue)
    {
        var amount = (value - fromTime) / (toTime - fromTime);
        // Runtime-created Unity keyframes default to zero in/out tangents. Smoothstep reproduces
        // the previous AnimationCurve segments while allowing route distance to be evaluated
        // independently from the AudioSource transform.
        amount = amount * amount * (3f - 2f * amount);
        return fromValue + (toValue - fromValue) * amount;
    }
}
