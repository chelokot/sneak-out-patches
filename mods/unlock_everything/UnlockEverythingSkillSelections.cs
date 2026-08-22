using Kinguinverse.WebServiceProvider.Types_v2;
using ClientCharacterType = Types.CharacterType;
using CharactersSkillsRuntime = Types.Structs.CharactersSkills;
using EntitySkillsComponentRuntime = Gameplay.Player.Components.EntitySkillsComponent;
using Il2CppCollections = Il2CppSystem.Collections.Generic;
using PlayersActiveSkillsRuntime = Collections.Skills.PlayersActiveSkills;
using PlayerSkillRuntime = Types.Structs.PlayerSkill;
using RuntimeCharacterType = Types.CharacterType;
using SimplifiedSkillsRuntime = Types.Structs.SimplifiedWebPlayerSkills;
using SpookedNetworkPlayerRuntime = Gameplay.Player.Components.SpookedNetworkPlayer;
using SpookedSkillType = Types.SpookedSkillType;
using TreeSkillSlotTypeRuntime = Types.TreeSkillSlotType;

namespace SneakOut.UnlockEverything;

internal static partial class UnlockEverythingSelections
{
    private static bool TryGetSkillTier(CharacterSkillCards? skillCards, SkillType skillType, out int tier)
    {
        tier = 0;

        var activeSkill = skillCards?.ActiveSkillCard;
        if (activeSkill is not null && activeSkill.SkillType == skillType)
        {
            tier = activeSkill.Tier;
            return tier > 0;
        }

        var passiveSkill1 = skillCards?.PassiveSkillCard1;
        if (passiveSkill1 is not null && passiveSkill1.SkillType == skillType)
        {
            tier = passiveSkill1.Tier;
            return tier > 0;
        }

        var passiveSkill2 = skillCards?.PassiveSkillCard2;
        if (passiveSkill2 is not null && passiveSkill2.SkillType == skillType)
        {
            tier = passiveSkill2.Tier;
            return tier > 0;
        }

        var passiveSkill3 = skillCards?.PassiveSkillCard3;
        if (passiveSkill3 is not null && passiveSkill3.SkillType == skillType)
        {
            tier = passiveSkill3.Tier;
            return tier > 0;
        }

        var passiveSkill4 = skillCards?.PassiveSkillCard4;
        if (passiveSkill4 is not null && passiveSkill4.SkillType == skillType)
        {
            tier = passiveSkill4.Tier;
            return tier > 0;
        }

        return false;
    }

    private static bool TryMapWebCharacterTypeToRuntimeCharacterType(CharacterType characterType, out RuntimeCharacterType runtimeCharacterType)
    {
        runtimeCharacterType = characterType switch
        {
            CharacterType.Penguin => RuntimeCharacterType.victim_penguin,
            CharacterType.Ghost => RuntimeCharacterType.ghost,
            CharacterType.Reaper => RuntimeCharacterType.murderer_ripper,
            CharacterType.Scarecrow => RuntimeCharacterType.murderer_scarecrow,
            CharacterType.Dracula => RuntimeCharacterType.murderer_dracula,
            CharacterType.Butcher => RuntimeCharacterType.murderer_butcher,
            CharacterType.Clown => RuntimeCharacterType.murderer_clown,
            CharacterType.Mimic => RuntimeCharacterType.seeker_with_generic_skills,
            _ => RuntimeCharacterType.spectator
        };

        return runtimeCharacterType != RuntimeCharacterType.spectator;
    }

