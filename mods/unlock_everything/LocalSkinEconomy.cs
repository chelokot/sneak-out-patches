namespace SneakOut.UnlockEverything;

internal static class LocalSkinEconomy
{
    public const int SkinPartGoldPrice = 1_000;

    public static int DisplayedGold(int authoritativeGold, int localPurchaseCount)
    {
        var safeGold = Math.Max(0, authoritativeGold);
        var safePurchaseCount = Math.Max(0, localPurchaseCount);
        var totalCost = Math.Min((long)int.MaxValue, (long)safePurchaseCount * SkinPartGoldPrice);
        return (int)Math.Max(0L, safeGold - totalCost);
    }

    public static bool CanPurchase(int displayedGold) => displayedGold >= SkinPartGoldPrice;
}
