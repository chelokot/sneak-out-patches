using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Kinguinverse.WebServiceProvider.Types_v2;
using Localization;
using Scriptables;
using UI.Views;
using UnityEngine;
using UnityEngine.UI;
using RuntimeCharacterType = Types.CharacterType;

namespace SneakOut.MummyUnlock;

internal static class MummyPerkShopRuntime
{
    internal const RuntimeCharacterType MummyCharacterType = MummyUnlockRuntime.MummyCharacterType;
    internal const RuntimeCharacterType ReaperCharacterType = RuntimeCharacterType.murderer_ripper;
    internal const string MummyCharacterNameKey = "SNEAKOUT_MUMMY_NAME";

    private static SkillTreeScriptable? _emptyActiveSkillTree;
    private static MainBoostersView? _currentView;
    private static bool _mummySelected;

    internal static void PrepareView(MainBoostersView view)
    {
        if (view is null)
        {
            return;
        }

        _currentView = view;

        var reaperPassiveTree = FindTree(view._passiveSkillTree, ReaperCharacterType);
        var reaperActiveTree = FindTree(view._activeSkillTree, ReaperCharacterType);
        if (reaperPassiveTree?.SkillTree is null || reaperActiveTree?.SkillTree is null)
        {
            MummyUnlockRuntime.LogInfo("Mummy perk shop was not prepared because the Reaper skill trees are unavailable");
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
        if (translator?._dictionary is null
            || translator._dictionary.ContainsKey(MummyCharacterNameKey))
        {
            return;
        }

        translator._dictionary.Add(MummyCharacterNameKey, "Mummy");
    }

    internal static void ApplyCarouselIcons(MainBoostersView view)
    {
        if (view?.ViewModel is null)
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

            ApplyCarouselSprite(selectionImages[imageIndex], MummyAbilityIconRuntime.GetCharacterSprite());
        }
    }

    internal static void ApplyCharacterName(MainBoostersView view)
    {
        if (view?.ViewModel is null
            || view._characterNameText is null
            || view.ViewModel.GetCurrentCharacter() != MummyCharacterType)
        {
            return;
        }

        view._characterNameText.text = "Mummy";
    }

    internal static void UpdateSelectionContext(MainBoostersView? view)
    {
        if (view is null || view.Pointer == IntPtr.Zero)
        {
            _currentView = null;
            _mummySelected = false;
            return;
        }

        _currentView = view;
        _mummySelected = view.ViewModel is not null
            && view.ViewModel.GetCurrentCharacter() == MummyCharacterType;
    }

    internal static bool ShouldProvideMummySkillCard(SkillType skillType)
    {
        return _mummySelected && MummyPerkStore.IsAllowedPassive(skillType);
    }

    internal static void RefreshCurrentView()
    {
        var view = _currentView;
        if (!_mummySelected || view is null || view.Pointer == IntPtr.Zero)
        {
            return;
        }

        view.SetSkillTree();
        view.SetEquippedSkills();
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
        emptyTree.name = "Mummy Unlock Active Skills (Empty)";
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

}
