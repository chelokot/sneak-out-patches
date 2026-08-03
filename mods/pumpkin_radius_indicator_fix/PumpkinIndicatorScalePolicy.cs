namespace SneakOut.PumpkinRadiusIndicatorFix;

internal readonly record struct Scale3(float X, float Y, float Z);

internal static class PumpkinIndicatorScalePolicy
{
    private const float MinimumParentScale = 0.0001f;

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
