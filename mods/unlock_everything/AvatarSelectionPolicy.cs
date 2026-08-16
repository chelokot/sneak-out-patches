namespace SneakOut.UnlockEverything;

internal static class AvatarSelectionPolicy
{
    public static int PreserveOwnedProductId(int existingId, int syntheticId)
    {
        return existingId > 0 ? existingId : syntheticId;
    }
}
