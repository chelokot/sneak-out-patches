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
using UI.Views;
using ClientCharacterType = Types.CharacterType;
using Il2CppCollections = Il2CppSystem.Collections.Generic;
using Il2CppTasks = Il2CppSystem.Threading.Tasks;
using Tasks = System.Threading.Tasks;

namespace SneakOut.BackendStabilizer;

internal static class BackendStabilizerOverlay
{
    public static void EnsureClientCache(ClientCache clientCache)
    {
        if (BackendStabilizerRuntime.UseLocalStub)
        {
            BackendStabilizerStub.PopulateClientCache(clientCache);
            return;
        }

        if (BackendStabilizerRuntime.UseProfileOverlay)
        {
            BackendStabilizerStub.ApplyProfileOverlay(clientCache);
        }
    }
}

internal static class BackendStabilizerSelections
{
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
        return BackendStabilizerRuntime.CurrentClientCache?.UserWebPlayer;
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

    private static void SaveSelection(Character character)
    {
        LocalSelectionsStore.SaveCharacterSelection(character);
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

    private static bool TryGetCharacterId(ClientCharacterType clientCharacterType, out int characterId)
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

        var typeName = productType.GetType().Name;
        var valueName = productType.ToString();

        if (string.Equals(typeName, nameof(AvatarType), StringComparison.Ordinal) && System.Enum.TryParse(valueName, out avatarType))
        {
            return avatarType != AvatarType.None;
        }

        if (string.Equals(typeName, nameof(AvatarFrameType), StringComparison.Ordinal) && System.Enum.TryParse(valueName, out avatarFrameType))
        {
            return avatarFrameType != AvatarFrameType.None;
        }

        if (string.Equals(typeName, nameof(DescriptionType), StringComparison.Ordinal) && System.Enum.TryParse(valueName, out descriptionType))
        {
            return descriptionType != DescriptionType.none;
        }

        return false;
    }

    public static bool ApplyAvatarSelection(int characterId, int avatarId)
    {
        if (!BackendStabilizerRuntime.UsePersistentSelections)
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
            SaveSelection(character);
            return true;
        }

