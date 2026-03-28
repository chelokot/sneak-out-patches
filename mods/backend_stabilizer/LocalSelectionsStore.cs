using BepInEx;
using Kinguinverse.WebServiceProvider.Types_v2;
using Il2CppCollections = Il2CppSystem.Collections.Generic;
using System.Text.Json;

namespace SneakOut.BackendStabilizer;

internal static class LocalSelectionsStore
{
    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly string StoragePath = Path.Combine(Paths.ConfigPath, "chelokot.sneakout.persistent-selections.json");

    private static PersistedSelectionsRoot? _root;

    public static void Initialize()
    {
        lock (Sync)
        {
            _root ??= Load();
        }
    }

    public static void SaveCharacterSelection(Character character)
    {
        lock (Sync)
        {
            BackendStabilizerRuntime.LogSkinSelectionSnapshot("LocalSelectionsStore.SaveCharacterSelection", character);
            var profileKey = BackendStabilizerStub.GetProfileStorageKey();
            BackendStabilizerRuntime.LogPersistentSelectionStore("LocalSelectionsStore.SaveCharacterSelection:key", profileKey, character.Type);
            var profileSelections = GetOrCreateProfileSelections(profileKey);
            profileSelections.Characters[((int)character.Type).ToString()] = PersistedCharacterSelection.FromCharacter(character);
            Save();
        }
    }

    public static void SaveCharacterSkin(CharacterType characterType, CharacterSkin characterSkin)
    {
        lock (Sync)
        {
            var profileKey = BackendStabilizerStub.GetProfileStorageKey();
            BackendStabilizerRuntime.LogPersistentSelectionStore("LocalSelectionsStore.SaveCharacterSkin:key", profileKey, characterType);
            var profileSelections = GetOrCreateProfileSelections(profileKey);
            if (!profileSelections.Characters.TryGetValue(((int)characterType).ToString(), out var selection))
            {
                selection = new PersistedCharacterSelection();
                profileSelections.Characters[((int)characterType).ToString()] = selection;
            }

            selection.CharacterSkin = (int)characterSkin;
            Save();
        }
    }

    public static void SaveSkinPartSelection(CharacterType characterType, SkinType skinType, SkinPartType skinPartType)
    {
        lock (Sync)
        {
            var profileKey = BackendStabilizerStub.GetProfileStorageKey();
            BackendStabilizerRuntime.LogPersistentSelectionStore("LocalSelectionsStore.SaveSkinPartSelection:key", profileKey, characterType, skinType, skinPartType);
            var profileSelections = GetOrCreateProfileSelections(profileKey);
            if (!profileSelections.Characters.TryGetValue(((int)characterType).ToString(), out var selection))
            {
                selection = new PersistedCharacterSelection();
                profileSelections.Characters[((int)characterType).ToString()] = selection;
            }

            switch (skinType)
            {
                case SkinType.Head:
                    selection.HeadSkinPartType = (int)skinPartType;
                    break;
                case SkinType.Chest:
                    selection.ChestSkinPartType = (int)skinPartType;
                    break;
                case SkinType.Legs:
                    selection.LegsSkinPartType = (int)skinPartType;
                    break;
                case SkinType.Hands:
                    selection.HandsSkinPartType = (int)skinPartType;
                    break;
                case SkinType.Back:
                    selection.BackSkinPartType = (int)skinPartType;
                    break;
                case SkinType.Whole:
                    selection.WholeSkinPartType = (int)skinPartType;
                    break;
                default:
                    return;
            }

            Save();
        }
    }

    public static void ApplySelections(WebPlayer player)
    {
        lock (Sync)
        {
            var profileSelections = GetExistingProfileSelections();
            if (profileSelections is null)
            {
                return;
            }

            foreach (var character in player.Characters ?? new Il2CppCollections.List<Character>())
            {
                if (character is null)
                {
                    continue;
                }

                if (!profileSelections.Characters.TryGetValue(((int)character.Type).ToString(), out var selection))
                {
                    continue;
                }

                BackendStabilizerRuntime.LogPersistentSelectionLoad("LocalSelectionsStore.ApplySelections", character, selection);
                ApplySelection(player, character, selection);
                BackendStabilizerRuntime.LogSkinSelectionSnapshot("LocalSelectionsStore.ApplySelections:applied", character);
            }
        }
    }

