using Collections;
using Kinguinverse.WebServiceProvider.Types_v2;
using PlayerSkillRuntime = Types.Structs.PlayerSkill;
using PlayersActiveSkillsRuntime = Collections.Skills.PlayersActiveSkills;
using RuntimeCharacterType = Types.CharacterType;
using SpookedNetworkPlayerRuntime = Gameplay.Player.Components.SpookedNetworkPlayer;
using TreeSkillSlotTypeRuntime = Types.TreeSkillSlotType;

namespace SneakOut.MummyUnlock;

internal static class MummyPerkRuntime
{
    private const int MaxSkillExperience = 9_999;
    private const int MaxSkillTier = 5;

    private static readonly Dictionary<SkillType, SkillCard> SyntheticCards = new();
    private static SpookedNetworkPlayerRuntime? _localNetworkPlayer;

    internal static SkillCard GetSyntheticCard(SkillType skillType)
    {
        if (!MummyPerkStore.IsAllowedPassive(skillType))
        {
            throw new ArgumentOutOfRangeException(nameof(skillType), skillType, "Skill is not part of Mummy's passive catalog");
        }

        if (SyntheticCards.TryGetValue(skillType, out var card)
            && card is not null
            && card.Pointer != IntPtr.Zero)
        {
            return card;
        }

        card = new SkillCard(
            150_000 + (int)skillType,
            skillType,
            MaxSkillExperience,
            MaxSkillTier);
        SyntheticCards[skillType] = card;
        return card;
    }

    internal static bool ApplySelection(SkillType skillType, TreeSkillSlotTypeRuntime slotType)
    {
        if (!MummyPerkStore.IsAllowedPassive(skillType)
            || slotType is not (TreeSkillSlotTypeRuntime.Right
                or TreeSkillSlotTypeRuntime.Down
                or TreeSkillSlotTypeRuntime.Left))
        {
            return false;
        }

        MummySkillsRegistry.TryGetSkills(MummyUnlockRuntime.MummyCharacterType, out var skills);
        var selectedSkill = new PlayerSkillRuntime(skillType, MaxSkillTier);
        switch (slotType)
        {
            case TreeSkillSlotTypeRuntime.Right:
                skills.PassiveSkill1 = selectedSkill;
                break;
            case TreeSkillSlotTypeRuntime.Down:
                skills.PassiveSkill2 = selectedSkill;
                break;
            case TreeSkillSlotTypeRuntime.Left:
                skills.PassiveSkill3 = selectedSkill;
                break;
        }

        return MummySkillsRegistry.TrySaveSkills(MummyUnlockRuntime.MummyCharacterType, skills)
            && MummySkillsRegistry.TryGetSkillFromSlot(
                MummyUnlockRuntime.MummyCharacterType,
                slotType,
                out var savedSkillType)
            && savedSkillType == skillType;
    }

    internal static void RememberNetworkPlayer(SpookedNetworkPlayerRuntime? networkPlayer)
    {
        if (networkPlayer is null
            || networkPlayer.Pointer == IntPtr.Zero
            || !networkPlayer.HasInputAuthority
            || networkPlayer.IsBot)
        {
            return;
        }

        _localNetworkPlayer = networkPlayer;
    }

    internal static bool TryHaveSkillEquipped(
        int internalId,
        SkillType skillType,
        RuntimeCharacterType characterType,
        out bool equipped)
    {
        equipped = false;
        if (characterType != MummyUnlockRuntime.MummyCharacterType
            || !IsCurrentMummy(internalId))
        {
            return false;
        }

        return MummySkillsRegistry.TryHaveSkillEquipped(
            MummyUnlockRuntime.MummyCharacterType,
            skillType,
            out equipped);
    }

    internal static bool TryGetModifier(
        PlayersActiveSkillsRuntime playersActiveSkills,
        int internalId,
        SkillType skillType,
        Types.SkillModifierType modifierType,
        out float modifier)
    {
        modifier = 0f;
        if (!IsCurrentMummy(internalId)
            || !MummySkillsRegistry.TryGetSkillTier(
                MummyUnlockRuntime.MummyCharacterType,
                skillType,
                out var tier))
        {
            return false;
        }

        try
        {
            modifier = playersActiveSkills.GetModifierDirectly(
                skillType,
                modifierType,
                tier,
                MummyPerkShopRuntime.ReaperCharacterType);
            return true;
        }
        catch (Exception exception)
        {
            MummyUnlockRuntime.LogError("Looking up a Mummy passive modifier failed", exception);
            return false;
        }
    }

    internal static void Clear()
    {
        _localNetworkPlayer = null;
    }

    private static bool IsCurrentMummy(int internalId)
    {
        var player = _localNetworkPlayer;
        return internalId > 0
            && player is not null
            && player.Pointer != IntPtr.Zero
            && player.InternalId == internalId
            && player.CharacterType == MummyUnlockRuntime.MummyCharacterType;
    }
}
