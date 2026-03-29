using Base;
using Collections;
using Events;
using HarmonyLib;
using Kinguinverse.DataUtils.Events;
using Kinguinverse.WebServiceProvider;
using Kinguinverse.WebServiceProvider.Requests;
using Kinguinverse.WebServiceProvider.Responses;
using Kinguinverse.WebServiceProvider.Responses.V2;
using Kinguinverse.WebServiceProvider.Types.Games;
using Kinguinverse.WebServiceProvider.Types_v2;
using Kinguinverse.WebServiceProvider.Types_v2.Products;
using UI;
using UI.Buttons;
using UI.Views;
using ClientCharacterType = Types.CharacterType;
using Il2CppCollections = Il2CppSystem.Collections.Generic;
using Il2CppTasks = Il2CppSystem.Threading.Tasks;
using RuntimeCharacterType = Types.CharacterType;
using SpookedSkillType = Types.SpookedSkillType;
using Tasks = System.Threading.Tasks;

namespace SneakOut.UnlockEverything;

internal static class UnlockEverythingOverlay
{
    public static void EnsureClientCache(ClientCache clientCache)
    {
        if (UnlockEverythingRuntime.UseLocalStub)
        {
            UnlockEverythingStub.PopulateClientCache(clientCache);
            return;
        }

        if (UnlockEverythingRuntime.UseProfileOverlay)
        {
            UnlockEverythingStub.ApplyProfileOverlay(clientCache);
        }
    }
}

