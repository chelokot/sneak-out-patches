using Collections;
using Kinguinverse.WebServiceProvider.Types_v2;
using CharactersSkillsRuntime = Types.Structs.CharactersSkills;
using GameRuntime = Game.Game;
using SpookedNetworkPlayerRuntime = Gameplay.Player.Components.SpookedNetworkPlayer;

namespace SneakOut.UnlockEverything;

internal static partial class UnlockEverythingSelections
{
    private static AvatarType _pendingAvatarType = AvatarType.None;
    private static AvatarFrameType _pendingAvatarFrameType = AvatarFrameType.None;
    private static DescriptionType _pendingDescriptionType = DescriptionType.none;
    private static PlayerNewMetaInventory? _currentInventory;
    private static SpookedNetworkPlayerRuntime? _currentNetworkPlayer;
    private static readonly Dictionary<int, CharactersSkillsRuntime> LoadedCharactersSkillsByInternalId = new();
    private static readonly HashSet<string> LoggedRemoteSkillDiagnostics = new();

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
        return GameRuntime.InternalId;
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

    private static bool ShouldLogRemoteSkillDiagnostic(int internalId)
    {
        return internalId > 0 && !IsCurrentInternalId(internalId);
    }

    private static void LogRemoteSkillDiagnosticOnce(string source, int internalId, SkillType skillType, string details)
    {
        if (!ShouldLogRemoteSkillDiagnostic(internalId))
        {
            return;
        }

        var key = $"{source}|{internalId}|{skillType}|{details}";
        if (!LoggedRemoteSkillDiagnostics.Add(key))
        {
            return;
        }

        UnlockEverythingRuntime.LogSkillUiEvent(source, $"internalId={internalId}, skill={skillType}, {details}");
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
}
