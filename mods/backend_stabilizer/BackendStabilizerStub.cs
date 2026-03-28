using Base;
using Kinguinverse.WebServiceProvider;
using Kinguinverse.WebServiceProvider.Requests;
using Kinguinverse.WebServiceProvider.Responses;
using Kinguinverse.WebServiceProvider.Responses.V2;
using Kinguinverse.WebServiceProvider.Types.Games;
using Kinguinverse.WebServiceProvider.Types.Users;
using Kinguinverse.WebServiceProvider.Types_v2;
using Kinguinverse.WebServiceProvider.Types_v2.Products;
using Il2CppTimeSpan = Il2CppSystem.TimeSpan;
using Il2CppCollections = Il2CppSystem.Collections.Generic;
using Il2CppTasks = Il2CppSystem.Threading.Tasks;
using Il2CppNet = Il2CppSystem.Net;
using System.Linq;
using ClientCharacterType = Types.CharacterType;

namespace SneakOut.BackendStabilizer;

internal static class BackendStabilizerStub
{
    private const int MaxCurrencyAmount = 999_999;
    private const int MaxExperienceAmount = 9_999_999;
    private const int MaxSkillTier = 5;
    private const int MaxSkillExperience = 9_999;
    private const string CommunityEmail = "community@local";
    private const string CommunityServerName = "community-local";

    private static readonly object Sync = new();
    private static readonly Il2CppCollections.Dictionary<string, string> Metadata = new();

    private static int _userId = 424242;
    private static string _authorizationToken = System.Guid.NewGuid().ToString("N");
    private static string _sessionToken = System.Guid.NewGuid().ToString();
    private static string _kinguinEmail = CommunityEmail;
    private static ulong _steamId = 0;
    private static string _steamNickname = "Community Player";
    private static string _playfabId = string.Empty;
    private static string _pgosId = string.Empty;
    private static Il2CppTimeSpan _timeZoneOffset = Il2CppTimeSpan.Zero;

    public static void Initialize()
    {
        lock (Sync)
        {
            Metadata.Clear();
            Metadata["games_played_as_seeker"] = "0";
            Metadata["total_games_finished"] = "0";
            Metadata["can_be_seeker"] = "true";
            Metadata["default_seeker"] = CharacterType.Reaper.ToString();
            Metadata["default_character"] = CharacterType.Penguin.ToString();
        }
    }

    public static Il2CppTasks.Task<Result<RefreshLobbyPlayerResponse>> RefreshPlayer()
    {
        lock (Sync)
        {
            return Success(CreateRefreshPlayerResponse());
        }
    }

    public static Il2CppTasks.Task<Result<Il2CppCollections.List<Kinguinverse.WebServiceProvider.Types.Products.ProductDto>>> GetProducts()
    {
        return Success(new Il2CppCollections.List<Kinguinverse.WebServiceProvider.Types.Products.ProductDto>());
    }

    public static Il2CppTasks.Task<Result<Products>> GetProductsV2()
    {
        lock (Sync)
        {
            return Success(CreateProducts());
        }
    }

    public static Il2CppTasks.Task<Result<GetUserMetadataResponse>> GetGameUserMetadata(int userId, string key)
    {
        lock (Sync)
        {
            var value = Metadata.TryGetValue(key, out var metadataValue) ? metadataValue : string.Empty;
            return Success(new GetUserMetadataResponse(value));
        }
    }

    public static Il2CppTasks.Task<Result<Il2CppCollections.Dictionary<string, string>>> GetGameUserMetadatas(int userId)
    {
        lock (Sync)
        {
            var values = new Il2CppCollections.Dictionary<string, string>();
            foreach (var pair in Metadata)
            {
                values[pair.Key] = pair.Value;
            }

            return Success(values);
        }
    }

    public static Il2CppTasks.Task<Result> SetGameUserMetadata(int userId, string key, string value)
    {
        lock (Sync)
        {
            Metadata[key] = value;
            return Success();
        }
    }

    public static Il2CppTasks.Task<Result> SetGameUserMetadatas(int userId, SetUserMetadatasRequest request)
    {
        lock (Sync)
        {
            if (request?.GameUserMetadatas is not null)
            {
                foreach (var pair in request.GameUserMetadatas)
                {
                    Metadata[pair.Key] = pair.Value;
                }
            }

            return Success();
        }
    }

    public static Il2CppTasks.Task<Result<Il2CppCollections.List<PlayerSystemMessage>>> GetPlayerMessages()
    {
        return Success(new Il2CppCollections.List<PlayerSystemMessage>());
    }

    public static Il2CppTasks.Task<Result<PlayerResources>> GetPlayerResources()
    {
        lock (Sync)
        {
            return Success(CreateResources());
        }
    }

    public static Il2CppTasks.Task<Result<PlayerBoosters>> GetMyBoosters()
    {
        return Success(new PlayerBoosters());
    }

