using BepInEx.Logging;
using Collections;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Types;
using UI.Views;
using UI.Views.Lobby;
using UnityEngine;

namespace SneakOut.MummyUnlock;

internal static class MummyUnlockRuntime
{
    private static readonly CharacterType MummyCharacterType = CharacterType.murderer_mummy;
    private static readonly CharacterType[] ShopSeekerOrder =
    {
        CharacterType.murderer_dracula,
        CharacterType.murderer_ripper,
        CharacterType.murderer_scarecrow,
        CharacterType.murderer_clown,
        CharacterType.murderer_butcher,
        CharacterType.murderer_mummy
    };

    private static ManualLogSource? _logger;
    private static Harmony? _harmony;
    private static bool _loggedAvailableSeekers;
    private static bool _loggedCharacterShopAvatars;
    private static bool _loggedMummySpriteCandidates;
    private static bool _loggedSeekerSelectionViewImages;

    public static void Initialize(ManualLogSource logger)
    {
        _logger = logger;
        MummyAbilityIconRuntime.Initialize();
        MummyAnimatorFallbackRuntime.Initialize(logger);
        MummySarcophagusVisualRuntime.Initialize(logger);
        _harmony ??= new Harmony(MummyUnlockPlugin.PluginGuid);
        _harmony.PatchAll();
        LogMummySpriteCandidatesOnce();
    }

    public static void EnsureAvailableSeekersContainMummy(SeekerSelectionViewModel viewModel)
    {
        viewModel.AvailableSeekers = AppendCharacter(viewModel.AvailableSeekers, MummyCharacterType);
    }

    public static void PrepareCharacterShop(CharacterShopView shopView)
    {
        try
        {
            var rawEntries = shopView._charactersToBuy;
            if (rawEntries is null || rawEntries.Count == 0)
            {
                return;
            }

            var entriesByCharacter = new Dictionary<CharacterType, CharacterShopView.CharacterToBuy>();
            for (var index = 0; index < rawEntries.Count; index++)
            {
                var entry = rawEntries[index];
                if (entry.CharacterType == CharacterType.victim_penguin)
                {
                    continue;
                }

                entriesByCharacter[entry.CharacterType] = entry;
            }

            if (!entriesByCharacter.ContainsKey(MummyCharacterType))
            {
                entriesByCharacter[MummyCharacterType] = CreateMummyEntry();
            }

            var sanitizedEntries = new List<CharacterShopView.CharacterToBuy>();
            foreach (var characterType in ShopSeekerOrder)
            {
                if (entriesByCharacter.TryGetValue(characterType, out var entry))
                {
                    sanitizedEntries.Add(entry);
                }
            }

            if (sanitizedEntries.Count == 0)
            {
                return;
            }

            shopView._charactersToBuy = sanitizedEntries.ToArray();
            shopView._currentCharacter = 0;
            shopView._indiciesToView = BuildIndicesToView(sanitizedEntries.Count, 0);
            shopView._canShift = sanitizedEntries.Count > 1;

            _logger?.LogInfo($"Prepared CharacterShopView with seeker-only list: [{string.Join(", ", sanitizedEntries.Select(entry => $"{entry.CharacterType}:{entry.NameKey}"))}]");
        }
        catch (Exception exception)
        {
            _logger?.LogError($"CharacterShopView preparation failed: {exception}");
        }
    }

    public static void LogAvailableSeekers(SeekerSelectionViewModel viewModel)
    {
        var availableSeekers = viewModel.AvailableSeekers;
        _logger?.LogInfo($"AvailableSeekers: {FormatCharacterArray(availableSeekers)}");
        _logger?.LogInfo($"AvailableSeekers contains mummy: {ContainsCharacter(availableSeekers, MummyCharacterType)}");

        if (_loggedAvailableSeekers)
        {
            return;
        }

        _loggedAvailableSeekers = true;
    }