        return false;
    }

    public static bool ApplyAvatarFrameSelection(int characterId, int avatarFrameId)
    {
        if (!BackendStabilizerRuntime.UsePersistentSelections)
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
            SaveSelection(character);
            return true;
        }

        return false;
    }

    public static bool ApplyTitleSelection(int characterId, DescriptionType descriptionType)
    {
        if (!BackendStabilizerRuntime.UsePersistentSelections)
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
        SaveSelection(character);
        return true;
    }

    public static bool ApplyDanceSelection(int characterId, EmoteType emoteType)
    {
        if (!BackendStabilizerRuntime.UsePersistentSelections)
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
        if (!BackendStabilizerRuntime.UsePersistentSelections)
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
        if (!BackendStabilizerRuntime.UsePersistentSelections)
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
        var skinId = BackendStabilizerStub.GetCharacterSkinId(characterSkin);
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
        if (!BackendStabilizerRuntime.UsePersistentSelections)
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
            return true;
        }

        return false;
    }

    public static bool ApplySkinPartSelection(ClientCharacterType clientCharacterType, SkinType skinType, SkinPartType skinPartType)
    {
        if (!BackendStabilizerRuntime.UsePersistentSelections)
        {
            return false;
        }

        var player = GetPlayer();
        if (!TryMapClientCharacterType(clientCharacterType, out var characterType))
        {
            BackendStabilizerRuntime.LogSkinSelectionResolution("BackendStabilizerSelections.ApplySkinPartSelection:mapFailed", clientCharacterType, CharacterType.None, 0, player is not null, false);
            return false;
        }

        var character = GetCharacterByType(characterType);
        var characterId = character?.CharacterId ?? 0;
        BackendStabilizerRuntime.LogSkinSelectionResolution("BackendStabilizerSelections.ApplySkinPartSelection:resolved", clientCharacterType, characterType, characterId, player is not null, character is not null);
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
            selectedSkinPart = new SkinPart(BackendStabilizerStub.GetSkinPartId(skinPartType), skinType, skinPartType);
            player.Skins.SkinParts.Add(selectedSkinPart);
        }
        else
        {
            selectedSkinPart.Id = BackendStabilizerStub.GetSkinPartId(skinPartType);
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
        BackendStabilizerRuntime.LogSkinSelectionSnapshot("BackendStabilizerSelections.ApplySkinPartSelection:applied", character);
        SyncPreviewCharacterData(skinType, skinPartType);
        PublishSkinRefresh(skinPartType, skinType);
        return true;
    }

    internal static void SyncPreviewCharacterData(SkinType skinType, SkinPartType skinPartType)
    {
        try
        {
            var gameType = AccessTools.TypeByName("Game");
            var internalIdProperty = AccessTools.Property(gameType, "InternalId");
            if (internalIdProperty?.GetValue(null) is not int internalId || internalId <= 0)
            {
                BackendStabilizerRuntime.LogSkinPreview("BackendStabilizerSelections.SyncPreviewCharacterData:noInternalId", 0, skinType, skinPartType, false);
                return;
            }

            var previewViews = UnityEngine.Resources.FindObjectsOfTypeAll<PlayerCustomizationView>();
            BackendStabilizerRuntime.LogSkinPreview("BackendStabilizerSelections.SyncPreviewCharacterData:views", previewViews.Length, skinType, skinPartType, true);
            foreach (var previewView in previewViews)
            {
                if (previewView is null)
                {
                    continue;
                }

                var spookedPlayerCharacterDataMethod = AccessTools.Method(typeof(PlayerCustomizationView), "get__spookedPlayerCharacterData");
                if (spookedPlayerCharacterDataMethod?.Invoke(previewView, Array.Empty<object>()) is not SpookedPlayerCharacterData spookedPlayerCharacterData)
                {
                    BackendStabilizerRuntime.LogSkinPreview("BackendStabilizerSelections.SyncPreviewCharacterData:noPlayerData", internalId, skinType, skinPartType, false);
                    continue;
                }

                var currentCharacterData = spookedPlayerCharacterData[internalId];
                BackendStabilizerRuntime.LogSkinPreview("BackendStabilizerSelections.SyncPreviewCharacterData:before", internalId, skinType, skinPartType, true);
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
                AccessTools.Method(typeof(PlayerCustomizationView), "set__currentCharacterData")?.Invoke(previewView, new object[] { currentCharacterData });
                BackendStabilizerRuntime.LogSkinPreview("BackendStabilizerSelections.SyncPreviewCharacterData:applied", internalId, skinType, skinPartType, true);
            }
        }
        catch (Exception exception)
        {
            BackendStabilizerRuntime.LogError("Backend stabilizer preview CharacterData sync failed", exception);
        }
    }

    private static void PublishSkinRefresh(SkinPartType skinPartType, SkinType skinType)
    {
        var gameType = AccessTools.TypeByName("Game");
        var internalIdProperty = AccessTools.Property(gameType, "InternalId");
        if (internalIdProperty?.GetValue(null) is not int internalId)
        {
            BackendStabilizerRuntime.LogSkinPreview("BackendStabilizerSelections.PublishSkinRefresh:noInternalId", 0, skinType, skinPartType, false);
            return;
        }

        if (internalId <= 0)
        {
            BackendStabilizerRuntime.LogSkinPreview("BackendStabilizerSelections.PublishSkinRefresh:invalidInternalId", internalId, skinType, skinPartType, false);
            return;
        }

        BackendStabilizerRuntime.LogSkinPreview("BackendStabilizerSelections.PublishSkinRefresh:publish", internalId, skinType, skinPartType, true);
        GameEventsManager.Publish<TryOnCharacterOutfitLocallyEvent>(null, new TryOnCharacterOutfitLocallyEvent(internalId, skinPartType, skinType));
        GameEventsManager.Publish<RefreshCharacterOutfit>(null, new RefreshCharacterOutfit(internalId));
    }

    public static bool RemoveSkinPartSelection(int characterId, SkinType skinType)
    {
        if (!BackendStabilizerRuntime.UsePersistentSelections)
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

    public static bool ApplyTreeSkillSelection(ClientCharacterType clientCharacterType, SkillType skillType, int slotType)
    {
        if (!BackendStabilizerRuntime.UsePersistentSelections)
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
        if (character is null || cards is null)
        {
            return false;
        }

        SkillCard? selectedCard = null;
        foreach (var card in cards)
        {
            if (card is not null && card.SkillType == skillType)
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
        switch (slotType)
        {
            case 1:
                character.SkillCards.PassiveSkillCard1 = selectedCard;
                break;
            case 2:
                character.SkillCards.PassiveSkillCard2 = selectedCard;
                break;
            case 3:
                character.SkillCards.PassiveSkillCard3 = selectedCard;
                break;
            case 4:
                character.SkillCards.PassiveSkillCard4 = selectedCard;
                break;
            default:
                return false;
        }

        SaveSelection(character);
        return true;
    }

    public static bool ApplyAvatarModificationSelection(Il2CppSystem.Enum productType, ClientCharacterType clientCharacterType)
    {
        if (!BackendStabilizerRuntime.UsePersistentSelections || !TryGetCharacterId(clientCharacterType, out var characterId))
        {
            return false;
        }

        if (!TryParseAvatarProduct(productType, out var avatarType, out var avatarFrameType, out var descriptionType))
        {
            return false;
        }

        if (avatarType != AvatarType.None)
        {
            return ApplyAvatarSelection(characterId, BackendStabilizerStub.GetAvatarId(avatarType));
        }

        if (avatarFrameType != AvatarFrameType.None)
        {
            return ApplyAvatarFrameSelection(characterId, BackendStabilizerStub.GetAvatarFrameId(avatarFrameType));
        }

        if (descriptionType != DescriptionType.none)
        {
            return ApplyTitleSelection(characterId, descriptionType);
        }

        return false;
    }

    public static void SaveCurrentCharacterSelection(CharacterType characterType)
    {
        if (!BackendStabilizerRuntime.UsePersistentSelections)
        {
            return;
        }

        var clientCache = BackendStabilizerRuntime.CurrentClientCache;
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
        if (!BackendStabilizerRuntime.UsePersistentSelections || !BackendStabilizerStub.TryGetCharacterTypeById(characterId, out var characterType))
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
        if (!BackendStabilizerRuntime.UsePersistentSelections || task is null)
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
            BackendStabilizerRuntime.LogError("Backend stabilizer selection persistence failed", exception);
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
        if (!BackendStabilizerRuntime.UsePersistentSelections || task is null)
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
            BackendStabilizerRuntime.LogError("Backend stabilizer skin persistence failed", exception);
        }
    }

    public static void SaveAfterCompletion(Il2CppTasks.Task<bool> task, CharacterType characterType)
    {
        if (!BackendStabilizerRuntime.UsePersistentSelections || task is null)
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
            BackendStabilizerRuntime.LogError("Backend stabilizer selection persistence failed", exception);
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

[HarmonyPatch(typeof(ClientCache), nameof(ClientCache.OnClientConfirmed))]
internal static class ClientCacheOnClientConfirmedPatch
{
    private static void Postfix(ClientCache __instance)
    {
        try
        {
            BackendStabilizerRuntime.TrackClientCache(__instance);
            BackendStabilizerOverlay.EnsureClientCache(__instance);

            BackendStabilizerRuntime.LogClientCacheState("ClientCache.OnClientConfirmed", __instance);
        }
        catch (Exception exception)
        {
            BackendStabilizerRuntime.LogError("Backend stabilizer ClientCache.OnClientConfirmed postfix failed", exception);
        }
    }
}

[HarmonyPatch(typeof(PlayerNewMetaInventory), nameof(PlayerNewMetaInventory.GetSkillCard))]
internal static class PlayerNewMetaInventoryGetSkillCardPatch
{
    private static void Postfix(SkillType skillType, ref SkillCard __result)
    {
        if (!BackendStabilizerRuntime.UseProfileOverlay && !BackendStabilizerRuntime.UseLocalStub)
        {
            return;
        }

        if (skillType == SkillType.None || __result is not null)
        {
            return;
        }

        try
        {
            __result = BackendStabilizerStub.CreateMaxSkillCard(skillType);
        }
        catch (Exception exception)
        {
            BackendStabilizerRuntime.LogError("Backend stabilizer PlayerNewMetaInventory.GetSkillCard postfix failed", exception);
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
            BackendStabilizerRuntime.TrackClientCache(__instance);
            if (BackendStabilizerRuntime.UseLocalStub)
            {
                BackendStabilizerStub.PopulateClientCache(__instance);
            }

            BackendStabilizerRuntime.LogClientCacheState("ClientCache.RefreshPlayer", __instance);
        }
        catch (Exception exception)
        {
            BackendStabilizerRuntime.LogError("Backend stabilizer ClientCache.RefreshPlayer prefix failed", exception);
        }
    }

    private static void Postfix(ClientCache __instance, Il2CppTasks.Task __result)
    {
        if (!BackendStabilizerRuntime.UseProfileOverlay || BackendStabilizerRuntime.UseLocalStub)
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
                BackendStabilizerStub.ApplyProfileOverlay(__instance);
                BackendStabilizerRuntime.LogClientCacheState("ClientCache.RefreshPlayer:completed", __instance);
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
                        BackendStabilizerStub.ApplyProfileOverlay(__instance);
                        BackendStabilizerRuntime.LogClientCacheState("ClientCache.RefreshPlayer:completed", __instance);
                    }
                    catch (Exception exception)
                    {
                        BackendStabilizerRuntime.LogError("Backend stabilizer ClientCache.RefreshPlayer completion overlay failed", exception);
                    }
                });
        }
        catch (Exception exception)
        {
            BackendStabilizerRuntime.LogError("Backend stabilizer ClientCache.RefreshPlayer completion overlay failed", exception);
        }
    }
}

