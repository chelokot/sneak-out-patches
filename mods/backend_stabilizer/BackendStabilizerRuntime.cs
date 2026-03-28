using Base;
using BepInEx.Logging;
using HarmonyLib;
using Kinguinverse.WebServiceProvider.Responses;
using Kinguinverse.WebServiceProvider.Responses.V2;
using Kinguinverse.WebServiceProvider.Types.Games;
using Kinguinverse.WebServiceProvider.Types.Users;
using Kinguinverse.WebServiceProvider.Types_v2;
using Kinguinverse.WebServiceProvider.Types_v2.Products;
using System.Collections.Generic;
using ClientCharacterType = Types.CharacterType;

namespace SneakOut.BackendStabilizer;

internal static class BackendStabilizerRuntime
{
    private static ManualLogSource? _logger;
    private static Harmony? _harmony;
    private static BackendStabilizerConfig? _configuration;
    private static readonly HashSet<string> SuppressedLoopSources = new();
    private static ClientCache? _currentClientCache;

    public static void Initialize(ManualLogSource logger, BackendStabilizerConfig configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _harmony ??= new Harmony(BackendStabilizerPlugin.PluginGuid);
        _harmony.PatchAll();
        BackendStabilizerStub.Initialize();
        LocalSelectionsStore.Initialize();
        LogInfo($"Configured localStubEnabled={configuration.EnableLocalStub.Value}, profileOverlayEnabled={configuration.EnableProfileOverlay.Value}, persistentSelectionsEnabled={configuration.EnablePersistentSelections.Value}");
    }

    public static bool UseProfileOverlay => _configuration is not null && _configuration.EnableProfileOverlay.Value;
    public static bool UseLocalStub => _configuration is not null && _configuration.EnableLocalStub.Value;
    public static bool UsePersistentSelections => _configuration is not null && _configuration.EnablePersistentSelections.Value;
    public static ClientCache? CurrentClientCache => _currentClientCache;

    public static void TrackClientCache(ClientCache clientCache)
    {
        _currentClientCache = clientCache;
    }

    public static void LogClientCacheState(string source, ClientCache clientCache)
    {
        if (!ShouldLog())
        {
            return;
        }

        var authorizationTokenLength = GetLength(clientCache.AuthorizationToken);
        var sessionTokenLength = GetLength(clientCache.UserSessionToken);
        var hasUserInfo = clientCache.UserInfo is not null;
        var hasUserWebPlayer = clientCache.UserWebPlayer is not null;
        LogInfo(
            $"{source}: authorizationTokenLength={authorizationTokenLength}, sessionTokenLength={sessionTokenLength}, hasUserInfo={hasUserInfo}, hasUserWebPlayer={hasUserWebPlayer}, hasDailyQuests={clientCache.PlayerDailyQuests is not null}, hasSeasonPassProgression={clientCache.CurrentSeasonPassProgression is not null}, hasSeasonPass={clientCache.SeasonPass is not null}, banned={clientCache.Banned}, possibleFirewallBlocked={clientCache.PossibleFirewallBlocked}");
    }

    public static void LogRefreshPlayerResponse(string source, RefreshLobbyPlayerResponse response)
    {
        if (!ShouldLog())
        {
            return;
        }

        LogInfo(
            $"{source}: exp={response.Exp}, hasResources={response.Resources is not null}, hasConsumables={response.Consumables is not null}, hasDailyQuests={response.DailyQuests is not null}, hasSeasonPassProgression={response.CurrentSeasonPassProgression is not null}, claimedRewards='{response.CurrentSeasonPassProgression?.ClaimedRewards ?? string.Empty}', seasonPassName='{response.CurrentSeasonPassProgression?.SeasonPassUniqueName ?? string.Empty}'");
    }

    public static void LogProductsResponse(string source, Products response)
    {
        if (!ShouldLog())
        {
            return;
        }

        var characterProductsCount = response.CharacterProducts is null ? 0 : response.CharacterProducts.Count;
        var consumableProductsCount = response.ConsumableProducts is null ? 0 : response.ConsumableProducts.Count;
        var boosterProductsCount = response.CardBoosters is null ? 0 : response.CardBoosters.Count;
        LogInfo($"{source}: characterProductsCount={characterProductsCount}, consumableProductsCount={consumableProductsCount}, boosterProductsCount={boosterProductsCount}");
    }

    public static void LogResourcesResponse(string source, PlayerResources response)
    {
        if (!ShouldLog())
        {
            return;
        }

        var resources = response.Resources;
        var count = resources is null ? 0 : resources.Count;
        LogInfo($"{source}: resourceEntries={count}");
    }

