using BepInEx.Logging;
using Collections;
using Gameplay.Player.Components;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Types;
using UI.Views;
using UI.Views.Lobby;
using AvatarType = Kinguinverse.WebServiceProvider.Types_v2.AvatarType;

namespace SneakOut.MummyUnlock;

internal static class MummyUnlockRuntime
{
    private static readonly CharacterType MummyCharacterType = CharacterType.murderer_mummy;

    private static ManualLogSource? _logger;
    private static Harmony? _harmony;

    public static void Initialize(ManualLogSource logger)
    {
        _logger = logger;
        MummySarcophagusVisualRuntime.Initialize(logger);
        _harmony ??= new Harmony(MummyUnlockPlugin.PluginGuid);
        _harmony.PatchAll();
    }

    public static void EnsureAvailableSeekersContainMummy(SeekerSelectionViewModel viewModel)
    {
        viewModel.AvailableSeekers = AppendCharacter(viewModel.AvailableSeekers, MummyCharacterType);
    }

    public static bool TryGetMummyAvatar(SpookedNetworkPlayer networkPlayer, out AvatarType avatarType)
    {
        avatarType = AvatarType.None;
        if (networkPlayer.CharacterType != MummyCharacterType)
        {
            return false;
        }

        // The retail network schema has no MummyAvatarType field and the stock
        // GetCurrentAvatar switch deliberately falls into its error branch for
        // murderer_mummy. PlayersPanel refreshes that branch repeatedly, and
        // producing the IL2CPP error stack can stall Wine for several seconds.
        // Reuse an existing synchronized hunter avatar slot as a presentation
        // fallback; this does not alter the selected character or its gameplay.
        avatarType = networkPlayer.ButcherAvatarType;
        if (avatarType == AvatarType.None)
        {
            avatarType = AvatarType.Butcher;
        }

        return true;
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

            var entries = new List<CharacterShopView.CharacterToBuy>(rawEntries.Count + 1);
            for (var index = 0; index < rawEntries.Count; index++)
            {
                var entry = rawEntries[index];
                if (entry.CharacterType == MummyCharacterType)
                {
                    return;
                }

                entries.Add(entry);
            }

            entries.Add(CreateMummyEntry());
            shopView._charactersToBuy = entries.ToArray();
            _logger?.LogInfo($"Appended mummy to CharacterShopView: [{string.Join(", ", entries.Select(entry => $"{entry.CharacterType}:{entry.NameKey}"))}]");
        }
        catch (Exception exception)
        {
            _logger?.LogError($"CharacterShopView preparation failed: {exception}");
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

            if (shopView._buyPanel is not null)
            {
                shopView._buyPanel.SetActive(false);
            }

            return true;
        }
        catch (Exception exception)
        {
            _logger?.LogError($"CharacterShopView custom description render failed: {exception}");
            return false;
        }
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

    private static CharacterShopView.CharacterToBuy CreateMummyEntry()
    {
        var mummyEntry = new CharacterShopView.CharacterToBuy
        {
            CharacterType = MummyCharacterType,
            NameKey = MummyLocalization.CharacterNameKey,
            CharacterSprite = MummyAbilityIconRuntime.GetCharacterSprite(),
            FirstSkill = SpookedSkillType.MummySandTrap,
            SecondSkill = SpookedSkillType.MummySarcophagus,
            FirstSkillNameKey = MummyLocalization.SandTrapNameKey,
            FirstSkillDescriptionKey = "MUMMY_SKILL_DESC_0",
            SecondSkillNameKey = MummyLocalization.SarcophagusNameKey,
            SecondSkillDescriptionKey = MummyLocalization.SarcophagusDescriptionKey
        };
        return mummyEntry;
    }

    private static string TranslateShopText(CharacterShopView shopView, string rawKey)
    {
        if (rawKey.StartsWith("SNEAKOUT_MUMMY_", StringComparison.Ordinal))
        {
            return MummyLocalization.Translate(rawKey);
        }

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

    private static int ClampCurrentCharacter(int currentCharacter, int characterCount)
    {
        return Math.Clamp(currentCharacter, 0, characterCount - 1);
    }
}