    public static void PopulateClientCache(ClientCache clientCache)
    {
        lock (Sync)
        {
            CaptureIdentity(clientCache);
            var seasonPass = CreateSeasonPass();
            if (string.IsNullOrWhiteSpace(clientCache.AuthorizationToken))
            {
                clientCache.AuthorizationToken = _authorizationToken;
            }

            if (string.IsNullOrWhiteSpace(clientCache.UserSessionToken))
            {
                clientCache.UserSessionToken = _sessionToken;
            }

            if (string.IsNullOrWhiteSpace(clientCache.KinguinEmail))
            {
                clientCache.KinguinEmail = _kinguinEmail;
            }

            if (clientCache.UserInfo is null)
            {
                clientCache.UserInfo = CreateUserInfo();
            }

            clientCache.UserWebPlayer = CreatePlayer();
            if (BackendStabilizerRuntime.UsePersistentSelections)
            {
                LocalSelectionsStore.ApplySelections(clientCache.UserWebPlayer);
            }
            clientCache.PlayerDailyQuests = CreateDailyQuests();
            clientCache.CurrentSeasonPassProgression = CreateSeasonPassProgression(seasonPass.Name);
            clientCache.SeasonPass = seasonPass;
            clientCache.Messages = new Il2CppCollections.List<PlayerSystemMessage>();
            clientCache.Banned = false;
            clientCache.PossibleFirewallBlocked = false;
        }
    }

    public static void ApplyProfileOverlay(ClientCache clientCache)
    {
        lock (Sync)
        {
            CaptureIdentity(clientCache);

            if (clientCache.UserInfo is null)
            {
                clientCache.UserInfo = CreateUserInfo();
            }

            if (clientCache.UserWebPlayer is null)
            {
                clientCache.UserWebPlayer = CreatePlayer();
            }
            else
            {
                var player = clientCache.UserWebPlayer;
                player.BaseData ??= CreateBaseData();
                player.Cards = EnsurePlayerCards(player.Cards);
                player.Avatars = EnsurePlayerAvatars(player.Avatars);
                player.AvatarFrames = EnsurePlayerAvatarFrames(player.AvatarFrames);
                player.CharacterSkins = EnsurePlayerCharacterSkins(player.CharacterSkins);
                player.Ownership = CreateOwnership();
                player.Boosters ??= new PlayerBoosters();
                player.Emotions = EnsurePlayerEmotions(player.Emotions);
                player.BoostTickets ??= new PlayerBoostTickets();
                player.ResourceSources ??= new Il2CppCollections.List<ResourceSource>();
                player.Consumables ??= new PlayerConsumables();
                player.Skins = EnsurePlayerSkins(player.Skins);
                player.Descriptions = EnsureDescriptions(player.Descriptions);
                player.Characters = EnsureCharacters(player.Characters);
            }

            if (BackendStabilizerRuntime.UsePersistentSelections && clientCache.UserWebPlayer is not null)
            {
                LocalSelectionsStore.ApplySelections(clientCache.UserWebPlayer);
            }

            if (clientCache.PlayerDailyQuests is null)
            {
                clientCache.PlayerDailyQuests = CreateDailyQuests();
            }

            if (clientCache.SeasonPass is null)
            {
                clientCache.SeasonPass = CreateSeasonPass();
            }

            if (clientCache.CurrentSeasonPassProgression is null && clientCache.SeasonPass is not null)
            {
                clientCache.CurrentSeasonPassProgression = CreateSeasonPassProgression(clientCache.SeasonPass.Name);
            }

            clientCache.Messages ??= new Il2CppCollections.List<PlayerSystemMessage>();
            clientCache.Banned = false;
            clientCache.PossibleFirewallBlocked = false;
        }
    }

    public static Il2CppCollections.List<SkillCard> CreateCardsForCharacter(CharacterType characterType)
    {
        var cards = new Il2CppCollections.List<SkillCard>();
        foreach (var skillType in System.Enum.GetValues<SkillType>())
        {
            if (skillType == SkillType.None || !MatchesCharacterSkill(characterType, skillType))
            {
                continue;
            }

            cards.Add(new SkillCard(20_000 + cards.Count + (int)characterType * 100, skillType, MaxSkillExperience, MaxSkillTier));
        }

        return cards;
    }

    public static SkillCard CreateMaxSkillCard(SkillType skillType)
    {
        return new SkillCard(GetSkillCardId(skillType), skillType, MaxSkillExperience, MaxSkillTier);
    }

    public static SkillCard EnsureMaxSkillCard(SkillCard? skillCard, SkillType skillType)
    {
        if (skillCard is null)
        {
            return CreateMaxSkillCard(skillType);
        }

        skillCard.Id = GetSkillCardId(skillType);
        skillCard.SkillType = skillType;
        skillCard.Experience = MaxSkillExperience;
        skillCard.Tier = MaxSkillTier;
        return skillCard;
    }

    public static void ApplyRefreshPlayerOverlay(RefreshLobbyPlayerResponse response)
    {
        lock (Sync)
        {
            CaptureIdentity(null);
            var seasonPass = CreateSeasonPass();
            response.Consumables ??= new PlayerConsumables();
            response.DailyQuests = CreateDailyQuests();
            response.CurrentSeasonPassProgression = CreateSeasonPassProgression(seasonPass.Name);
        }
    }

    private static RefreshLobbyPlayerResponse CreateRefreshPlayerResponse()
    {
        var response = new RefreshLobbyPlayerResponse();
        response.Exp = MaxExperienceAmount;
        response.Resources = CreateResources();
        response.Consumables = new PlayerConsumables();
        response.DailyQuests = CreateDailyQuests();
        response.CurrentSeasonPassProgression = CreateSeasonPassProgression(CreateSeasonPass().Name);
        return response;
    }

    private static UserInfoDto CreateUserInfo()
    {
        return new UserInfoDto(
            _userId,
            UserRoleDto.Player,
            1,
            _kinguinEmail,
            false,
            CommunityServerName,
            false,
            "community-local",
            _kinguinEmail);
    }