    public static void LogCharacterShop(CharacterShopView shopView)
    {
        try
        {
            var charactersToBuy = shopView._charactersToBuy;
            if (charactersToBuy is null)
            {
                _logger?.LogWarning("CharacterShopView._charactersToBuy is null");
                return;
            }

            var entries = new List<string>();
            for (var index = 0; index < charactersToBuy.Count; index++)
            {
                var entry = charactersToBuy[index];
                entries.Add($"{entry.CharacterType}:{entry.NameKey}");
            }

            _logger?.LogInfo($"CharacterShopView currentCharacter={shopView._currentCharacter} canShift={shopView._canShift}");
            _logger?.LogInfo($"CharacterShopView indiciesToView: {FormatIntArray(shopView._indiciesToView)}");
            _logger?.LogInfo($"CharacterShopView._charactersToBuy: [{string.Join(", ", entries)}]");
            _logger?.LogInfo($"CharacterShopView contains mummy: {entries.Any(entry => entry.Contains(MummyCharacterType.ToString(), StringComparison.Ordinal))}");
        }
        catch (Exception exception)
        {
            _logger?.LogError($"CharacterShopView logging failed: {exception}");
        }
    }

    public static void LogCharacterShopStep(string source, CharacterShopView shopView)
    {
        try
        {
            RefreshCharacterShopUi(shopView);
            _logger?.LogInfo($"CharacterShopView[{source}] currentCharacter={shopView._currentCharacter} canShift={shopView._canShift} indiciesToView={FormatIntArray(shopView._indiciesToView)}");
        }
        catch (Exception exception)
        {
            _logger?.LogError($"CharacterShopView[{source}] state logging failed: {exception}");
        }
    }

    public static void PrepareCharacterShopForShift(string source, CharacterShopView shopView)
    {
        try
        {
            var charactersToBuy = shopView._charactersToBuy;
            if (charactersToBuy is null || charactersToBuy.Count == 0)
            {
                return;
            }

            var clampedCurrentCharacter = ClampCurrentCharacter(shopView._currentCharacter, charactersToBuy.Count);
            shopView._currentCharacter = clampedCurrentCharacter;
            shopView._indiciesToView = BuildIndicesToView(charactersToBuy.Count, clampedCurrentCharacter);
            shopView._canShift = charactersToBuy.Count > 1;
            _logger?.LogInfo($"CharacterShopView[{source}:prefix] currentCharacter={shopView._currentCharacter} canShift={shopView._canShift} indiciesToView={FormatIntArray(shopView._indiciesToView)}");
        }
        catch (Exception exception)
        {
            _logger?.LogError($"CharacterShopView[{source}:prefix] prepare failed: {exception}");
        }
    }

    public static void LogSeekerSelectionView(UI.Views.SeekerSelectionView view)
    {
        try
        {
            var images = view._selectionsImages;
            var selectionIndices = view._selectionIndices;
            var imageSummaries = new List<string>();
            for (var index = 0; index < images.Count; index++)
            {
                var image = images[index];
                var spriteName = image.sprite is null ? "null" : image.sprite.name;
                imageSummaries.Add($"{index}:{image.gameObject.name}:{spriteName}");
            }

            _logger?.LogInfo($"SeekerSelectionView selectionIndices: {FormatIntArray(selectionIndices)}");
            _logger?.LogInfo($"SeekerSelectionView images: [{string.Join(", ", imageSummaries)}]");

            if (_loggedSeekerSelectionViewImages)
            {
                return;
            }

            _loggedSeekerSelectionViewImages = true;
        }
        catch (Exception exception)
        {
            _logger?.LogError($"SeekerSelectionView logging failed: {exception}");
        }
    }