    private static bool TryGetSkillTierFromCharactersSkillsPayload(CharactersSkillsRuntime charactersSkills, SkillType skillType, out RuntimeCharacterType characterType, out int tier)
    {
        characterType = RuntimeCharacterType.spectator;
        tier = 0;

        if (TryGetSkillTier(charactersSkills.PenguinSkills, skillType, out tier))
        {
            characterType = RuntimeCharacterType.victim_penguin;
            return true;
        }

        if (TryGetSkillTier(charactersSkills.ScarecrowSkills, skillType, out tier))
        {
            characterType = RuntimeCharacterType.murderer_scarecrow;
            return true;
        }

        if (TryGetSkillTier(charactersSkills.RipperSkills, skillType, out tier))
        {
            characterType = RuntimeCharacterType.murderer_ripper;
            return true;
        }

        if (TryGetSkillTier(charactersSkills.DraculaSkills, skillType, out tier))
        {
            characterType = RuntimeCharacterType.murderer_dracula;
            return true;
        }

        if (TryGetSkillTier(charactersSkills.ButcherSkills, skillType, out tier))
        {
            characterType = RuntimeCharacterType.murderer_butcher;
            return true;
        }

        if (TryGetSkillTier(charactersSkills.ClownSkills, skillType, out tier))
        {
            characterType = RuntimeCharacterType.murderer_clown;
            return true;
        }

        return false;
    }

    private static bool TryGetSkillTier(SimplifiedSkillsRuntime skills, SkillType skillType, out int tier)
    {
        if (TryGetSkillTier(skills.ActiveSkill, skillType, out tier)
            || TryGetSkillTier(skills.PassiveSkill1, skillType, out tier)
            || TryGetSkillTier(skills.PassiveSkill2, skillType, out tier)
            || TryGetSkillTier(skills.PassiveSkill3, skillType, out tier)
            || TryGetSkillTier(skills.PassiveSkill4, skillType, out tier))
        {
            return true;
        }

        tier = 0;
        return false;
    }

    private static bool TryGetSkillTier(PlayerSkillRuntime playerSkill, SkillType skillType, out int tier)
    {
        tier = playerSkill.Tier;
        return playerSkill.SkillType == skillType && tier > 0;
    }

    private static bool TryGetLoadedSkillTier(int internalId, SkillType skillType, out RuntimeCharacterType characterType, out int tier)
    {
        characterType = RuntimeCharacterType.spectator;
        tier = 0;

        return LoadedCharactersSkillsByInternalId.TryGetValue(internalId, out var charactersSkillsPayload)
            && TryGetSkillTierFromCharactersSkillsPayload(charactersSkillsPayload, skillType, out characterType, out tier);
    }

    private static void RememberLoadedCharactersSkills(int internalId, CharactersSkillsRuntime charactersSkills)
    {
        if (internalId <= 0)
        {
            return;
        }

        LoadedCharactersSkillsByInternalId[internalId] = charactersSkills;
        if (ShouldLogRemoteSkillDiagnostic(internalId))
        {
            UnlockEverythingRuntime.LogSkillUiEvent(
                "UnlockEverythingSelections.RememberLoadedCharactersSkills",
                $"internalId={internalId}, payload={DescribeCharactersSkillsPayload(charactersSkills)}");
        }
    }

    internal static void ClearLoadedCharactersSkills()
    {
        LoadedCharactersSkillsByInternalId.Clear();
        LoggedRemoteSkillDiagnostics.Clear();
    }

