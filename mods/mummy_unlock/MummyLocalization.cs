using Localization;
using Types;

namespace SneakOut.MummyUnlock;

internal static class MummyLocalization
{
    public const string CharacterNameKey = "SNEAKOUT_MUMMY_NAME";
    public const string SandTrapNameKey = "SNEAKOUT_MUMMY_SAND_TRAP";
    public const string SarcophagusNameKey = "SNEAKOUT_MUMMY_SARCOPHAGUS";
    public const string SarcophagusDescriptionKey = "SNEAKOUT_MUMMY_SARCOPHAGUS_DESCRIPTION";

    public static string Translate(string key)
    {
        var language = LanguagesManager.CurrentLanguage;
        return key switch
        {
            CharacterNameKey => CharacterName(language),
            SandTrapNameKey => SandTrapName(language),
            SarcophagusNameKey => SarcophagusName(language),
            SarcophagusDescriptionKey => SarcophagusDescription(language),
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown Mummy localization key")
        };
    }

    private static string CharacterName(SpookedLanguage language)
    {
        return language switch
        {
            SpookedLanguage.English => "Mummy",
            SpookedLanguage.Polish => "Mumia",
            SpookedLanguage.French => "Momie",
            SpookedLanguage.Italian => "Mummia",
            SpookedLanguage.German => "Mumie",
            SpookedLanguage.Spanish => "Momia",
            SpookedLanguage.Japanese => "ミイラ",
            SpookedLanguage.Portuguese => "Múmia",
            SpookedLanguage.PortugueseBrasil => "Múmia",
            SpookedLanguage.Arabic => "مومياء",
            SpookedLanguage.Korean => "미라",
            SpookedLanguage.Russian => "Мумия",
            SpookedLanguage.ChineseSimplified => "木乃伊",
            SpookedLanguage.ChineseTraditional => "木乃伊",
            SpookedLanguage.Turkish => "Mumya",
            SpookedLanguage.Hungarian => "Múmia",
            SpookedLanguage.Thai => "มัมมี่",
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, "Unsupported language")
        };
    }

    private static string SandTrapName(SpookedLanguage language)
    {
        return language switch
        {
            SpookedLanguage.English => "Sand Trap",
            SpookedLanguage.Polish => "Piaskowa pułapka",
            SpookedLanguage.French => "Piège de sable",
            SpookedLanguage.Italian => "Trappola di sabbia",
            SpookedLanguage.German => "Sandfalle",
            SpookedLanguage.Spanish => "Trampa de arena",
            SpookedLanguage.Japanese => "砂の罠",
            SpookedLanguage.Portuguese => "Armadilha de areia",
            SpookedLanguage.PortugueseBrasil => "Armadilha de areia",
            SpookedLanguage.Arabic => "فخ رملي",
            SpookedLanguage.Korean => "모래 함정",
            SpookedLanguage.Russian => "Песчаная ловушка",
            SpookedLanguage.ChineseSimplified => "沙地陷阱",
            SpookedLanguage.ChineseTraditional => "沙地陷阱",
            SpookedLanguage.Turkish => "Kum tuzağı",
            SpookedLanguage.Hungarian => "Homokcsapda",
            SpookedLanguage.Thai => "กับดักทราย",
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, "Unsupported language")
        };
    }

    private static string SarcophagusName(SpookedLanguage language)
    {
        return language switch
        {
            SpookedLanguage.English => "Sarcophagus",
            SpookedLanguage.Polish => "Sarkofag",
            SpookedLanguage.French => "Sarcophage",
            SpookedLanguage.Italian => "Sarcofago",
            SpookedLanguage.German => "Sarkophag",
            SpookedLanguage.Spanish => "Sarcófago",
            SpookedLanguage.Japanese => "石棺",
            SpookedLanguage.Portuguese => "Sarcófago",
            SpookedLanguage.PortugueseBrasil => "Sarcófago",
            SpookedLanguage.Arabic => "تابوت",
            SpookedLanguage.Korean => "석관",
            SpookedLanguage.Russian => "Саркофаг",
            SpookedLanguage.ChineseSimplified => "石棺",
            SpookedLanguage.ChineseTraditional => "石棺",
            SpookedLanguage.Turkish => "Lahit",
            SpookedLanguage.Hungarian => "Szarkofág",
            SpookedLanguage.Thai => "โลงศพ",
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, "Unsupported language")
        };
    }

    private static string SarcophagusDescription(SpookedLanguage language)
    {
        return language switch
        {
            SpookedLanguage.English => "Place a sarcophagus. Enter one to emerge from another.",
            SpookedLanguage.Polish => "Umieść sarkofag. Wejdź do jednego, aby wyjść z innego.",
            SpookedLanguage.French => "Placez un sarcophage. Entrez dans l'un pour ressortir d'un autre.",
            SpookedLanguage.Italian => "Posiziona un sarcofago. Entra in uno per uscire da un altro.",
            SpookedLanguage.German => "Stelle einen Sarkophag auf. Betritt einen, um aus einem anderen herauszukommen.",
            SpookedLanguage.Spanish => "Coloca un sarcófago. Entra en uno para salir por otro.",
            SpookedLanguage.Japanese => "石棺を設置する。1つに入ると別の石棺から出られる。",
            SpookedLanguage.Portuguese => "Coloca um sarcófago. Entra num para sair por outro.",
            SpookedLanguage.PortugueseBrasil => "Coloque um sarcófago. Entre em um para sair por outro.",
            SpookedLanguage.Arabic => "ضع تابوتًا. ادخل أحدها لتخرج من تابوت آخر.",
            SpookedLanguage.Korean => "석관을 설치합니다. 하나에 들어가 다른 석관으로 나옵니다.",
            SpookedLanguage.Russian => "Установите саркофаг. Войдите в один, чтобы выйти из другого.",
            SpookedLanguage.ChineseSimplified => "放置一具石棺。进入其中一个即可从另一个出来。",
            SpookedLanguage.ChineseTraditional => "放置一具石棺。進入其中一個即可從另一個出來。",
            SpookedLanguage.Turkish => "Bir lahit yerleştir. Birine girerek diğerinden çık.",
            SpookedLanguage.Hungarian => "Helyezz le egy szarkofágot. Lépj be az egyikbe, hogy egy másikon gyere ki.",
            SpookedLanguage.Thai => "วางโลงศพ เข้าไปในโลงหนึ่งเพื่อออกจากอีกโลงหนึ่ง",
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, "Unsupported language")
        };
    }
}