    public static bool HasPersistedSkinSelection(CharacterType characterType)
    {
        lock (Sync)
        {
            var profileSelections = GetExistingProfileSelections();
            if (profileSelections is null)
            {
                return false;
            }

            if (!profileSelections.Characters.TryGetValue(((int)characterType).ToString(), out var selection))
            {
                return false;
            }

            return selection.HeadSkinPartType.HasValue
                || selection.ChestSkinPartType.HasValue
                || selection.LegsSkinPartType.HasValue
                || selection.HandsSkinPartType.HasValue
                || selection.BackSkinPartType.HasValue
                || selection.WholeSkinPartType.HasValue;
        }
    }

    private static void ApplySelection(WebPlayer player, Character character, PersistedCharacterSelection selection)
    {
        if (selection.AvatarType.HasValue)
        {
            character.Avatar = FindOrCreateAvatar(player, (AvatarType)selection.AvatarType.Value);
        }

        if (selection.AvatarFrameType.HasValue)
        {
            character.AvatarFrame = FindOrCreateAvatarFrame(player, (AvatarFrameType)selection.AvatarFrameType.Value);
        }

        if (selection.DescriptionType.HasValue)
        {
            character.Description = (DescriptionType)selection.DescriptionType.Value;
            player.Descriptions ??= new Il2CppCollections.List<DescriptionType>();
            if (!player.Descriptions.Contains(character.Description))
            {
                player.Descriptions.Add(character.Description);
            }
        }

        if (selection.CharacterSkin.HasValue)
        {
            character.CharacterSkin = (CharacterSkin)selection.CharacterSkin.Value;
            EnsurePlayerCharacterSkin(player, character.CharacterSkin);
        }

        if (selection.Fart.HasValue)
        {
            character.Fart = (EmoteType)selection.Fart.Value;
        }

        if (selection.Dance.HasValue)
        {
            character.Dance = (EmoteType)selection.Dance.Value;
        }

        ApplyEmotes(player, character, selection);
        ApplySkinParts(player, character, selection);
        ApplySkillCards(player, character, selection);
    }

    private static void ApplyEmotes(WebPlayer player, Character character, PersistedCharacterSelection selection)
    {
        character.Emotions ??= new CharacterEmotions();
        if (selection.Emote1.HasValue)
        {
            character.Emotions.Emotion1 = FindOrCreateEmote(player, (EmoteType)selection.Emote1.Value);
        }

        if (selection.Emote2.HasValue)
        {
            character.Emotions.Emotion2 = FindOrCreateEmote(player, (EmoteType)selection.Emote2.Value);
        }

        if (selection.Emote3.HasValue)
        {
            character.Emotions.Emotion3 = FindOrCreateEmote(player, (EmoteType)selection.Emote3.Value);
        }

        if (selection.Emote4.HasValue)
        {
            character.Emotions.Emotion4 = FindOrCreateEmote(player, (EmoteType)selection.Emote4.Value);
        }

        if (selection.Emote5.HasValue)
        {
            character.Emotions.Emotion5 = FindOrCreateEmote(player, (EmoteType)selection.Emote5.Value);
        }

        if (selection.Emote6.HasValue)
        {
            character.Emotions.Emotion6 = FindOrCreateEmote(player, (EmoteType)selection.Emote6.Value);
        }
    }

    private static void ApplySkinParts(WebPlayer player, Character character, PersistedCharacterSelection selection)
    {
        character.SkinParts ??= new SkinParts();
        if (selection.HeadSkinPartType.HasValue)
        {
            character.SkinParts.Head = FindOrCreateSkinPart(player, SkinType.Head, (SkinPartType)selection.HeadSkinPartType.Value);
        }

        if (selection.ChestSkinPartType.HasValue)
        {
            character.SkinParts.Chest = FindOrCreateSkinPart(player, SkinType.Chest, (SkinPartType)selection.ChestSkinPartType.Value);
        }

        if (selection.LegsSkinPartType.HasValue)
        {
            character.SkinParts.Legs = FindOrCreateSkinPart(player, SkinType.Legs, (SkinPartType)selection.LegsSkinPartType.Value);
        }

        if (selection.HandsSkinPartType.HasValue)
        {
            character.SkinParts.Hands = FindOrCreateSkinPart(player, SkinType.Hands, (SkinPartType)selection.HandsSkinPartType.Value);
        }

        if (selection.BackSkinPartType.HasValue)
        {
            character.SkinParts.Back = FindOrCreateSkinPart(player, SkinType.Back, (SkinPartType)selection.BackSkinPartType.Value);
        }

        if (selection.WholeSkinPartType.HasValue)
        {
            character.SkinParts.Whole = FindOrCreateSkinPart(player, SkinType.Whole, (SkinPartType)selection.WholeSkinPartType.Value);
        }
    }