    private static PlayerDailyQuests CreateDailyQuests()
    {
        return new PlayerDailyQuests(
            DailyQuestType.StunHunter_3X,
            true,
            DailyQuestType.FinishBigTask,
            true,
            DailyQuestType.WinOneGameAsAHunter,
            true);
    }

    private static WebPlayer CreatePlayer()
    {
        var player = new WebPlayer();
        player.BaseData = CreateBaseData();
        player.ActiveBoosts = new PlayerActiveBoosts();
        player.ResourceSources = new Il2CppCollections.List<ResourceSource>();
        player.Cards = CreatePlayerCards();
        player.Resources = CreateResources();
        player.Consumables = new PlayerConsumables();
        player.Skins = new PlayerSkins();
        player.Avatars = CreatePlayerAvatars();
        player.AvatarFrames = CreatePlayerAvatarFrames();
        player.CharacterSkins = CreatePlayerCharacterSkins();
        player.Boosters = new PlayerBoosters();
        player.Emotions = CreatePlayerEmotions();
        player.BoostTickets = new PlayerBoostTickets();
        player.Characters = CreateCharacters();
        player.Ownership = CreateOwnership();
        player.Descriptions = new Il2CppCollections.List<DescriptionType>();
        return player;
    }

    private static WebPlayerBaseData CreateBaseData()
    {
        var baseData = new WebPlayerBaseData();
        baseData.PlayerId = _userId;
        baseData.Experience = MaxExperienceAmount;
        baseData.Nickname = _steamNickname;
        baseData.SteamId = new Il2CppSystem.Nullable<ulong>(_steamId);
        baseData.PlayfabId = _playfabId;
        baseData.KinguinId = _userId.ToString();
        baseData.PgosId = _pgosId;
        baseData.TimeZoneOffset = _timeZoneOffset;
        baseData.Banned = false;
        return baseData;
    }

    private static void CaptureIdentity(ClientCache? clientCache)
    {
        if (clientCache is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(clientCache.AuthorizationToken))
        {
            _authorizationToken = clientCache.AuthorizationToken;
        }

        if (!string.IsNullOrWhiteSpace(clientCache.UserSessionToken))
        {
            _sessionToken = clientCache.UserSessionToken;
        }

        if (!string.IsNullOrWhiteSpace(clientCache.KinguinEmail))
        {
            _kinguinEmail = clientCache.KinguinEmail;
        }

        if (clientCache.UserInfo is not null)
        {
            _userId = clientCache.UserInfo.UserId;
            if (!string.IsNullOrWhiteSpace(clientCache.UserInfo.KinguinEmail))
            {
                _kinguinEmail = clientCache.UserInfo.KinguinEmail;
            }
        }

        if (clientCache.UserWebPlayer is null)
        {
            return;
        }

        var baseData = clientCache.UserWebPlayer.BaseData;
        _userId = baseData.PlayerId;
        _steamNickname = baseData.Nickname;
        _steamId = baseData.SteamId.GetValueOrDefault();
        _playfabId = baseData.PlayfabId ?? string.Empty;
        _pgosId = baseData.PgosId ?? string.Empty;
        _timeZoneOffset = baseData.TimeZoneOffset;
    }

    public static string GetProfileStorageKey()
    {
        lock (Sync)
        {
            var currentClientCache = BackendStabilizerRuntime.CurrentClientCache;
            var currentBaseData = currentClientCache?.UserWebPlayer?.BaseData;
            if (currentBaseData is not null)
            {
                if (currentBaseData.PlayerId != 0)
                {
                    return $"user:{currentBaseData.PlayerId}";
                }
            }

            return $"user:{_userId}";
        }
    }

    public static string GetLegacyProfileStorageKey()
    {
        lock (Sync)
        {
            var currentUserId = BackendStabilizerRuntime.CurrentClientCache?.UserWebPlayer?.BaseData?.PlayerId;
            return $"user:{(currentUserId.GetValueOrDefault() != 0 ? currentUserId.GetValueOrDefault() : _userId)}";
        }
    }

    public static bool TryGetCharacterTypeById(int characterId, out CharacterType characterType)
    {
        lock (Sync)
        {
            var clientCache = BackendStabilizerRuntime.CurrentClientCache;
            var characters = clientCache?.UserWebPlayer?.Characters;
            if (characters is not null)
            {
                foreach (var character in characters)
                {
                    if (character is null || character.CharacterId != characterId)
                    {
                        continue;
                    }

                    characterType = character.Type;
                    return true;
                }
            }

            characterType = CharacterType.None;
            return false;
        }
    }

    private static Il2CppCollections.List<GameUserMetadataDto> CreateMetadataDtos()
    {
        var values = new Il2CppCollections.List<GameUserMetadataDto>();
        foreach (var pair in Metadata)
        {
            values.Add(new GameUserMetadataDto(pair.Key, pair.Value));
        }

        return values;
    }

    private static PlayerResources CreateResources()
    {
        var values = new Il2CppCollections.List<Resource>();
        values.Add(new Resource(ResourceType.Gold, MaxCurrencyAmount));
        values.Add(new Resource(ResourceType.Crown, MaxCurrencyAmount));
        values.Add(new Resource(ResourceType.Shard, MaxCurrencyAmount));
        values.Add(new Resource(ResourceType.Experience, MaxExperienceAmount));
        values.Add(new Resource(ResourceType.Level, 100));
        values.Add(new Resource(ResourceType.BattlePassExperience, MaxExperienceAmount));
        return new PlayerResources(values);
    }