internal static class UnlockEverythingSelections
{
    private static AvatarType _pendingAvatarType = AvatarType.None;
    private static AvatarFrameType _pendingAvatarFrameType = AvatarFrameType.None;
    private static DescriptionType _pendingDescriptionType = DescriptionType.none;
    private static PlayerNewMetaInventory? _currentInventory;
    private static UnityEngine.Component? _currentNetworkPlayer;
    private static readonly Dictionary<int, object> LoadedCharactersSkillsByInternalId = new();
    private static readonly Type? GameType = AccessTools.TypeByName("Game");
    private static readonly Type? SpookedNetworkPlayerType = AccessTools.TypeByName("Gameplay.Player.Components.SpookedNetworkPlayer");
    private static readonly Type? NetworkPlayerRegistryType = AccessTools.TypeByName("NetworkPlayerRegistry");
    private static readonly Type? CharactersSkillsType = AccessTools.TypeByName("CharactersSkills");
    private static readonly Type? TreeSkillSlotTypeType = AccessTools.TypeByName("TreeSkillSlotType");
    private static readonly Type? MyPlayerRegistryType = AccessTools.TypeByName("MyPlayerRegistry");
    private static readonly Type? PlayersActiveSkillsType = AccessTools.TypeByName("Collections.Skills.PlayersActiveSkills");
    private static readonly Type? EntitySkillsComponentType = AccessTools.TypeByName("Gameplay.Player.Components.EntitySkillsComponent");
    private static readonly Type? SceneSpawnerType = AccessTools.TypeByName("Gameplay.Spawn.SceneSpawner");
    private static readonly Type? ScopeCleanerType = AccessTools.TypeByName("ScopeCleaner");
    private static readonly System.Reflection.PropertyInfo? GameInternalIdProperty =
        GameType is null ? null : AccessTools.Property(GameType, "InternalId");
    private static readonly System.Reflection.MethodInfo? PlayerNewMetaInventoryGetMyPlayerRegistryMethod =
        AccessTools.Method(typeof(PlayerNewMetaInventory), "get__myPlayerRegistry");
    private static readonly System.Reflection.MethodInfo? NetworkPlayerRegistryGetItemMethod =
        NetworkPlayerRegistryType is null ? null : AccessTools.Method(NetworkPlayerRegistryType, "get_Item", new[] { typeof(int) });
    private static readonly System.Reflection.MethodInfo? PlayerCustomizationViewGetSpookedPlayerCharacterDataMethod =
        AccessTools.Method(typeof(PlayerCustomizationView), "get__spookedPlayerCharacterData");
    private static readonly System.Reflection.MethodInfo? PlayerCustomizationViewSetCurrentCharacterDataMethod =
        AccessTools.Method(typeof(PlayerCustomizationView), "set__currentCharacterData");
    private static readonly System.Reflection.MethodInfo? SpookedNetworkPlayerGetCharacterDataMethod =
        SpookedNetworkPlayerType is null ? null : AccessTools.Method(SpookedNetworkPlayerType, "get_CharacterData");
    private static readonly System.Reflection.MethodInfo? SpookedNetworkPlayerChangeCharacterDataMethod =
        SpookedNetworkPlayerType is null ? null : AccessTools.Method(SpookedNetworkPlayerType, "ChangeCharacterData");
    private static readonly System.Reflection.MethodInfo? SpookedNetworkPlayerInitMethod =
        SpookedNetworkPlayerType is null ? null : AccessTools.Method(SpookedNetworkPlayerType, "Init");
    private static readonly System.Reflection.MethodInfo? SpookedNetworkPlayerGetCharactersSkillsMethod =
        SpookedNetworkPlayerType is null ? null : AccessTools.Method(SpookedNetworkPlayerType, "get_CharactersSkills");
    private static readonly System.Reflection.MethodInfo? SpookedNetworkPlayerGetCharacterTypeMethod =
        SpookedNetworkPlayerType is null ? null : AccessTools.Method(SpookedNetworkPlayerType, "get_CharacterType");
    private static readonly System.Reflection.MethodInfo? SpookedNetworkPlayerGetInternalIdMethod =
        SpookedNetworkPlayerType is null ? null : AccessTools.Method(SpookedNetworkPlayerType, "get_InternalId");
    private static readonly System.Reflection.MethodInfo? SpookedNetworkPlayerSetCharactersSkillsMethod =
        SpookedNetworkPlayerType is null ? null : AccessTools.Method(SpookedNetworkPlayerType, "set_CharactersSkills");
    private static readonly System.Reflection.MethodInfo? EntitySkillsComponentRefreshPlayerSkillsMethod =
        EntitySkillsComponentType is null ? null : AccessTools.Method(EntitySkillsComponentType, "RefreshPlayerSkills");
    private static readonly System.Reflection.PropertyInfo? EntitySkillsComponentInternalIdProperty =
        EntitySkillsComponentType is null ? null : AccessTools.Property(EntitySkillsComponentType, "InternalId");
    private static readonly System.Reflection.MethodInfo? CharactersSkillsToCharacterSkillsMethod =
        CharactersSkillsType is null ? null : AccessTools.Method(CharactersSkillsType, "ToCharacterSkills");
    private static readonly System.Reflection.MethodInfo? CharactersSkillsAddOrReplaceSkillInSlotMethod =
        CharactersSkillsType is null ? null : AccessTools.Method(CharactersSkillsType, "AddOrReplaceSkillInSlot");
    private static readonly System.Reflection.MethodInfo? CharactersSkillsGetSkillFromSlotMethod =
        CharactersSkillsType is null ? null : AccessTools.Method(CharactersSkillsType, "GetSkillFromSlot");
    private static readonly System.Reflection.MethodInfo? PlayersActiveSkillsGetModifierDirectlyMethod =
        PlayersActiveSkillsType is null ? null : AccessTools.Method(PlayersActiveSkillsType, "GetModifierDirectly");
    private static readonly System.Reflection.MethodInfo? AvatarAndFrameViewGetCurrentCategorySelectedMethod =
        AccessTools.Method(typeof(AvatarAndFrameView), "get__currentCategorySelected");
    private static readonly System.Reflection.MethodInfo? AvatarAndFrameViewGetCurrentSelectedProductMethod =
        AccessTools.Method(typeof(AvatarAndFrameView), "get__currentSelectedProduct");
    private static readonly System.Reflection.FieldInfo? MyPlayerRegistryCharactersSkillsField =
        typeof(MyPlayerRegistry).GetField("CharactersSkills", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
    private static readonly string[] CharacterSkillsFieldNames =
    {
        "PenguinSkills",
        "ScarecrowSkills",
        "RipperSkills",
        "DraculaSkills",
        "ButcherSkills",
        "ClownSkills"
    };
    private static readonly string[] SimplifiedSkillFieldNames =
    {
        "ActiveSkill",
        "PassiveSkill1",
        "PassiveSkill2",
        "PassiveSkill3",
        "PassiveSkill4"
    };

    internal static bool TryGetSkinPartType(Il2CppSystem.Enum itemType, out SkinPartType skinPartType)
    {
        skinPartType = SkinPartType.None;
        if (itemType is null)
        {
            return false;
        }

        return System.Enum.TryParse(itemType.ToString(), out skinPartType) && skinPartType != SkinPartType.None;
    }

    private static WebPlayer? GetPlayer()
    {
        return UnlockEverythingRuntime.CurrentClientCache?.UserWebPlayer;
    }

    private static int GetCurrentInternalId()
    {
        if (_currentInventory is not null
            && PlayerNewMetaInventoryGetMyPlayerRegistryMethod is not null
            && MyPlayerRegistryType is not null)
        {
            var myPlayerRegistry = PlayerNewMetaInventoryGetMyPlayerRegistryMethod.Invoke(_currentInventory, Array.Empty<object>());
            var networkIdField = myPlayerRegistry?.GetType().GetField("NetworkId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (networkIdField?.GetValue(myPlayerRegistry) is int networkId && networkId > 0)
            {
                return networkId;
            }
        }

        return GameInternalIdProperty?.GetValue(null) is int internalId ? internalId : 0;
    }

    private static Character? GetCharacterByType(CharacterType characterType)
    {
        var characters = GetPlayer()?.Characters;
        if (characters is null)
        {
            return null;
        }

        foreach (var character in characters)
        {
            if (character is not null && character.Type == characterType)
            {
                return character;
            }
        }

        return null;
    }

    private static Character? GetCharacterById(int characterId)
    {
        var characters = GetPlayer()?.Characters;
        if (characters is null)
        {
            return null;
        }

        foreach (var character in characters)
        {
            if (character is not null && character.CharacterId == characterId)
            {
                return character;
            }
        }

        return null;
    }

    private static bool IsCurrentInternalId(int internalId)
    {
        return internalId > 0 && internalId == GetCurrentInternalId();
    }

    internal static bool IsCurrentInternalIdForLogging(int internalId)
    {
        return IsCurrentInternalId(internalId);
    }

    private static bool TryGetLocalCharacterForType(int internalId, CharacterType characterType, out Character character)
    {
        character = null!;

        if (!IsCurrentInternalId(internalId) || characterType == CharacterType.None)
        {
            return false;
        }

        var currentCharacter = GetCharacterByType(characterType);
        if (currentCharacter is null)
        {
            return false;
        }

        character = currentCharacter;
        return true;
    }

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

    private static bool TryMapCharactersSkillsFieldToRuntimeCharacterType(string characterSkillsFieldName, out RuntimeCharacterType runtimeCharacterType)
    {
        runtimeCharacterType = characterSkillsFieldName switch
        {
            "PenguinSkills" => RuntimeCharacterType.victim_penguin,
            "ScarecrowSkills" => RuntimeCharacterType.murderer_scarecrow,
            "RipperSkills" => RuntimeCharacterType.murderer_ripper,
            "DraculaSkills" => RuntimeCharacterType.murderer_dracula,
            "ButcherSkills" => RuntimeCharacterType.murderer_butcher,
            "ClownSkills" => RuntimeCharacterType.murderer_clown,
            _ => RuntimeCharacterType.spectator
        };

        return runtimeCharacterType != RuntimeCharacterType.spectator;
    }

    private static bool TryGetSkillTierFromCharactersSkillsPayload(object charactersSkillsPayload, SkillType skillType, out RuntimeCharacterType characterType, out int tier)
    {
        characterType = RuntimeCharacterType.spectator;
        tier = 0;

        var charactersSkillsType = charactersSkillsPayload.GetType();
        foreach (var characterSkillsFieldName in CharacterSkillsFieldNames)
        {
            var characterSkillsField = charactersSkillsType.GetField(characterSkillsFieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (characterSkillsField is null)
            {
                continue;
            }

            var simplifiedSkillsPayload = characterSkillsField.GetValue(charactersSkillsPayload);
            if (simplifiedSkillsPayload is null || !TryMapCharactersSkillsFieldToRuntimeCharacterType(characterSkillsFieldName, out var payloadCharacterType))
            {
                continue;
            }

            var simplifiedSkillsType = simplifiedSkillsPayload.GetType();
            foreach (var simplifiedSkillFieldName in SimplifiedSkillFieldNames)
            {
                var simplifiedSkillField = simplifiedSkillsType.GetField(simplifiedSkillFieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (simplifiedSkillField?.GetValue(simplifiedSkillsPayload) is not { } playerSkillPayload)
                {
                    continue;
                }

                var playerSkillType = playerSkillPayload.GetType();
                var skillTypeField = playerSkillType.GetField("SkillType", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                var tierField = playerSkillType.GetField("Tier", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (skillTypeField?.GetValue(playerSkillPayload) is not SkillType payloadSkillType || tierField?.GetValue(playerSkillPayload) is not int payloadTier)
                {
                    continue;
                }

                if (payloadSkillType != skillType || payloadTier <= 0)
                {
                    continue;
                }

                characterType = payloadCharacterType;
                tier = payloadTier;
                return true;
            }
        }

        return false;
    }

    private static bool TryGetLoadedSkillTier(int internalId, SkillType skillType, out RuntimeCharacterType characterType, out int tier)
    {
        characterType = RuntimeCharacterType.spectator;
        tier = 0;

        return LoadedCharactersSkillsByInternalId.TryGetValue(internalId, out var charactersSkillsPayload)
            && TryGetSkillTierFromCharactersSkillsPayload(charactersSkillsPayload, skillType, out characterType, out tier);
    }

    internal static void RememberLoadedCharactersSkills(int internalId, object? charactersSkillsPayload)
    {
        if (internalId <= 0 || charactersSkillsPayload is null)
        {
            return;
        }

        LoadedCharactersSkillsByInternalId[internalId] = charactersSkillsPayload;
    }

    internal static void ClearLoadedCharactersSkills()
    {
        LoadedCharactersSkillsByInternalId.Clear();
    }

    internal static bool TryGetLocalSkillTier(int internalId, SkillType skillType, out RuntimeCharacterType characterType, out int tier)
    {
        characterType = RuntimeCharacterType.spectator;
        tier = 0;

        if (TryGetLoadedSkillTier(internalId, skillType, out characterType, out tier))
        {
            return true;
        }

        if (!UnlockEverythingRuntime.UsePersistentSelections || !IsCurrentInternalId(internalId))
        {
            return false;
        }

        var player = GetPlayer();
        if (player?.Characters is null)
        {
            return false;
        }

        foreach (var currentCharacter in player.Characters)
        {
            if (currentCharacter is null || !TryGetSkillTier(currentCharacter.SkillCards, skillType, out tier))
            {
                continue;
            }

            return TryMapWebCharacterTypeToRuntimeCharacterType(currentCharacter.Type, out characterType);
        }

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

    internal static bool TryGetLocalFirstSkill(object entitySkillsComponent, out SpookedSkillType spookedSkillType)
    {
        spookedSkillType = SpookedSkillType.None;

        if (EntitySkillsComponentInternalIdProperty?.GetValue(entitySkillsComponent) is not int internalId
            || !TryGetLocalCharacterForType(internalId, CharacterType.Penguin, out var character))
        {
            return false;
        }

        var activeSkillType = character.SkillCards?.ActiveSkillCard?.SkillType ?? SkillType.None;
        return TryMapSkillTypeToSpookedSkillType(activeSkillType, out spookedSkillType);
    }

    internal static bool TryGetLocalSkillEquipped(int internalId, SkillType skillType, RuntimeCharacterType characterType, out bool equipped)
    {
        equipped = false;

        if (TryGetLoadedSkillTier(internalId, skillType, out var loadedCharacterType, out _))
        {
            equipped = loadedCharacterType == characterType;
            return true;
        }

        if (!TryMapClientCharacterType(characterType, out var webCharacterType)
            || !TryGetLocalCharacterForType(internalId, webCharacterType, out var character))
        {
            return false;
        }

        equipped = TryGetSkillTier(character.SkillCards, skillType, out _);
        return true;
    }

    internal static System.Reflection.MethodBase? GetPlayersActiveSkillsHaveSkillEquippedTargetMethod()
    {
        return PlayersActiveSkillsType is null ? null : AccessTools.Method(PlayersActiveSkillsType, "HaveSkillEquipped");
    }

    internal static System.Reflection.MethodBase? GetPlayersActiveSkillsGetPlayerSkillModifierTargetMethod()
    {
        return PlayersActiveSkillsType is null ? null : AccessTools.Method(PlayersActiveSkillsType, "GetPlayerSkillModifier");
    }

    internal static System.Reflection.MethodBase? GetEntitySkillsComponentGetSkillTargetMethod()
    {
        return EntitySkillsComponentType is null ? null : AccessTools.Method(EntitySkillsComponentType, "GetSkill");
    }

    internal static bool TryGetDirectSkillModifier(object playersActiveSkills, SkillType skillType, object skillModifierType, RuntimeCharacterType characterType, int tier, out float modifier)
    {
        modifier = 0;

        if (PlayersActiveSkillsGetModifierDirectlyMethod?.Invoke(playersActiveSkills, new object[] { skillType, skillModifierType, tier, characterType }) is not float directModifier)
        {
            return false;
        }

        modifier = directModifier;
        return true;
    }

    internal static System.Reflection.MethodBase? GetSceneSpawnerOnPlayerLoadedTargetMethod()
    {
        return SceneSpawnerType is null ? null : AccessTools.Method(SceneSpawnerType, "OnPlayerLoaded");
    }

    internal static System.Reflection.MethodBase? GetSpookedNetworkPlayerInitTargetMethod()
    {
        return SpookedNetworkPlayerInitMethod;
    }

    internal static System.Reflection.MethodBase? GetScopeCleanerCleanTargetMethod()
    {
        return ScopeCleanerType is null ? null : AccessTools.Method(ScopeCleanerType, "Clean");
    }

    internal static System.Reflection.MethodBase? GetScopeCleanerGameplayCleanTargetMethod()
    {
        return ScopeCleanerType is null ? null : AccessTools.Method(ScopeCleanerType, "GameplayClean");
    }

    internal static string DescribeCharactersSkillsPayload(object? charactersSkillsPayload)
    {
        if (charactersSkillsPayload is null)
        {
            return "null";
        }

        var charactersSkillsType = charactersSkillsPayload.GetType();
        var segments = new List<string>();
        foreach (var characterSkillsFieldName in CharacterSkillsFieldNames)
        {
            var characterSkillsField = charactersSkillsType.GetField(characterSkillsFieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (characterSkillsField is null)
            {
                continue;
            }

            var simplifiedSkills = characterSkillsField.GetValue(charactersSkillsPayload);
            if (simplifiedSkills is null)
            {
                segments.Add($"{characterSkillsFieldName}=null");
                continue;
            }

            segments.Add($"{characterSkillsFieldName}={DescribeSimplifiedSkillsPayload(simplifiedSkills)}");
        }

        return string.Join("; ", segments);
    }

    internal static string DescribeLiveSpookedNetworkPlayerCharactersSkills(object networkPlayer)
    {
        return DescribeCharactersSkillsPayload(SpookedNetworkPlayerGetCharactersSkillsMethod?.Invoke(networkPlayer, Array.Empty<object>()));
    }

    internal static int GetNetworkPlayerInternalId(object networkPlayer)
    {
        return SpookedNetworkPlayerGetInternalIdMethod?.Invoke(networkPlayer, Array.Empty<object>()) is int internalId ? internalId : 0;
    }

    internal static bool TryMaxCharactersSkillsPayload(ref object? charactersSkillsPayload)
    {
        if (charactersSkillsPayload is null)
        {
            return false;
        }

        var charactersSkillsType = charactersSkillsPayload.GetType();
        var changed = false;
        var updatedPayload = charactersSkillsPayload;

        foreach (var characterSkillsFieldName in CharacterSkillsFieldNames)
        {
            var characterSkillsField = charactersSkillsType.GetField(characterSkillsFieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (characterSkillsField is null)
            {
                continue;
            }

            var simplifiedSkills = characterSkillsField.GetValue(updatedPayload);
            if (simplifiedSkills is null)
            {
                continue;
            }

            var updatedSimplifiedSkills = simplifiedSkills;
            if (!TryMaxSimplifiedSkillsPayload(ref updatedSimplifiedSkills))
            {
                continue;
            }

            characterSkillsField.SetValue(updatedPayload, updatedSimplifiedSkills);
            changed = true;
        }

        charactersSkillsPayload = updatedPayload;
        return changed;
    }

    private static string DescribeSimplifiedSkillsPayload(object simplifiedSkillsPayload)
    {
        var simplifiedSkillsType = simplifiedSkillsPayload.GetType();
        var segments = new List<string>();
        foreach (var simplifiedSkillFieldName in SimplifiedSkillFieldNames)
        {
            var simplifiedSkillField = simplifiedSkillsType.GetField(simplifiedSkillFieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (simplifiedSkillField is null)
            {
                continue;
            }

            segments.Add($"{simplifiedSkillFieldName}={DescribePlayerSkillPayload(simplifiedSkillField.GetValue(simplifiedSkillsPayload))}");
        }

        return string.Join(",", segments);
    }

    private static string DescribePlayerSkillPayload(object? playerSkillPayload)
    {
        if (playerSkillPayload is null)
        {
            return "null";
        }

        var playerSkillType = playerSkillPayload.GetType();
        var skillTypeField = playerSkillType.GetField("SkillType", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        var tierField = playerSkillType.GetField("Tier", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (skillTypeField is null || tierField is null)
        {
            return "unknown";
        }

        var skillTypeValue = System.Convert.ToInt32(skillTypeField.GetValue(playerSkillPayload));
        var tierValue = System.Convert.ToInt32(tierField.GetValue(playerSkillPayload));
        return $"{skillTypeValue}/{tierValue}";
    }

    private static bool TryMaxSimplifiedSkillsPayload(ref object simplifiedSkillsPayload)
    {
        var simplifiedSkillsType = simplifiedSkillsPayload.GetType();
        var changed = false;

        foreach (var simplifiedSkillFieldName in SimplifiedSkillFieldNames)
        {
            var simplifiedSkillField = simplifiedSkillsType.GetField(simplifiedSkillFieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (simplifiedSkillField is null)
            {
                continue;
            }

            var playerSkillPayload = simplifiedSkillField.GetValue(simplifiedSkillsPayload);
            if (playerSkillPayload is null)
            {
                continue;
            }

            var updatedPlayerSkillPayload = playerSkillPayload;
            if (!TryMaxPlayerSkillPayload(ref updatedPlayerSkillPayload))
            {
                continue;
            }

            simplifiedSkillField.SetValue(simplifiedSkillsPayload, updatedPlayerSkillPayload);
            changed = true;
        }

        return changed;
    }

    private static bool TryMaxPlayerSkillPayload(ref object playerSkillPayload)
    {
        var playerSkillType = playerSkillPayload.GetType();
        var skillTypeField = playerSkillType.GetField("SkillType", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        var tierField = playerSkillType.GetField("Tier", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (skillTypeField is null || tierField is null)
        {
            return false;
        }

        if (System.Convert.ToInt32(skillTypeField.GetValue(playerSkillPayload)) == 0)
        {
            return false;
        }

        if (System.Convert.ToInt32(tierField.GetValue(playerSkillPayload)) == 5)
        {
            return false;
        }

        tierField.SetValue(playerSkillPayload, (byte)5);
        return true;
    }

    private static void SaveSelection(Character character)
    {
        LocalSelectionsStore.SaveCharacterSelection(character);
    }

    internal static void RememberInventory(PlayerNewMetaInventory inventory)
    {
        _currentInventory = inventory;
    }

    internal static void RememberNetworkPlayer(object networkPlayer)
    {
        if (networkPlayer is not UnityEngine.Component component
            || SpookedNetworkPlayerGetInternalIdMethod?.Invoke(networkPlayer, Array.Empty<object>()) is not int candidateInternalId
            || candidateInternalId <= 0)
        {
            return;
        }

        var currentInternalId = GetCurrentInternalId();
        if (currentInternalId > 0 && candidateInternalId != currentInternalId)
        {
            return;
        }

        _currentNetworkPlayer = component;
    }

    private static object? GetMyPlayerRegistry(PlayerNewMetaInventory? preferredInventory = null)
    {
        if (PlayerNewMetaInventoryGetMyPlayerRegistryMethod is null)
        {
            return null;
        }

        if (preferredInventory is not null)
        {
            var preferredRegistry = PlayerNewMetaInventoryGetMyPlayerRegistryMethod.Invoke(preferredInventory, Array.Empty<object>());
            if (preferredRegistry is not null)
            {
                _currentInventory = preferredInventory;
                return preferredRegistry;
            }
        }

        if (_currentInventory is not null)
        {
            var currentRegistry = PlayerNewMetaInventoryGetMyPlayerRegistryMethod.Invoke(_currentInventory, Array.Empty<object>());
            if (currentRegistry is not null)
            {
                return currentRegistry;
            }
        }

        foreach (var view in UnityEngine.Resources.FindObjectsOfTypeAll<MainBoostersView>())
        {
            if (view is null)
            {
                continue;
            }

            var runtimeType = view.GetType();
            var inventory =
                AccessTools.Method(runtimeType, "get__playerNewMetaInventory")?.Invoke(view, Array.Empty<object>()) as PlayerNewMetaInventory
                ?? AccessTools.Field(runtimeType, "_playerNewMetaInventory")?.GetValue(view) as PlayerNewMetaInventory;
            if (inventory is null)
            {
                continue;
            }

            var registry = PlayerNewMetaInventoryGetMyPlayerRegistryMethod.Invoke(inventory, Array.Empty<object>());
            if (registry is null)
            {
                continue;
            }

            _currentInventory = inventory;
            return registry;
        }

        return null;
    }

    private static object? GetNetworkPlayerRegistry()
    {
        if (_currentInventory is not null)
        {
            var currentRegistry = _currentInventory.GetType()
                .GetField("_networkPlayerRegistry", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)?
                .GetValue(_currentInventory);
            if (currentRegistry is not null)
            {
                return currentRegistry;
            }
        }

        foreach (var view in UnityEngine.Resources.FindObjectsOfTypeAll<MainBoostersView>())
        {
            if (view is null)
            {
                continue;
            }

            var runtimeType = view.GetType();
            var inventory =
                AccessTools.Method(runtimeType, "get__playerNewMetaInventory")?.Invoke(view, Array.Empty<object>()) as PlayerNewMetaInventory
                ?? AccessTools.Field(runtimeType, "_playerNewMetaInventory")?.GetValue(view) as PlayerNewMetaInventory;
            if (inventory is null)
            {
                continue;
            }

            _currentInventory = inventory;
            var registry = inventory.GetType()
                .GetField("_networkPlayerRegistry", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)?
                .GetValue(inventory);
            if (registry is not null)
            {
                return registry;
            }
        }

        return null;
    }

    private static object? GetCurrentNetworkPlayer()
    {
        if (NetworkPlayerRegistryGetItemMethod is null || SpookedNetworkPlayerGetInternalIdMethod is null)
        {
            return null;
        }

        var internalId = GetCurrentInternalId();
        if (internalId <= 0)
        {
            return null;
        }

        if (_currentNetworkPlayer is not null)
        {
            if (_currentNetworkPlayer == null)
            {
                _currentNetworkPlayer = null;
            }
            else if (SpookedNetworkPlayerGetInternalIdMethod.Invoke(_currentNetworkPlayer, Array.Empty<object>()) is int cachedInternalId
                     && cachedInternalId == internalId)
            {
                return _currentNetworkPlayer;
            }
            else
            {
                _currentNetworkPlayer = null;
            }
        }

        var registry = GetNetworkPlayerRegistry();
        var networkPlayer = registry is null ? null : NetworkPlayerRegistryGetItemMethod.Invoke(registry, new object[] { internalId });
        if (networkPlayer is not null)
        {
            RememberNetworkPlayer(networkPlayer);
            return networkPlayer;
        }

        foreach (var candidate in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.MonoBehaviour>())
        {
            if (candidate is null || candidate.GetType() != SpookedNetworkPlayerType)
            {
                continue;
            }

            if (SpookedNetworkPlayerGetInternalIdMethod.Invoke(candidate, Array.Empty<object>()) is int candidateInternalId
                && candidateInternalId == internalId)
            {
                RememberNetworkPlayer(candidate);
                return candidate;
            }
        }

        return null;
    }

    private static bool SyncMyPlayerRegistryCharactersSkills(PlayerNewMetaInventory? preferredInventory = null)
    {
        var player = GetPlayer();
        if (player?.Characters is null || CharactersSkillsToCharacterSkillsMethod is null)
        {
            return false;
        }

        var myPlayerRegistry = GetMyPlayerRegistry(preferredInventory);
        if (myPlayerRegistry is null)
        {
            UnlockEverythingRuntime.LogSkillUiEvent("UnlockEverythingSelections.SyncMyPlayerRegistryCharactersSkills", "noRegistry");
            return false;
        }

        var charactersSkills = CharactersSkillsToCharacterSkillsMethod.Invoke(null, new object[] { player.Characters });
        if (charactersSkills is null)
        {
            UnlockEverythingRuntime.LogSkillUiEvent("UnlockEverythingSelections.SyncMyPlayerRegistryCharactersSkills", "noCharactersSkills");
            return false;
        }

        if (MyPlayerRegistryCharactersSkillsField is null)
        {
            UnlockEverythingRuntime.LogSkillUiEvent("UnlockEverythingSelections.SyncMyPlayerRegistryCharactersSkills", "noCharactersSkillsField");
            return false;
        }

        MyPlayerRegistryCharactersSkillsField.SetValue(myPlayerRegistry, charactersSkills);
        UnlockEverythingRuntime.LogSkillUiEvent("UnlockEverythingSelections.SyncMyPlayerRegistryCharactersSkills", "applied");
        return true;
    }

    private static void SyncMyPlayerRegistryCharacterData(Character character)
    {
        var myPlayerRegistry = GetMyPlayerRegistry();
        if (myPlayerRegistry is null)
        {
            UnlockEverythingRuntime.LogSkinSelectionSnapshot("UnlockEverythingSelections.SyncMyPlayerRegistryCharacterData:noRegistry", character);
            return;
        }

        var characterDataField = myPlayerRegistry.GetType().GetField("CharacterData", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (characterDataField is null || characterDataField.GetValue(myPlayerRegistry) is not Types.Structs.CharacterData characterData)
        {
            UnlockEverythingRuntime.LogSkinSelectionSnapshot("UnlockEverythingSelections.SyncMyPlayerRegistryCharacterData:noCharacterData", character);
            return;
        }

        var skinParts = character.SkinParts;
        characterData.HeadType = skinParts?.Head?.SkinPartType ?? SkinPartType.None;
        characterData.TorsoType = skinParts?.Chest?.SkinPartType ?? SkinPartType.None;
        characterData.ArmsType = skinParts?.Hands?.SkinPartType ?? SkinPartType.None;
        characterData.LegsType = skinParts?.Legs?.SkinPartType ?? SkinPartType.None;
        characterData.BackType = skinParts?.Back?.SkinPartType ?? SkinPartType.None;
        characterData.WholeType = skinParts?.Whole?.SkinPartType ?? SkinPartType.None;
        characterDataField.SetValue(myPlayerRegistry, characterData);
        UnlockEverythingRuntime.LogSkinSelectionSnapshot("UnlockEverythingSelections.SyncMyPlayerRegistryCharacterData:applied", character);
    }

    internal static void SyncInventoryRegistryCharactersSkills(PlayerNewMetaInventory inventory)
    {
        try
        {
            RememberInventory(inventory);
            if (SyncMyPlayerRegistryCharactersSkills(inventory))
            {
                UnlockEverythingRuntime.LogSkillUiEvent("UnlockEverythingSelections.SyncInventoryRegistryCharactersSkills", "applied");
            }
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend stabilizer registry skill sync failed", exception);
        }
    }

    private static void SyncLivePlayerCharactersSkills()
    {
        try
        {
            SyncMyPlayerRegistryCharactersSkills();
            var player = GetPlayer();
            var networkPlayer = GetCurrentNetworkPlayer();
            if (player?.Characters is null || networkPlayer is null || CharactersSkillsToCharacterSkillsMethod is null || SpookedNetworkPlayerSetCharactersSkillsMethod is null)
            {
                UnlockEverythingRuntime.LogSkillUiEvent("UnlockEverythingSelections.SyncLivePlayerCharactersSkills", "missingSource");
                return;
            }

            var charactersSkills = CharactersSkillsToCharacterSkillsMethod.Invoke(null, new object[] { player.Characters });
            if (charactersSkills is null)
            {
                UnlockEverythingRuntime.LogSkillUiEvent("UnlockEverythingSelections.SyncLivePlayerCharactersSkills", "noCharactersSkills");
                return;
            }

            SpookedNetworkPlayerSetCharactersSkillsMethod.Invoke(networkPlayer, new[] { charactersSkills });
            var entitySkillsComponent = (networkPlayer as UnityEngine.Component)?.GetComponent("EntitySkillsComponent");
            EntitySkillsComponentRefreshPlayerSkillsMethod?.Invoke(entitySkillsComponent, Array.Empty<object>());
            UnlockEverythingRuntime.LogSkillUiEvent("UnlockEverythingSelections.SyncLivePlayerCharactersSkills", "applied");
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend stabilizer live characters skills sync failed", exception);
        }
    }

    internal static void SyncOpenBoosterViews()
    {
        try
        {
            foreach (var view in UnityEngine.Resources.FindObjectsOfTypeAll<MainBoostersView>())
            {
                if (view is null)
                {
                    continue;
                }

                var runtimeType = view.GetType();
                var inventory =
                    AccessTools.Method(runtimeType, "get__playerNewMetaInventory")?.Invoke(view, Array.Empty<object>()) as PlayerNewMetaInventory
                    ?? AccessTools.Field(runtimeType, "_playerNewMetaInventory")?.GetValue(view) as PlayerNewMetaInventory;
                if (inventory is null)
                {
                    continue;
                }

                SyncInventoryRegistryCharactersSkills(inventory);
            }

            PlayerNewMetaInventoryOnTreeSkillChangePatch.RefreshSkillViews();
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend stabilizer open booster skill sync failed", exception);
        }
    }

    private static void SyncLivePlayerAvatarState(Character character)
    {
        try
        {
            var networkPlayer = GetCurrentNetworkPlayer();
            if (networkPlayer is null)
            {
                UnlockEverythingRuntime.LogAvatarSelectionSync("UnlockEverythingSelections.SyncLivePlayerAvatarState:noNetworkPlayer", character.CharacterId, character.Type, character.Avatar?.AvatarType ?? AvatarType.None, character.AvatarFrame?.AvatarFrameType ?? AvatarFrameType.None, character.Description, false);
                return;
            }

            var runtimeType = networkPlayer.GetType();
            var avatarType = character.Avatar?.AvatarType ?? AvatarType.None;
            switch (character.Type)
            {
                case CharacterType.Penguin:
                    AccessTools.Method(runtimeType, "set_VictimAvatarType")?.Invoke(networkPlayer, new object[] { avatarType });
                    break;
                case CharacterType.Reaper:
                    AccessTools.Method(runtimeType, "set_RipperAvatarType")?.Invoke(networkPlayer, new object[] { avatarType });
                    break;
                case CharacterType.Dracula:
                    AccessTools.Method(runtimeType, "set_DraculaAvatarType")?.Invoke(networkPlayer, new object[] { avatarType });
                    break;
                case CharacterType.Scarecrow:
                    AccessTools.Method(runtimeType, "set_ScarecrowAvatarType")?.Invoke(networkPlayer, new object[] { avatarType });
                    break;
                case CharacterType.Butcher:
                    AccessTools.Method(runtimeType, "set_ButcherAvatarType")?.Invoke(networkPlayer, new object[] { avatarType });
                    break;
                case CharacterType.Clown:
                    AccessTools.Method(runtimeType, "set_ClownAvatarType")?.Invoke(networkPlayer, new object[] { avatarType });
                    break;
            }

            AccessTools.Method(runtimeType, "set_AvatarFrameBorderType")?.Invoke(networkPlayer, new object[] { character.AvatarFrame?.AvatarFrameType ?? AvatarFrameType.None });
            AccessTools.Method(runtimeType, "set_DescriptionType")?.Invoke(networkPlayer, new object[] { character.Description });
            UnlockEverythingRuntime.LogAvatarSelectionSync("UnlockEverythingSelections.SyncLivePlayerAvatarState:applied", character.CharacterId, character.Type, avatarType, character.AvatarFrame?.AvatarFrameType ?? AvatarFrameType.None, character.Description, true);
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend stabilizer live avatar sync failed", exception);
        }
    }

    private static void SyncLivePlayerCharacterData(Character character)
    {
        try
        {
            SyncMyPlayerRegistryCharacterData(character);
            var networkPlayer = GetCurrentNetworkPlayer();
            if (networkPlayer is null)
            {
                UnlockEverythingRuntime.LogSkinSelectionSnapshot("UnlockEverythingSelections.SyncLivePlayerCharacterData:noNetworkPlayer", character);
                return;
            }

            if (!TryMapWebCharacterTypeToRuntimeCharacterType(character.Type, out var runtimeCharacterType)
                || SpookedNetworkPlayerGetCharacterTypeMethod?.Invoke(networkPlayer, Array.Empty<object>()) is not RuntimeCharacterType currentCharacterType
                || currentCharacterType != runtimeCharacterType)
            {
                UnlockEverythingRuntime.LogSkinSelectionSnapshot("UnlockEverythingSelections.SyncLivePlayerCharacterData:characterMismatch", character);
                return;
            }

            if (SpookedNetworkPlayerGetCharacterDataMethod?.Invoke(networkPlayer, Array.Empty<object>()) is not Types.Structs.CharacterData characterData)
            {
                UnlockEverythingRuntime.LogSkinSelectionSnapshot("UnlockEverythingSelections.SyncLivePlayerCharacterData:noCharacterData", character);
                return;
            }

            var skinParts = character.SkinParts;
            characterData.HeadType = skinParts?.Head?.SkinPartType ?? SkinPartType.None;
            characterData.TorsoType = skinParts?.Chest?.SkinPartType ?? SkinPartType.None;
            characterData.ArmsType = skinParts?.Hands?.SkinPartType ?? SkinPartType.None;
            characterData.LegsType = skinParts?.Legs?.SkinPartType ?? SkinPartType.None;
            characterData.BackType = skinParts?.Back?.SkinPartType ?? SkinPartType.None;
            characterData.WholeType = skinParts?.Whole?.SkinPartType ?? SkinPartType.None;
            SpookedNetworkPlayerChangeCharacterDataMethod?.Invoke(networkPlayer, new object[] { characterData });
            UnlockEverythingRuntime.LogSkinSelectionSnapshot("UnlockEverythingSelections.SyncLivePlayerCharacterData:applied", character);
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend stabilizer live character data sync failed", exception);
        }
    }

    private static bool TryGetCharacterId(CharacterType characterType, out int characterId)
    {
        characterId = 0;
        var character = GetCharacterByType(characterType);
        if (character is null)
        {
            return false;
        }

        characterId = character.CharacterId;
        return characterId != 0;
    }

    private static bool TryMapClientCharacterType(ClientCharacterType clientCharacterType, out CharacterType characterType)
    {
        characterType = clientCharacterType switch
        {
            ClientCharacterType.victim_penguin => CharacterType.Penguin,
            ClientCharacterType.ghost => CharacterType.Ghost,
            ClientCharacterType.murderer_ripper => CharacterType.Reaper,
            ClientCharacterType.murderer_scarecrow => CharacterType.Scarecrow,
            ClientCharacterType.murderer_dracula => CharacterType.Dracula,
            ClientCharacterType.murderer_butcher => CharacterType.Butcher,
            ClientCharacterType.murderer_clown => CharacterType.Clown,
            ClientCharacterType.seeker_with_generic_skills => CharacterType.Mimic,
            _ => CharacterType.None
        };

        return characterType != CharacterType.None;
    }

    internal static bool TryGetCharacterId(ClientCharacterType clientCharacterType, out int characterId)
    {
        characterId = 0;
        return TryMapClientCharacterType(clientCharacterType, out var characterType) && TryGetCharacterId(characterType, out characterId);
    }

    private static bool TryResolveCharacterSkin(int characterSkinTypeId, out CharacterSkin characterSkin)
    {
        characterSkin = CharacterSkin.None;
        var skins = GetPlayer()?.CharacterSkins?.Skins;
        if (skins is not null)
        {
            foreach (var skin in skins)
            {
                if (skin is not null && skin.Id == characterSkinTypeId)
                {
                    characterSkin = skin.Skin;
                    return characterSkin != CharacterSkin.None;
                }
            }
        }

        if (!System.Enum.IsDefined(typeof(CharacterSkin), characterSkinTypeId))
        {
            return false;
        }

        characterSkin = (CharacterSkin)characterSkinTypeId;
        return characterSkin != CharacterSkin.None;
    }

    private static bool TryParseAvatarProduct(Il2CppSystem.Enum productType, out AvatarType avatarType, out AvatarFrameType avatarFrameType, out DescriptionType descriptionType)
    {
        avatarType = AvatarType.None;
        avatarFrameType = AvatarFrameType.None;
        descriptionType = DescriptionType.none;
        if (productType is null)
        {
            return false;
        }

        var runtimeType = productType.GetType();
        var typeName = runtimeType.Name;
        var valueField = AccessTools.Field(runtimeType, "value__");
        var value = valueField?.GetValue(productType) switch
        {
            int intValue => intValue,
            byte byteValue => byteValue,
            short shortValue => shortValue,
            long longValue => unchecked((int)longValue),
            _ => int.MinValue
        };
        UnlockEverythingRuntime.LogSkillUiEvent("UnlockEverythingSelections.TryParseAvatarProduct", $"typeName={typeName}, value={value}");
        if (value == int.MinValue)
        {
            return false;
        }

        if (string.Equals(typeName, nameof(AvatarType), StringComparison.Ordinal) && System.Enum.IsDefined(typeof(AvatarType), value))
        {
            avatarType = (AvatarType)value;
            return avatarType != AvatarType.None;
        }

        if (string.Equals(typeName, nameof(AvatarFrameType), StringComparison.Ordinal) && System.Enum.IsDefined(typeof(AvatarFrameType), value))
        {
            avatarFrameType = (AvatarFrameType)value;
            return avatarFrameType != AvatarFrameType.None;
        }

        if (string.Equals(typeName, nameof(DescriptionType), StringComparison.Ordinal) && System.Enum.IsDefined(typeof(DescriptionType), value))
        {
            descriptionType = (DescriptionType)value;
            return descriptionType != DescriptionType.none;
        }

        return false;
    }

    internal static bool TryParseAvatarProductFromView(AvatarAndFrameView view, out AvatarType avatarType, out AvatarFrameType avatarFrameType, out DescriptionType descriptionType)
    {
        avatarType = AvatarType.None;
        avatarFrameType = AvatarFrameType.None;
        descriptionType = DescriptionType.none;

        if (view is null)
        {
            return false;
        }

        var category = view._currentCategorySelected;
        var selectedProduct = view._currentSelectedProduct;
        if (selectedProduct is null)
        {
            selectedProduct = TryGetSelectedAvatarProductFromButtons(view);
        }
        if (category < 0 || selectedProduct is null)
        {
            return false;
        }

        var runtimeType = selectedProduct.GetType();
        var valueField = AccessTools.Field(runtimeType, "value__");
        var value = valueField?.GetValue(selectedProduct) switch
        {
            int intValue => intValue,
            byte byteValue => byteValue,
            short shortValue => shortValue,
            long longValue => unchecked((int)longValue),
            _ => int.MinValue
        };
        if (value == int.MinValue)
        {
            return false;
        }

        switch (category)
        {
            case 0 when System.Enum.IsDefined(typeof(AvatarType), value):
                avatarType = (AvatarType)value;
                return avatarType != AvatarType.None;
            case 1 when System.Enum.IsDefined(typeof(AvatarFrameType), value):
                avatarFrameType = (AvatarFrameType)value;
                return avatarFrameType != AvatarFrameType.None;
            case 2 when System.Enum.IsDefined(typeof(DescriptionType), value):
                descriptionType = (DescriptionType)value;
                return descriptionType != DescriptionType.none;
        }

        return false;
    }

    private static Il2CppSystem.Enum? TryGetSelectedAvatarProductFromButtons(AvatarAndFrameView view)
    {
        var eventSystemType = AccessTools.TypeByName("UnityEngine.EventSystems.EventSystem");
        var currentEventSystem = eventSystemType is null
            ? null
            : AccessTools.Property(eventSystemType, "current")?.GetValue(null);
        var selectedObject = currentEventSystem is null
            ? null
            : AccessTools.Property(eventSystemType!, "currentSelectedGameObject")?.GetValue(currentEventSystem) as UnityEngine.GameObject;
        if (selectedObject is null)
        {
            return null;
        }

        if (view._avatarModyfiRecordButtons is not null)
        {
            foreach (var button in view._avatarModyfiRecordButtons)
            {
                if (button is null)
                {
                    continue;
                }

                var buttonComponent = AccessTools.Field(button.GetType(), "_button")?.GetValue(button) as UnityEngine.Component;
                if (button.gameObject == selectedObject || buttonComponent?.gameObject == selectedObject)
                {
                    return button.StoredProduct;
                }
            }
        }

        if (view._titleRecordButtons is not null)
        {
            foreach (var button in view._titleRecordButtons)
            {
                if (button is null)
                {
                    continue;
                }

                var buttonComponent = AccessTools.Field(button.GetType(), "_button")?.GetValue(button) as UnityEngine.Component;
                if (button.gameObject == selectedObject || buttonComponent?.gameObject == selectedObject)
                {
                    return button.StoredProduct;
                }
            }
        }

        return null;
    }

    internal static void RememberPendingAvatarSelection(AvatarAndFrameView view, Il2CppSystem.Enum productType)
    {
        if (TryParseAvatarProduct(productType, out var avatarType, out var avatarFrameType, out var descriptionType)
            || TryParseAvatarProductFromView(view, out avatarType, out avatarFrameType, out descriptionType))
        {
            _pendingAvatarType = avatarType;
            _pendingAvatarFrameType = avatarFrameType;
            _pendingDescriptionType = descriptionType;
            UnlockEverythingRuntime.LogSkillUiEvent("UnlockEverythingSelections.RememberPendingAvatarSelection", $"avatar={avatarType}, frame={avatarFrameType}, title={descriptionType}");
        }
    }

    internal static void RememberPendingAvatarSelection(Il2CppSystem.Enum productType)
    {
        UnlockEverythingRuntime.LogSkillUiEvent("UnlockEverythingSelections.RememberPendingAvatarSelection:entered", $"productPtr=0x{Il2CppInterop.Runtime.IL2CPP.Il2CppObjectBaseToPtr((Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)(object)productType):x}");
        if (!TryParseAvatarProduct(productType, out var avatarType, out var avatarFrameType, out var descriptionType))
        {
            UnlockEverythingRuntime.LogSkillUiEvent("UnlockEverythingSelections.RememberPendingAvatarSelection", "parseFailed");
            return;
        }

        _pendingAvatarType = avatarType;
        _pendingAvatarFrameType = avatarFrameType;
        _pendingDescriptionType = descriptionType;
        UnlockEverythingRuntime.LogSkillUiEvent("UnlockEverythingSelections.RememberPendingAvatarSelection", $"avatar={avatarType}, frame={avatarFrameType}, title={descriptionType}");
    }

    private static bool TryConsumePendingAvatarSelection(out AvatarType avatarType, out AvatarFrameType avatarFrameType, out DescriptionType descriptionType)
    {
        avatarType = _pendingAvatarType;
        avatarFrameType = _pendingAvatarFrameType;
        descriptionType = _pendingDescriptionType;
        var parsed = avatarType != AvatarType.None || avatarFrameType != AvatarFrameType.None || descriptionType != DescriptionType.none;
        if (!parsed)
        {
            return false;
        }

        _pendingAvatarType = AvatarType.None;
        _pendingAvatarFrameType = AvatarFrameType.None;
        _pendingDescriptionType = DescriptionType.none;
        UnlockEverythingRuntime.LogSkillUiEvent("UnlockEverythingSelections.TryConsumePendingAvatarSelection", $"avatar={avatarType}, frame={avatarFrameType}, title={descriptionType}");
        return true;
    }

    internal static bool TryParseAvatarProductFromAnyOpenView(out AvatarType avatarType, out AvatarFrameType avatarFrameType, out DescriptionType descriptionType)
    {
        avatarType = AvatarType.None;
        avatarFrameType = AvatarFrameType.None;
        descriptionType = DescriptionType.none;

        foreach (var view in UnityEngine.Resources.FindObjectsOfTypeAll<AvatarAndFrameView>())
        {
            if (view is null)
            {
                continue;
            }

            if (TryParseAvatarProductFromView(view, out avatarType, out avatarFrameType, out descriptionType))
            {
                UnlockEverythingRuntime.LogSkillUiEvent("UnlockEverythingSelections.TryParseAvatarProductFromAnyOpenView", $"avatar={avatarType}, frame={avatarFrameType}, title={descriptionType}");
                return true;
            }
        }

        return false;
    }

    internal static bool TryGetAvatarMenuCharacterId(out int characterId)
    {
        return TryGetCharacterId(CharacterType.Penguin, out characterId);
    }

    public static bool ApplyAvatarSelection(int characterId, int avatarId)
    {
        if (!UnlockEverythingRuntime.UsePersistentSelections)
        {
            return false;
        }

        var player = GetPlayer();
        var character = GetCharacterById(characterId);
        var avatars = player?.Avatars?.Avatars;
        if (character is null || avatars is null)
        {
            return false;
        }

        foreach (var avatar in avatars)
        {
            if (avatar is null || avatar.Id != avatarId)
            {
                continue;
            }

            character.Avatar = avatar;
            SyncLivePlayerAvatarState(character);
            SaveSelection(character);
            return true;
        }

        return false;
    }

    public static bool ApplyAvatarFrameSelection(int characterId, int avatarFrameId)
    {
        if (!UnlockEverythingRuntime.UsePersistentSelections)
        {
            return false;
        }

        var player = GetPlayer();
        var character = GetCharacterById(characterId);
        var avatarFrames = player?.AvatarFrames?.AvatarFrames;
        if (character is null || avatarFrames is null)
        {
            return false;
        }

        foreach (var avatarFrame in avatarFrames)
        {
            if (avatarFrame is null || avatarFrame.Id != avatarFrameId)
            {
                continue;
            }

            character.AvatarFrame = avatarFrame;
            SyncLivePlayerAvatarState(character);
            SaveSelection(character);
            return true;
        }

        return false;
    }

    public static bool ApplyTitleSelection(int characterId, DescriptionType descriptionType)
    {
        if (!UnlockEverythingRuntime.UsePersistentSelections)
        {
            return false;
        }

        var player = GetPlayer();
        var character = GetCharacterById(characterId);
        if (character is null || player is null)
        {
            return false;
        }

        player.Descriptions ??= new Il2CppCollections.List<DescriptionType>();
        if (!player.Descriptions.Contains(descriptionType))
        {
            player.Descriptions.Add(descriptionType);
        }

        character.Description = descriptionType;
        SyncLivePlayerAvatarState(character);
        SaveSelection(character);
        return true;
    }

    public static bool ApplyDanceSelection(int characterId, EmoteType emoteType)
    {
        if (!UnlockEverythingRuntime.UsePersistentSelections)
        {
            return false;
        }

        var character = GetCharacterById(characterId);
        if (character is null)
        {
            return false;
        }

        character.Dance = emoteType;
        SaveSelection(character);
        return true;
    }

    public static bool ApplyFartSelection(int characterId, EmoteType emoteType)
    {
        if (!UnlockEverythingRuntime.UsePersistentSelections)
        {
            return false;
        }

        var character = GetCharacterById(characterId);
        if (character is null)
        {
            return false;
        }

        character.Fart = emoteType;
        SaveSelection(character);
        return true;
    }

    public static bool ApplyCharacterSkinSelection(int characterId, int characterSkinTypeId)
    {
        if (!UnlockEverythingRuntime.UsePersistentSelections)
        {
            return false;
        }

        var player = GetPlayer();
        var character = GetCharacterById(characterId);
        if (player is null || character is null)
        {
            return false;
        }

        if (!TryResolveCharacterSkin(characterSkinTypeId, out var characterSkin))
        {
            return false;
        }

        player.CharacterSkins ??= new PlayerCharacterSkins(new Il2CppCollections.List<PlayerCharacterSkin>());
        player.CharacterSkins.Skins ??= new Il2CppCollections.List<PlayerCharacterSkin>();
        var skinId = UnlockEverythingStub.GetCharacterSkinId(characterSkin);
        var existingSkin = false;
        foreach (var skin in player.CharacterSkins.Skins)
        {
            if (skin is null || skin.Skin != characterSkin)
            {
                continue;
            }

            skin.Id = skinId;
            existingSkin = true;
            break;
        }

        if (!existingSkin)
        {
            player.CharacterSkins.Skins.Add(new PlayerCharacterSkin(skinId, characterSkin));
        }

        character.CharacterSkin = characterSkin;
        SaveSelection(character);
        return true;
    }

    public static bool ApplySkinPartSelection(int characterId, int skinPartId)
    {
        if (!UnlockEverythingRuntime.UsePersistentSelections)
        {
            return false;
        }

        var player = GetPlayer();
        var character = GetCharacterById(characterId);
        var skinParts = player?.Skins?.SkinParts;
        if (character is null || skinParts is null)
        {
            return false;
        }

        foreach (var skinPart in skinParts)
        {
            if (skinPart is null || skinPart.Id != skinPartId)
            {
                continue;
            }

            character.SkinParts ??= new SkinParts();
            switch (skinPart.SkinType)
            {
                case SkinType.Head:
                    character.SkinParts.Head = skinPart;
                    break;
                case SkinType.Chest:
                    character.SkinParts.Chest = skinPart;
                    break;
                case SkinType.Legs:
                    character.SkinParts.Legs = skinPart;
                    break;
                case SkinType.Hands:
                    character.SkinParts.Hands = skinPart;
                    break;
                case SkinType.Back:
                    character.SkinParts.Back = skinPart;
                    break;
                case SkinType.Whole:
                    character.SkinParts.Whole = skinPart;
                    break;
                default:
                    return false;
            }

            SaveSelection(character);
            SyncLivePlayerCharacterData(character);
            return true;
        }

        return false;
    }

    public static bool ApplySkinPartSelection(ClientCharacterType clientCharacterType, SkinType skinType, SkinPartType skinPartType)
    {
        if (!UnlockEverythingRuntime.UsePersistentSelections)
        {
            return false;
        }

        var player = GetPlayer();
        if (!TryMapClientCharacterType(clientCharacterType, out var characterType))
        {
            UnlockEverythingRuntime.LogSkinSelectionResolution("UnlockEverythingSelections.ApplySkinPartSelection:mapFailed", clientCharacterType, CharacterType.None, 0, player is not null, false);
            return false;
        }

        var character = GetCharacterByType(characterType);
        var characterId = character?.CharacterId ?? 0;
        UnlockEverythingRuntime.LogSkinSelectionResolution("UnlockEverythingSelections.ApplySkinPartSelection:resolved", clientCharacterType, characterType, characterId, player is not null, character is not null);
        if (player is null || character is null)
        {
            return false;
        }

        player.Skins ??= new PlayerSkins(new Il2CppCollections.List<SkinPart>());
        player.Skins.SkinParts ??= new Il2CppCollections.List<SkinPart>();

        SkinPart? selectedSkinPart = null;
        foreach (var skinPart in player.Skins.SkinParts)
        {
            if (skinPart is null || skinPart.SkinPartType != skinPartType)
            {
                continue;
            }

            selectedSkinPart = skinPart;
            break;
        }

        if (selectedSkinPart is null)
        {
            selectedSkinPart = new SkinPart(UnlockEverythingStub.GetSkinPartId(skinPartType), skinType, skinPartType);
            player.Skins.SkinParts.Add(selectedSkinPart);
        }
        else
        {
            selectedSkinPart.Id = UnlockEverythingStub.GetSkinPartId(skinPartType);
            selectedSkinPart.SkinType = skinType;
            selectedSkinPart.SkinPartType = skinPartType;
        }

        character.SkinParts ??= new SkinParts();
        switch (skinType)
        {
            case SkinType.Head:
                character.SkinParts.Head = selectedSkinPart;
                break;
            case SkinType.Chest:
                character.SkinParts.Chest = selectedSkinPart;
                break;
            case SkinType.Legs:
                character.SkinParts.Legs = selectedSkinPart;
                break;
            case SkinType.Hands:
                character.SkinParts.Hands = selectedSkinPart;
                break;
            case SkinType.Back:
                character.SkinParts.Back = selectedSkinPart;
                break;
            case SkinType.Whole:
                character.SkinParts.Whole = selectedSkinPart;
                break;
            default:
                return false;
        }

        SaveSelection(character);
        SyncLivePlayerCharacterData(character);
        UnlockEverythingRuntime.LogSkinSelectionSnapshot("UnlockEverythingSelections.ApplySkinPartSelection:applied", character);
        SyncPreviewCharacterData(skinType, skinPartType);
        PublishSkinRefresh(skinPartType, skinType);
        return true;
    }

    internal static void SyncPreviewCharacterData(SkinType skinType, SkinPartType skinPartType)
    {
        try
        {
            var internalId = GetCurrentInternalId();
            if (internalId <= 0)
            {
                UnlockEverythingRuntime.LogSkinPreview("UnlockEverythingSelections.SyncPreviewCharacterData:noInternalId", 0, skinType, skinPartType, false);
                return;
            }

            var previewViews = UnityEngine.Resources.FindObjectsOfTypeAll<PlayerCustomizationView>();
            UnlockEverythingRuntime.LogSkinPreview("UnlockEverythingSelections.SyncPreviewCharacterData:views", previewViews.Length, skinType, skinPartType, true);
            foreach (var previewView in previewViews)
            {
                if (previewView is null)
                {
                    continue;
                }

                if (PlayerCustomizationViewGetSpookedPlayerCharacterDataMethod?.Invoke(previewView, Array.Empty<object>()) is not SpookedPlayerCharacterData spookedPlayerCharacterData)
                {
                    UnlockEverythingRuntime.LogSkinPreview("UnlockEverythingSelections.SyncPreviewCharacterData:noPlayerData", internalId, skinType, skinPartType, false);
                    continue;
                }

                var currentCharacterData = spookedPlayerCharacterData[internalId];
                UnlockEverythingRuntime.LogSkinPreview("UnlockEverythingSelections.SyncPreviewCharacterData:before", internalId, skinType, skinPartType, true);
                switch (skinType)
                {
                    case SkinType.Head:
                        currentCharacterData.HeadType = skinPartType;
                        break;
                    case SkinType.Chest:
                        currentCharacterData.TorsoType = skinPartType;
                        break;
                    case SkinType.Hands:
                        currentCharacterData.ArmsType = skinPartType;
                        break;
                    case SkinType.Legs:
                        currentCharacterData.LegsType = skinPartType;
                        break;
                    case SkinType.Back:
                        currentCharacterData.BackType = skinPartType;
                        break;
                    case SkinType.Whole:
                        currentCharacterData.WholeType = skinPartType;
                        break;
                    default:
                        return;
                }

                spookedPlayerCharacterData[internalId] = currentCharacterData;
                PlayerCustomizationViewSetCurrentCharacterDataMethod?.Invoke(previewView, new object[] { currentCharacterData });
                UnlockEverythingRuntime.LogSkinPreview("UnlockEverythingSelections.SyncPreviewCharacterData:applied", internalId, skinType, skinPartType, true);
            }
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend stabilizer preview CharacterData sync failed", exception);
        }
    }

    private static void PublishSkinRefresh(SkinPartType skinPartType, SkinType skinType)
    {
        var internalId = GetCurrentInternalId();
        if (internalId == 0)
        {
            UnlockEverythingRuntime.LogSkinPreview("UnlockEverythingSelections.PublishSkinRefresh:noInternalId", 0, skinType, skinPartType, false);
            return;
        }

        if (internalId <= 0)
        {
            UnlockEverythingRuntime.LogSkinPreview("UnlockEverythingSelections.PublishSkinRefresh:invalidInternalId", internalId, skinType, skinPartType, false);
            return;
        }

        UnlockEverythingRuntime.LogSkinPreview("UnlockEverythingSelections.PublishSkinRefresh:publish", internalId, skinType, skinPartType, true);
        GameEventsManager.Publish<TryOnCharacterOutfitLocallyEvent>(null, new TryOnCharacterOutfitLocallyEvent(internalId, skinPartType, skinType));
        GameEventsManager.Publish<RefreshCharacterOutfit>(null, new RefreshCharacterOutfit(internalId));
    }

    public static bool RemoveSkinPartSelection(int characterId, SkinType skinType)
    {
        if (!UnlockEverythingRuntime.UsePersistentSelections)
        {
            return false;
        }

        var character = GetCharacterById(characterId);
        if (character?.SkinParts is null)
        {
            return false;
        }

        switch (skinType)
        {
            case SkinType.Head:
                character.SkinParts.Head = null;
                break;
            case SkinType.Chest:
                character.SkinParts.Chest = null;
                break;
            case SkinType.Legs:
                character.SkinParts.Legs = null;
                break;
            case SkinType.Hands:
                character.SkinParts.Hands = null;
                break;
            case SkinType.Back:
                character.SkinParts.Back = null;
                break;
            case SkinType.Whole:
                character.SkinParts.Whole = null;
                break;
            default:
                return false;
        }

        SaveSelection(character);
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

    private static SkillType GetRegistrySkillFromSlot(object charactersSkills, ClientCharacterType clientCharacterType, int slotType)
    {
        if (CharactersSkillsGetSkillFromSlotMethod is null || TreeSkillSlotTypeType is null)
        {
            return SkillType.None;
        }

        var slotValue = System.Enum.ToObject(TreeSkillSlotTypeType, slotType);
        return CharactersSkillsGetSkillFromSlotMethod.Invoke(charactersSkills, new[] { slotValue, (object)clientCharacterType }) is SkillType skillType
            ? skillType
            : SkillType.None;
    }

    private static void RebuildCharacterSkillCardsFromRegistry(object charactersSkills, ClientCharacterType clientCharacterType, Character character, Il2CppCollections.List<SkillCard> cards)
    {
        character.SkillCards ??= new CharacterSkillCards();
        character.SkillCards.ActiveSkillCard = FindSkillCard(cards, GetRegistrySkillFromSlot(charactersSkills, clientCharacterType, 1));
        character.SkillCards.PassiveSkillCard1 = FindSkillCard(cards, GetRegistrySkillFromSlot(charactersSkills, clientCharacterType, 2));
        character.SkillCards.PassiveSkillCard2 = FindSkillCard(cards, GetRegistrySkillFromSlot(charactersSkills, clientCharacterType, 3));
        character.SkillCards.PassiveSkillCard3 = FindSkillCard(cards, GetRegistrySkillFromSlot(charactersSkills, clientCharacterType, 4));
        character.SkillCards.PassiveSkillCard4 = null;
    }

    public static bool ApplyTreeSkillSelection(ClientCharacterType clientCharacterType, SkillType skillType, int slotType)
    {
        if (!UnlockEverythingRuntime.UsePersistentSelections)
        {
            return false;
        }

        var player = GetPlayer();
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

        if (CharactersSkillsToCharacterSkillsMethod is null || TreeSkillSlotTypeType is null || CharactersSkillsAddOrReplaceSkillInSlotMethod is null)
        {
            return false;
        }

        var charactersSkills = CharactersSkillsToCharacterSkillsMethod.Invoke(null, new object[] { player.Characters });
        if (charactersSkills is null)
        {
            return false;
        }

        var slotValue = System.Enum.ToObject(TreeSkillSlotTypeType, slotType);
        CharactersSkillsAddOrReplaceSkillInSlotMethod.Invoke(charactersSkills, new object[] { skillType, slotValue, clientCharacterType, selectedCard.Tier });
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

    public static void ApplyStartupSkinSelectionsToLivePreview()
    {
        if (!UnlockEverythingRuntime.UsePersistentSelections)
        {
            return;
        }

        if (GetCurrentInternalId() <= 0 || !LocalSelectionsStore.HasPersistedSkinSelection(CharacterType.Penguin))
        {
            return;
        }

        var character = GetCharacterByType(CharacterType.Penguin);
        if (character?.SkinParts is null)
        {
            return;
        }

        ApplyStartupSkinPart(character.SkinParts.Head);
        ApplyStartupSkinPart(character.SkinParts.Chest);
        ApplyStartupSkinPart(character.SkinParts.Legs);
        ApplyStartupSkinPart(character.SkinParts.Hands);
        ApplyStartupSkinPart(character.SkinParts.Back);
        ApplyStartupSkinPart(character.SkinParts.Whole);
        SyncLivePlayerCharacterData(character);
    }

    private static void ApplyStartupSkinPart(SkinPart skinPart)
    {
        if (skinPart is null || skinPart.SkinType == SkinType.None || skinPart.SkinPartType == SkinPartType.None)
        {
            return;
        }

        UnlockEverythingRuntime.LogSkinPreview("UnlockEverythingSelections.ApplyStartupSkinSelectionsToLivePreview", 0, skinPart.SkinType, skinPart.SkinPartType, true);
        SyncPreviewCharacterData(skinPart.SkinType, skinPart.SkinPartType);
        PublishSkinRefresh(skinPart.SkinPartType, skinPart.SkinType);
    }

    public static bool ApplyAvatarModificationSelection(Il2CppSystem.Enum productType, ClientCharacterType clientCharacterType)
    {
        if (!UnlockEverythingRuntime.UsePersistentSelections || !TryGetCharacterId(clientCharacterType, out var characterId))
        {
            return false;
        }

        var parsed = TryParseAvatarProduct(productType, out var avatarType, out var avatarFrameType, out var descriptionType);
        UnlockEverythingRuntime.LogSkillUiEvent("UnlockEverythingSelections.ApplyAvatarModificationSelection:direct", $"parsed={parsed}, avatar={avatarType}, frame={avatarFrameType}, title={descriptionType}");
        if (!parsed)
        {
            parsed = TryParseAvatarProductFromAnyOpenView(out avatarType, out avatarFrameType, out descriptionType);
            UnlockEverythingRuntime.LogSkillUiEvent("UnlockEverythingSelections.ApplyAvatarModificationSelection:view", $"parsed={parsed}, avatar={avatarType}, frame={avatarFrameType}, title={descriptionType}");
        }

        if (!parsed)
        {
            parsed = TryConsumePendingAvatarSelection(out avatarType, out avatarFrameType, out descriptionType);
            UnlockEverythingRuntime.LogSkillUiEvent("UnlockEverythingSelections.ApplyAvatarModificationSelection:pending", $"parsed={parsed}, avatar={avatarType}, frame={avatarFrameType}, title={descriptionType}");
        }

        if (!parsed)
        {
            return false;
        }

        if (avatarType != AvatarType.None)
        {
            return ApplyAvatarSelection(characterId, UnlockEverythingStub.GetAvatarId(avatarType));
        }

        if (avatarFrameType != AvatarFrameType.None)
        {
            return ApplyAvatarFrameSelection(characterId, UnlockEverythingStub.GetAvatarFrameId(avatarFrameType));
        }

        if (descriptionType != DescriptionType.none)
        {
            return ApplyTitleSelection(characterId, descriptionType);
        }

        return false;
    }

    public static void SaveCurrentCharacterSelection(CharacterType characterType)
    {
        if (!UnlockEverythingRuntime.UsePersistentSelections)
        {
            return;
        }

        var clientCache = UnlockEverythingRuntime.CurrentClientCache;
        Character? character = null;
        var characters = clientCache?.UserWebPlayer?.Characters;
        if (characters is not null)
        {
            foreach (var existingCharacter in characters)
            {
                if (existingCharacter is null || existingCharacter.Type != characterType)
                {
                    continue;
                }

                character = existingCharacter;
                break;
            }
        }

        if (character is null)
        {
            return;
        }

        LocalSelectionsStore.SaveCharacterSelection(character);
    }

    public static void SaveCurrentCharacterSelection(ClientCharacterType clientCharacterType)
    {
        if (!TryMapClientCharacterType(clientCharacterType, out var characterType))
        {
            return;
        }

        SaveCurrentCharacterSelection(characterType);
    }

    public static void SaveCharacterSkinSelection(int characterId, int characterSkinTypeId)
    {
        if (!UnlockEverythingRuntime.UsePersistentSelections || !UnlockEverythingStub.TryGetCharacterTypeById(characterId, out var characterType))
        {
            return;
        }

        if (!TryResolveCharacterSkin(characterSkinTypeId, out var characterSkin))
        {
            return;
        }

        LocalSelectionsStore.SaveCharacterSkin(characterType, characterSkin);
    }

    public static void SaveAfterCompletion(Il2CppTasks.Task task, CharacterType characterType)
    {
        if (!UnlockEverythingRuntime.UsePersistentSelections || task is null)
        {
            return;
        }

        try
        {
            if (task.IsCompletedSuccessfully)
            {
                SaveCurrentCharacterSelection(characterType);
                return;
            }

            _ = Tasks.Task.Run(
                async () =>
                {
                    while (!task.IsCompleted)
                    {
                        await Tasks.Task.Delay(50).ConfigureAwait(false);
                    }

                    if (!task.IsCompletedSuccessfully)
                    {
                        return;
                    }

                    SaveCurrentCharacterSelection(characterType);
                });
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend stabilizer selection persistence failed", exception);
        }
    }

    public static void SaveAfterCompletion(Il2CppTasks.Task task, ClientCharacterType clientCharacterType)
    {
        if (!TryMapClientCharacterType(clientCharacterType, out var characterType))
        {
            return;
        }

        SaveAfterCompletion(task, characterType);
    }

    public static void SaveSkinPartAfterCompletion(Il2CppTasks.Task task, ClientCharacterType clientCharacterType, SkinType skinType, SkinPartType skinPartType)
    {
        if (!UnlockEverythingRuntime.UsePersistentSelections || task is null)
        {
            return;
        }

        if (!TryMapClientCharacterType(clientCharacterType, out var characterType))
        {
            return;
        }

        try
        {
            if (task.IsCompletedSuccessfully)
            {
                LocalSelectionsStore.SaveSkinPartSelection(characterType, skinType, skinPartType);
                return;
            }

            _ = Tasks.Task.Run(
                async () =>
                {
                    while (!task.IsCompleted)
                    {
                        await Tasks.Task.Delay(50).ConfigureAwait(false);
                    }

                    if (!task.IsCompletedSuccessfully)
                    {
                        return;
                    }

                    LocalSelectionsStore.SaveSkinPartSelection(characterType, skinType, skinPartType);
                });
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend stabilizer skin persistence failed", exception);
        }
    }

    public static void SaveAfterCompletion(Il2CppTasks.Task<bool> task, CharacterType characterType)
    {
        if (!UnlockEverythingRuntime.UsePersistentSelections || task is null)
        {
            return;
        }

        try
        {
            if (task.IsCompletedSuccessfully)
            {
                if (task.Result)
                {
                    SaveCurrentCharacterSelection(characterType);
                }

                return;
            }

            _ = Tasks.Task.Run(
                async () =>
                {
                    while (!task.IsCompleted)
                    {
                        await Tasks.Task.Delay(50).ConfigureAwait(false);
                    }

                    if (!task.IsCompletedSuccessfully || !task.Result)
                    {
                        return;
                    }

                    SaveCurrentCharacterSelection(characterType);
                });
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend stabilizer selection persistence failed", exception);
        }
    }

    public static void SaveAfterCompletion(Il2CppTasks.Task<bool> task, ClientCharacterType clientCharacterType)
    {
        if (!TryMapClientCharacterType(clientCharacterType, out var characterType))
        {
            return;
        }

        SaveAfterCompletion(task, characterType);
    }
}

[HarmonyPatch(typeof(AvatarAndFrameView), "EquipModification")]
internal static class AvatarAndFrameViewEquipModificationPatch
{
    private static bool Prefix(AvatarAndFrameView __instance)
    {
        if (__instance is not null && UnlockEverythingSelections.TryParseAvatarProductFromView(__instance, out var avatarType, out var avatarFrameType, out var descriptionType))
        {
            UnlockEverythingRuntime.LogSkillUiEvent("AvatarAndFrameView.EquipModification", $"avatar={avatarType}, frame={avatarFrameType}, title={descriptionType}");
        }
        else
        {
            UnlockEverythingRuntime.LogSkillUiEvent("AvatarAndFrameView.EquipModification", "selectionUnavailable");
        }

        return true;
    }
}

[HarmonyPatch(typeof(AvatarAndFrameView), "BuyProduct")]
internal static class AvatarAndFrameViewBuyProductPatch
{
    private static void Prefix(AvatarAndFrameView __instance)
    {
        if (__instance is not null && UnlockEverythingSelections.TryParseAvatarProductFromView(__instance, out var avatarType, out var avatarFrameType, out var descriptionType))
        {
            UnlockEverythingRuntime.LogSkillUiEvent("AvatarAndFrameView.BuyProduct", $"avatar={avatarType}, frame={avatarFrameType}, title={descriptionType}");
        }
        else
        {
            UnlockEverythingRuntime.LogSkillUiEvent("AvatarAndFrameView.BuyProduct", "selectionUnavailable");
        }
    }
}

[HarmonyPatch(typeof(AvatarModyfiRecordButton), "PassChosenCostume")]
internal static class AvatarModyfiRecordButtonPassChosenCostumePatch
{
    private static void Prefix(AvatarModyfiRecordButton __instance)
    {
        if (__instance?.StoredProduct is null)
        {
            UnlockEverythingRuntime.LogSkillUiEvent("AvatarModyfiRecordButton.PassChosenCostume", "storedProductNull");
            return;
        }

        UnlockEverythingRuntime.LogSkillUiEvent("AvatarModyfiRecordButton.PassChosenCostume", "storedProductPresent");
        UnlockEverythingSelections.RememberPendingAvatarSelection(__instance.StoredProduct);
    }
}

[HarmonyPatch(typeof(TitleRecordButton), "PassChosenCostume")]
internal static class TitleRecordButtonPassChosenCostumePatch
{
    private static void Prefix(TitleRecordButton __instance)
    {
        if (__instance?.StoredProduct is null)
        {
            UnlockEverythingRuntime.LogSkillUiEvent("TitleRecordButton.PassChosenCostume", "storedProductNull");
            return;
        }

        UnlockEverythingRuntime.LogSkillUiEvent("TitleRecordButton.PassChosenCostume", "storedProductPresent");
        UnlockEverythingSelections.RememberPendingAvatarSelection(__instance.StoredProduct);
    }
}

[HarmonyPatch(typeof(ClientCache), nameof(ClientCache.OnClientConfirmed))]
internal static class ClientCacheOnClientConfirmedPatch
{
    private static void Postfix(ClientCache __instance)
    {
        try
        {
            UnlockEverythingRuntime.TrackClientCache(__instance);
            UnlockEverythingOverlay.EnsureClientCache(__instance);

            UnlockEverythingRuntime.LogClientCacheState("ClientCache.OnClientConfirmed", __instance);
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend stabilizer ClientCache.OnClientConfirmed postfix failed", exception);
        }
    }
}

[HarmonyPatch(typeof(PlayerNewMetaInventory), nameof(PlayerNewMetaInventory.GetSkillCard))]
internal static class PlayerNewMetaInventoryGetSkillCardPatch
{
    private static void Postfix(SkillType skillType, ref SkillCard __result)
    {
        if (!UnlockEverythingRuntime.UseProfileOverlay && !UnlockEverythingRuntime.UseLocalStub)
        {
            return;
        }

        if (skillType == SkillType.None || __result is not null)
        {
            return;
        }

        try
        {
            __result = UnlockEverythingStub.CreateMaxSkillCard(skillType);
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend stabilizer PlayerNewMetaInventory.GetSkillCard postfix failed", exception);
        }
    }
}

[HarmonyPatch(typeof(ClientCache), nameof(ClientCache.RefreshPlayer))]
internal static class ClientCacheRefreshPlayerPatch
{
    private static void Prefix(ClientCache __instance)
    {
        try
        {
            UnlockEverythingRuntime.TrackClientCache(__instance);
            if (UnlockEverythingRuntime.UseLocalStub)
            {
                UnlockEverythingStub.PopulateClientCache(__instance);
            }

            UnlockEverythingRuntime.LogClientCacheState("ClientCache.RefreshPlayer", __instance);
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend stabilizer ClientCache.RefreshPlayer prefix failed", exception);
        }
    }

    private static void Postfix(ClientCache __instance, Il2CppTasks.Task __result)
    {
        if (!UnlockEverythingRuntime.UseProfileOverlay || UnlockEverythingRuntime.UseLocalStub)
        {
            return;
        }

        try
        {
            if (__result is null)
            {
                return;
            }

            if (__result.IsCompletedSuccessfully)
            {
                UnlockEverythingStub.ApplyProfileOverlay(__instance);
                UnlockEverythingRuntime.LogClientCacheState("ClientCache.RefreshPlayer:completed", __instance);
                return;
            }

            _ = Tasks.Task.Run(
                async () =>
                {
                    while (!__result.IsCompleted)
                    {
                        await Tasks.Task.Delay(50).ConfigureAwait(false);
                    }

                    if (!__result.IsCompletedSuccessfully)
                    {
                        return;
                    }

                    try
                    {
                        UnlockEverythingStub.ApplyProfileOverlay(__instance);
                        UnlockEverythingRuntime.LogClientCacheState("ClientCache.RefreshPlayer:completed", __instance);
                    }
                    catch (Exception exception)
                    {
                        UnlockEverythingRuntime.LogError("Backend stabilizer ClientCache.RefreshPlayer completion overlay failed", exception);
                    }
                });
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend stabilizer ClientCache.RefreshPlayer completion overlay failed", exception);
        }
    }
}

[HarmonyPatch(typeof(PlayerNewMetaInventory), nameof(PlayerNewMetaInventory.OnAvatarModyficationChange))]
internal static class PlayerNewMetaInventoryOnAvatarModyficationChangePatch
{
    private static bool Prefix(PlayerNewMetaInventory __instance, Il2CppSystem.Enum productType, ClientCharacterType characterType, ref Il2CppTasks.Task<bool> __result)
    {
        UnlockEverythingSelections.RememberInventory(__instance);
        UnlockEverythingRuntime.LogSkillUiEvent("PlayerNewMetaInventory.OnAvatarModyficationChange:entered", $"characterType={characterType}");
        if (!UnlockEverythingRuntime.UsePersistentSelections)
        {
            return true;
        }

        var handledLocally = UnlockEverythingSelections.ApplyAvatarModificationSelection(productType, characterType);
        UnlockEverythingRuntime.LogAvatarModificationSelection("PlayerNewMetaInventory.OnAvatarModyficationChange:prefix", productType, characterType, handledLocally);
        if (!handledLocally)
        {
            return true;
        }

        __result = Il2CppTasks.Task.FromResult(true);
        return false;
    }

    private static void Postfix(ClientCharacterType characterType, Il2CppTasks.Task<bool> __result)
    {
        UnlockEverythingSelections.SaveAfterCompletion(__result, characterType);
    }
}

[HarmonyPatch(typeof(PlayerNewMetaInventory), nameof(PlayerNewMetaInventory.EmoteChange))]
internal static class PlayerNewMetaInventoryEmoteChangePatch
{
    private static void Postfix(ClientCharacterType characterType, Il2CppTasks.Task<bool> __result)
    {
        UnlockEverythingSelections.SaveAfterCompletion(__result, characterType);
    }
}

[HarmonyPatch(typeof(PlayerNewMetaInventory), nameof(PlayerNewMetaInventory.ChangeSkinEquipped))]
internal static class PlayerNewMetaInventoryChangeSkinEquippedPatch
{
    private static SkinType? _pendingSkinType;
    private static SkinPartType? _pendingSkinPartType;

    public static bool TryConsumePendingSkinType(out SkinType skinType)
    {
        if (_pendingSkinType.HasValue)
        {
            skinType = _pendingSkinType.Value;
            _pendingSkinType = null;
            return true;
        }

        skinType = SkinType.None;
        return false;
    }

    public static bool TryConsumePendingSkinPartType(out SkinPartType skinPartType)
    {
        if (_pendingSkinPartType.HasValue)
        {
            skinPartType = _pendingSkinPartType.Value;
            _pendingSkinPartType = null;
            return true;
        }

        skinPartType = SkinPartType.None;
        return false;
    }

    private static bool Prefix(PlayerNewMetaInventory __instance, SkinPartType partType, SkinType skinType, ClientCharacterType characterType, ref Il2CppTasks.Task __result)
    {
        UnlockEverythingSelections.RememberInventory(__instance);
        var handledLocally = UnlockEverythingRuntime.UsePersistentSelections
            && UnlockEverythingSelections.ApplySkinPartSelection(characterType, skinType, partType);
        UnlockEverythingRuntime.LogSkinPath("PlayerNewMetaInventory.ChangeSkinEquipped:prefix", characterType, skinType, partType, handledLocally);
        if (!handledLocally)
        {
            return true;
        }

        _pendingSkinType = skinType;
        _pendingSkinPartType = partType;
        __result = Il2CppTasks.Task.CompletedTask;
        return false;
    }

    private static void Postfix(SkinPartType partType, SkinType skinType, ClientCharacterType characterType, Il2CppTasks.Task __result)
    {
        UnlockEverythingSelections.SaveAfterCompletion(__result, characterType);
        UnlockEverythingSelections.SaveSkinPartAfterCompletion(__result, characterType, skinType, partType);
    }
}

[HarmonyPatch(typeof(PlayerNewMetaInventory), nameof(PlayerNewMetaInventory.GetMyCurrentSkinPartType))]
internal static class PlayerNewMetaInventoryGetMyCurrentSkinPartTypeLoggingPatch
{
    private static void Postfix(SkinType skinType, ClientCharacterType characterType, SkinPartType __result)
    {
        UnlockEverythingRuntime.LogSkinPath("PlayerNewMetaInventory.GetMyCurrentSkinPartType", characterType, skinType, __result, true);
    }
}

[HarmonyPatch(typeof(CustomizeCharacterNewMetaView), "OnCostumePiecked")]
internal static class CustomizeCharacterNewMetaViewOnCostumePieckedLoggingPatch
{
    private static void Postfix(CustomizeCharacterNewMetaView __instance, SkinPartType skinPartType, SkinType skinType)
    {
        UnlockEverythingRuntime.LogSkinPreview("CustomizeCharacterNewMetaView.OnCostumePiecked", 0, skinType, skinPartType, true);
        UnlockEverythingRuntime.LogCostumePieces("CustomizeCharacterNewMetaView.OnCostumePiecked:pieces", __instance);
    }
}

[HarmonyPatch(typeof(CustomizeCharacterNewMetaView), "OnEquipButton")]
internal static class CustomizeCharacterNewMetaViewOnEquipButtonLoggingPatch
{
    private static void Postfix()
    {
        UnlockEverythingRuntime.LogSkinRefreshEvent("CustomizeCharacterNewMetaView.OnEquipButton", 0);
    }
}

[HarmonyPatch(typeof(MainBoostersViewModel), "ChangeEquippedSkill")]
internal static class MainBoostersViewModelChangeEquippedSkillLoggingPatch
{
    private static void Prefix(SkillType nextSkill, int slotType)
    {
        UnlockEverythingRuntime.LogSkillUiEvent("MainBoostersViewModel.ChangeEquippedSkill", $"nextSkill={nextSkill}, slotType={slotType}");
    }
}

[HarmonyPatch(typeof(MainBoostersViewModel), "TreeSkillEquipped")]
internal static class MainBoostersViewModelTreeSkillEquippedLoggingPatch
{
    private static void Postfix(object sender, Il2CppSystem.EventArgs args)
    {
        UnlockEverythingRuntime.LogSkillUiEvent("MainBoostersViewModel.TreeSkillEquipped", $"senderType={sender?.GetType().Name ?? "null"}, argsType={args?.GetType().Name ?? "null"}");
        PlayerNewMetaInventoryOnTreeSkillChangePatch.RefreshSkillViews();
    }
}

[HarmonyPatch(typeof(MainBoostersView), "Open")]
internal static class MainBoostersViewOpenSkillSyncPatch
{
    private static void Postfix(MainBoostersView __instance)
    {
        if (!UnlockEverythingRuntime.UsePersistentSelections || __instance is null)
        {
            return;
        }

        try
        {
            var runtimeType = __instance.GetType();
            var inventory =
                AccessTools.Method(runtimeType, "get__playerNewMetaInventory")?.Invoke(__instance, Array.Empty<object>()) as PlayerNewMetaInventory
                ?? AccessTools.Field(runtimeType, "_playerNewMetaInventory")?.GetValue(__instance) as PlayerNewMetaInventory;
            if (inventory is null)
            {
                UnlockEverythingRuntime.LogSkillUiEvent("MainBoostersView.Open", "startupSkillSyncSkipped=noInventory");
                return;
            }

            UnlockEverythingSelections.SyncInventoryRegistryCharactersSkills(inventory);
            PlayerNewMetaInventoryOnTreeSkillChangePatch.RefreshSkillViews();
            UnlockEverythingRuntime.LogSkillUiEvent("MainBoostersView.Open", "startupSkillSyncApplied=1");
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend stabilizer MainBoostersView.Open skill sync failed", exception);
        }
    }
}

[HarmonyPatch(typeof(CustomizeCharacterNewMetaView), "CostumeChange")]
internal static class CustomizeCharacterNewMetaViewCostumeChangePatch
{
    private static void Postfix(CustomizeCharacterNewMetaView __instance, Il2CppTasks.Task __result)
    {
        if (!UnlockEverythingRuntime.UsePersistentSelections || __result is null)
        {
            return;
        }

        try
        {
            if (__result.IsCompletedSuccessfully)
            {
                RefreshCostumeView(__instance);
                return;
            }

            _ = Tasks.Task.Run(
                async () =>
                {
                    while (!__result.IsCompleted)
                    {
                        await Tasks.Task.Delay(50).ConfigureAwait(false);
                    }

                    if (!__result.IsCompletedSuccessfully)
                    {
                        return;
                    }

                    RefreshCostumeView(__instance);
                });
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend stabilizer CustomizeCharacterNewMetaView.CostumeChange postfix failed", exception);
        }
    }

    private static void RefreshCostumeView(CustomizeCharacterNewMetaView view)
    {
        try
        {
            var currentSkinTypeProperty = AccessTools.Property(typeof(CustomizeCharacterNewMetaView), "_currentSkinType");
            var currentSkinPartTypeProperty = AccessTools.Property(typeof(CustomizeCharacterNewMetaView), "_currentSkinPartType");
            var currentCategorySelectedIndexProperty = AccessTools.Property(typeof(CustomizeCharacterNewMetaView), "_currentCategorySelectedIndex");
            var currentSkinType = PlayerNewMetaInventoryChangeSkinEquippedPatch.TryConsumePendingSkinType(out var pendingSkinType)
                ? pendingSkinType
                : currentSkinTypeProperty?.GetValue(view) is SkinType skinType ? skinType : SkinType.None;
            var currentSkinPartType = PlayerNewMetaInventoryChangeSkinEquippedPatch.TryConsumePendingSkinPartType(out var pendingSkinPartType)
                ? pendingSkinPartType
                : currentSkinPartTypeProperty?.GetValue(view) is SkinPartType skinPartType ? skinPartType : SkinPartType.None;
            UnlockEverythingRuntime.LogSkinPreview("CustomizeCharacterNewMetaView.CostumeChange:refresh", 0, currentSkinType, currentSkinPartType, true);
            if (currentSkinType == SkinType.None)
            {
                return;
            }

            currentSkinTypeProperty?.SetValue(view, currentSkinType);
            currentSkinPartTypeProperty?.SetValue(view, currentSkinPartType);
            currentCategorySelectedIndexProperty?.SetValue(view, GetCategoryIndex(currentSkinType));
            InvokeCategoryView(view, currentSkinType);
            UnlockEverythingRuntime.LogCostumePieces("CustomizeCharacterNewMetaView.CostumeChange:afterShowCostume", view);
            AccessTools.Method(typeof(CustomizeCharacterNewMetaView), "CurrentCostumeSelectedSprite")?.Invoke(view, new object[] { currentSkinType });
            if (currentSkinPartType != SkinPartType.None)
            {
                AccessTools.Method(typeof(CustomizeCharacterNewMetaView), "ChangeSelectedSprite")?.Invoke(view, new object[] { currentSkinPartType });
            }

            UnlockEverythingRuntime.LogCostumePieces("CustomizeCharacterNewMetaView.CostumeChange:afterSelectionRefresh", view);
            UnlockEverythingSelections.SyncPreviewCharacterData(currentSkinType, currentSkinPartType);
            TryRefreshPreviewModel(currentSkinType, currentSkinPartType);
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend stabilizer CustomizeCharacterNewMetaView.CostumeChange refresh failed", exception);
        }
    }

    private static int GetCategoryIndex(SkinType skinType)
    {
        return skinType switch
        {
            SkinType.Head => 0,
            SkinType.Hands => 1,
            SkinType.Chest => 2,
            SkinType.Legs => 3,
            SkinType.Back => 4,
            SkinType.Whole => 5,
            _ => 0
        };
    }

    private static void InvokeCategoryView(CustomizeCharacterNewMetaView view, SkinType skinType)
    {
        var methodName = skinType switch
        {
            SkinType.Head => "ShowHeadTypes",
            SkinType.Hands => "ShowArmsTypes",
            SkinType.Chest => "ShowTorsoTypes",
            SkinType.Legs => "ShowLegsTypes",
            SkinType.Back => "ShowBackTypes",
            SkinType.Whole => "ShowWholeTypes",
            _ => null
        };

        if (methodName is null)
        {
            return;
        }

        AccessTools.Method(typeof(CustomizeCharacterNewMetaView), methodName)?.Invoke(view, Array.Empty<object>());
    }

    private static void TryRefreshPreviewModel(SkinType skinType, SkinPartType skinPartType)
    {
        try
        {
            var previewViews = UnityEngine.Resources.FindObjectsOfTypeAll<PlayerCustomizationView>();
            UnlockEverythingRuntime.LogSkinPreview("CustomizeCharacterNewMetaView.CostumeChange:previewViews", previewViews.Length, skinType, skinPartType, true);
            var tryPreviewOutfitMethod = AccessTools.Method(typeof(PlayerCustomizationView), "TryPreviewOutfit");
            if (tryPreviewOutfitMethod is null)
            {
                return;
            }

            foreach (var previewView in previewViews)
            {
                if (previewView is null)
                {
                    continue;
                }

                tryPreviewOutfitMethod.Invoke(previewView, new object[] { skinPartType, skinType });
                UnlockEverythingRuntime.LogSkinPreview("CustomizeCharacterNewMetaView.CostumeChange:previewInvoked", 0, skinType, skinPartType, true);
            }
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend stabilizer CustomizeCharacterNewMetaView preview refresh failed", exception);
        }
    }
}

[HarmonyPatch(typeof(PlayerCustomizationView), "OnTryOnCharacterOutfitLocally")]
internal static class PlayerCustomizationViewTryOnCharacterOutfitLoggingPatch
{
    private static void Postfix(object sender, EventArgs args)
    {
        if (args is null || !string.Equals(args.GetType().Name, nameof(TryOnCharacterOutfitLocallyEvent), StringComparison.Ordinal))
        {
            return;
        }

        var internalId = AccessTools.Property(args.GetType(), "InternalId")?.GetValue(args) is int value ? value : 0;
        var skinType = AccessTools.Property(args.GetType(), "SkinType")?.GetValue(args) is SkinType currentSkinType ? currentSkinType : SkinType.None;
        var skinPartType = AccessTools.Property(args.GetType(), "CostumeType")?.GetValue(args) is SkinPartType currentSkinPartType ? currentSkinPartType : SkinPartType.None;
        UnlockEverythingRuntime.LogSkinPreview("PlayerCustomizationView.OnTryOnCharacterOutfitLocally", internalId, skinType, skinPartType, true);
    }
}

[HarmonyPatch(typeof(PlayerCustomizationView), "OnRefreshCharacterOutfit")]
internal static class PlayerCustomizationViewRefreshCharacterOutfitLoggingPatch
{
    private static void Postfix(object sender, EventArgs args)
    {
        if (args is null || !string.Equals(args.GetType().Name, nameof(RefreshCharacterOutfit), StringComparison.Ordinal))
        {
            return;
        }

        var internalId = AccessTools.Property(args.GetType(), "InternalId")?.GetValue(args) is int value ? value : 0;
        UnlockEverythingRuntime.LogSkinRefreshEvent("PlayerCustomizationView.OnRefreshCharacterOutfit", internalId);
    }
}

[HarmonyPatch(typeof(PlayerCustomizationView), "TryPreviewOutfit")]
internal static class PlayerCustomizationViewTryPreviewOutfitLoggingPatch
{
    private static void Postfix(SkinPartType costumeType, SkinType skinType)
    {
        UnlockEverythingRuntime.LogSkinPreview("PlayerCustomizationView.TryPreviewOutfit", 0, skinType, costumeType, true);
    }
}


[HarmonyPatch(typeof(PlayerNewMetaInventory), nameof(PlayerNewMetaInventory.OnTreeSkillChange))]
internal static class PlayerNewMetaInventoryOnTreeSkillChangePatch
{
    private static bool Prefix(PlayerNewMetaInventory __instance, SkillType cardSkillType, int slotType, ClientCharacterType characterType)
    {
        UnlockEverythingSelections.RememberInventory(__instance);
        UnlockEverythingRuntime.LogSkillUiEvent("PlayerNewMetaInventory.OnTreeSkillChange:prefix", $"skill={cardSkillType}, slotType={slotType}, characterType={characterType}");
        return true;
    }

    private static void Postfix(ClientCharacterType characterType, Il2CppTasks.Task<bool> __result)
    {
        UnlockEverythingSelections.SaveAfterCompletion(__result, characterType);
    }

    internal static void RefreshSkillViews()
    {
        try
        {
            foreach (var view in UnityEngine.Resources.FindObjectsOfTypeAll<MainBoostersView>())
            {
                if (view is null)
                {
                    continue;
                }

                AccessTools.Method(typeof(MainBoostersView), "SetSkillTree")?.Invoke(view, Array.Empty<object>());
                AccessTools.Method(typeof(MainBoostersView), "SetEquippedSkills")?.Invoke(view, Array.Empty<object>());
                var equippedSkillsField = AccessTools.Field(typeof(MainBoostersView), "_equippedSkills");
                if (equippedSkillsField?.GetValue(view) is System.Collections.IEnumerable equippedSkills)
                {
                    var count = 0;
                    foreach (var _ in equippedSkills)
                    {
                        count++;
                    }

                    UnlockEverythingRuntime.LogSkillUiEvent("MainBoostersView.SetEquippedSkills", $"equippedSkillsCount={count}");
                }
            }
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend stabilizer skill view refresh failed", exception);
        }
    }
}

[HarmonyPatch]
internal static class PlayersActiveSkillsHaveSkillEquippedPatch
{
    private static System.Reflection.MethodBase? TargetMethod()
    {
        return UnlockEverythingSelections.GetPlayersActiveSkillsHaveSkillEquippedTargetMethod();
    }

    private static void Postfix(int internalId, SkillType cardSkillType, RuntimeCharacterType characterType, ref bool __result)
    {
        var originalResult = __result;
        if (UnlockEverythingSelections.TryGetLocalSkillEquipped(internalId, cardSkillType, characterType, out var equipped))
        {
            __result = equipped;
            if (!UnlockEverythingSelections.IsCurrentInternalIdForLogging(internalId))
            {
                UnlockEverythingRuntime.LogSkillUiEvent(
                    "PlayersActiveSkills.HaveSkillEquipped:remote",
                    $"internalId={internalId}, skill={cardSkillType}, characterType={characterType}, before={originalResult}, after={__result}");
            }
        }
    }
}

[HarmonyPatch]
internal static class PlayersActiveSkillsGetPlayerSkillModifierPatch
{
    private static System.Reflection.MethodBase? TargetMethod()
    {
        return UnlockEverythingSelections.GetPlayersActiveSkillsGetPlayerSkillModifierTargetMethod();
    }

    private static void Postfix(object __instance, int internalId, SkillType cardSkillType, object skillModifierType, ref float __result)
    {
        var originalResult = __result;
        if (!UnlockEverythingSelections.TryGetLocalSkillTier(internalId, cardSkillType, out var characterType, out var tier))
        {
            return;
        }

        if (UnlockEverythingSelections.TryGetDirectSkillModifier(__instance, cardSkillType, skillModifierType, characterType, tier, out var modifier))
        {
            __result = modifier;
            if (!UnlockEverythingSelections.IsCurrentInternalIdForLogging(internalId))
            {
                UnlockEverythingRuntime.LogSkillUiEvent(
                    "PlayersActiveSkills.GetPlayerSkillModifier:remote",
                    $"internalId={internalId}, skill={cardSkillType}, modifierType={skillModifierType}, tier={tier}, characterType={characterType}, before={originalResult}, after={__result}");
            }
        }
    }
}

[HarmonyPatch]
internal static class EntitySkillsComponentGetSkillPatch
{
    private static System.Reflection.MethodBase? TargetMethod()
    {
        return UnlockEverythingSelections.GetEntitySkillsComponentGetSkillTargetMethod();
    }

    private static void Postfix(object __instance, bool firstSkill, ref SpookedSkillType __result)
    {
        if (!firstSkill)
        {
            return;
        }

        if (UnlockEverythingSelections.TryGetLocalFirstSkill(__instance, out var firstSkillType))
        {
            __result = firstSkillType;
        }
    }
}

[HarmonyPatch]
internal static class SpookedNetworkPlayerSpawnedRememberPatch
{
    private static System.Reflection.MethodBase? TargetMethod()
    {
        var type = AccessTools.TypeByName("Gameplay.Player.Components.SpookedNetworkPlayer");
        return type is null ? null : AccessTools.Method(type, "Spawned");
    }

    private static void Postfix(object __instance)
    {
        UnlockEverythingSelections.RememberNetworkPlayer(__instance);
    }
}

[HarmonyPatch]
internal static class SpookedNetworkPlayerSpawnedReadyStartupSkinPatch
{
    private static System.Reflection.MethodBase? TargetMethod()
    {
        var type = AccessTools.TypeByName("Gameplay.Player.Components.SpookedNetworkPlayer");
        return type is null ? null : AccessTools.Method(type, "RPC_SpawnedReady");
    }

    private static void Postfix(object __instance)
    {
        UnlockEverythingSelections.RememberNetworkPlayer(__instance);
        UnlockEverythingSelections.ApplyStartupSkinSelectionsToLivePreview();
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.PutOnCharacterSkin))]
internal static class KinguinverseWebServicePutOnCharacterSkinPatch
{
    private static bool Prefix(int characterId, int characterSkinTypeId, ref Il2CppTasks.Task<Result<bool>> __result)
    {
        var handledLocally = UnlockEverythingRuntime.UsePersistentSelections && UnlockEverythingSelections.ApplyCharacterSkinSelection(characterId, characterSkinTypeId);
        UnlockEverythingRuntime.LogSkinWebCall("KinguinverseWebService.PutOnCharacterSkin:prefix", characterId, characterSkinTypeId, handledLocally);
        if (!handledLocally)
        {
            UnlockEverythingSelections.SaveCharacterSkinSelection(characterId, characterSkinTypeId);
            return true;
        }

        __result = UnlockEverythingStub.SuccessBoolean();
        return false;
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.PutOnAvatar))]
internal static class KinguinverseWebServicePutOnAvatarPatch
{
    private static bool Prefix(int characterId, int avatarId, ref Il2CppTasks.Task<Result<bool>> __result)
    {
        var handledLocally = UnlockEverythingRuntime.UsePersistentSelections && UnlockEverythingSelections.ApplyAvatarSelection(characterId, avatarId);
        if (!handledLocally)
        {
            UnlockEverythingRuntime.LogSkillUiEvent("KinguinverseWebService.PutOnAvatar:prefix", $"characterId={characterId}, avatarId={avatarId}, handledLocally=False");
            return true;
        }

        UnlockEverythingRuntime.LogSkillUiEvent("KinguinverseWebService.PutOnAvatar:prefix", $"characterId={characterId}, avatarId={avatarId}, handledLocally=True");
        __result = UnlockEverythingStub.SuccessBoolean();
        return false;
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.PutOnAvatarFrame))]
internal static class KinguinverseWebServicePutOnAvatarFramePatch
{
    private static bool Prefix(int characterId, int avatarFrameId, ref Il2CppTasks.Task<Result<bool>> __result)
    {
        var handledLocally = UnlockEverythingRuntime.UsePersistentSelections && UnlockEverythingSelections.ApplyAvatarFrameSelection(characterId, avatarFrameId);
        if (!handledLocally)
        {
            UnlockEverythingRuntime.LogSkillUiEvent("KinguinverseWebService.PutOnAvatarFrame:prefix", $"characterId={characterId}, avatarFrameId={avatarFrameId}, handledLocally=False");
            return true;
        }

        UnlockEverythingRuntime.LogSkillUiEvent("KinguinverseWebService.PutOnAvatarFrame:prefix", $"characterId={characterId}, avatarFrameId={avatarFrameId}, handledLocally=True");
        __result = UnlockEverythingStub.SuccessBoolean();
        return false;
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.PutOnCharacterDescription))]
internal static class KinguinverseWebServicePutOnCharacterDescriptionPatch
{
    private static bool Prefix(int characterId, DescriptionType descriptionType, ref Il2CppTasks.Task<Result<bool>> __result)
    {
        var handledLocally = UnlockEverythingRuntime.UsePersistentSelections && UnlockEverythingSelections.ApplyTitleSelection(characterId, descriptionType);
        if (!handledLocally)
        {
            UnlockEverythingRuntime.LogSkillUiEvent("KinguinverseWebService.PutOnCharacterDescription:prefix", $"characterId={characterId}, descriptionType={descriptionType}, handledLocally=False");
            return true;
        }

        UnlockEverythingRuntime.LogSkillUiEvent("KinguinverseWebService.PutOnCharacterDescription:prefix", $"characterId={characterId}, descriptionType={descriptionType}, handledLocally=True");
        __result = UnlockEverythingStub.SuccessBoolean();
        return false;
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.PutOnCharacterFart))]
internal static class KinguinverseWebServicePutOnCharacterFartPatch
{
    private static bool Prefix(int characterId, EmoteType emoteFart, ref Il2CppTasks.Task<Result<bool>> __result)
    {
        if (!UnlockEverythingRuntime.UsePersistentSelections || !UnlockEverythingSelections.ApplyFartSelection(characterId, emoteFart))
        {
            return true;
        }

        __result = UnlockEverythingStub.SuccessBoolean();
        return false;
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.PutOnCharacterDance))]
internal static class KinguinverseWebServicePutOnCharacterDancePatch
{
    private static bool Prefix(int characterId, EmoteType emoteFart, ref Il2CppTasks.Task<Result<bool>> __result)
    {
        if (!UnlockEverythingRuntime.UsePersistentSelections || !UnlockEverythingSelections.ApplyDanceSelection(characterId, emoteFart))
        {
            return true;
        }

        __result = UnlockEverythingStub.SuccessBoolean();
        return false;
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.PutOnSkinPart))]
internal static class KinguinverseWebServicePutOnSkinPartPatch
{
    private static bool Prefix(int characterId, int skinPartId, ref Il2CppTasks.Task<Result<bool>> __result)
    {
        var handledLocally = UnlockEverythingRuntime.UsePersistentSelections && UnlockEverythingSelections.ApplySkinPartSelection(characterId, skinPartId);
        UnlockEverythingRuntime.LogSkinWebCall("KinguinverseWebService.PutOnSkinPart:prefix", characterId, skinPartId, handledLocally);
        if (!handledLocally)
        {
            return true;
        }

        __result = UnlockEverythingStub.SuccessBoolean();
        return false;
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.PutOnSkillCard))]
internal static class KinguinverseWebServicePutOnSkillCardPatch
{
    private static bool Prefix(int characterId, int skillCardSlot, int skillCardId, ref Il2CppTasks.Task<Result<bool>> __result)
    {
        var handledLocally = UnlockEverythingRuntime.UsePersistentSelections && UnlockEverythingSelections.ApplySkillCardSelection(characterId, skillCardSlot, skillCardId);
        UnlockEverythingRuntime.LogSkillUiEvent("KinguinverseWebService.PutOnSkillCard:prefix", $"characterId={characterId}, slot={skillCardSlot}, skillCardId={skillCardId}, handledLocally={handledLocally}");
        if (!handledLocally)
        {
            return true;
        }

        __result = UnlockEverythingStub.SuccessBoolean();
        return false;
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.PutOffSkillCard))]
internal static class KinguinverseWebServicePutOffSkillCardPatch
{
    private static bool Prefix(int characterId, int skillCardSlot, ref Il2CppTasks.Task<Result<bool>> __result)
    {
        var handledLocally = UnlockEverythingRuntime.UsePersistentSelections && UnlockEverythingSelections.RemoveSkillCardSelection(characterId, skillCardSlot);
        UnlockEverythingRuntime.LogSkillUiEvent("KinguinverseWebService.PutOffSkillCard:prefix", $"characterId={characterId}, slot={skillCardSlot}, handledLocally={handledLocally}");
        if (!handledLocally)
        {
            return true;
        }

        __result = UnlockEverythingStub.SuccessBoolean();
        return false;
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.PutOffSkinPart))]
internal static class KinguinverseWebServicePutOffSkinPartPatch
{
    private static bool Prefix(int characterId, SkinType skinType, ref Il2CppTasks.Task<Result<bool>> __result)
    {
        var handledLocally = UnlockEverythingRuntime.UsePersistentSelections && UnlockEverythingSelections.RemoveSkinPartSelection(characterId, skinType);
        UnlockEverythingRuntime.LogSkinRemove("KinguinverseWebService.PutOffSkinPart:prefix", characterId, skinType, handledLocally);
        if (!handledLocally)
        {
            return true;
        }

        __result = UnlockEverythingStub.SuccessBoolean();
        return false;
    }
}

[HarmonyPatch(typeof(DailyQuestsView), "Refresh")]
internal static class DailyQuestsViewRefreshPatch
{
    private static bool Prefix(DailyQuestsView __instance)
    {
        if (!UnlockEverythingRuntime.UseLocalStub && !UnlockEverythingRuntime.UseProfileOverlay)
        {
            return true;
        }

        try
        {
            if (__instance._clientCache.PlayerDailyQuests is null)
            {
                UnlockEverythingOverlay.EnsureClientCache(__instance._clientCache);
            }

            UnlockEverythingRuntime.LogSuppressedLoop("DailyQuestsView.Refresh");
            return false;
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend stabilizer DailyQuestsView.Refresh prefix failed", exception);
            return false;
        }
    }
}

[HarmonyPatch(typeof(BattlepassView), "OnOnWebplayerRefreshEvent")]
internal static class BattlepassViewOnWebplayerRefreshPatch
{
    private static bool Prefix(BattlepassView __instance)
    {
        if (!UnlockEverythingRuntime.UseLocalStub && !UnlockEverythingRuntime.UseProfileOverlay)
        {
            return true;
        }

        try
        {
            if (__instance._clientCache.SeasonPass is null || __instance._clientCache.CurrentSeasonPassProgression is null)
            {
                UnlockEverythingOverlay.EnsureClientCache(__instance._clientCache);
            }

            UnlockEverythingRuntime.LogSuppressedLoop("BattlepassView.OnOnWebplayerRefreshEvent");
            return false;
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend stabilizer BattlepassView.OnOnWebplayerRefreshEvent prefix failed", exception);
            return false;
        }
    }
}

[HarmonyPatch(typeof(BattlepassView), "SetEndTime")]
internal static class BattlepassViewSetEndTimePatch
{
    private static bool Prefix(BattlepassView __instance)
    {
        if (!UnlockEverythingRuntime.UseLocalStub && !UnlockEverythingRuntime.UseProfileOverlay)
        {
            return true;
        }

        try
        {
            if (__instance._clientCache.SeasonPass is null)
            {
                UnlockEverythingOverlay.EnsureClientCache(__instance._clientCache);
            }

            UnlockEverythingRuntime.LogSuppressedLoop("BattlepassView.SetEndTime");
            return false;
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend stabilizer BattlepassView.SetEndTime prefix failed", exception);
            return false;
        }
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.RefreshPlayer))]
internal static class KinguinverseWebServiceRefreshPlayerPatch
{
    private static bool Prefix(ref Il2CppTasks.Task<Result<RefreshLobbyPlayerResponse>> __result)
    {
        if (!UnlockEverythingRuntime.UseLocalStub)
        {
            return true;
        }

        try
        {
            __result = UnlockEverythingStub.RefreshPlayer();
            return false;
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend local stub RefreshPlayer failed", exception);
            return true;
        }
    }

    private static void Postfix(Il2CppTasks.Task<Result<RefreshLobbyPlayerResponse>> __result)
    {
        if (UnlockEverythingRuntime.UseLocalStub || __result is null)
        {
            return;
        }

        try
        {
            if (__result.IsCompletedSuccessfully)
            {
                ApplyRefreshPlayerOverlay(__result);
                return;
            }

            _ = Tasks.Task.Run(
                async () =>
                {
                    while (!__result.IsCompleted)
                    {
                        await Tasks.Task.Delay(50).ConfigureAwait(false);
                    }

                    if (!__result.IsCompletedSuccessfully)
                    {
                        return;
                    }

                    try
                    {
                        ApplyRefreshPlayerOverlay(__result);
                    }
                    catch (Exception exception)
                    {
                        UnlockEverythingRuntime.LogError("Backend research RefreshPlayer completion overlay failed", exception);
                    }
                });
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend research RefreshPlayer postfix failed", exception);
        }
    }

    private static void ApplyRefreshPlayerOverlay(Il2CppTasks.Task<Result<RefreshLobbyPlayerResponse>> task)
    {
        var result = task.Result;
        if (result is null || !result.IsSuccessful || result.Value is null)
        {
            return;
        }

        if (UnlockEverythingRuntime.UseProfileOverlay)
        {
            UnlockEverythingStub.ApplyRefreshPlayerOverlay(result.Value);
        }

        UnlockEverythingRuntime.LogRefreshPlayerResponse("KinguinverseWebService.RefreshPlayer", result.Value);
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.GetProducts))]
internal static class KinguinverseWebServiceGetProductsPatch
{
    private static bool Prefix(ref Il2CppTasks.Task<Result<Il2CppCollections.List<Kinguinverse.WebServiceProvider.Types.Products.ProductDto>>> __result)
    {
        if (!UnlockEverythingRuntime.UseLocalStub)
        {
            return true;
        }

        try
        {
            __result = UnlockEverythingStub.GetProducts();
            return false;
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend local stub GetProducts failed", exception);
            return true;
        }
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.GetProductsV2))]
internal static class KinguinverseWebServiceGetProductsV2Patch
{
    private static bool Prefix(ref Il2CppTasks.Task<Result<Products>> __result)
    {
        if (!UnlockEverythingRuntime.UseLocalStub)
        {
            return true;
        }

        try
        {
            __result = UnlockEverythingStub.GetProductsV2();
            return false;
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend local stub GetProductsV2 failed", exception);
            return true;
        }
    }

    private static void Postfix(Il2CppTasks.Task<Result<Products>> __result)
    {
        if (UnlockEverythingRuntime.UseLocalStub || __result is null || !__result.IsCompletedSuccessfully)
        {
            return;
        }

        try
        {
            var result = __result.Result;
            if (result is not null && result.IsSuccessful && result.Value is not null)
            {
                UnlockEverythingRuntime.LogProductsResponse("KinguinverseWebService.GetProductsV2", result.Value);
            }
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend research GetProductsV2 postfix failed", exception);
        }
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.GetGameUserMetadata))]
internal static class KinguinverseWebServiceGetGameUserMetadataPatch
{
    private static bool Prefix(int userId, string key, ref Il2CppTasks.Task<Result<GetUserMetadataResponse>> __result)
    {
        if (!UnlockEverythingRuntime.UseLocalStub)
        {
            return true;
        }

        try
        {
            __result = UnlockEverythingStub.GetGameUserMetadata(userId, key);
            return false;
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend local stub GetGameUserMetadata failed", exception);
            return true;
        }
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.GetGameUserMetadatas))]
internal static class KinguinverseWebServiceGetGameUserMetadatasPatch
{
    private static bool Prefix(int userId, ref Il2CppTasks.Task<Result<Il2CppCollections.Dictionary<string, string>>> __result)
    {
        if (!UnlockEverythingRuntime.UseLocalStub)
        {
            return true;
        }

        try
        {
            __result = UnlockEverythingStub.GetGameUserMetadatas(userId);
            return false;
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend local stub GetGameUserMetadatas failed", exception);
            return true;
        }
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.SetGameUserMetadata))]
internal static class KinguinverseWebServiceSetGameUserMetadataPatch
{
    private static bool Prefix(int userId, string key, string value, ref Il2CppTasks.Task<Result> __result)
    {
        if (!UnlockEverythingRuntime.UseLocalStub)
        {
            return true;
        }

        try
        {
            __result = UnlockEverythingStub.SetGameUserMetadata(userId, key, value);
            return false;
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend local stub SetGameUserMetadata failed", exception);
            return true;
        }
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.SetGameUserMetadatas))]
internal static class KinguinverseWebServiceSetGameUserMetadatasPatch
{
    private static bool Prefix(int userId, SetUserMetadatasRequest request, ref Il2CppTasks.Task<Result> __result)
    {
        if (!UnlockEverythingRuntime.UseLocalStub)
        {
            return true;
        }

        try
        {
            __result = UnlockEverythingStub.SetGameUserMetadatas(userId, request);
            return false;
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend local stub SetGameUserMetadatas failed", exception);
            return true;
        }
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.GetPlayerMessages))]
internal static class KinguinverseWebServiceGetPlayerMessagesPatch
{
    private static bool Prefix(ref Il2CppTasks.Task<Result<Il2CppCollections.List<PlayerSystemMessage>>> __result)
    {
        if (!UnlockEverythingRuntime.UseLocalStub)
        {
            return true;
        }

        try
        {
            __result = UnlockEverythingStub.GetPlayerMessages();
            return false;
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend local stub GetPlayerMessages failed", exception);
            return true;
        }
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.GetPlayerResources))]
internal static class KinguinverseWebServiceGetPlayerResourcesPatch
{
    private static bool Prefix(ref Il2CppTasks.Task<Result<PlayerResources>> __result)
    {
        if (!UnlockEverythingRuntime.UseLocalStub)
        {
            return true;
        }

        try
        {
            __result = UnlockEverythingStub.GetPlayerResources();
            return false;
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend local stub GetPlayerResources failed", exception);
            return true;
        }
    }