[HarmonyPatch(typeof(PlayerNewMetaInventory), nameof(PlayerNewMetaInventory.OnAvatarModyficationChange))]
internal static class PlayerNewMetaInventoryOnAvatarModyficationChangePatch
{
    private static bool Prefix(Il2CppSystem.Enum productType, ClientCharacterType characterType, ref Il2CppTasks.Task<bool> __result)
    {
        if (!BackendStabilizerRuntime.UsePersistentSelections || !BackendStabilizerSelections.ApplyAvatarModificationSelection(productType, characterType))
        {
            return true;
        }

        __result = Il2CppTasks.Task.FromResult(true);
        return false;
    }

    private static void Postfix(ClientCharacterType characterType, Il2CppTasks.Task<bool> __result)
    {
        BackendStabilizerSelections.SaveAfterCompletion(__result, characterType);
    }
}

[HarmonyPatch(typeof(PlayerNewMetaInventory), nameof(PlayerNewMetaInventory.EmoteChange))]
internal static class PlayerNewMetaInventoryEmoteChangePatch
{
    private static void Postfix(ClientCharacterType characterType, Il2CppTasks.Task<bool> __result)
    {
        BackendStabilizerSelections.SaveAfterCompletion(__result, characterType);
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

    private static bool Prefix(SkinPartType partType, SkinType skinType, ClientCharacterType characterType, ref Il2CppTasks.Task __result)
    {
        var handledLocally = BackendStabilizerRuntime.UsePersistentSelections
            && BackendStabilizerSelections.ApplySkinPartSelection(characterType, skinType, partType);
        BackendStabilizerRuntime.LogSkinPath("PlayerNewMetaInventory.ChangeSkinEquipped:prefix", characterType, skinType, partType, handledLocally);
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
        BackendStabilizerSelections.SaveAfterCompletion(__result, characterType);
        BackendStabilizerSelections.SaveSkinPartAfterCompletion(__result, characterType, skinType, partType);
    }
}

[HarmonyPatch(typeof(PlayerNewMetaInventory), nameof(PlayerNewMetaInventory.GetMyCurrentSkinPartType))]
internal static class PlayerNewMetaInventoryGetMyCurrentSkinPartTypeLoggingPatch
{
    private static void Postfix(SkinType skinType, ClientCharacterType characterType, SkinPartType __result)
    {
        BackendStabilizerRuntime.LogSkinPath("PlayerNewMetaInventory.GetMyCurrentSkinPartType", characterType, skinType, __result, true);
    }
}

[HarmonyPatch(typeof(CustomizeCharacterNewMetaView), "OnCostumePiecked")]
internal static class CustomizeCharacterNewMetaViewOnCostumePieckedLoggingPatch
{
    private static void Postfix(CustomizeCharacterNewMetaView __instance, SkinPartType skinPartType, SkinType skinType)
    {
        BackendStabilizerRuntime.LogSkinPreview("CustomizeCharacterNewMetaView.OnCostumePiecked", 0, skinType, skinPartType, true);
        BackendStabilizerRuntime.LogCostumePieces("CustomizeCharacterNewMetaView.OnCostumePiecked:pieces", __instance);
    }
}

[HarmonyPatch(typeof(CustomizeCharacterNewMetaView), "OnEquipButton")]
internal static class CustomizeCharacterNewMetaViewOnEquipButtonLoggingPatch
{
    private static void Postfix()
    {
        BackendStabilizerRuntime.LogSkinRefreshEvent("CustomizeCharacterNewMetaView.OnEquipButton", 0);
    }
}

[HarmonyPatch(typeof(CustomizeCharacterNewMetaView), "CostumeChange")]
internal static class CustomizeCharacterNewMetaViewCostumeChangePatch
{
    private static void Postfix(CustomizeCharacterNewMetaView __instance, Il2CppTasks.Task __result)
    {
        if (!BackendStabilizerRuntime.UsePersistentSelections || __result is null)
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
            BackendStabilizerRuntime.LogError("Backend stabilizer CustomizeCharacterNewMetaView.CostumeChange postfix failed", exception);
        }
    }