    private static void ApplySkillCards(WebPlayer player, Character character, PersistedCharacterSelection selection)
    {
        character.SkillCards ??= new CharacterSkillCards();
        if (selection.ActiveSkill.HasValue)
        {
            character.SkillCards.ActiveSkillCard = FindOrCreateSkillCard(player, (SkillType)selection.ActiveSkill.Value);
        }

        if (selection.PassiveSkill1.HasValue)
        {
            character.SkillCards.PassiveSkillCard1 = FindOrCreateSkillCard(player, (SkillType)selection.PassiveSkill1.Value);
        }

        if (selection.PassiveSkill2.HasValue)
        {
            character.SkillCards.PassiveSkillCard2 = FindOrCreateSkillCard(player, (SkillType)selection.PassiveSkill2.Value);
        }

        if (selection.PassiveSkill3.HasValue)
        {
            character.SkillCards.PassiveSkillCard3 = FindOrCreateSkillCard(player, (SkillType)selection.PassiveSkill3.Value);
        }

        if (selection.PassiveSkill4.HasValue)
        {
            character.SkillCards.PassiveSkillCard4 = FindOrCreateSkillCard(player, (SkillType)selection.PassiveSkill4.Value);
        }
    }

    private static Avatar FindOrCreateAvatar(WebPlayer player, AvatarType avatarType)
    {
        player.Avatars ??= new PlayerAvatars(new Il2CppCollections.List<Avatar>());
        Avatar? existingAvatar = null;
        if (player.Avatars.Avatars is not null)
        {
            foreach (var avatar in player.Avatars.Avatars)
            {
                if (avatar is null || avatar.AvatarType != avatarType)
                {
                    continue;
                }

                existingAvatar = avatar;
                break;
            }
        }

        if (existingAvatar is not null)
        {
            return existingAvatar;
        }

        var createdAvatar = new Avatar(BackendStabilizerStub.GetAvatarId(avatarType), avatarType);
        player.Avatars.Avatars ??= new Il2CppCollections.List<Avatar>();
        player.Avatars.Avatars.Add(createdAvatar);
        return createdAvatar;
    }

    private static AvatarFrame FindOrCreateAvatarFrame(WebPlayer player, AvatarFrameType avatarFrameType)
    {
        player.AvatarFrames ??= new PlayerAvatarFrames(new Il2CppCollections.List<AvatarFrame>());
        AvatarFrame? existingFrame = null;
        if (player.AvatarFrames.AvatarFrames is not null)
        {
            foreach (var frame in player.AvatarFrames.AvatarFrames)
            {
                if (frame is null || frame.AvatarFrameType != avatarFrameType)
                {
                    continue;
                }

                existingFrame = frame;
                break;
            }
        }

        if (existingFrame is not null)
        {
            return existingFrame;
        }

        var createdFrame = new AvatarFrame(BackendStabilizerStub.GetAvatarFrameId(avatarFrameType), avatarFrameType);
        player.AvatarFrames.AvatarFrames ??= new Il2CppCollections.List<AvatarFrame>();
        player.AvatarFrames.AvatarFrames.Add(createdFrame);
        return createdFrame;
    }

    private static Emote FindOrCreateEmote(WebPlayer player, EmoteType emoteType)
    {
        player.Emotions ??= new PlayerEmotions(new Il2CppCollections.List<Emote>());
        Emote? existingEmote = null;
        if (player.Emotions.AllEmotions is not null)
        {
            foreach (var emote in player.Emotions.AllEmotions)
            {
                if (emote is null || emote.EmoteType != emoteType)
                {
                    continue;
                }

                existingEmote = emote;
                break;
            }
        }

        if (existingEmote is not null)
        {
            return existingEmote;
        }

        var createdEmote = new Emote(BackendStabilizerStub.GetEmoteId(emoteType), emoteType);
        player.Emotions.AllEmotions ??= new Il2CppCollections.List<Emote>();
        player.Emotions.AllEmotions.Add(createdEmote);
        return createdEmote;
    }

