namespace SneakOut.UnlockEverything;

internal static class SkinPartCatalogPolicy
{
    public static TEnum[] AllConcreteEnumValues<TEnum>(TEnum none)
        where TEnum : struct, Enum
    {
        return Enum.GetValues<TEnum>()
            .Where(value => !EqualityComparer<TEnum>.Default.Equals(value, none))
            .Distinct()
            .ToArray();
    }
}