    public static void LogCharacterCards(string source, CharacterType characterType, int count)
    {
        if (!ShouldLog())
        {
            return;
        }

        LogInfo($"{source}: characterType={characterType}, count={count}");
    }

    public static void LogSkinPath(string source, CharacterType characterType, SkinType skinType, SkinPartType skinPartType, bool handledLocally)
    {
        if (!ShouldLog())
        {
            return;
        }

        LogInfo($"{source}: characterType={characterType}, skinType={skinType}, skinPartType={skinPartType}, handledLocally={handledLocally}");
    }

    public static void LogSkinPath(string source, ClientCharacterType characterType, SkinType skinType, SkinPartType skinPartType, bool handledLocally)
    {
        if (!ShouldLog())
        {
            return;
        }

        LogInfo($"{source}: characterType={characterType}, skinType={skinType}, skinPartType={skinPartType}, handledLocally={handledLocally}");
    }

    public static void LogSkinWebCall(string source, int characterId, int itemId, bool handledLocally)
    {
        if (!ShouldLog())
        {
            return;
        }

        LogInfo($"{source}: characterId={characterId}, itemId={itemId}, handledLocally={handledLocally}");
    }

    public static void LogSkinRemove(string source, int characterId, SkinType skinType, bool handledLocally)
    {
        if (!ShouldLog())
        {
            return;
        }

        LogInfo($"{source}: characterId={characterId}, skinType={skinType}, handledLocally={handledLocally}");
    }

    public static void LogSkinPreview(string source, int internalId, SkinType skinType, SkinPartType skinPartType, bool taskCompleted)
    {
        if (!ShouldLog())
        {
            return;
        }

        LogInfo($"{source}: internalId={internalId}, skinType={skinType}, skinPartType={skinPartType}, taskCompleted={taskCompleted}");
    }

    public static void LogSkinRefreshEvent(string source, int internalId)
    {
        if (!ShouldLog())
        {
            return;
        }

        LogInfo($"{source}: internalId={internalId}");
    }

    public static void LogSkinSelectionResolution(string source, ClientCharacterType clientCharacterType, CharacterType characterType, int characterId, bool hasPlayer, bool hasCharacter)
    {
        if (!ShouldLog())
        {
            return;
        }

        LogInfo($"{source}: clientCharacterType={clientCharacterType}, characterType={characterType}, characterId={characterId}, hasPlayer={hasPlayer}, hasCharacter={hasCharacter}");
    }

    public static void LogSkinSelectionSnapshot(string source, Character character)
    {
        if (!ShouldLog())
        {
            return;
        }

        var skinParts = character.SkinParts;
        LogInfo(
            $"{source}: characterId={character.CharacterId}, characterType={character.Type}, characterSkin={character.CharacterSkin}, head={skinParts?.Head?.SkinPartType}, chest={skinParts?.Chest?.SkinPartType}, legs={skinParts?.Legs?.SkinPartType}, hands={skinParts?.Hands?.SkinPartType}, back={skinParts?.Back?.SkinPartType}, whole={skinParts?.Whole?.SkinPartType}");
    }

    public static void LogPersistentSelectionLoad(string source, Character character, PersistedCharacterSelection selection)
    {
        if (!ShouldLog())
        {
            return;
        }

        LogInfo(
            $"{source}: characterId={character.CharacterId}, characterType={character.Type}, characterSkin={selection.CharacterSkin}, head={selection.HeadSkinPartType}, chest={selection.ChestSkinPartType}, legs={selection.LegsSkinPartType}, hands={selection.HandsSkinPartType}, back={selection.BackSkinPartType}, whole={selection.WholeSkinPartType}");
    }

    public static void LogSkinOwnershipLookup(string source, object? itemType, object? result)
    {
        if (!ShouldLog())
        {
            return;
        }

        LogInfo($"{source}: itemType={itemType}, result={result}");
    }

    public static void LogError(string message, Exception exception)
    {
        _logger?.LogError($"{message}: {exception}");
    }

    public static void LogSuppressedLoop(string source)
    {
        if (!SuppressedLoopSources.Add(source))
        {
            return;
        }

        _logger?.LogWarning($"Suppressed unstable lobby view path: {source}");
    }

    private static int GetLength(string? value)
    {
        return value is null ? 0 : value.Length;
    }

    private static bool ShouldLog()
    {
        return _configuration is not null && _configuration.EnableResearchLogging.Value;
    }

    private static void LogInfo(string message)
    {
        _logger?.LogInfo(message);
    }
}