    private static void Postfix(Il2CppTasks.Task<Result<PlayerResources>> __result)
    {
        if (UnlockEverythingRuntime.UseLocalStub || __result is null || !__result.IsCompletedSuccessfully)
        {
            return;
        }

        try
        {
            var result = __result.Result;
            if (result is not null && result.IsSuccessful && result.Value is not null)
            {
                UnlockEverythingRuntime.LogResourcesResponse("KinguinverseWebService.GetPlayerResources", result.Value);
            }
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend research GetPlayerResources postfix failed", exception);
        }
    }
}

[HarmonyPatch]
internal static class KinguinverseWebServiceGetPlayerByUserIdPatch
{
    private static System.Reflection.MethodBase? TargetMethod()
    {
        return AccessTools.Method(typeof(KinguinverseWebService), nameof(KinguinverseWebService.GetPlayer), new[] { typeof(int) });
    }

    private static void Postfix(int userId, Il2CppTasks.Task<Result<WebPlayersSimplified>> __result)
    {
        if (__result is null || !UnlockEverythingRuntime.UseProfileOverlay)
        {
            return;
        }

        try
        {
            if (!__result.IsCompletedSuccessfully)
            {
                return;
            }

            if (__result.Result is { IsSuccessful: true, Value: not null } result)
            {
                UnlockEverythingStub.ApplyWebPlayerSimplifiedOverlay(result.Value);
                UnlockEverythingRuntime.LogSkillUiEvent("KinguinverseWebService.GetPlayer:overlayApplied", $"userId={userId}, characters={result.Value.Characters?.Count ?? 0}");
            }
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend stabilizer GetPlayer(int) postfix failed", exception);
        }
    }
}