    private static void RefreshCostumeView(CustomizeCharacterNewMetaView view)
    {
        try
        {
            var currentSkinTypeProperty = AccessTools.Property(typeof(CustomizeCharacterNewMetaView), "_currentSkinType");
            var currentSkinPartTypeProperty = AccessTools.Property(typeof(CustomizeCharacterNewMetaView), "_currentSkinPartType");
            var currentSkinType = PlayerNewMetaInventoryChangeSkinEquippedPatch.TryConsumePendingSkinType(out var pendingSkinType)
                ? pendingSkinType
                : currentSkinTypeProperty?.GetValue(view) is SkinType skinType ? skinType : SkinType.None;
            var currentSkinPartType = PlayerNewMetaInventoryChangeSkinEquippedPatch.TryConsumePendingSkinPartType(out var pendingSkinPartType)
                ? pendingSkinPartType
                : currentSkinPartTypeProperty?.GetValue(view) is SkinPartType skinPartType ? skinPartType : SkinPartType.None;
            BackendStabilizerRuntime.LogSkinPreview("CustomizeCharacterNewMetaView.CostumeChange:refresh", 0, currentSkinType, currentSkinPartType, true);
            if (currentSkinType == SkinType.None)
            {
                return;
            }

            AccessTools.Method(typeof(CustomizeCharacterNewMetaView), "ShowCostume")?.Invoke(view, new object[] { currentSkinType });
            BackendStabilizerRuntime.LogCostumePieces("CustomizeCharacterNewMetaView.CostumeChange:afterShowCostume", view);
            AccessTools.Method(typeof(CustomizeCharacterNewMetaView), "CurrentCostumeSelectedSprite")?.Invoke(view, new object[] { currentSkinType });
            if (currentSkinPartType != SkinPartType.None)
            {
                AccessTools.Method(typeof(CustomizeCharacterNewMetaView), "ChangeSelectedSprite")?.Invoke(view, new object[] { currentSkinPartType });
            }

            BackendStabilizerRuntime.LogCostumePieces("CustomizeCharacterNewMetaView.CostumeChange:afterSelectionRefresh", view);
            BackendStabilizerSelections.SyncPreviewCharacterData(currentSkinType, currentSkinPartType);
            TryRefreshPreviewModel(currentSkinType, currentSkinPartType);
        }
        catch (Exception exception)
        {
            BackendStabilizerRuntime.LogError("Backend stabilizer CustomizeCharacterNewMetaView.CostumeChange refresh failed", exception);
        }
    }

