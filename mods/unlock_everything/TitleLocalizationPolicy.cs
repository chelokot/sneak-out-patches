namespace SneakOut.UnlockEverything;

internal static class TitleLocalizationPolicy
{
    public static IReadOnlyList<KeyValuePair<string, string>> MissingEntries { get; } =
        new KeyValuePair<string, string>[]
        {
            new("TITLE_FOUNDER", "Founder"),
            new("TITLE_MAYOR", "Mayor"),
            new("TITLE_MAGNATE", "Magnate"),
            new("TITLE_DISTINGUISHED", "Distinguished"),
            new("TITLE_FART_MASTER", "Fart Master"),
            new("TITLE_CHAIR_DESTROYER", "Chair Destroyer"),
            new("TITLE_ONION", "Onion"),
            new("TITLE_DUDE", "Dude"),
            new("TITLE_ARISTOCRATE", "Aristocrat"),
            new("TITLE_BOSS", "Boss"),
        };
}
