namespace SneakOut.PumpkinRadiusIndicatorFix;

internal readonly record struct Scale3(float X, float Y, float Z);
internal readonly record struct PumpkinRadii(float Trigger, float Kill, float Stun);

internal static class PumpkinIndicatorScalePolicy
{
    private const float MinimumParentScale = 0.0001f;
    public const float StunIndicatorOpacity = 0.2f;
    public const float ExplosionIndicatorDurationSeconds = 1.5f;

    public static bool TryResolveRadii(float skillRange, float stunExtension, out PumpkinRadii radii)
    {
        radii = default;
        if (!float.IsFinite(skillRange) || skillRange <= 0f
            || !float.IsFinite(stunExtension) || stunExtension < 0f)
        {
            return false;
        }

        // The current client uses SkillSettings.Range both for the periodic trigger query and
        // for the instant-kill comparison. Only the outer stun query adds the gameplay setting.
        radii = new PumpkinRadii(skillRange, skillRange, skillRange + stunExtension);
        return float.IsFinite(radii.Stun) && radii.Stun > 0f;
    }

    public static bool TryCalculate(float radius, Scale3 parentLossyScale, out Scale3 localScale)
    {
        localScale = default;
        if (!float.IsFinite(radius) || radius <= 0f
            || !IsUsable(parentLossyScale.X)
            || !IsUsable(parentLossyScale.Y)
            || !IsUsable(parentLossyScale.Z))
        {
            return false;
        }

        localScale = new Scale3(
            radius / MathF.Abs(parentLossyScale.X),
            radius / MathF.Abs(parentLossyScale.Y),
            radius / MathF.Abs(parentLossyScale.Z));
        return true;
    }

    private static bool IsUsable(float value)
    {
        return float.IsFinite(value) && MathF.Abs(value) >= MinimumParentScale;
    }
}