    internal static bool TryGetLocalSkillTier(int internalId, SkillType skillType, out RuntimeCharacterType characterType, out int tier)
    {
        characterType = RuntimeCharacterType.spectator;
        tier = 0;

        var currentNetworkPlayer = IsCurrentInternalId(internalId)
            ? GetCurrentNetworkPlayer()
            : null;
        if (UnlockEverythingRuntime.UsePersistentSelections
            && currentNetworkPlayer?.CharacterType == MummyPerkShopRuntime.MummyCharacterType
            && ExtendedCharactersSkillsRegistry.TryGetSkillTier(
                MummyPerkShopRuntime.MummyCharacterType,
                skillType,
                out tier))
        {
            // Reaper owns the shared perk definitions/modifier curves. The equipped value
            // remains in Mummy's independent registry entry.
            characterType = MummyPerkShopRuntime.ReaperCharacterType;
            return true;
        }

        if (TryGetLoadedSkillTier(internalId, skillType, out characterType, out tier))
        {
            LogRemoteSkillDiagnosticOnce(
                "UnlockEverythingSelections.TryGetLocalSkillTier:loaded",
                internalId,
                skillType,
                $"characterType={characterType}, tier={tier}");
            return true;
        }

        if (!UnlockEverythingRuntime.UsePersistentSelections)
        {
            LogRemoteSkillDiagnosticOnce(
                "UnlockEverythingSelections.TryGetLocalSkillTier:miss",
                internalId,
                skillType,
                "reason=persistentSelectionsDisabled");
            return false;
        }

        if (internalId > 0 && !IsCurrentInternalId(internalId))
        {
            LogRemoteSkillDiagnosticOnce(
                "UnlockEverythingSelections.TryGetLocalSkillTier:miss",
                internalId,
                skillType,
                "reason=remoteWithoutLoadedPayload");
            return false;
        }

        var player = GetPlayer();
        if (player?.Characters is null)
        {
            LogRemoteSkillDiagnosticOnce(
                "UnlockEverythingSelections.TryGetLocalSkillTier:miss",
                internalId,
                skillType,
                "reason=noPlayerCharacters");
            return false;
        }

        foreach (var currentCharacter in player.Characters)
        {
            if (currentCharacter is null || !TryGetSkillTier(currentCharacter.SkillCards, skillType, out tier))
            {
                continue;
            }

            var mapped = TryMapWebCharacterTypeToRuntimeCharacterType(currentCharacter.Type, out characterType);
            if (mapped)
            {
                LogRemoteSkillDiagnosticOnce(
                    "UnlockEverythingSelections.TryGetLocalSkillTier:local",
                    internalId,
                    skillType,
                    $"characterType={characterType}, tier={tier}");
            }

            return mapped;
        }

        LogRemoteSkillDiagnosticOnce(
            "UnlockEverythingSelections.TryGetLocalSkillTier:miss",
            internalId,
            skillType,
            "reason=skillNotFound");
        return false;
    }

    private static bool TryMapSkillTypeToSpookedSkillType(SkillType skillType, out SpookedSkillType spookedSkillType)
    {
        spookedSkillType = skillType switch
        {
            SkillType.PenguinPropChange => SpookedSkillType.VictimPropChange,
            SkillType.PenguinSlide => SpookedSkillType.VictimSlide,
            SkillType.PenguinShield => SpookedSkillType.VictimShield,
            _ => SpookedSkillType.None
        };

        return spookedSkillType != SpookedSkillType.None;
    }

    internal static bool TryGetLocalFirstSkill(EntitySkillsComponentRuntime entitySkillsComponent, out SpookedSkillType spookedSkillType)
    {
        spookedSkillType = SpookedSkillType.None;

        if (!TryGetLocalCharacterForType(entitySkillsComponent.InternalId, CharacterType.Penguin, out var character))
        {
            return false;
        }

        var activeSkillType = character.SkillCards?.ActiveSkillCard?.SkillType ?? SkillType.None;
        return TryMapSkillTypeToSpookedSkillType(activeSkillType, out spookedSkillType);
    }

    internal static bool TryGetLocalSkillEquipped(int internalId, SkillType skillType, RuntimeCharacterType characterType, out bool equipped)
    {
        equipped = false;

        if (characterType == MummyPerkShopRuntime.MummyCharacterType)
        {
            if (!IsCurrentInternalId(internalId))
            {
                return false;
            }

            return ExtendedCharactersSkillsRegistry.TryHaveSkillEquipped(
                characterType,
                skillType,
                out equipped);
        }

        if (TryGetLoadedSkillTier(internalId, skillType, out var loadedCharacterType, out _))
        {
            equipped = loadedCharacterType == characterType;
            LogRemoteSkillDiagnosticOnce(
                "UnlockEverythingSelections.TryGetLocalSkillEquipped:loaded",
                internalId,
                skillType,
                $"requestedCharacterType={characterType}, loadedCharacterType={loadedCharacterType}, equipped={equipped}");
            return true;
        }

        if (!TryMapClientCharacterType(characterType, out var webCharacterType)
            || !TryGetLocalCharacterForType(internalId, webCharacterType, out var character))
        {
            LogRemoteSkillDiagnosticOnce(
                "UnlockEverythingSelections.TryGetLocalSkillEquipped:miss",
                internalId,
                skillType,
                $"requestedCharacterType={characterType}, reason=noCharacter");
            return false;
        }

        equipped = TryGetSkillTier(character.SkillCards, skillType, out _);
        LogRemoteSkillDiagnosticOnce(
            "UnlockEverythingSelections.TryGetLocalSkillEquipped:local",
            internalId,
            skillType,
            $"requestedCharacterType={characterType}, equipped={equipped}");
        return true;
    }

