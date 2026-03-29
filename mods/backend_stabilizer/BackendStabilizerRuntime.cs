using Base;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Kinguinverse.WebServiceProvider.Responses;
using Kinguinverse.WebServiceProvider.Responses.V2;
using Kinguinverse.WebServiceProvider.Types.Games;
using Kinguinverse.WebServiceProvider.Types.Users;
using Kinguinverse.WebServiceProvider.Types_v2;
using Kinguinverse.WebServiceProvider.Types_v2.Products;
using System.IO;
using UI.Views;
using System.Collections;
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
    private static readonly object ResearchLogLock = new();
    private static string? _researchLogPath;

    public static void Initialize(ManualLogSource logger, BackendStabilizerConfig configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _researchLogPath = Path.Combine(Paths.BepInExRootPath, "profile-reports", "backend-stabilizer-research.log");
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

    public static void LogCostumePieces(string source, CustomizeCharacterNewMetaView view)
    {
        if (!ShouldLog() || view is null)
        {
            return;
        }

        try
        {
            var getPiecesMethod = AccessTools.Method(typeof(CustomizeCharacterNewMetaView), "get__costumePieceViews");
            if (getPiecesMethod?.Invoke(view, Array.Empty<object>()) is not IEnumerable piecesEnumerable)
            {
                LogInfo($"{source}: costumePieces=unavailable");
                return;
            }

            var states = new List<string>();
            var index = 0;
            foreach (var item in piecesEnumerable)
            {
                var piece = item as CostumePieceView;
                if (piece is null)
                {
                    states.Add($"{index}:null");
                    index++;
                    continue;
                }

                var storedSkinType = piece.StoredSkinType;
                var storedSkinPartType = piece.StoredSkinPartType;
                var lockObject = AccessTools.Method(typeof(CostumePieceView), "get__lockObject")?.Invoke(piece, Array.Empty<object>()) as UnityEngine.GameObject;
                var equippedObject = AccessTools.Method(typeof(CostumePieceView), "get__equippedObject")?.Invoke(piece, Array.Empty<object>()) as UnityEngine.GameObject;
                states.Add($"{index}:{storedSkinType}/{storedSkinPartType}:lock={(lockObject?.activeSelf ?? false)}:equipped={(equippedObject?.activeSelf ?? false)}");
                index++;
            }

            LogInfo($"{source}: [{string.Join(", ", states)}]");
        }
        catch (Exception exception)
        {
            LogError($"{source} logging failed", exception);
        }
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

    public static void LogSkillSelectionSnapshot(string source, Character character)
    {
        if (!ShouldLog())
        {
            return;
        }

        var skillCards = character.SkillCards;
        LogInfo(
            $"{source}: characterId={character.CharacterId}, characterType={character.Type}, active={skillCards?.ActiveSkillCard?.SkillType}, passive1={skillCards?.PassiveSkillCard1?.SkillType}, passive2={skillCards?.PassiveSkillCard2?.SkillType}, passive3={skillCards?.PassiveSkillCard3?.SkillType}, passive4={skillCards?.PassiveSkillCard4?.SkillType}");
    }

    public static void LogPersistentSelectionStore(string source, string profileKey, CharacterType characterType)
    {
        if (!ShouldLog())
        {
            return;
        }

        LogInfo($"{source}: profileKey={profileKey}, characterType={characterType}");
    }

    public static void LogPersistentSelectionStore(string source, string profileKey, CharacterType characterType, SkinType skinType, SkinPartType skinPartType)
    {
        if (!ShouldLog())
        {
            return;
        }

        LogInfo($"{source}: profileKey={profileKey}, characterType={characterType}, skinType={skinType}, skinPartType={skinPartType}");
    }

    public static void LogPersistentSelectionFileWrite(string source, string storagePath)
    {
        if (!ShouldLog())
        {
            return;
        }

        LogInfo($"{source}: storagePath={storagePath}");
    }

    public static void LogSkinOwnershipLookup(string source, object? itemType, object? result)
    {
        if (!ShouldLog())
        {
            return;
        }

        LogInfo($"{source}: itemType={itemType}, result={result}");
    }

    public static void LogSkillUiEvent(string source, object? details)
    {
        if (!ShouldLog())
        {
            return;
        }

        LogInfo($"{source}: {details}");
    }

    public static void LogAvatarModificationSelection(string source, object? productType, ClientCharacterType characterType, bool handledLocally)
    {
        if (!ShouldLog())
        {
            return;
        }

        var objectPointer = productType is Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase objectBase
            ? Il2CppInterop.Runtime.IL2CPP.Il2CppObjectBaseToPtr(objectBase)
            : IntPtr.Zero;
        LogInfo($"{source}: productPtr=0x{objectPointer:x}, characterType={characterType}, handledLocally={handledLocally}");
    }

    public static void LogAvatarSelectionSync(string source, int characterId, CharacterType characterType, AvatarType avatarType, AvatarFrameType avatarFrameType, DescriptionType descriptionType, bool applied)
    {
        if (!ShouldLog())
        {
            return;
        }

        LogInfo($"{source}: characterId={characterId}, characterType={characterType}, avatarType={avatarType}, avatarFrameType={avatarFrameType}, descriptionType={descriptionType}, applied={applied}");
    }

    public static void LogError(string message, Exception exception)
    {
        var formattedMessage = $"{message}: {exception}";
        _logger?.LogError(formattedMessage);
        WriteResearchLog("ERROR", formattedMessage, force: true);
    }

    public static void LogSuppressedLoop(string source)
    {
        if (!SuppressedLoopSources.Add(source))
        {
            return;
        }

        var message = $"Suppressed unstable lobby view path: {source}";
        _logger?.LogWarning(message);
        WriteResearchLog("WARN", message, force: true);
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
        WriteResearchLog("INFO", message, force: false);
    }

    private static void WriteResearchLog(string level, string message, bool force)
    {
        if (!force && !ShouldLog())
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_researchLogPath))
        {
            return;
        }

        try
        {
            lock (ResearchLogLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_researchLogPath)!);
                File.AppendAllText(_researchLogPath, $"[{DateTime.Now:O}] {level} {message}{Environment.NewLine}");
            }
        }
        catch
        {
        }
    }
}
