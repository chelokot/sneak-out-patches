namespace SneakOut.UnlockEverything;

internal static class LocalSkinEconomy
{
    public const int SkinPartGoldPrice = 1_000;

    internal readonly record struct OverlayResult(int DisplayedGold, int ChargedPurchaseCount);

    public static int DisplayedGold(int authoritativeGold, int localPurchaseCount)
    {
        var safeGold = Math.Max(0, authoritativeGold);
        var safePurchaseCount = Math.Max(0, localPurchaseCount);
        var totalCost = Math.Min((long)int.MaxValue, (long)safePurchaseCount * SkinPartGoldPrice);
        return (int)Math.Max(0L, safeGold - totalCost);
    }

    public static bool CanPurchase(int displayedGold) => displayedGold >= SkinPartGoldPrice;

    public static OverlayResult ResolveOverlay(
        int currentGold,
        int purchaseCount,
        int? previousDisplayedGold,
        int previousChargedPurchaseCount)
    {
        var safePurchaseCount = Math.Max(0, purchaseCount);
        if (previousDisplayedGold is not { } previous)
        {
            return new OverlayResult(
                DisplayedGold(currentGold, safePurchaseCount),
                safePurchaseCount);
        }

        var newlyUnchargedPurchases = Math.Max(0, safePurchaseCount - previousChargedPurchaseCount);
        if (newlyUnchargedPurchases == 0 && currentGold == previous)
        {
            return new OverlayResult(previous, safePurchaseCount);
        }

        if (newlyUnchargedPurchases > 0)
        {
            var expectedStockDebit = DisplayedGold(previous, newlyUnchargedPurchases);
            if (currentGold == expectedStockDebit)
            {
                // SpookedShopNewMeta already applied the successful local task's price to
                // this in-memory WebPlayer. Adopt that value instead of charging it twice.
                return new OverlayResult(currentGold, safePurchaseCount);
            }

            if (currentGold == previous)
            {
                // Some shop paths refresh the profile before their continuation performs
                // the stock debit. Apply the local ledger exactly once in that ordering.
                return new OverlayResult(
                    DisplayedGold(currentGold, newlyUnchargedPurchases),
                    safePurchaseCount);
            }
        }

        // A different value is an authoritative backend refresh. Server Gold is never
        // mutated; the durable local ledger is only layered over the displayed copy.
        return new OverlayResult(
            DisplayedGold(currentGold, safePurchaseCount),
            safePurchaseCount);
    }
}
