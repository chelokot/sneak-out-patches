using HarmonyLib;
using Localization;

namespace SneakOut.UnlockEverything;

[HarmonyPatch(typeof(GameTranslator), nameof(GameTranslator.ReloadDictionary))]
internal static class GameTranslatorReloadDictionaryPatch
{
    private static void Postfix(GameTranslator __instance)
    {
        var dictionary = __instance?._dictionary;
        if (dictionary is null)
        {
            return;
        }

        try
        {
            foreach (var entry in TitleLocalizationPolicy.MissingEntries)
            {
                if (!dictionary.ContainsKey(entry.Key))
                {
                    dictionary.Add(entry.Key, entry.Value);
                }
            }
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Failed to add missing title translations", exception);
        }
    }
}