    internal static bool TryGetDirectSkillModifier(PlayersActiveSkillsRuntime playersActiveSkills, SkillType skillType, Types.SkillModifierType skillModifierType, RuntimeCharacterType characterType, int tier, out float modifier)
    {
        modifier = 0;

        try
        {
            modifier = playersActiveSkills.GetModifierDirectly(skillType, skillModifierType, tier, characterType);
            return true;
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Unlock Everything direct skill modifier lookup failed", exception);
            return false;
        }
    }

    internal static string DescribeCharactersSkillsPayload(CharactersSkillsRuntime charactersSkills)
    {
        return string.Join(
            "; ",
            $"PenguinSkills={DescribeSimplifiedSkillsPayload(charactersSkills.PenguinSkills)}",
            $"ScarecrowSkills={DescribeSimplifiedSkillsPayload(charactersSkills.ScarecrowSkills)}",
            $"RipperSkills={DescribeSimplifiedSkillsPayload(charactersSkills.RipperSkills)}",
            $"DraculaSkills={DescribeSimplifiedSkillsPayload(charactersSkills.DraculaSkills)}",
            $"ButcherSkills={DescribeSimplifiedSkillsPayload(charactersSkills.ButcherSkills)}",
            $"ClownSkills={DescribeSimplifiedSkillsPayload(charactersSkills.ClownSkills)}");
    }

    internal static string DescribeLiveSpookedNetworkPlayerCharactersSkills(SpookedNetworkPlayerRuntime networkPlayer)
    {
        return DescribeCharactersSkillsPayload(networkPlayer.CharactersSkills);
    }

    internal static int GetNetworkPlayerInternalId(SpookedNetworkPlayerRuntime networkPlayer)
    {
        return networkPlayer.InternalId;
    }

    internal static void RememberLoadedCharactersSkillsFromNetworkPlayer(SpookedNetworkPlayerRuntime networkPlayer)
    {
        if (!UnlockEverythingRuntime.UseProfileOverlay)
        {
            return;
        }

        RememberLoadedCharactersSkills(networkPlayer.InternalId, networkPlayer.CharactersSkills);
    }

    internal static bool TryMaxCharactersSkillsPayload(ref CharactersSkillsRuntime charactersSkills)
    {
        var changed = false;
        changed |= TryMaxSimplifiedSkillsPayload(ref charactersSkills.PenguinSkills);
        changed |= TryMaxSimplifiedSkillsPayload(ref charactersSkills.ScarecrowSkills);
        changed |= TryMaxSimplifiedSkillsPayload(ref charactersSkills.RipperSkills);
        changed |= TryMaxSimplifiedSkillsPayload(ref charactersSkills.DraculaSkills);
        changed |= TryMaxSimplifiedSkillsPayload(ref charactersSkills.ButcherSkills);
        changed |= TryMaxSimplifiedSkillsPayload(ref charactersSkills.ClownSkills);
        return changed;
    }

    private static string DescribeSimplifiedSkillsPayload(SimplifiedSkillsRuntime skills)
    {
        return string.Join(
            ",",
            $"ActiveSkill={DescribePlayerSkillPayload(skills.ActiveSkill)}",
            $"PassiveSkill1={DescribePlayerSkillPayload(skills.PassiveSkill1)}",
            $"PassiveSkill2={DescribePlayerSkillPayload(skills.PassiveSkill2)}",
            $"PassiveSkill3={DescribePlayerSkillPayload(skills.PassiveSkill3)}",
            $"PassiveSkill4={DescribePlayerSkillPayload(skills.PassiveSkill4)}");
    }

    private static string DescribePlayerSkillPayload(PlayerSkillRuntime playerSkill)
    {
        return $"{(int)playerSkill.SkillType}/{playerSkill.Tier}";
    }

