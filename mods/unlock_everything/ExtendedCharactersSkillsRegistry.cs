using Kinguinverse.WebServiceProvider.Types_v2;
using RuntimeCharacterType = Types.CharacterType;
using SimplifiedSkillsRuntime = Types.Structs.SimplifiedWebPlayerSkills;
using TreeSkillSlotTypeRuntime = Types.TreeSkillSlotType;

namespace SneakOut.UnlockEverything;

/// <summary>
/// Supplies character-skill entries that the retail CharactersSkills value does not contain.
/// Persistence is already partitioned by profile; the runtime character key is the seventh
/// registry coordinate beneath that profile.
/// </summary>
internal static class ExtendedCharactersSkillsRegistry
{
    internal const string MummyStorageKey = "runtime:12";

    internal static bool TryGetSkills(
        RuntimeCharacterType characterType,
        out SimplifiedSkillsRuntime skills)
    {
        skills = default;
        if (!CanHandle(characterType))
        {
            return false;
        }

        LocalSelectionsStore.TryGetRuntimeCharacterSkills(MummyStorageKey, out skills);
        NormalizeMummySkills(ref skills);
        return true;
    }

    internal static bool TrySaveSkills(
        RuntimeCharacterType characterType,
        SimplifiedSkillsRuntime skills)
    {
        if (!CanHandle(characterType))
        {
            return false;
        }

        NormalizeMummySkills(ref skills);
        LocalSelectionsStore.SaveRuntimeCharacterSkills(MummyStorageKey, skills);
        return true;
    }

    internal static bool TryGetSkillFromSlot(
        RuntimeCharacterType characterType,
        TreeSkillSlotTypeRuntime slotType,
        out SkillType skillType)
    {
        skillType = SkillType.None;
        if (!TryGetSkills(characterType, out var skills))
        {
            return false;
        }

        skillType = slotType switch
        {
            TreeSkillSlotTypeRuntime.Up => skills.ActiveSkill.SkillType,
            TreeSkillSlotTypeRuntime.Right => skills.PassiveSkill1.SkillType,
            TreeSkillSlotTypeRuntime.Down => skills.PassiveSkill2.SkillType,
            TreeSkillSlotTypeRuntime.Left => skills.PassiveSkill3.SkillType,
            _ => SkillType.None
        };
        return true;
    }

    internal static bool TryHaveSkillEquipped(
        RuntimeCharacterType characterType,
        SkillType skillType,
        out bool equipped)
    {
        equipped = false;
        if (!TryGetSkills(characterType, out var skills))
        {
            return false;
        }

        equipped = skillType != SkillType.None
            && (skills.PassiveSkill1.SkillType == skillType
                || skills.PassiveSkill2.SkillType == skillType
                || skills.PassiveSkill3.SkillType == skillType);
        return true;
    }

    internal static bool TryGetSkillTier(
        RuntimeCharacterType characterType,
        SkillType skillType,
        out int tier)
    {
        tier = 0;
        if (!TryGetSkills(characterType, out var skills) || skillType == SkillType.None)
        {
            return false;
        }

        if (skills.PassiveSkill1.SkillType == skillType)
        {
            tier = skills.PassiveSkill1.Tier;
        }
        else if (skills.PassiveSkill2.SkillType == skillType)
        {
            tier = skills.PassiveSkill2.Tier;
        }
        else if (skills.PassiveSkill3.SkillType == skillType)
        {
            tier = skills.PassiveSkill3.Tier;
        }

        return tier > 0;
    }

    internal static RuntimeCharacterType GetDefinitionCharacter(
        SkillType skillType,
        RuntimeCharacterType requestedCharacterType)
    {
        if (requestedCharacterType != MummyPerkShopRuntime.MummyCharacterType)
        {
            return requestedCharacterType;
        }

        return skillType switch
        {
            SkillType.ReaperHelloThere
                or SkillType.ReaperDontStop
                or SkillType.ReaperConnection
                or SkillType.ReaperOtherWorld
                or SkillType.ReaperTooGoodForYou => MummyPerkShopRuntime.ReaperCharacterType,
            SkillType.ScarecrowBigPockets
                or SkillType.ScarecrowFlyingFriend
                or SkillType.ScarecrowNewSneakers
                or SkillType.ScarecrowWaitForMe
                or SkillType.ScarecrowShySeeker => RuntimeCharacterType.murderer_scarecrow,
            SkillType.DraculaBolt
                or SkillType.DraculaBatman
                or SkillType.DraculaBloodBoost
                or SkillType.DraculaBloodSense
                or SkillType.DraculaNowhereToHide => RuntimeCharacterType.murderer_dracula,
            SkillType.ButcherToxicBoost
                or SkillType.ButcherSharpHook
                or SkillType.ButcherStunLover
                or SkillType.ButcherClosePresence
                or SkillType.ButcherGraveCurse => RuntimeCharacterType.murderer_butcher,
            SkillType.ClownBigPockets
                or SkillType.ClownBigHammer
                or SkillType.ClownJoker
                or SkillType.ClownTheFunIsOver => RuntimeCharacterType.murderer_clown,
            _ => MummyPerkShopRuntime.ReaperCharacterType
        };
    }

    private static bool CanHandle(RuntimeCharacterType characterType)
    {
        return UnlockEverythingRuntime.UsePersistentSelections
            && characterType == MummyPerkShopRuntime.MummyCharacterType;
    }

    private static void NormalizeMummySkills(ref SimplifiedSkillsRuntime skills)
    {
        // Mummy keeps the game's native active abilities. This registry extension supplies
        // only the three passive slots exposed by the borrowed Reaper passive tree.
        skills.ActiveSkill = default;
        skills.PassiveSkill4 = default;
        NormalizeTier(ref skills.PassiveSkill1);
        NormalizeTier(ref skills.PassiveSkill2);
        NormalizeTier(ref skills.PassiveSkill3);
    }

    private static void NormalizeTier(ref Types.Structs.PlayerSkill skill)
    {
        if (skill.SkillType == SkillType.None)
        {
            skill = default;
            return;
        }

        skill.Tier = 5;
    }
}