[HarmonyPatch]
internal static class SceneSpawnerOnPlayerLoadedSkillPayloadPatch
{
    private static System.Reflection.MethodBase? TargetMethod()
    {
        return UnlockEverythingSelections.GetSceneSpawnerOnPlayerLoadedTargetMethod();
    }

    private static void Prefix(object[] __args)
    {
        if (!UnlockEverythingRuntime.UseProfileOverlay || __args.Length <= 18)
        {
            return;
        }

        try
        {
            var before = UnlockEverythingSelections.DescribeCharactersSkillsPayload(__args[18]);
            var payload = __args[18];
            var applied = UnlockEverythingSelections.TryMaxCharactersSkillsPayload(ref payload);
            __args[18] = payload!;
            var nickname = __args.Length > 16 ? __args[16]?.ToString() ?? string.Empty : string.Empty;
            var internalId = __args.Length > 0 && __args[0] is int value ? value : 0;
            UnlockEverythingSelections.RememberLoadedCharactersSkills(internalId, __args[18]);
            UnlockEverythingRuntime.LogSkillUiEvent(
                "SceneSpawner.OnPlayerLoaded:skillsPayload",
                $"internalId={internalId}, nickname={nickname}, applied={applied}, before={before}, after={UnlockEverythingSelections.DescribeCharactersSkillsPayload(__args[18])}");
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend stabilizer SceneSpawner.OnPlayerLoaded skills payload fix failed", exception);
        }
    }
}