    public static int GetAvatarId(AvatarType avatarType)
    {
        return 100_000 + (int)avatarType;
    }

    public static int GetAvatarFrameId(AvatarFrameType avatarFrameType)
    {
        return 110_000 + (int)avatarFrameType;
    }

    public static int GetEmoteId(EmoteType emoteType)
    {
        return 120_000 + (int)emoteType;
    }

    public static int GetSkinPartId(SkinPartType skinPartType)
    {
        return 130_000 + (int)skinPartType;
    }

    public static int GetCharacterSkinId(CharacterSkin characterSkin)
    {
        return 140_000 + (int)characterSkin;
    }

    public static int GetSkillCardId(SkillType skillType)
    {
        return 30_000 + (int)skillType;
    }

    public static SkinType GetSkinTypeForSkinPart(SkinPartType skinPartType)
    {
        var skinPartName = skinPartType.ToString();
        if (skinPartName.Contains("Head", StringComparison.Ordinal))
        {
            return SkinType.Head;
        }

        if (skinPartName.Contains("Torso", StringComparison.Ordinal) || skinPartName.Contains("Chest", StringComparison.Ordinal))
        {
            return SkinType.Chest;
        }

        if (skinPartName.Contains("Legs", StringComparison.Ordinal))
        {
            return SkinType.Legs;
        }

        if (skinPartName.Contains("Arms", StringComparison.Ordinal) || skinPartName.Contains("Hands", StringComparison.Ordinal))
        {
            return SkinType.Hands;
        }

        if (skinPartName.Contains("Back", StringComparison.Ordinal))
        {
            return SkinType.Back;
        }

        return SkinType.Whole;
    }

    private static PlayerOwnership CreateOwnership()
    {
        var ownership = new PlayerOwnership();
        ownership.AccountBind = true;
        ownership.OwnDLC_MG13 = true;
        ownership.OwnDLC_FounderPack = true;
        ownership.OwnDLC_StarterPack_Iron = true;
        ownership.OwnDLC_StarterPack_Bronze = true;
        ownership.OwnDLC_StarterPack_Silver = true;
        ownership.OwnDLC_StarterPack_Gold = true;
        ownership.OwnDLC_StarterPack_Diamond = true;
        ownership.OwnDLC_May4 = true;
        ownership.FirstPlayTestOwnership = true;
        ownership.SecondPlayTestOwnership = true;
        ownership.WeAreSorryReward = true;
        ownership.OwnFlamingo = true;
        ownership.OwnBandura = true;
        ownership.OwnDold = true;
        ownership.OwnTurkiye = true;
        return ownership;
    }

    private static Il2CppCollections.List<Character> CreateCharacters()
    {
        var characters = new Il2CppCollections.List<Character>();
        var characterId = 1;

        foreach (var characterType in System.Enum.GetValues<CharacterType>())
        {
            if (characterType is CharacterType.None or CharacterType.Seeker)
            {
                continue;
            }

            var character = new Character();
            character.CharacterId = characterId++;
            character.Type = characterType;
            character.CharacterSkin = CharacterSkin.None;
            character.Avatar = CreateAvatar(GetAvatarType(characterType), character.CharacterId);
            character.AvatarFrame = CreateAvatarFrame(AvatarFrameType.Diamond, character.CharacterId);
            character.SkinParts = new SkinParts();
            character.Emotions = CreateCharacterEmotions(characterType);
            character.Description = DescriptionType.none;
            character.SkillCards = CreateCharacterSkillCards(characterType);
            character.Fart = GetDefaultFart(characterType);
            character.Dance = GetDefaultDance(characterType);
            characters.Add(character);
        }

        return characters;
    }

    private static void EnsureCharacterDefaults(Character character)
    {
        character.Avatar ??= CreateAvatar(GetAvatarType(character.Type), character.CharacterId);
        character.AvatarFrame ??= CreateAvatarFrame(AvatarFrameType.Diamond, character.CharacterId);
        character.SkinParts ??= new SkinParts();
        character.Emotions ??= CreateCharacterEmotions(character.Type);
        character.SkillCards = EnsureCharacterSkillCards(character.Type, character.SkillCards);

        if (character.Fart == EmoteType.None)
        {
            character.Fart = GetDefaultFart(character.Type);
        }

        if (character.Dance == EmoteType.None)
        {
            character.Dance = GetDefaultDance(character.Type);
        }
    }

    private static PlayerEmotions CreatePlayerEmotions()
    {
        var emotions = new Il2CppCollections.List<Emote>();
        foreach (var emoteType in System.Enum.GetValues<EmoteType>())
        {
            if (emoteType == EmoteType.None)
            {
                continue;
            }

            emotions.Add(new Emote(GetEmoteId(emoteType), emoteType));
        }

        return new PlayerEmotions(emotions);
    }

    private static CharacterEmotions CreateCharacterEmotions(CharacterType characterType)
    {
        var defaultEmotes = GetDefaultCharacterEmotions(characterType);
        return new CharacterEmotions(
            CreateEmote(1, defaultEmotes[0]),
            CreateEmote(2, defaultEmotes[1]),
            CreateEmote(3, defaultEmotes[2]),
            CreateEmote(4, defaultEmotes[3]),
            CreateEmote(5, defaultEmotes[4]),
            CreateEmote(6, defaultEmotes[5]));
    }