    private static bool TryMaxSimplifiedSkillsPayload(ref SimplifiedSkillsRuntime skills)
    {
        var changed = false;
        changed |= TryMaxPlayerSkillPayload(ref skills.ActiveSkill);
        changed |= TryMaxPlayerSkillPayload(ref skills.PassiveSkill1);
        changed |= TryMaxPlayerSkillPayload(ref skills.PassiveSkill2);
        changed |= TryMaxPlayerSkillPayload(ref skills.PassiveSkill3);
        changed |= TryMaxPlayerSkillPayload(ref skills.PassiveSkill4);
        return changed;
    }

    private static bool TryMaxPlayerSkillPayload(ref PlayerSkillRuntime playerSkill)
    {
        if (playerSkill.SkillType == SkillType.None || playerSkill.Tier == 5)
        {
            return false;
        }

        playerSkill.Tier = 5;
        return true;
    }

    private static SkillCard? FindSkillCard(Il2CppCollections.List<SkillCard> cards, SkillType skillType)
    {
        foreach (var card in cards)
        {
            if (card is not null && card.SkillType == skillType)
            {
                return card;
            }
        }

        return null;
    }

    private static SkillType GetRegistrySkillFromSlot(CharactersSkillsRuntime charactersSkills, ClientCharacterType clientCharacterType, TreeSkillSlotTypeRuntime slotType)
    {
        return charactersSkills.GetSkillFromSlot(slotType, clientCharacterType);
    }

    private static void RebuildCharacterSkillCardsFromRegistry(CharactersSkillsRuntime charactersSkills, ClientCharacterType clientCharacterType, Character character, Il2CppCollections.List<SkillCard> cards)
    {
        character.SkillCards ??= new CharacterSkillCards();
        character.SkillCards.ActiveSkillCard = FindSkillCard(cards, GetRegistrySkillFromSlot(charactersSkills, clientCharacterType, TreeSkillSlotTypeRuntime.Up));
        character.SkillCards.PassiveSkillCard1 = FindSkillCard(cards, GetRegistrySkillFromSlot(charactersSkills, clientCharacterType, TreeSkillSlotTypeRuntime.Right));
        character.SkillCards.PassiveSkillCard2 = FindSkillCard(cards, GetRegistrySkillFromSlot(charactersSkills, clientCharacterType, TreeSkillSlotTypeRuntime.Down));
        character.SkillCards.PassiveSkillCard3 = FindSkillCard(cards, GetRegistrySkillFromSlot(charactersSkills, clientCharacterType, TreeSkillSlotTypeRuntime.Left));
        character.SkillCards.PassiveSkillCard4 = null;
    }