[HarmonyPatch]
internal static class SpookedNetworkPlayerInitSkillPayloadPatch
{
    private static System.Reflection.MethodBase? TargetMethod()
    {
        return UnlockEverythingSelections.GetSpookedNetworkPlayerInitTargetMethod();
    }

    private static void Prefix(object[] __args)
    {
        if (!UnlockEverythingRuntime.UseProfileOverlay || __args.Length <= 3)
        {
            return;
        }

        try
        {
            var before = UnlockEverythingSelections.DescribeCharactersSkillsPayload(__args[3]);
            var payload = __args[3];
            var applied = UnlockEverythingSelections.TryMaxCharactersSkillsPayload(ref payload);
            __args[3] = payload!;
            var internalId = __args[0] is int networkId ? networkId : 0;
            var nickname = __args[1]?.ToString() ?? string.Empty;
            UnlockEverythingSelections.RememberLoadedCharactersSkills(internalId, __args[3]);
            UnlockEverythingRuntime.LogSkillUiEvent(
                "SpookedNetworkPlayer.Init:skillsPayload",
                $"internalId={internalId}, nickname={nickname}, applied={applied}, before={before}, after={UnlockEverythingSelections.DescribeCharactersSkillsPayload(__args[3])}");
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend stabilizer SpookedNetworkPlayer.Init skills payload fix failed", exception);
        }
    }

