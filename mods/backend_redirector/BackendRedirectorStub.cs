using Base;
using Kinguinverse.WebServiceProvider;
using Kinguinverse.WebServiceProvider.Requests;
using Kinguinverse.WebServiceProvider.Responses;
using Kinguinverse.WebServiceProvider.Responses.V2;
using Kinguinverse.WebServiceProvider.Types.Games;
using Kinguinverse.WebServiceProvider.Types.Users;
using Kinguinverse.WebServiceProvider.Types_v2;
using Kinguinverse.WebServiceProvider.Types_v2.Products;
using Il2CppGuid = Il2CppSystem.Guid;
using Il2CppTimeSpan = Il2CppSystem.TimeSpan;
using Il2CppCollections = Il2CppSystem.Collections.Generic;
using Il2CppTasks = Il2CppSystem.Threading.Tasks;
using Il2CppNet = Il2CppSystem.Net;
using System.Linq;

namespace SneakOut.BackendRedirector;

internal static class BackendRedirectorStub
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

    public static Il2CppTasks.Task<Result<LogInResponseV2>> SteamLogInV2(SteamLogInRequest request)
    {
        lock (Sync)
        {
            _authorizationToken = System.Guid.NewGuid().ToString("N");
            _sessionToken = System.Guid.NewGuid().ToString();
            _steamId = request.SteamId;
            _steamNickname = string.IsNullOrWhiteSpace(request.SteamNickname) ? "Community Player" : request.SteamNickname;
            _playfabId = request.PlayfabId ?? string.Empty;
            _pgosId = request.PgosId ?? string.Empty;
            _timeZoneOffset = request.TimeZoneOffsetToUtc;

            return Success(CreateLoginResponse());
        }
    }

    public static Il2CppTasks.Task<Result<GetUserSessionResponse>> GetUserSession(Il2CppGuid sessionToken)
    {
        lock (Sync)
        {
            return Success(CreateUserSessionResponse());
        }
    }

    public static Il2CppTasks.Task<Result<GetUserSessionV2Response>> GetUserSessionV2(Il2CppGuid sessionToken)
    {
        lock (Sync)
        {
            return Success(CreateUserSessionV2Response());
        }
    }

    public static Il2CppTasks.Task<Result<RefreshLobbyPlayerResponse>> RefreshPlayer()
    {
        lock (Sync)
        {
            return Success(CreateRefreshPlayerResponse());
        }
    }

    public static Il2CppTasks.Task<Result<LogInResponseV2>> GetPlayer(string sessionToken)
    {
        lock (Sync)
        {
            return Success(CreateLoginResponse());
        }
    }

    public static Il2CppTasks.Task<Result<WebPlayersSimplified>> GetPlayer(int userId)
    {
        lock (Sync)
        {
            return Success(CreatePlayerSimplified());
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
            clientCache.PlayerDailyQuests = CreateDailyQuests();
            clientCache.CurrentSeasonPassProgression = CreateSeasonPassProgression(seasonPass.Name);
            clientCache.SeasonPass = seasonPass;
            clientCache.Messages = new Il2CppCollections.List<PlayerSystemMessage>();
            clientCache.Banned = false;
            clientCache.PossibleFirewallBlocked = false;
        }
    }

    private static LogInResponseV2 CreateLoginResponse()
    {
        return new LogInResponseV2(_authorizationToken, _sessionToken, _kinguinEmail, _userId, CreatePlayer());
    }

    private static GetUserSessionResponse CreateUserSessionResponse()
    {
        return new GetUserSessionResponse(
            true,
            _userId,
            CreateMetadataDtos(),
            CommunityServerName,
            false,
            _kinguinEmail);
    }

    private static GetUserSessionV2Response CreateUserSessionV2Response()
    {
        return new GetUserSessionV2Response(
            true,
            _userId,
            CommunityServerName,
            false,
            _kinguinEmail,
            new UserAllData(CreateBaseData()));
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
        player.Emotions = new PlayerEmotions();
        player.BoostTickets = new PlayerBoostTickets();
        player.Characters = CreateCharacters();
        player.Ownership = CreateOwnership();
        player.Descriptions = new Il2CppCollections.List<DescriptionType>();
        return player;
    }

    private static WebPlayersSimplified CreatePlayerSimplified()
    {
        var player = new WebPlayersSimplified();
        player.BaseData = CreateBaseData();
        player.Characters = CreateCharacters();
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

    private static void CaptureIdentity(ClientCache clientCache)
    {
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
            character.Emotions = new CharacterEmotions();
            character.Description = DescriptionType.none;
            character.SkillCards = CreateCharacterSkillCards(characterType);
            character.Fart = EmoteType.None;
            character.Dance = EmoteType.None;
            characters.Add(character);
        }

        return characters;
    }

    private static PlayerCards CreatePlayerCards()
    {
        var skillCards = new Il2CppCollections.List<SkillCard>();
        var cardId = 1;

        foreach (var skillType in System.Enum.GetValues<SkillType>())
        {
            if (skillType == SkillType.None)
            {
                continue;
            }

            skillCards.Add(new SkillCard(cardId++, skillType, MaxSkillExperience, MaxSkillTier));
        }

        return new PlayerCards(skillCards);
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
        var avatarId = 1;

        foreach (var avatarType in System.Enum.GetValues<AvatarType>())
        {
            if (avatarType == AvatarType.None)
            {
                continue;
            }

            avatars.Add(CreateAvatar(avatarType, avatarId++));
        }

        return new PlayerAvatars(avatars);
    }

    private static PlayerAvatarFrames CreatePlayerAvatarFrames()
    {
        var frames = new Il2CppCollections.List<AvatarFrame>();
        var frameId = 1;

        foreach (var frameType in System.Enum.GetValues<AvatarFrameType>())
        {
            if (frameType == AvatarFrameType.None)
            {
                continue;
            }

            frames.Add(CreateAvatarFrame(frameType, frameId++));
        }

        return new PlayerAvatarFrames(frames);
    }

    private static PlayerCharacterSkins CreatePlayerCharacterSkins()
    {
        var skins = new Il2CppCollections.List<PlayerCharacterSkin>();
        var skinId = 1;

        foreach (var characterSkin in System.Enum.GetValues<CharacterSkin>())
        {
            if (characterSkin == CharacterSkin.None)
            {
                continue;
            }

            skins.Add(new PlayerCharacterSkin(skinId++, characterSkin));
        }

        return new PlayerCharacterSkins(skins);
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
        progression.ClaimedRewards = "all";
        return progression;
    }

    private static Il2CppTasks.Task<Result<T>> Success<T>(T value)
    {
        return Il2CppTasks.Task.FromResult(new Result<T>(value, true, Il2CppNet.HttpStatusCode.OK, string.Empty));
    }

    private static Il2CppTasks.Task<Result> Success()
    {
        return Il2CppTasks.Task.FromResult(new Result(true, Il2CppNet.HttpStatusCode.OK, string.Empty));
    }
}