    private static Emote CreateEmote(int emoteId, EmoteType emoteType)
    {
        return new Emote(emoteId, emoteType);
    }

    private static EmoteType GetDefaultDance(CharacterType characterType)
    {
        return Types.CharacterTypeExtension.GetDefaultDanceForCharacter(ToClientCharacterType(characterType));
    }

    private static EmoteType GetDefaultFart(CharacterType characterType)
    {
        return characterType == CharacterType.Penguin
            ? EmoteType.emotion_penguin_fart_1
            : EmoteType.None;
    }

    private static EmoteType[] GetDefaultCharacterEmotions(CharacterType characterType)
    {
        var defaultEmotes = Types.CharacterTypeExtension.GetDefaultEmotesForCharacter(ToClientCharacterType(characterType));
        if (defaultEmotes is not null && defaultEmotes.Length >= 6)
        {
            return defaultEmotes.Take(6).ToArray();
        }

        return new[]
        {
            EmoteType.emotion_penguin_wave,
            EmoteType.emotion_penguin_follow_me,
            EmoteType.emotion_penguin_like,
            EmoteType.emotion_penguin_jump,
            EmoteType.emotion_penguin_fart_1,
            EmoteType.emotion_penguin_dance_1
        };
    }

    private static ClientCharacterType ToClientCharacterType(CharacterType characterType)
    {
        return characterType switch
        {
            CharacterType.Penguin => ClientCharacterType.victim_penguin,
            CharacterType.Ghost => ClientCharacterType.ghost,
            CharacterType.Reaper => ClientCharacterType.murderer_ripper,
            CharacterType.Scarecrow => ClientCharacterType.murderer_scarecrow,
            CharacterType.Dracula => ClientCharacterType.murderer_dracula,
            CharacterType.Butcher => ClientCharacterType.murderer_butcher,
            CharacterType.Clown => ClientCharacterType.murderer_clown,
            CharacterType.Mimic => ClientCharacterType.seeker_with_generic_skills,
            _ => ClientCharacterType.victim_penguin
        };
    }

    private static PlayerCards CreatePlayerCards()
    {
        var skillCards = new Il2CppCollections.List<SkillCard>();

        foreach (var skillType in System.Enum.GetValues<SkillType>())
        {
            if (skillType == SkillType.None)
            {
                continue;
            }

            skillCards.Add(CreateMaxSkillCard(skillType));
        }

        return new PlayerCards(skillCards);
    }

    private static PlayerCards EnsurePlayerCards(PlayerCards? playerCards)
    {
        if (playerCards is null || playerCards.SkillCards is null)
        {
            return CreatePlayerCards();
        }

        var normalizedCards = new Il2CppCollections.List<SkillCard>();
        var existingCardsByType = new Dictionary<SkillType, SkillCard>();

        foreach (var existingCard in playerCards.SkillCards)
        {
            if (existingCard is null || existingCard.SkillType == SkillType.None)
            {
                continue;
            }

            existingCardsByType[existingCard.SkillType] = EnsureMaxSkillCard(existingCard, existingCard.SkillType);
        }

        foreach (var skillType in System.Enum.GetValues<SkillType>())
        {
            if (skillType == SkillType.None)
            {
                continue;
            }

            normalizedCards.Add(existingCardsByType.TryGetValue(skillType, out var existingCard)
                ? existingCard
                : CreateMaxSkillCard(skillType));
        }

        playerCards.SkillCards = normalizedCards;
        return playerCards;
    }

    private static CharacterSkillCards CreateCharacterSkillCards(CharacterType characterType)
    {
        var matchingSkillTypes = System.Enum.GetValues<SkillType>()
            .Where(skillType => skillType != SkillType.None)
            .Where(skillType => MatchesCharacterSkill(characterType, skillType))
            .Take(5)
            .ToArray();

        var cards = matchingSkillTypes
            .Select((skillType, index) => new SkillCard(10_000 + (int)characterType * 10 + index, skillType, MaxSkillExperience, MaxSkillTier))
            .ToArray();

        var characterSkillCards = new CharacterSkillCards();
        if (cards.Length > 0)
        {
            characterSkillCards.ActiveSkillCard = cards[0];
        }

        if (cards.Length > 1)
        {
            characterSkillCards.PassiveSkillCard1 = cards[1];
        }

        if (cards.Length > 2)
        {
            characterSkillCards.PassiveSkillCard2 = cards[2];
        }

        if (cards.Length > 3)
        {
            characterSkillCards.PassiveSkillCard3 = cards[3];
        }

        if (cards.Length > 4)
        {
            characterSkillCards.PassiveSkillCard4 = cards[4];
        }

        return characterSkillCards;
    }

