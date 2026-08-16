namespace SneakOut.UnlockEverything;

internal static class PersistentSelectionPolicy
{
    public static bool HasSkinPartSelection(
        int? head,
        int? chest,
        int? legs,
        int? hands,
        int? back,
        int? whole)
    {
        return head.HasValue
            || chest.HasValue
            || legs.HasValue
            || hands.HasValue
            || back.HasValue
            || whole.HasValue;
    }

    public static bool IsLegacyEmptyAppearance(
        int? characterSkin,
        int? head,
        int? chest,
        int? legs,
        int? hands,
        int? back,
        int? whole)
    {
        return characterSkin is 0
            && head is 0
            && chest is 0
            && legs is 0
            && hands is 0
            && back is 0
            && whole is 0;
    }
}
