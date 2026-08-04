using Collections;
using Events;
using Kinguinverse.DataUtils.Events;
using Kinguinverse.WebServiceProvider.Responses;
using Kinguinverse.WebServiceProvider.Types.Games;
using Kinguinverse.WebServiceProvider.Types_v2;
using UI;
using UI.Views;
using UnityEngine.EventSystems;
using ClientCharacterType = Types.CharacterType;
using Il2CppCollections = Il2CppSystem.Collections.Generic;
using Il2CppTasks = Il2CppSystem.Threading.Tasks;

namespace SneakOut.UnlockEverything;

internal static partial class UnlockEverythingSelections
{
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

        string productName;
        try
        {
            productName = productType.ToString();
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Failed to read avatar product enum", exception);
            return false;
        }

        UnlockEverythingRuntime.LogSkillUiEvent("UnlockEverythingSelections.TryParseAvatarProduct", $"name={productName}");
        if (System.Enum.TryParse(productName, out avatarType) && avatarType != AvatarType.None)
        {
            return true;
        }

        avatarType = AvatarType.None;
        if (System.Enum.TryParse(productName, out avatarFrameType) && avatarFrameType != AvatarFrameType.None)
        {
            return true;
        }

        avatarFrameType = AvatarFrameType.None;
        return System.Enum.TryParse(productName, out descriptionType)
            && descriptionType != DescriptionType.none;
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

        string productName;
        try
        {
            productName = selectedProduct.ToString();
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Failed to read selected avatar product", exception);
            return false;
        }

        switch (category)
        {
            case 0 when System.Enum.TryParse(productName, out avatarType):
                return avatarType != AvatarType.None;
            case 1 when System.Enum.TryParse(productName, out avatarFrameType):
                return avatarFrameType != AvatarFrameType.None;
            case 2 when System.Enum.TryParse(productName, out descriptionType):
                return descriptionType != DescriptionType.none;
        }