    private static CharacterSkillCards EnsureCharacterSkillCards(CharacterType characterType, CharacterSkillCards? skillCards)
    {
        var normalizedSkillCards = skillCards ?? new CharacterSkillCards();

        if (normalizedSkillCards.ActiveSkillCard is not null)
        {
            normalizedSkillCards.ActiveSkillCard = EnsureMaxSkillCard(normalizedSkillCards.ActiveSkillCard, normalizedSkillCards.ActiveSkillCard.SkillType);
        }

        if (normalizedSkillCards.PassiveSkillCard1 is not null)
        {
            normalizedSkillCards.PassiveSkillCard1 = EnsureMaxSkillCard(normalizedSkillCards.PassiveSkillCard1, normalizedSkillCards.PassiveSkillCard1.SkillType);
        }

        if (normalizedSkillCards.PassiveSkillCard2 is not null)
        {
            normalizedSkillCards.PassiveSkillCard2 = EnsureMaxSkillCard(normalizedSkillCards.PassiveSkillCard2, normalizedSkillCards.PassiveSkillCard2.SkillType);
        }

        if (normalizedSkillCards.PassiveSkillCard3 is not null)
        {
            normalizedSkillCards.PassiveSkillCard3 = EnsureMaxSkillCard(normalizedSkillCards.PassiveSkillCard3, normalizedSkillCards.PassiveSkillCard3.SkillType);
        }

        if (normalizedSkillCards.PassiveSkillCard4 is not null)
        {
            normalizedSkillCards.PassiveSkillCard4 = EnsureMaxSkillCard(normalizedSkillCards.PassiveSkillCard4, normalizedSkillCards.PassiveSkillCard4.SkillType);
        }

        return normalizedSkillCards;
    }

    private static bool MatchesCharacterSkill(CharacterType characterType, SkillType skillType)
    {
        var skillName = skillType.ToString();
        return characterType switch
        {
            CharacterType.Penguin => skillName.StartsWith("Penguin", StringComparison.Ordinal),
            CharacterType.Reaper => skillName.StartsWith("Reaper", StringComparison.Ordinal),
            CharacterType.Scarecrow => skillName.StartsWith("Scarecrow", StringComparison.Ordinal),
            CharacterType.Dracula => skillName.StartsWith("Dracula", StringComparison.Ordinal),
            CharacterType.Butcher => skillName.StartsWith("Butcher", StringComparison.Ordinal),
            CharacterType.Clown => skillName.StartsWith("Clown", StringComparison.Ordinal),
            _ => false,
        };
    }

    private static PlayerAvatars CreatePlayerAvatars()
    {
        var avatars = new Il2CppCollections.List<Avatar>();

        foreach (var avatarType in System.Enum.GetValues<AvatarType>())
        {
            if (avatarType == AvatarType.None)
            {
                continue;
            }

            avatars.Add(CreateAvatar(avatarType, GetAvatarId(avatarType)));
        }

        return new PlayerAvatars(avatars);
    }

    private static PlayerAvatarFrames CreatePlayerAvatarFrames()
    {
        var frames = new Il2CppCollections.List<AvatarFrame>();

        foreach (var frameType in System.Enum.GetValues<AvatarFrameType>())
        {
            if (frameType == AvatarFrameType.None)
            {
                continue;
            }

            frames.Add(CreateAvatarFrame(frameType, GetAvatarFrameId(frameType)));
        }

        return new PlayerAvatarFrames(frames);
    }

    private static PlayerCharacterSkins CreatePlayerCharacterSkins()
    {
        var skins = new Il2CppCollections.List<PlayerCharacterSkin>();

        foreach (var characterSkin in System.Enum.GetValues<CharacterSkin>())
        {
            if (characterSkin == CharacterSkin.None)
            {
                continue;
            }

            skins.Add(new PlayerCharacterSkin(GetCharacterSkinId(characterSkin), characterSkin));
        }

        return new PlayerCharacterSkins(skins);
    }

    private static PlayerAvatars EnsurePlayerAvatars(PlayerAvatars? avatars)
    {
        var normalizedAvatars = avatars ?? new PlayerAvatars(new Il2CppCollections.List<Avatar>());
        normalizedAvatars.Avatars ??= new Il2CppCollections.List<Avatar>();
        var existingAvatars = new Dictionary<AvatarType, Avatar>();
        foreach (var avatar in normalizedAvatars.Avatars)
        {
            if (avatar is null || avatar.AvatarType == AvatarType.None)
            {
                continue;
            }

            existingAvatars[avatar.AvatarType] = avatar;
        }
        var mergedAvatars = new Il2CppCollections.List<Avatar>();

        foreach (var avatarType in System.Enum.GetValues<AvatarType>())
        {
            if (avatarType == AvatarType.None)
            {
                continue;
            }

            if (existingAvatars.TryGetValue(avatarType, out var avatar))
            {
                avatar.Id = GetAvatarId(avatarType);
                avatar.AvatarType = avatarType;
                mergedAvatars.Add(avatar);
                continue;
            }

            mergedAvatars.Add(CreateAvatar(avatarType, GetAvatarId(avatarType)));
        }

        normalizedAvatars.Avatars = mergedAvatars;
        return normalizedAvatars;
    }