    public static bool TryRenderCharacterShopDescription(CharacterShopView shopView)
    {
        try
        {
            var charactersToBuy = shopView._charactersToBuy;
            if (charactersToBuy is null || charactersToBuy.Count == 0)
            {
                return false;
            }

            var currentCharacter = ClampCurrentCharacter(shopView._currentCharacter, charactersToBuy.Count);
            shopView._currentCharacter = currentCharacter;

            var currentEntry = charactersToBuy[currentCharacter];
            if (currentEntry.CharacterType != MummyCharacterType)
            {
                return false;
            }

            shopView._characterImage.sprite = currentEntry.CharacterSprite;
            shopView._characterImage.overrideSprite = currentEntry.CharacterSprite;
            shopView._characterName.text = TranslateShopText(shopView, currentEntry.NameKey);
            shopView._firstSkillName.text = TranslateShopText(shopView, currentEntry.FirstSkillNameKey);
            shopView._secondSkillName.text = TranslateShopText(shopView, currentEntry.SecondSkillNameKey);
            shopView._firstSkillDescription.text = TranslateShopText(shopView, currentEntry.FirstSkillDescriptionKey);
            shopView._secondSkillDescription.text = TranslateShopText(shopView, currentEntry.SecondSkillDescriptionKey);
            MummyAbilityIconRuntime.ApplyToCharacterShopView(shopView);

            if (shopView._buyButton is not null)
            {
                shopView._buyButton.interactable = false;
            }

            if (shopView._buyPanel is not null)
            {
                shopView._buyPanel.SetActive(false);
            }

            if (shopView._gamepadBuy is not null)
            {
                shopView._gamepadBuy.SetActive(false);
            }

            if (shopView._costText is not null)
            {
                shopView._costText.text = string.Empty;
            }

            return true;
        }
        catch (Exception exception)
        {
            _logger?.LogError($"CharacterShopView custom description render failed: {exception}");
            return false;
        }
    }

    private static string FormatCharacterArray(IReadOnlyCollection<CharacterType>? characters)
    {
        if (characters is null || characters.Count == 0)
        {
            return "[]";
        }

        return $"[{string.Join(", ", characters)}]";
    }

    private static string FormatIntArray(IReadOnlyCollection<int>? values)
    {
        if (values is null || values.Count == 0)
        {
            return "[]";
        }

        return $"[{string.Join(", ", values)}]";
    }

    private static bool ContainsCharacter(IEnumerable<CharacterType>? characters, CharacterType targetCharacter)
    {
        if (characters is null)
        {
            return false;
        }

        foreach (var character in characters)
        {
            if (character == targetCharacter)
            {
                return true;
            }
        }

        return false;
    }

    private static Il2CppStructArray<CharacterType> AppendCharacter(IEnumerable<CharacterType>? sourceCharacters, CharacterType targetCharacter)
    {
        var resultCharacters = new List<CharacterType>();
        if (sourceCharacters is not null)
        {
            foreach (var sourceCharacter in sourceCharacters)
            {
                resultCharacters.Add(sourceCharacter);
            }
        }

        if (!resultCharacters.Contains(targetCharacter))
        {
            resultCharacters.Add(targetCharacter);
        }

        return resultCharacters.ToArray();
    }

    private static Il2CppStructArray<int> BuildIndicesToView(int count, int currentIndex)
    {
        var indices = new int[7];
        for (var offset = -3; offset <= 3; offset++)
        {
            var wrappedIndex = (currentIndex + offset) % count;
            if (wrappedIndex < 0)
            {
                wrappedIndex += count;
            }

            indices[offset + 3] = wrappedIndex;
        }

        return indices;
    }

    private static CharacterShopView.CharacterToBuy CreateMummyEntry()
    {
        var mummyEntry = new CharacterShopView.CharacterToBuy
        {
            CharacterType = MummyCharacterType,
            NameKey = "Mummy",
            CharacterSprite = MummyAbilityIconRuntime.GetCharacterSprite(),
            FirstSkill = SpookedSkillType.MummySandTrap,
            SecondSkill = SpookedSkillType.MummySarcophagus,
            FirstSkillNameKey = "Sand Trap",
            FirstSkillDescriptionKey = "Summon sand trap",
            SecondSkillNameKey = "Sarcophagus",
            SecondSkillDescriptionKey = "Mummy skill 2 description"
        };
        return mummyEntry;
    }

    private static void RefreshCharacterShopTextsIfNeeded(CharacterShopView shopView)
    {
        TryRenderCharacterShopDescription(shopView);
    }

