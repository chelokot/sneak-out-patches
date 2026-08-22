using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Localization;
using Scriptables;
using UI;
using UI.Views;
using UnityEngine;
using UnityEngine.UI;
using RuntimeCharacterType = Types.CharacterType;

namespace SneakOut.UnlockEverything;

internal static class MummyPerkShopRuntime
{
    internal const RuntimeCharacterType MummyCharacterType = RuntimeCharacterType.murderer_mummy;
    internal const RuntimeCharacterType ReaperCharacterType = RuntimeCharacterType.murderer_ripper;
    internal const string MummyCharacterNameKey = "SNEAKOUT_MUMMY_NAME";

    private const string CharacterIconResourceName = "SneakOut.UnlockEverything.Assets.mummy_character_icon.png";

    private static SkillTreeScriptable? _emptyActiveSkillTree;
    private static Texture2D? _characterTexture;
    private static Sprite? _characterSprite;

    internal static void PrepareView(MainBoostersView view)
    {
        if ((!UnlockEverythingRuntime.UseProfileOverlay && !UnlockEverythingRuntime.UseLocalStub) || view is null)
        {
            return;
        }

        var reaperPassiveTree = FindTree(view._passiveSkillTree, ReaperCharacterType);
        var reaperActiveTree = FindTree(view._activeSkillTree, ReaperCharacterType);
        if (reaperPassiveTree?.SkillTree is null || reaperActiveTree?.SkillTree is null)
        {
            UnlockEverythingRuntime.LogOperational("Mummy perk shop was not prepared because the Reaper skill trees are unavailable");
            return;
        }

        view._passiveSkillTree = AppendTree(
            view._passiveSkillTree,
            new CharactersSkillTrees
            {
                CharacterType = MummyCharacterType,
                SkillTree = reaperPassiveTree.SkillTree,
            });
        view._activeSkillTree = AppendTree(
            view._activeSkillTree,
            new CharactersSkillTrees
            {
                CharacterType = MummyCharacterType,
                SkillTree = GetEmptyActiveSkillTree(reaperActiveTree.SkillTree),
            });

        AddCharacterNameTranslation(view._gameTranslator);
        var characterNames = view._characterNames;
        if (characterNames is not null && !characterNames.ContainsKey(MummyCharacterType))
        {
            characterNames.Add(MummyCharacterType, MummyCharacterNameKey);
        }
    }

    internal static void AddCharacterNameTranslation(GameTranslator? translator)
    {
        if ((!UnlockEverythingRuntime.UseProfileOverlay && !UnlockEverythingRuntime.UseLocalStub)
            || translator?._dictionary is null
            || translator._dictionary.ContainsKey(MummyCharacterNameKey))
        {
            return;
        }

        translator._dictionary.Add(MummyCharacterNameKey, "Mummy");
    }

    internal static void ApplyCarouselIcons(MainBoostersView view)
    {
        if ((!UnlockEverythingRuntime.UseProfileOverlay && !UnlockEverythingRuntime.UseLocalStub)
            || view?.ViewModel is null)
        {
            return;
        }

        var selectionImages = view._selectionsImages;
        var selectionIndices = view._selectionIndices;
        var characterTypes = view.ViewModel.TreeCharacterTypes;
        if (selectionImages is null || selectionIndices is null || characterTypes is null)
        {
            return;
        }

        var visibleCount = Math.Min(selectionImages.Length, selectionIndices.Length);
        for (var imageIndex = 0; imageIndex < visibleCount; imageIndex++)
        {
            var characterIndex = selectionIndices[imageIndex];
            if (characterIndex < 0 || characterIndex >= characterTypes.Length
                || characterTypes[characterIndex] != MummyCharacterType)
            {
                continue;
            }

            ApplyCarouselSprite(selectionImages[imageIndex], GetCharacterSprite());
        }
    }

    internal static void ApplyCharacterName(MainBoostersView view)
    {
        if ((!UnlockEverythingRuntime.UseProfileOverlay && !UnlockEverythingRuntime.UseLocalStub)
            || view?.ViewModel is null
            || view._characterNameText is null
            || view.ViewModel.GetCurrentCharacter() != MummyCharacterType)
        {
            return;
        }

        view._characterNameText.text = "Mummy";
    }