    private static void TryRefreshPreviewModel(SkinType skinType, SkinPartType skinPartType)
    {
        try
        {
            var previewViews = UnityEngine.Resources.FindObjectsOfTypeAll<PlayerCustomizationView>();
            BackendStabilizerRuntime.LogSkinPreview("CustomizeCharacterNewMetaView.CostumeChange:previewViews", previewViews.Length, skinType, skinPartType, true);
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
                BackendStabilizerRuntime.LogSkinPreview("CustomizeCharacterNewMetaView.CostumeChange:previewInvoked", 0, skinType, skinPartType, true);
            }
        }
        catch (Exception exception)
        {
            BackendStabilizerRuntime.LogError("Backend stabilizer CustomizeCharacterNewMetaView preview refresh failed", exception);
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
        BackendStabilizerRuntime.LogSkinPreview("PlayerCustomizationView.OnTryOnCharacterOutfitLocally", internalId, skinType, skinPartType, true);
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
        BackendStabilizerRuntime.LogSkinRefreshEvent("PlayerCustomizationView.OnRefreshCharacterOutfit", internalId);
    }
}

[HarmonyPatch(typeof(PlayerCustomizationView), "TryPreviewOutfit")]
internal static class PlayerCustomizationViewTryPreviewOutfitLoggingPatch
{
    private static void Postfix(SkinPartType costumeType, SkinType skinType)
    {
        BackendStabilizerRuntime.LogSkinPreview("PlayerCustomizationView.TryPreviewOutfit", 0, skinType, costumeType, true);
    }
}


[HarmonyPatch(typeof(PlayerNewMetaInventory), nameof(PlayerNewMetaInventory.OnTreeSkillChange))]
internal static class PlayerNewMetaInventoryOnTreeSkillChangePatch
{
    private static bool Prefix(SkillType cardSkillType, int slotType, ClientCharacterType characterType, ref Il2CppTasks.Task<bool> __result)
    {
        if (!BackendStabilizerRuntime.UsePersistentSelections)
        {
            return true;
        }

        if (!BackendStabilizerSelections.ApplyTreeSkillSelection(characterType, cardSkillType, slotType))
        {
            return true;
        }

        __result = Il2CppTasks.Task.FromResult(true);
        return false;
    }