    private static void RefreshCharacterShopUi(CharacterShopView shopView)
    {
        var charactersToBuy = shopView._charactersToBuy;
        var characterAvatars = shopView._characterAvatars;
        if (charactersToBuy is null)
        {
            return;
        }

        var clampedCurrentCharacter = ClampCurrentCharacter(shopView._currentCharacter, charactersToBuy.Count);
        shopView._currentCharacter = clampedCurrentCharacter;
        shopView._indiciesToView = BuildIndicesToView(charactersToBuy.Count, clampedCurrentCharacter);
        shopView._canShift = charactersToBuy.Count > 1;

        if (characterAvatars is not null)
        {
            var visibleIndices = shopView._indiciesToView;
            var visibleCount = Math.Min(characterAvatars.Count, visibleIndices.Count);
            for (var index = 0; index < visibleCount; index++)
            {
                var avatarImage = characterAvatars[index];
                var entryIndex = visibleIndices[index];
                var entrySprite = charactersToBuy[entryIndex].CharacterSprite;
                avatarImage.sprite = entrySprite;
                avatarImage.overrideSprite = entrySprite;
                avatarImage.enabled = true;
            }

            for (var index = visibleCount; index < characterAvatars.Count; index++)
            {
                characterAvatars[index].enabled = false;
            }

            LogCharacterShopAvatarsOnce(characterAvatars);
        }

        RefreshCharacterShopTextsIfNeeded(shopView);
    }

    private static string TranslateShopText(CharacterShopView shopView, string rawKey)
    {
        if (string.IsNullOrWhiteSpace(rawKey))
        {
            return string.Empty;
        }

        var translator = shopView._gameTranslator;
        if (translator is null)
        {
            return rawKey;
        }

        try
        {
            return translator.Translate(rawKey);
        }
        catch
        {
            return rawKey;
        }
    }

    private static void LogCharacterShopAvatarsOnce(Il2CppReferenceArray<UnityEngine.UI.Image> characterAvatars)
    {
        if (_loggedCharacterShopAvatars)
        {
            return;
        }

        _loggedCharacterShopAvatars = true;
        var avatarSummaries = new List<string>();
        for (var index = 0; index < characterAvatars.Count; index++)
        {
            var avatarImage = characterAvatars[index];
            var spriteName = avatarImage.sprite is null ? "null" : avatarImage.sprite.name;
            avatarSummaries.Add($"{index}:{avatarImage.gameObject.name}:{spriteName}:{DescribeAvatarHierarchy(avatarImage.gameObject)}");
        }

        _logger?.LogInfo($"CharacterShopView avatar images: [{string.Join(", ", avatarSummaries)}]");
    }

    private static string DescribeAvatarHierarchy(GameObject gameObject)
    {
        var transform = gameObject.transform;
        var parts = new List<string>();
        for (var index = 0; index < transform.childCount; index++)
        {
            var child = transform.GetChild(index);
            var childImage = child.gameObject.GetComponent<UnityEngine.UI.Image>();
            var childSpriteName = childImage is null || childImage.sprite is null ? "null" : childImage.sprite.name;
            parts.Add($"{child.gameObject.name}:{childSpriteName}");
        }

        return parts.Count == 0 ? "no-children" : string.Join("|", parts);
    }

    private static void LogMummySpriteCandidatesOnce()
    {
        if (_loggedMummySpriteCandidates)
        {
            return;
        }

        _loggedMummySpriteCandidates = true;
        try
        {
            var spriteCandidates = Resources.FindObjectsOfTypeAll<Sprite>()
                .Where(sprite => sprite is not null && sprite.name.Contains("mummy", StringComparison.OrdinalIgnoreCase))
                .Select(sprite => sprite.name)
                .Distinct()
                .OrderBy(name => name)
                .ToArray();
            _logger?.LogInfo($"Mummy sprite candidates: [{string.Join(", ", spriteCandidates)}]");
        }
        catch (Exception exception)
        {
            _logger?.LogError($"Mummy sprite candidate scan failed: {exception}");
        }
    }

    private static int ClampCurrentCharacter(int currentCharacter, int characterCount)
    {
        if (characterCount == 0)
        {
            return 0;
        }

        if (currentCharacter < 0)
        {
            return 0;
        }

        if (currentCharacter >= characterCount)
        {
            return characterCount - 1;
        }

        return currentCharacter;
    }

    private static T? GetPropertyValue<T>(object instance, string propertyName)
    {
        var property = AccessTools.Property(instance.GetType(), propertyName);
        if (property is null)
        {
            return default;
        }

        var value = property.GetValue(instance);
        if (value is null)
        {
            return default;
        }

        return (T)value;
    }
}
