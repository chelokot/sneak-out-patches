namespace SneakOut.UnlockEverything;

internal static class SkinPartCatalogPolicy
{
    public static bool IsLocallyPurchasable<TEnum>(TEnum value, TEnum none)
        where TEnum : struct, Enum
    {
        return !EqualityComparer<TEnum>.Default.Equals(value, none);
    }

    public static TEnum[] AllConcreteEnumValues<TEnum>(TEnum none)
        where TEnum : struct, Enum
    {
        return Enum.GetValues<TEnum>()
            .Where(value => !EqualityComparer<TEnum>.Default.Equals(value, none))
            .Distinct()
            .OrderBy(value => Convert.ToInt64(value))
            .ToArray();
    }
}
