using Kinguinverse.WebServiceProvider.Types_v2;
using Types.Structs;

namespace SneakOut.UnlockEverything;

internal static partial class UnlockEverythingSelections
{
    internal static void NormalizeEquippedSkinParts(Character character, SkinType? newlySelectedType = null)
    {
        var skinParts = character.SkinParts;
        if (skinParts is null)
        {
            return;
        }

        if (newlySelectedType == SkinType.Whole
            || (newlySelectedType is null
                && skinParts.Whole is not null
                && skinParts.Whole.SkinPartType != SkinPartType.None))
        {
            skinParts.Head = EmptySkinPart(SkinType.Head);
            skinParts.Chest = EmptySkinPart(SkinType.Chest);
            skinParts.Legs = EmptySkinPart(SkinType.Legs);
            skinParts.Hands = EmptySkinPart(SkinType.Hands);
            skinParts.Back = EmptySkinPart(SkinType.Back);
            return;
        }

        if (newlySelectedType is not null and not SkinType.None)
        {
            skinParts.Whole = EmptySkinPart(SkinType.Whole);
        }
    }

    internal static SkinPart EmptySkinPart(SkinType skinType)
    {
        return new SkinPart(0, skinType, SkinPartType.None);
    }

    internal static void NormalizeCharacterData(ref CharacterData characterData)
    {
        if (characterData.WholeType == SkinPartType.None)
        {
            return;
        }

        characterData.HeadType = SkinPartType.None;
        characterData.TorsoType = SkinPartType.None;
        characterData.ArmsType = SkinPartType.None;
        characterData.LegsType = SkinPartType.None;
        characterData.BackType = SkinPartType.None;
    }
}