    private static PlayerAvatarFrames EnsurePlayerAvatarFrames(PlayerAvatarFrames? avatarFrames)
    {
        var normalizedFrames = avatarFrames ?? new PlayerAvatarFrames(new Il2CppCollections.List<AvatarFrame>());
        normalizedFrames.AvatarFrames ??= new Il2CppCollections.List<AvatarFrame>();
        var existingFrames = new Dictionary<AvatarFrameType, AvatarFrame>();
        foreach (var frame in normalizedFrames.AvatarFrames)
        {
            if (frame is null || frame.AvatarFrameType == AvatarFrameType.None)
            {
                continue;
            }

            existingFrames[frame.AvatarFrameType] = frame;
        }
        var mergedFrames = new Il2CppCollections.List<AvatarFrame>();

        foreach (var avatarFrameType in System.Enum.GetValues<AvatarFrameType>())
        {
            if (avatarFrameType == AvatarFrameType.None)
            {
                continue;
            }

            if (existingFrames.TryGetValue(avatarFrameType, out var frame))
            {
                frame.Id = GetAvatarFrameId(avatarFrameType);
                frame.AvatarFrameType = avatarFrameType;
                mergedFrames.Add(frame);
                continue;
            }

            mergedFrames.Add(CreateAvatarFrame(avatarFrameType, GetAvatarFrameId(avatarFrameType)));
        }

        normalizedFrames.AvatarFrames = mergedFrames;
        return normalizedFrames;
    }

    private static PlayerCharacterSkins EnsurePlayerCharacterSkins(PlayerCharacterSkins? characterSkins)
    {
        var normalizedCharacterSkins = characterSkins ?? new PlayerCharacterSkins(new Il2CppCollections.List<PlayerCharacterSkin>());
        normalizedCharacterSkins.Skins ??= new Il2CppCollections.List<PlayerCharacterSkin>();
        var existingCharacterSkins = new Dictionary<CharacterSkin, PlayerCharacterSkin>();
        foreach (var skin in normalizedCharacterSkins.Skins)
        {
            if (skin is null || skin.Skin == CharacterSkin.None)
            {
                continue;
            }

            existingCharacterSkins[skin.Skin] = skin;
        }
        var mergedCharacterSkins = new Il2CppCollections.List<PlayerCharacterSkin>();

        foreach (var characterSkin in System.Enum.GetValues<CharacterSkin>())
        {
            if (characterSkin == CharacterSkin.None)
            {
                continue;
            }

            if (existingCharacterSkins.TryGetValue(characterSkin, out var skin))
            {
                skin.Id = GetCharacterSkinId(characterSkin);
                skin.Skin = characterSkin;
                mergedCharacterSkins.Add(skin);
                continue;
            }

            mergedCharacterSkins.Add(new PlayerCharacterSkin(GetCharacterSkinId(characterSkin), characterSkin));
        }

        normalizedCharacterSkins.Skins = mergedCharacterSkins;
        return normalizedCharacterSkins;
    }

    private static PlayerEmotions EnsurePlayerEmotions(PlayerEmotions? playerEmotions)
    {
        var normalizedEmotions = playerEmotions ?? new PlayerEmotions(new Il2CppCollections.List<Emote>());
        normalizedEmotions.AllEmotions ??= new Il2CppCollections.List<Emote>();
        var existingEmotions = new Dictionary<EmoteType, Emote>();
        foreach (var emote in normalizedEmotions.AllEmotions)
        {
            if (emote is null || emote.EmoteType == EmoteType.None)
            {
                continue;
            }

            existingEmotions[emote.EmoteType] = emote;
        }
        var mergedEmotions = new Il2CppCollections.List<Emote>();

        foreach (var emoteType in System.Enum.GetValues<EmoteType>())
        {
            if (emoteType == EmoteType.None)
            {
                continue;
            }

            if (existingEmotions.TryGetValue(emoteType, out var emote))
            {
                emote.EmoteId = GetEmoteId(emoteType);
                emote.EmoteType = emoteType;
                mergedEmotions.Add(emote);
                continue;
            }

            mergedEmotions.Add(new Emote(GetEmoteId(emoteType), emoteType));
        }

        normalizedEmotions.AllEmotions = mergedEmotions;
        return normalizedEmotions;
    }

    private static PlayerSkins EnsurePlayerSkins(PlayerSkins? playerSkins)
    {
        var normalizedSkins = playerSkins ?? new PlayerSkins(new Il2CppCollections.List<SkinPart>());
        normalizedSkins.SkinParts ??= new Il2CppCollections.List<SkinPart>();
        var existingSkinParts = new Dictionary<SkinPartType, SkinPart>();
        foreach (var skinPart in normalizedSkins.SkinParts)
        {
            if (skinPart is null || skinPart.SkinPartType == SkinPartType.None)
            {
                continue;
            }

            existingSkinParts[skinPart.SkinPartType] = skinPart;
        }
        var mergedSkinParts = new Il2CppCollections.List<SkinPart>();

        foreach (var skinPartType in System.Enum.GetValues<SkinPartType>())
        {
            if (skinPartType == SkinPartType.None)
            {
                continue;
            }

            if (existingSkinParts.TryGetValue(skinPartType, out var skinPart))
            {
                skinPart.Id = GetSkinPartId(skinPartType);
                skinPart.SkinPartType = skinPartType;
                skinPart.SkinType = GetSkinTypeForSkinPart(skinPartType);
                mergedSkinParts.Add(skinPart);
                continue;
            }

            mergedSkinParts.Add(new SkinPart(GetSkinPartId(skinPartType), GetSkinTypeForSkinPart(skinPartType), skinPartType));
        }

        normalizedSkins.SkinParts = mergedSkinParts;
        return normalizedSkins;
    }

    private static Il2CppCollections.List<DescriptionType> EnsureDescriptions(Il2CppCollections.List<DescriptionType>? descriptions)
    {
        var mergedDescriptions = descriptions ?? new Il2CppCollections.List<DescriptionType>();
        foreach (var descriptionType in System.Enum.GetValues<DescriptionType>())
        {
            if (descriptionType == DescriptionType.none || mergedDescriptions.Contains(descriptionType))
            {
                continue;
            }

            mergedDescriptions.Add(descriptionType);
        }

        return mergedDescriptions;
    }