    private static SkinPart FindOrCreateSkinPart(WebPlayer player, SkinType skinType, SkinPartType skinPartType)
    {
        player.Skins ??= new PlayerSkins(new Il2CppCollections.List<SkinPart>());
        SkinPart? existingSkinPart = null;
        if (player.Skins.SkinParts is not null)
        {
            foreach (var part in player.Skins.SkinParts)
            {
                if (part is null || part.SkinType != skinType || part.SkinPartType != skinPartType)
                {
                    continue;
                }

                existingSkinPart = part;
                break;
            }
        }

        if (existingSkinPart is not null)
        {
            return existingSkinPart;
        }

        var createdSkinPart = new SkinPart(BackendStabilizerStub.GetSkinPartId(skinPartType), skinType, skinPartType);
        player.Skins.SkinParts ??= new Il2CppCollections.List<SkinPart>();
        player.Skins.SkinParts.Add(createdSkinPart);
        return createdSkinPart;
    }

    private static SkillCard FindOrCreateSkillCard(WebPlayer player, SkillType skillType)
    {
        player.Cards ??= new PlayerCards(new Il2CppCollections.List<SkillCard>());
        SkillCard? existingCard = null;
        if (player.Cards.SkillCards is not null)
        {
            foreach (var card in player.Cards.SkillCards)
            {
                if (card is null || card.SkillType != skillType)
                {
                    continue;
                }

                existingCard = card;
                break;
            }
        }

        if (existingCard is not null)
        {
            return BackendStabilizerStub.EnsureMaxSkillCard(existingCard, skillType);
        }

        var createdCard = BackendStabilizerStub.CreateMaxSkillCard(skillType);
        player.Cards.SkillCards ??= new Il2CppCollections.List<SkillCard>();
        player.Cards.SkillCards.Add(createdCard);
        return createdCard;
    }

    private static void EnsurePlayerCharacterSkin(WebPlayer player, CharacterSkin characterSkin)
    {
        player.CharacterSkins ??= new PlayerCharacterSkins(new Il2CppCollections.List<PlayerCharacterSkin>());
        if (player.CharacterSkins.Skins is not null)
        {
            foreach (var skin in player.CharacterSkins.Skins)
            {
                if (skin is null || skin.Skin != characterSkin)
                {
                    continue;
                }

                return;
            }
        }

        player.CharacterSkins.Skins ??= new Il2CppCollections.List<PlayerCharacterSkin>();
        player.CharacterSkins.Skins.Add(new PlayerCharacterSkin(BackendStabilizerStub.GetCharacterSkinId(characterSkin), characterSkin));
    }

    private static PersistedProfileSelections GetOrCreateProfileSelections(string profileKey)
    {
        _root ??= Load();
        if (!_root.Profiles.TryGetValue(profileKey, out var profileSelections))
        {
            profileSelections = new PersistedProfileSelections();
            _root.Profiles[profileKey] = profileSelections;
        }

        return profileSelections;
    }

    private static PersistedProfileSelections? GetExistingProfileSelections()
    {
        _root ??= Load();
        if (_root.Profiles.Count == 0)
        {
            return null;
        }

        var primaryProfileKey = BackendStabilizerStub.GetProfileStorageKey();
        if (_root.Profiles.TryGetValue(primaryProfileKey, out var primarySelections))
        {
            return primarySelections;
        }

        var legacyProfileKey = BackendStabilizerStub.GetLegacyProfileStorageKey();
        if (!string.Equals(primaryProfileKey, legacyProfileKey, StringComparison.Ordinal)
            && _root.Profiles.TryGetValue(legacyProfileKey, out var legacySelections))
        {
            _root.Profiles[primaryProfileKey] = legacySelections;
            _root.Profiles.Remove(legacyProfileKey);
            Save();
            return legacySelections;
        }

        return null;
    }

    private static PersistedSelectionsRoot Load()
    {
        try
        {
            if (!File.Exists(StoragePath))
            {
                return new PersistedSelectionsRoot();
            }

            var content = File.ReadAllText(StoragePath);
            if (string.IsNullOrWhiteSpace(content))
            {
                return new PersistedSelectionsRoot();
            }

            return JsonSerializer.Deserialize<PersistedSelectionsRoot>(content, JsonOptions) ?? new PersistedSelectionsRoot();
        }
        catch (Exception exception)
        {
            BackendStabilizerRuntime.LogError("Backend stabilizer failed to load persistent selections", exception);
            return new PersistedSelectionsRoot();
        }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StoragePath)!);
            BackendStabilizerRuntime.LogPersistentSelectionFileWrite("LocalSelectionsStore.Save:file", StoragePath);
            File.WriteAllText(StoragePath, JsonSerializer.Serialize(_root ?? new PersistedSelectionsRoot(), JsonOptions));
        }
        catch (Exception exception)
        {
            BackendStabilizerRuntime.LogError("Backend stabilizer failed to save persistent selections", exception);
        }
    }
}

