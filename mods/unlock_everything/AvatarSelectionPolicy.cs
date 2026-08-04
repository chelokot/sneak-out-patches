namespace SneakOut.UnlockEverything;

internal static class AvatarSelectionPolicy
{
    public static int PreserveOwnedProductId(int existingId, int syntheticId)
    {
        return existingId > 0 ? existingId : syntheticId;
    }

    public static string GetTitleDisplayText(string? currentText, string enumName)
    {
        if (!LooksLikeMissingTranslation(currentText))
        {
            return currentText ?? string.Empty;
        }

        var normalizedName = enumName.StartsWith("TITLE_", StringComparison.OrdinalIgnoreCase)
            ? enumName["TITLE_".Length..]
            : enumName;
        var correctedName = normalizedName.ToLowerInvariant() switch
        {
            "aristocrate" => "aristocrat",
            "ambasador_of_china" => "ambassador_of_china",
            _ => normalizedName,
        };

        return string.Join(
            " ",
            correctedName
                .Split('_', StringSplitOptions.RemoveEmptyEntries)
                .Select(Capitalize));
    }

    private static bool LooksLikeMissingTranslation(string? text)
    {
        return string.IsNullOrWhiteSpace(text)
            || text.StartsWith("TITLE_", StringComparison.OrdinalIgnoreCase);
    }

    private static string Capitalize(string word)
    {
        return word.Length switch
        {
            0 => string.Empty,
            1 => word.ToUpperInvariant(),
            _ => char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant(),
        };
    }
}