    private static Il2CppCollections.List<Character> EnsureCharacters(Il2CppCollections.List<Character>? characters)
    {
        if (characters is null || characters.Count == 0)
        {
            return CreateCharacters();
        }

        var existingCharacters = new Dictionary<CharacterType, Character>();
        var mergedCharacters = new Il2CppCollections.List<Character>();
        var nextCharacterId = 1;

        foreach (var character in characters)
        {
            if (character is null || character.Type == CharacterType.None)
            {
                continue;
            }

            existingCharacters[character.Type] = character;
            if (character.CharacterId >= nextCharacterId)
            {
                nextCharacterId = character.CharacterId + 1;
            }
        }

        foreach (var characterType in System.Enum.GetValues<CharacterType>())
        {
            if (characterType is CharacterType.None or CharacterType.Seeker)
            {
                continue;
            }

            if (existingCharacters.TryGetValue(characterType, out var character))
            {
                EnsureCharacterDefaults(character);
                mergedCharacters.Add(character);
                continue;
            }

            var createdCharacter = new Character();
            createdCharacter.CharacterId = nextCharacterId++;
            createdCharacter.Type = characterType;
            createdCharacter.CharacterSkin = CharacterSkin.None;
            createdCharacter.Description = DescriptionType.none;
            createdCharacter.SkinParts = new SkinParts();
            EnsureCharacterDefaults(createdCharacter);
            mergedCharacters.Add(createdCharacter);
        }

        return mergedCharacters;
    }

    private static Avatar CreateAvatar(AvatarType avatarType, int id)
    {
        var avatar = new Avatar();
        avatar.Id = id;
        avatar.AvatarType = avatarType;
        return avatar;
    }

    private static AvatarFrame CreateAvatarFrame(AvatarFrameType frameType, int id)
    {
        var frame = new AvatarFrame();
        frame.Id = id;
        frame.AvatarFrameType = frameType;
        return frame;
    }

    private static AvatarType GetAvatarType(CharacterType characterType)
    {
        return characterType switch
        {
            CharacterType.Penguin => AvatarType.Penguin,
            CharacterType.Ghost => AvatarType.Ghost,
            CharacterType.Reaper => AvatarType.Reaper,
            CharacterType.Scarecrow => AvatarType.Scarecrow,
            CharacterType.Dracula => AvatarType.Dracula,
            CharacterType.Butcher => AvatarType.Butcher,
            CharacterType.Clown => AvatarType.Clown,
            _ => AvatarType.None,
        };
    }

    private static Products CreateProducts()
    {
        var products = new Products();
        products.CardBoosters = new Il2CppCollections.List<CardBoosterProduct>();
        products.ConsumableProducts = new Il2CppCollections.List<ConsumableProduct>();
        products.SkinPartProducts = new Il2CppCollections.List<SkinPartProduct>();
        products.TicketProducts = new Il2CppCollections.List<BoostTicketProduct>();
        products.CharacterProducts = CreateCharacterProducts();
        products.Emotions = new Il2CppCollections.List<Emote>();
        products.Avatars = CreatePlayerAvatars().Avatars;
        products.AvatarFrames = CreatePlayerAvatarFrames().AvatarFrames;
        products.InAppPacks = new Il2CppCollections.List<InAppPack>();
        products.UnPurchaseAbleSkinParts = new Il2CppCollections.List<SkinPart>();
        return products;
    }

    private static Il2CppCollections.List<CharacterProduct> CreateCharacterProducts()
    {
        var products = new Il2CppCollections.List<CharacterProduct>();
        var productId = 100;
        var freePrice = new ProductPrice(
            new Resource(ResourceType.Gold, 0),
            new Resource(ResourceType.None, 0),
            new Resource(ResourceType.None, 0));

        foreach (var characterType in System.Enum.GetValues<CharacterType>())
        {
            if (characterType is CharacterType.None or CharacterType.Penguin or CharacterType.Ghost or CharacterType.Seeker)
            {
                continue;
            }

            products.Add(new CharacterProduct(productId++, characterType, freePrice));
        }

        return products;
    }

    private static SeasonPass CreateSeasonPass()
    {
        return new AnimalSeason();
    }

    private static PlayerSeasonPassProgression CreateSeasonPassProgression(string seasonPassName)
    {
        var progression = new PlayerSeasonPassProgression();
        progression.SeasonPassUniqueName = seasonPassName;
        progression.Premium = true;
        progression.Experience = MaxExperienceAmount;
        progression.MaxAvailableReward = 100;
        progression.LastClaimedReward = 100;
        progression.ClaimedRewards = string.Join(",", Enumerable.Range(1, 100));
        return progression;
    }

    private static Il2CppTasks.Task<Result<T>> Success<T>(T value) where T : notnull
    {
        return Il2CppTasks.Task.FromResult(new Result<T>(value, true, Il2CppNet.HttpStatusCode.OK, string.Empty));
    }

    public static Il2CppTasks.Task<Result<bool>> SuccessBoolean()
    {
        return Success(true);
    }

    private static Il2CppTasks.Task<Result> Success()
    {
        return Il2CppTasks.Task.FromResult(new Result(true, Il2CppNet.HttpStatusCode.OK, string.Empty));
    }
}