internal sealed class PersistedSelectionsRoot
{
    public Dictionary<string, PersistedProfileSelections> Profiles { get; set; } = new();
}

internal sealed class PersistedProfileSelections
{
    public Dictionary<string, PersistedCharacterSelection> Characters { get; set; } = new();
}

internal sealed class PersistedCharacterSelection
{
    public int? AvatarType { get; set; }
    public int? AvatarFrameType { get; set; }
    public int? DescriptionType { get; set; }
    public int? CharacterSkin { get; set; }
    public int? Fart { get; set; }
    public int? Dance { get; set; }
    public int? Emote1 { get; set; }
    public int? Emote2 { get; set; }
    public int? Emote3 { get; set; }
    public int? Emote4 { get; set; }
    public int? Emote5 { get; set; }
    public int? Emote6 { get; set; }
    public int? HeadSkinPartType { get; set; }
    public int? ChestSkinPartType { get; set; }
    public int? LegsSkinPartType { get; set; }
    public int? HandsSkinPartType { get; set; }
    public int? BackSkinPartType { get; set; }
    public int? WholeSkinPartType { get; set; }
    public int? ActiveSkill { get; set; }
    public int? PassiveSkill1 { get; set; }
    public int? PassiveSkill2 { get; set; }
    public int? PassiveSkill3 { get; set; }
    public int? PassiveSkill4 { get; set; }

    public static PersistedCharacterSelection FromCharacter(Character character)
    {
        return new PersistedCharacterSelection
        {
            AvatarType = character.Avatar is null ? null : (int)character.Avatar.AvatarType,
            AvatarFrameType = character.AvatarFrame is null ? null : (int)character.AvatarFrame.AvatarFrameType,
            DescriptionType = (int)character.Description,
            CharacterSkin = (int)character.CharacterSkin,
            Fart = (int)character.Fart,
            Dance = (int)character.Dance,
            Emote1 = character.Emotions?.Emotion1 is null ? null : (int)character.Emotions.Emotion1.EmoteType,
            Emote2 = character.Emotions?.Emotion2 is null ? null : (int)character.Emotions.Emotion2.EmoteType,
            Emote3 = character.Emotions?.Emotion3 is null ? null : (int)character.Emotions.Emotion3.EmoteType,
            Emote4 = character.Emotions?.Emotion4 is null ? null : (int)character.Emotions.Emotion4.EmoteType,
            Emote5 = character.Emotions?.Emotion5 is null ? null : (int)character.Emotions.Emotion5.EmoteType,
            Emote6 = character.Emotions?.Emotion6 is null ? null : (int)character.Emotions.Emotion6.EmoteType,
            HeadSkinPartType = character.SkinParts?.Head is null ? null : (int)character.SkinParts.Head.SkinPartType,
            ChestSkinPartType = character.SkinParts?.Chest is null ? null : (int)character.SkinParts.Chest.SkinPartType,
            LegsSkinPartType = character.SkinParts?.Legs is null ? null : (int)character.SkinParts.Legs.SkinPartType,
            HandsSkinPartType = character.SkinParts?.Hands is null ? null : (int)character.SkinParts.Hands.SkinPartType,
            BackSkinPartType = character.SkinParts?.Back is null ? null : (int)character.SkinParts.Back.SkinPartType,
            WholeSkinPartType = character.SkinParts?.Whole is null ? null : (int)character.SkinParts.Whole.SkinPartType,
            ActiveSkill = character.SkillCards?.ActiveSkillCard is null ? null : (int)character.SkillCards.ActiveSkillCard.SkillType,
            PassiveSkill1 = character.SkillCards?.PassiveSkillCard1 is null ? null : (int)character.SkillCards.PassiveSkillCard1.SkillType,
            PassiveSkill2 = character.SkillCards?.PassiveSkillCard2 is null ? null : (int)character.SkillCards.PassiveSkillCard2.SkillType,
            PassiveSkill3 = character.SkillCards?.PassiveSkillCard3 is null ? null : (int)character.SkillCards.PassiveSkillCard3.SkillType,
            PassiveSkill4 = character.SkillCards?.PassiveSkillCard4 is null ? null : (int)character.SkillCards.PassiveSkillCard4.SkillType
        };
    }
}
