namespace SneakOut.UnlockEverything;

internal static class TitleAccessPolicy
{
    private const int FirstSupportedTitle = 1;
    private const int LastSupportedTitle = 18;
    private const int HiddenRarity = 4;

    public static bool ShouldShowInMenu(int descriptionType, int rarity, bool revealHiddenRarity)
    {
        return descriptionType is >= FirstSupportedTitle and <= LastSupportedTitle
            && (rarity != HiddenRarity || revealHiddenRarity);
    }
}