    private static void Postfix(object __instance)
    {
        if (!UnlockEverythingRuntime.UseProfileOverlay)
        {
            return;
        }

        try
        {
            var internalId = UnlockEverythingSelections.GetNetworkPlayerInternalId(__instance);
            UnlockEverythingRuntime.LogSkillUiEvent(
                "SpookedNetworkPlayer.Init:liveCharactersSkills",
                $"internalId={internalId}, payload={UnlockEverythingSelections.DescribeLiveSpookedNetworkPlayerCharactersSkills(__instance)}");
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend stabilizer SpookedNetworkPlayer.Init live skill snapshot failed", exception);
        }
    }
}

[HarmonyPatch]
internal static class ScopeCleanerCleanPatch
{
    private static System.Reflection.MethodBase? TargetMethod()
    {
        return UnlockEverythingSelections.GetScopeCleanerCleanTargetMethod();
    }

    private static void Postfix()
    {
        UnlockEverythingSelections.ClearLoadedCharactersSkills();
    }
}

[HarmonyPatch]
internal static class ScopeCleanerGameplayCleanPatch
{
    private static System.Reflection.MethodBase? TargetMethod()
    {
        return UnlockEverythingSelections.GetScopeCleanerGameplayCleanTargetMethod();
    }

    private static void Postfix()
    {
        UnlockEverythingSelections.ClearLoadedCharactersSkills();
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.GetMyBoosters))]
internal static class KinguinverseWebServiceGetMyBoostersPatch
{
    private static bool Prefix(ref Il2CppTasks.Task<Result<PlayerBoosters>> __result)
    {
        if (!UnlockEverythingRuntime.UseLocalStub)
        {
            return true;
        }

        try
        {
            __result = UnlockEverythingStub.GetMyBoosters();
            return false;
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend local stub GetMyBoosters failed", exception);
            return true;
        }
    }
}
