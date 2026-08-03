namespace SneakOut.KeyboardLayoutFix;

internal readonly record struct NativeMovementDecision(
    bool ShouldOverride,
    float Horizontal,
    float Vertical,
    bool OwnsMovement);

internal static class NativeMovementPolicy
{
    public static NativeMovementDecision Resolve(
        bool russianLayout,
        bool wDown,
        bool aDown,
        bool sDown,
        bool dDown,
        bool previouslyOwned)
    {
        if (!russianLayout)
        {
            return new NativeMovementDecision(false, 0f, 0f, false);
        }

        var horizontal = (dDown ? 1f : 0f) - (aDown ? 1f : 0f);
        var vertical = (wDown ? 1f : 0f) - (sDown ? 1f : 0f);
        if (horizontal != 0f || vertical != 0f)
        {
            return new NativeMovementDecision(true, horizontal, vertical, true);
        }

        return previouslyOwned
            ? new NativeMovementDecision(true, 0f, 0f, false)
            : new NativeMovementDecision(false, 0f, 0f, false);
    }
}