    internal static void ApplyPlayerListIcon(PlayerInGameRecord record, int playerId)
    {
        if ((!UnlockEverythingRuntime.UseProfileOverlay && !UnlockEverythingRuntime.UseLocalStub)
            || record?._networkPlayerRegistry is null
            || record._avatarImage is null)
        {
            return;
        }

        var player = record._networkPlayerRegistry[playerId];
        if (player is null
            || player.Pointer == IntPtr.Zero
            || player.CharacterType != MummyCharacterType)
        {
            return;
        }

        var sprite = GetCharacterSprite();
        record._avatarImage.overrideSprite = null;
        record._avatarImage.sprite = sprite;
        record._avatarImage.preserveAspect = true;
        record._avatarImage.enabled = true;
    }

    private static CharactersSkillTrees? FindTree(
        IEnumerable<CharactersSkillTrees>? trees,
        RuntimeCharacterType characterType)
    {
        if (trees is null)
        {
            return null;
        }

        foreach (var tree in trees)
        {
            if (tree is not null && tree.CharacterType == characterType)
            {
                return tree;
            }
        }

        return null;
    }

    private static Il2CppReferenceArray<CharactersSkillTrees> AppendTree(
        IEnumerable<CharactersSkillTrees>? source,
        CharactersSkillTrees mummyTree)
    {
        var trees = new List<CharactersSkillTrees>();
        var hasMummyTree = false;
        if (source is not null)
        {
            foreach (var tree in source)
            {
                if (tree is null)
                {
                    continue;
                }

                if (tree.CharacterType == MummyCharacterType)
                {
                    hasMummyTree = true;
                }

                trees.Add(tree);
            }
        }

        if (!hasMummyTree)
        {
            trees.Add(mummyTree);
        }

        return trees.ToArray();
    }

    private static SkillTreeScriptable GetEmptyActiveSkillTree(SkillTreeScriptable reaperActiveSkillTree)
    {
        if (_emptyActiveSkillTree is not null)
        {
            return _emptyActiveSkillTree;
        }

        var emptyTree = UnityEngine.Object.Instantiate(reaperActiveSkillTree);
        emptyTree.name = "Unlock Everything Mummy Active Skills (Empty)";
        emptyTree.Rows = Array.Empty<ListWrapper>();
        emptyTree.hideFlags = HideFlags.HideAndDontSave;
        UnityEngine.Object.DontDestroyOnLoad(emptyTree);
        _emptyActiveSkillTree = emptyTree;
        return emptyTree;
    }

    private static void ApplyCarouselSprite(Image? image, Sprite sprite)
    {
        if (image is null)
        {
            return;
        }

        // Match the seeker workstation: carousel images use sprite, while overrideSprite must
        // remain clear so the next stock carousel shift can replace the slot normally.
        image.overrideSprite = null;
        image.sprite = sprite;
        image.preserveAspect = true;
        image.enabled = true;
    }

    internal static Sprite GetCharacterSprite()
    {
        if (_characterSprite is not null)
        {
            return _characterSprite;
        }

        _characterSprite = Resources.FindObjectsOfTypeAll<Sprite>()
            .FirstOrDefault(sprite => sprite.name == "MummyCharacterIcon");
        if (_characterSprite is not null)
        {
            return _characterSprite;
        }

        _characterTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
        {
            name = "UnlockEverythingMummyCharacterIcon"
        };
        if (!ImageConversion.LoadImage(_characterTexture, ToIl2CppArray(LoadRequiredBytes(CharacterIconResourceName))))
        {
            throw new InvalidOperationException("Failed to decode the embedded Mummy character icon");
        }

        _characterTexture.wrapMode = TextureWrapMode.Clamp;
        _characterTexture.filterMode = FilterMode.Bilinear;
        _characterSprite = Sprite.Create(
            _characterTexture,
            new Rect(0f, 0f, _characterTexture.width, _characterTexture.height),
            new Vector2(0.5f, 0.5f),
            100f);
        _characterSprite.name = "MummyCharacterIcon";
        return _characterSprite;
    }

    private static byte[] LoadRequiredBytes(string resourceName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found");
        using var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);
        return memoryStream.ToArray();
    }

    private static Il2CppStructArray<byte> ToIl2CppArray(IReadOnlyList<byte> values)
    {
        var result = new Il2CppStructArray<byte>(values.Count);
        for (var index = 0; index < values.Count; index++)
        {
            result[index] = values[index];
        }

        return result;
    }

}