    private static void Postfix(ClientCharacterType characterType, Il2CppTasks.Task<bool> __result)
    {
        BackendStabilizerSelections.SaveAfterCompletion(__result, characterType);
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.PutOnCharacterSkin))]
internal static class KinguinverseWebServicePutOnCharacterSkinPatch
{
    private static bool Prefix(int characterId, int characterSkinTypeId, ref Il2CppTasks.Task<Result<bool>> __result)
    {
        var handledLocally = BackendStabilizerRuntime.UsePersistentSelections && BackendStabilizerSelections.ApplyCharacterSkinSelection(characterId, characterSkinTypeId);
        BackendStabilizerRuntime.LogSkinWebCall("KinguinverseWebService.PutOnCharacterSkin:prefix", characterId, characterSkinTypeId, handledLocally);
        if (!handledLocally)
        {
            BackendStabilizerSelections.SaveCharacterSkinSelection(characterId, characterSkinTypeId);
            return true;
        }

        __result = BackendStabilizerStub.SuccessBoolean();
        return false;
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.PutOnAvatar))]
internal static class KinguinverseWebServicePutOnAvatarPatch
{
    private static bool Prefix(int characterId, int avatarId, ref Il2CppTasks.Task<Result<bool>> __result)
    {
        if (!BackendStabilizerRuntime.UsePersistentSelections || !BackendStabilizerSelections.ApplyAvatarSelection(characterId, avatarId))
        {
            return true;
        }

        __result = BackendStabilizerStub.SuccessBoolean();
        return false;
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.PutOnAvatarFrame))]
internal static class KinguinverseWebServicePutOnAvatarFramePatch
{
    private static bool Prefix(int characterId, int avatarFrameId, ref Il2CppTasks.Task<Result<bool>> __result)
    {
        if (!BackendStabilizerRuntime.UsePersistentSelections || !BackendStabilizerSelections.ApplyAvatarFrameSelection(characterId, avatarFrameId))
        {
            return true;
        }

        __result = BackendStabilizerStub.SuccessBoolean();
        return false;
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.PutOnCharacterDescription))]
internal static class KinguinverseWebServicePutOnCharacterDescriptionPatch
{
    private static bool Prefix(int characterId, DescriptionType descriptionType, ref Il2CppTasks.Task<Result<bool>> __result)
    {
        if (!BackendStabilizerRuntime.UsePersistentSelections || !BackendStabilizerSelections.ApplyTitleSelection(characterId, descriptionType))
        {
            return true;
        }

        __result = BackendStabilizerStub.SuccessBoolean();
        return false;
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.PutOnCharacterFart))]
internal static class KinguinverseWebServicePutOnCharacterFartPatch
{
    private static bool Prefix(int characterId, EmoteType emoteFart, ref Il2CppTasks.Task<Result<bool>> __result)
    {
        if (!BackendStabilizerRuntime.UsePersistentSelections || !BackendStabilizerSelections.ApplyFartSelection(characterId, emoteFart))
        {
            return true;
        }

        __result = BackendStabilizerStub.SuccessBoolean();
        return false;
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.PutOnCharacterDance))]
internal static class KinguinverseWebServicePutOnCharacterDancePatch
{
    private static bool Prefix(int characterId, EmoteType emoteFart, ref Il2CppTasks.Task<Result<bool>> __result)
    {
        if (!BackendStabilizerRuntime.UsePersistentSelections || !BackendStabilizerSelections.ApplyDanceSelection(characterId, emoteFart))
        {
            return true;
        }

        __result = BackendStabilizerStub.SuccessBoolean();
        return false;
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.PutOnSkinPart))]
internal static class KinguinverseWebServicePutOnSkinPartPatch
{
    private static bool Prefix(int characterId, int skinPartId, ref Il2CppTasks.Task<Result<bool>> __result)
    {
        var handledLocally = BackendStabilizerRuntime.UsePersistentSelections && BackendStabilizerSelections.ApplySkinPartSelection(characterId, skinPartId);
        BackendStabilizerRuntime.LogSkinWebCall("KinguinverseWebService.PutOnSkinPart:prefix", characterId, skinPartId, handledLocally);
        if (!handledLocally)
        {
            return true;
        }

        __result = BackendStabilizerStub.SuccessBoolean();
        return false;
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.PutOffSkinPart))]
internal static class KinguinverseWebServicePutOffSkinPartPatch
{
    private static bool Prefix(int characterId, SkinType skinType, ref Il2CppTasks.Task<Result<bool>> __result)
    {
        var handledLocally = BackendStabilizerRuntime.UsePersistentSelections && BackendStabilizerSelections.RemoveSkinPartSelection(characterId, skinType);
        BackendStabilizerRuntime.LogSkinRemove("KinguinverseWebService.PutOffSkinPart:prefix", characterId, skinType, handledLocally);
        if (!handledLocally)
        {
            return true;
        }

        __result = BackendStabilizerStub.SuccessBoolean();
        return false;
    }
}

[HarmonyPatch(typeof(DailyQuestsView), "Refresh")]
internal static class DailyQuestsViewRefreshPatch
{
    private static bool Prefix(DailyQuestsView __instance)
    {
        if (!BackendStabilizerRuntime.UseLocalStub && !BackendStabilizerRuntime.UseProfileOverlay)
        {
            return true;
        }

        try
        {
            if (__instance._clientCache.PlayerDailyQuests is null)
            {
                BackendStabilizerOverlay.EnsureClientCache(__instance._clientCache);
            }

            BackendStabilizerRuntime.LogSuppressedLoop("DailyQuestsView.Refresh");
            return false;
        }
        catch (Exception exception)
        {
            BackendStabilizerRuntime.LogError("Backend stabilizer DailyQuestsView.Refresh prefix failed", exception);
            return false;
        }
    }
}