    public static bool ApplyTreeSkillSelection(ClientCharacterType clientCharacterType, SkillType skillType, int slotType)
    {
        if (!UnlockEverythingRuntime.UsePersistentSelections)
        {
            return false;
        }

        var player = GetPlayer();
        var mummyPassiveSelection = clientCharacterType == MummyPerkShopRuntime.MummyCharacterType;
        if (mummyPassiveSelection
            && (TreeSkillSlotTypeRuntime)slotType is not (TreeSkillSlotTypeRuntime.Right
                or TreeSkillSlotTypeRuntime.Down
                or TreeSkillSlotTypeRuntime.Left))
        {
            return false;
        }

        if (mummyPassiveSelection)
        {
            var mummyCards = player?.Cards?.SkillCards;
            var mummySelectedCard = mummyCards is null ? null : FindSkillCard(mummyCards, skillType);
            if (player is null || mummySelectedCard is null)
            {
                return false;
            }

            var extendedCharactersSkills = player.Characters is null
                ? default
                : CharactersSkillsRuntime.ToCharacterSkills(player.Characters);
            extendedCharactersSkills.AddOrReplaceSkillInSlot(
                skillType,
                (TreeSkillSlotTypeRuntime)slotType,
                clientCharacterType,
                mummySelectedCard.Tier);
            var saved = ExtendedCharactersSkillsRegistry.TryGetSkillFromSlot(
                    clientCharacterType,
                    (TreeSkillSlotTypeRuntime)slotType,
                    out var savedSkillType)
                && savedSkillType == skillType;
            UnlockEverythingRuntime.LogSkillUiEvent(
                "UnlockEverythingSelections.ApplyTreeSkillSelection:mummy",
                $"slot={(TreeSkillSlotTypeRuntime)slotType}, skill={skillType}, saved={saved}");
            return saved;
        }

        if (!TryMapClientCharacterType(clientCharacterType, out var characterType))
        {
            return false;
        }

        var character = GetCharacterByType(characterType);
        var cards = player?.Cards?.SkillCards;
        if (player is null || character is null || cards is null)
        {
            return false;
        }

        var selectedCard = FindSkillCard(cards, skillType);
        if (selectedCard is null)
        {
            return false;
        }

        var charactersSkills = CharactersSkillsRuntime.ToCharacterSkills(player.Characters);
        var slotValue = (TreeSkillSlotTypeRuntime)slotType;
        charactersSkills.AddOrReplaceSkillInSlot(skillType, slotValue, clientCharacterType, selectedCard.Tier);
        RebuildCharacterSkillCardsFromRegistry(charactersSkills, clientCharacterType, character, cards);
        UnlockEverythingRuntime.LogSkillSelectionSnapshot("UnlockEverythingSelections.ApplyTreeSkillSelection:applied", character);
        SaveSelection(character);
        SyncLivePlayerCharactersSkills();
        return true;
    }

    public static bool ApplySkillCardSelection(int characterId, int skillCardSlot, int skillCardId)
    {
        if (!UnlockEverythingRuntime.UsePersistentSelections)
        {
            return false;
        }

        var player = GetPlayer();
        var character = GetCharacterById(characterId);
        var cards = player?.Cards?.SkillCards;
        if (character is null || cards is null)
        {
            return false;
        }

        SkillCard? selectedCard = null;
        foreach (var card in cards)
        {
            if (card is not null && card.Id == skillCardId)
            {
                selectedCard = card;
                break;
            }
        }

        if (selectedCard is null)
        {
            return false;
        }

        character.SkillCards ??= new CharacterSkillCards();
        switch (skillCardSlot)
        {
            case 1:
                character.SkillCards.ActiveSkillCard = selectedCard;
                break;
            case 2:
                character.SkillCards.PassiveSkillCard1 = selectedCard;
                break;
            case 3:
                character.SkillCards.PassiveSkillCard2 = selectedCard;
                break;
            case 4:
                character.SkillCards.PassiveSkillCard3 = selectedCard;
                break;
            case 5:
                character.SkillCards.PassiveSkillCard4 = selectedCard;
                break;
            default:
                return false;
        }

        UnlockEverythingRuntime.LogSkillSelectionSnapshot("UnlockEverythingSelections.ApplySkillCardSelection:applied", character);
        SaveSelection(character);
        SyncLivePlayerCharactersSkills();
        SyncOpenBoosterViews();
        return true;
    }

    public static bool RemoveSkillCardSelection(int characterId, int skillCardSlot)
    {
        if (!UnlockEverythingRuntime.UsePersistentSelections)
        {
            return false;
        }

        var character = GetCharacterById(characterId);
        if (character?.SkillCards is null)
        {
            return false;
        }

        switch (skillCardSlot)
        {
            case 1:
                character.SkillCards.ActiveSkillCard = null;
                break;
            case 2:
                character.SkillCards.PassiveSkillCard1 = null;
                break;
            case 3:
                character.SkillCards.PassiveSkillCard2 = null;
                break;
            case 4:
                character.SkillCards.PassiveSkillCard3 = null;
                break;
            case 5:
                character.SkillCards.PassiveSkillCard4 = null;
                break;
            default:
                return false;
        }

        UnlockEverythingRuntime.LogSkillSelectionSnapshot("UnlockEverythingSelections.RemoveSkillCardSelection:applied", character);
        SaveSelection(character);
        SyncLivePlayerCharactersSkills();
        SyncOpenBoosterViews();
        return true;
    }
}