        return false;
    }

    private static Il2CppSystem.Enum? TryGetSelectedAvatarProductFromButtons(AvatarAndFrameView view)
    {
        var currentEventSystem = EventSystem.current;
        var selectedObject = currentEventSystem is null ? null : currentEventSystem.currentSelectedGameObject;
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

                if (button.gameObject == selectedObject || button._button?.gameObject == selectedObject)
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

                if (button.gameObject == selectedObject || button._button?.gameObject == selectedObject)
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

        ClearPendingAvatarSelection();
        UnlockEverythingRuntime.LogSkillUiEvent("UnlockEverythingSelections.TryConsumePendingAvatarSelection", $"avatar={avatarType}, frame={avatarFrameType}, title={descriptionType}");
        return true;
    }

    private static void ClearPendingAvatarSelection()
    {
        _pendingAvatarType = AvatarType.None;
        _pendingAvatarFrameType = AvatarFrameType.None;
        _pendingDescriptionType = DescriptionType.none;
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

    private static bool ApplyAvatarTypeSelection(int characterId, AvatarType avatarType)
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
            if (avatar is null || avatar.AvatarType != avatarType)
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

    private static bool ApplyAvatarFrameTypeSelection(int characterId, AvatarFrameType avatarFrameType)
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
            if (avatarFrame is null || avatarFrame.AvatarFrameType != avatarFrameType)
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

    internal static bool ApplyAvatarModificationSelection(AvatarAndFrameView view)
    {
        if (!UnlockEverythingRuntime.UsePersistentSelections || !TryGetAvatarMenuCharacterId(out var characterId))
        {
            return false;
        }

        var parsed = TryConsumePendingAvatarSelection(out var avatarType, out var avatarFrameType, out var descriptionType);
        UnlockEverythingRuntime.LogSkillUiEvent(
            "UnlockEverythingSelections.ApplyAvatarModificationSelection:viewDirect",
            $"parsed={parsed}, avatar={avatarType}, frame={avatarFrameType}, title={descriptionType}");
        if (!parsed)
        {
            parsed = TryParseAvatarProductFromView(view, out avatarType, out avatarFrameType, out descriptionType);
        }

        return parsed && ApplyParsedAvatarModification(characterId, avatarType, avatarFrameType, descriptionType);
    }

    private static bool ApplyParsedAvatarModification(
        int characterId,
        AvatarType avatarType,
        AvatarFrameType avatarFrameType,
        DescriptionType descriptionType)
    {
        if (avatarType != AvatarType.None)
        {
            return ApplyAvatarTypeSelection(characterId, avatarType);
        }

        if (avatarFrameType != AvatarFrameType.None)
        {
            return ApplyAvatarFrameTypeSelection(characterId, avatarFrameType);
        }

        return descriptionType != DescriptionType.none
            && ApplyTitleSelection(characterId, descriptionType);
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

    public static bool ApplyEmotionSelection(int characterId, EmoteType emoteType, int wheelSlotId)
    {
        if (!UnlockEverythingRuntime.UsePersistentSelections || wheelSlotId is < 1 or > 6)
        {
            return false;
        }

        var player = GetPlayer();
        var character = GetCharacterById(characterId);
        if (player is null || character is null || emoteType == EmoteType.None)
        {
            return false;
        }

        player.Emotions ??= new PlayerEmotions(new Il2CppCollections.List<Emote>());
        player.Emotions.AllEmotions ??= new Il2CppCollections.List<Emote>();
        Emote? selectedEmote = null;
        foreach (var emote in player.Emotions.AllEmotions)
        {
            if (emote is null || emote.EmoteType != emoteType)
            {
                continue;
            }

            selectedEmote = emote;
            break;
        }

        if (selectedEmote is null)
        {
            selectedEmote = new Emote(UnlockEverythingStub.GetEmoteId(emoteType), emoteType);
            player.Emotions.AllEmotions.Add(selectedEmote);
        }

        character.Emotions ??= new CharacterEmotions();
        switch (wheelSlotId)
        {
            case 1:
                character.Emotions.Emotion1 = selectedEmote;
                break;
            case 2:
                character.Emotions.Emotion2 = selectedEmote;
                break;
            case 3:
                character.Emotions.Emotion3 = selectedEmote;
                break;
            case 4:
                character.Emotions.Emotion4 = selectedEmote;
                break;
            case 5:
                character.Emotions.Emotion5 = selectedEmote;
                break;
            case 6:
                character.Emotions.Emotion6 = selectedEmote;
                break;
        }

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
        var syntheticSkinId = UnlockEverythingStub.GetCharacterSkinId(characterSkin);
        var existingSkin = false;
        foreach (var skin in player.CharacterSkins.Skins)
        {
            if (skin is null || skin.Skin != characterSkin)
            {
                continue;
            }

            skin.Id = AvatarSelectionPolicy.PreserveOwnedProductId(skin.Id, syntheticSkinId);
            existingSkin = true;
            break;
        }

        if (!existingSkin)
        {
            player.CharacterSkins.Skins.Add(new PlayerCharacterSkin(syntheticSkinId, characterSkin));
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

            NormalizeEquippedSkinParts(character, skinPart.SkinType);
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
            selectedSkinPart.Id = AvatarSelectionPolicy.PreserveOwnedProductId(
                selectedSkinPart.Id,
                UnlockEverythingStub.GetSkinPartId(skinPartType));
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

        NormalizeEquippedSkinParts(character, skinType);
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
            var localNetworkPlayer = GetCurrentNetworkPlayer();
            if (localNetworkPlayer is null || localNetworkPlayer.InternalId <= 0)
            {
                UnlockEverythingRuntime.LogSkinPreview("UnlockEverythingSelections.SyncPreviewCharacterData:noInternalId", 0, skinType, skinPartType, false);
                return;
            }

            var internalId = localNetworkPlayer.InternalId;

            var previewViews = UnityEngine.Resources.FindObjectsOfTypeAll<PlayerCustomizationView>();
            UnlockEverythingRuntime.LogSkinPreview("UnlockEverythingSelections.SyncPreviewCharacterData:views", previewViews.Length, skinType, skinPartType, true);
            foreach (var previewView in previewViews)
            {
                if (previewView is null)
                {
                    continue;
                }

                var spookedPlayerCharacterData = previewView._spookedPlayerCharacterData;
                if (spookedPlayerCharacterData is null)
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
                previewView._currentCharacterData = currentCharacterData;
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
        var localNetworkPlayer = GetCurrentNetworkPlayer();
        if (localNetworkPlayer is null)
        {
            UnlockEverythingRuntime.LogSkinPreview("UnlockEverythingSelections.PublishSkinRefresh:noInternalId", 0, skinType, skinPartType, false);
            return;
        }

        var internalId = localNetworkPlayer.InternalId;
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
                character.SkinParts.Head = EmptySkinPart(SkinType.Head);
                break;
            case SkinType.Chest:
                character.SkinParts.Chest = EmptySkinPart(SkinType.Chest);
                break;
            case SkinType.Legs:
                character.SkinParts.Legs = EmptySkinPart(SkinType.Legs);
                break;
            case SkinType.Hands:
                character.SkinParts.Hands = EmptySkinPart(SkinType.Hands);
                break;
            case SkinType.Back:
                character.SkinParts.Back = EmptySkinPart(SkinType.Back);
                break;
            case SkinType.Whole:
                character.SkinParts.Whole = EmptySkinPart(SkinType.Whole);
                break;
            default:
                return false;
        }

        SaveSelection(character);
        return true;
    }
    public static bool ApplyAvatarModificationSelection(Il2CppSystem.Enum productType, ClientCharacterType clientCharacterType)
    {
        if (!UnlockEverythingRuntime.UsePersistentSelections || !TryGetCharacterId(clientCharacterType, out var characterId))
        {
            return false;
        }

        // The boxed IL2CPP enum supplied to this async inventory method can already
        // be invalid by the time Harmony enters managed code. The record-button
        // patches capture and parse it while its native lifetime is still valid.
        // Never call a virtual method (ToString/GetHashCode/etc.) on productType here.
        var parsed = TryConsumePendingAvatarSelection(out var avatarType, out var avatarFrameType, out var descriptionType);
        UnlockEverythingRuntime.LogSkillUiEvent("UnlockEverythingSelections.ApplyAvatarModificationSelection:pending", $"parsed={parsed}, avatar={avatarType}, frame={avatarFrameType}, title={descriptionType}");

        if (!parsed)
        {
            return false;
        }

        ClearPendingAvatarSelection();

        return ApplyParsedAvatarModification(characterId, avatarType, avatarFrameType, descriptionType);
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

            UnlockEverythingRuntime.ContinueOnMainThread(
                task,
                () => SaveCurrentCharacterSelection(characterType),
                "Unlock Everything selection persistence failed");
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Unlock Everything selection persistence failed", exception);
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

            UnlockEverythingRuntime.ContinueOnMainThread(
                task,
                () => LocalSelectionsStore.SaveSkinPartSelection(characterType, skinType, skinPartType),
                "Unlock Everything skin persistence failed");
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Unlock Everything skin persistence failed", exception);
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

            UnlockEverythingRuntime.ContinueOnMainThread(
                task,
                result =>
                {
                    if (result)
                    {
                        SaveCurrentCharacterSelection(characterType);
                    }
                },
                "Unlock Everything selection persistence failed");
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Unlock Everything selection persistence failed", exception);
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