[HarmonyPatch(typeof(BattlepassView), "OnOnWebplayerRefreshEvent")]
internal static class BattlepassViewOnWebplayerRefreshPatch
{
    private static bool Prefix(BattlepassView __instance)
    {
        if (!BackendStabilizerRuntime.UseLocalStub && !BackendStabilizerRuntime.UseProfileOverlay)
        {
            return true;
        }

        try
        {
            if (__instance._clientCache.SeasonPass is null || __instance._clientCache.CurrentSeasonPassProgression is null)
            {
                BackendStabilizerOverlay.EnsureClientCache(__instance._clientCache);
            }

            BackendStabilizerRuntime.LogSuppressedLoop("BattlepassView.OnOnWebplayerRefreshEvent");
            return false;
        }
        catch (Exception exception)
        {
            BackendStabilizerRuntime.LogError("Backend stabilizer BattlepassView.OnOnWebplayerRefreshEvent prefix failed", exception);
            return false;
        }
    }
}

[HarmonyPatch(typeof(BattlepassView), "SetEndTime")]
internal static class BattlepassViewSetEndTimePatch
{
    private static bool Prefix(BattlepassView __instance)
    {
        if (!BackendStabilizerRuntime.UseLocalStub && !BackendStabilizerRuntime.UseProfileOverlay)
        {
            return true;
        }

        try
        {
            if (__instance._clientCache.SeasonPass is null)
            {
                BackendStabilizerOverlay.EnsureClientCache(__instance._clientCache);
            }

            BackendStabilizerRuntime.LogSuppressedLoop("BattlepassView.SetEndTime");
            return false;
        }
        catch (Exception exception)
        {
            BackendStabilizerRuntime.LogError("Backend stabilizer BattlepassView.SetEndTime prefix failed", exception);
            return false;
        }
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.RefreshPlayer))]
internal static class KinguinverseWebServiceRefreshPlayerPatch
{
    private static bool Prefix(ref Il2CppTasks.Task<Result<RefreshLobbyPlayerResponse>> __result)
    {
        if (!BackendStabilizerRuntime.UseLocalStub)
        {
            return true;
        }

        try
        {
            __result = BackendStabilizerStub.RefreshPlayer();
            return false;
        }
        catch (Exception exception)
        {
            BackendStabilizerRuntime.LogError("Backend local stub RefreshPlayer failed", exception);
            return true;
        }
    }

    private static void Postfix(Il2CppTasks.Task<Result<RefreshLobbyPlayerResponse>> __result)
    {
        if (BackendStabilizerRuntime.UseLocalStub || __result is null)
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
                        BackendStabilizerRuntime.LogError("Backend research RefreshPlayer completion overlay failed", exception);
                    }
                });
        }
        catch (Exception exception)
        {
            BackendStabilizerRuntime.LogError("Backend research RefreshPlayer postfix failed", exception);
        }
    }

    private static void ApplyRefreshPlayerOverlay(Il2CppTasks.Task<Result<RefreshLobbyPlayerResponse>> task)
    {
        var result = task.Result;
        if (result is null || !result.IsSuccessful || result.Value is null)
        {
            return;
        }

        if (BackendStabilizerRuntime.UseProfileOverlay)
        {
            BackendStabilizerStub.ApplyRefreshPlayerOverlay(result.Value);
        }

        BackendStabilizerRuntime.LogRefreshPlayerResponse("KinguinverseWebService.RefreshPlayer", result.Value);
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.GetProducts))]
internal static class KinguinverseWebServiceGetProductsPatch
{
    private static bool Prefix(ref Il2CppTasks.Task<Result<Il2CppCollections.List<Kinguinverse.WebServiceProvider.Types.Products.ProductDto>>> __result)
    {
        if (!BackendStabilizerRuntime.UseLocalStub)
        {
            return true;
        }

        try
        {
            __result = BackendStabilizerStub.GetProducts();
            return false;
        }
        catch (Exception exception)
        {
            BackendStabilizerRuntime.LogError("Backend local stub GetProducts failed", exception);
            return true;
        }
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.GetProductsV2))]
internal static class KinguinverseWebServiceGetProductsV2Patch
{
    private static bool Prefix(ref Il2CppTasks.Task<Result<Products>> __result)
    {
        if (!BackendStabilizerRuntime.UseLocalStub)
        {
            return true;
        }

        try
        {
            __result = BackendStabilizerStub.GetProductsV2();
            return false;
        }
        catch (Exception exception)
        {
            BackendStabilizerRuntime.LogError("Backend local stub GetProductsV2 failed", exception);
            return true;
        }
    }

