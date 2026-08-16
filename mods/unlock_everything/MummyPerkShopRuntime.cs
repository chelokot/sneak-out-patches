using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Scriptables;
using UI.Views;
using UnityEngine;
using RuntimeCharacterType = Types.CharacterType;

namespace SneakOut.UnlockEverything;

internal static class MummyPerkShopRuntime
{
    internal const RuntimeCharacterType MummyCharacterType = RuntimeCharacterType.murderer_mummy;
    internal const RuntimeCharacterType ReaperCharacterType = RuntimeCharacterType.murderer_ripper;

    private static SkillTreeScriptable? _emptyActiveSkillTree;

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

        var characterNames = view._characterNames;
        if (characterNames is not null
            && !characterNames.ContainsKey(MummyCharacterType)
            && characterNames.TryGetValue(ReaperCharacterType, out var reaperNameKey))
        {
            // SetSkillTree expects every carousel entry in this dictionary. The visible text is
            // replaced after the stock method translates this harmless fallback key.
            characterNames.Add(MummyCharacterType, reaperNameKey);
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
}