    private static void Postfix(Il2CppTasks.Task<Result<Products>> __result)
    {
        if (BackendStabilizerRuntime.UseLocalStub || __result is null || !__result.IsCompletedSuccessfully)
        {
            return;
        }

        try
        {
            var result = __result.Result;
            if (result is not null && result.IsSuccessful && result.Value is not null)
            {
                BackendStabilizerRuntime.LogProductsResponse("KinguinverseWebService.GetProductsV2", result.Value);
            }
        }
        catch (Exception exception)
        {
            BackendStabilizerRuntime.LogError("Backend research GetProductsV2 postfix failed", exception);
        }
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.GetGameUserMetadata))]
internal static class KinguinverseWebServiceGetGameUserMetadataPatch
{
    private static bool Prefix(int userId, string key, ref Il2CppTasks.Task<Result<GetUserMetadataResponse>> __result)
    {
        if (!BackendStabilizerRuntime.UseLocalStub)
        {
            return true;
        }

        try
        {
            __result = BackendStabilizerStub.GetGameUserMetadata(userId, key);
            return false;
        }
        catch (Exception exception)
        {
            BackendStabilizerRuntime.LogError("Backend local stub GetGameUserMetadata failed", exception);
            return true;
        }
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.GetGameUserMetadatas))]
internal static class KinguinverseWebServiceGetGameUserMetadatasPatch
{
    private static bool Prefix(int userId, ref Il2CppTasks.Task<Result<Il2CppCollections.Dictionary<string, string>>> __result)
    {
        if (!BackendStabilizerRuntime.UseLocalStub)
        {
            return true;
        }

        try
        {
            __result = BackendStabilizerStub.GetGameUserMetadatas(userId);
            return false;
        }
        catch (Exception exception)
        {
            BackendStabilizerRuntime.LogError("Backend local stub GetGameUserMetadatas failed", exception);
            return true;
        }
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.SetGameUserMetadata))]
internal static class KinguinverseWebServiceSetGameUserMetadataPatch
{
    private static bool Prefix(int userId, string key, string value, ref Il2CppTasks.Task<Result> __result)
    {
        if (!BackendStabilizerRuntime.UseLocalStub)
        {
            return true;
        }

        try
        {
            __result = BackendStabilizerStub.SetGameUserMetadata(userId, key, value);
            return false;
        }
        catch (Exception exception)
        {
            BackendStabilizerRuntime.LogError("Backend local stub SetGameUserMetadata failed", exception);
            return true;
        }
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.SetGameUserMetadatas))]
internal static class KinguinverseWebServiceSetGameUserMetadatasPatch
{
    private static bool Prefix(int userId, SetUserMetadatasRequest request, ref Il2CppTasks.Task<Result> __result)
    {
        if (!BackendStabilizerRuntime.UseLocalStub)
        {
            return true;
        }

        try
        {
            __result = BackendStabilizerStub.SetGameUserMetadatas(userId, request);
            return false;
        }
        catch (Exception exception)
        {
            BackendStabilizerRuntime.LogError("Backend local stub SetGameUserMetadatas failed", exception);
            return true;
        }
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.GetPlayerMessages))]
internal static class KinguinverseWebServiceGetPlayerMessagesPatch
{
    private static bool Prefix(ref Il2CppTasks.Task<Result<Il2CppCollections.List<PlayerSystemMessage>>> __result)
    {
        if (!BackendStabilizerRuntime.UseLocalStub)
        {
            return true;
        }

        try
        {
            __result = BackendStabilizerStub.GetPlayerMessages();
            return false;
        }
        catch (Exception exception)
        {
            BackendStabilizerRuntime.LogError("Backend local stub GetPlayerMessages failed", exception);
            return true;
        }
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.GetPlayerResources))]
internal static class KinguinverseWebServiceGetPlayerResourcesPatch
{
    private static bool Prefix(ref Il2CppTasks.Task<Result<PlayerResources>> __result)
    {
        if (!BackendStabilizerRuntime.UseLocalStub)
        {
            return true;
        }

        try
        {
            __result = BackendStabilizerStub.GetPlayerResources();
            return false;
        }
        catch (Exception exception)
        {
            BackendStabilizerRuntime.LogError("Backend local stub GetPlayerResources failed", exception);
            return true;
        }
    }

    private static void Postfix(Il2CppTasks.Task<Result<PlayerResources>> __result)
    {
        if (BackendStabilizerRuntime.UseLocalStub || __result is null || !__result.IsCompletedSuccessfully)
        {
            return;
        }

        try
        {
            var result = __result.Result;
            if (result is not null && result.IsSuccessful && result.Value is not null)
            {
                BackendStabilizerRuntime.LogResourcesResponse("KinguinverseWebService.GetPlayerResources", result.Value);
            }
        }
        catch (Exception exception)
        {
            BackendStabilizerRuntime.LogError("Backend research GetPlayerResources postfix failed", exception);
        }
    }
}

[HarmonyPatch(typeof(KinguinverseWebService), nameof(KinguinverseWebService.GetMyBoosters))]
internal static class KinguinverseWebServiceGetMyBoostersPatch
{
    private static bool Prefix(ref Il2CppTasks.Task<Result<PlayerBoosters>> __result)
    {
        if (!BackendStabilizerRuntime.UseLocalStub)
        {
            return true;
        }

        try
        {
            __result = BackendStabilizerStub.GetMyBoosters();
            return false;
        }
        catch (Exception exception)
        {
            BackendStabilizerRuntime.LogError("Backend local stub GetMyBoosters failed", exception);
            return true;
        }
    }
}
