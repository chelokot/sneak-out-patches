using Collections;
using Kinguinverse.WebServiceProvider.Types_v2;
using UI.Views;
using CharactersSkillsRuntime = Types.Structs.CharactersSkills;
using EntitySkillsComponentRuntime = Gameplay.Player.Components.EntitySkillsComponent;
using NetworkPlayerRegistryRuntime = Gameplay.Player.Components.NetworkPlayerRegistry;
using SpookedNetworkPlayerRuntime = Gameplay.Player.Components.SpookedNetworkPlayer;
using RuntimeCharacterType = Types.CharacterType;

namespace SneakOut.UnlockEverything;

internal static partial class UnlockEverythingSelections
{
    private static void SaveSelection(Character character)
    {
        LocalSelectionsStore.SaveCharacterSelection(character);
    }

    internal static void RememberInventory(PlayerNewMetaInventory inventory)
    {
        _currentInventory = inventory;
    }

    internal static void RememberNetworkPlayer(SpookedNetworkPlayerRuntime networkPlayer)
    {
        if (networkPlayer is null || networkPlayer.InternalId <= 0)
        {
            return;
        }

        var currentInternalId = GetCurrentInternalId();
        if (currentInternalId > 0 && networkPlayer.InternalId != currentInternalId)
        {
            return;
        }

        _currentNetworkPlayer = networkPlayer;
    }

    internal static bool ApplyPersistedSkinToLocalNetworkPlayer(SpookedNetworkPlayerRuntime networkPlayer)
    {
        if (!UnlockEverythingRuntime.UsePersistentSelections
            || networkPlayer is null
            || networkPlayer.Pointer == IntPtr.Zero
            || !networkPlayer.HasInputAuthority
            || networkPlayer.IsBot
            || networkPlayer.CharacterType != RuntimeCharacterType.victim_penguin)
        {
            return false;
        }

        try
        {
            var characterData = networkPlayer.CharacterData;
            if (!LocalSelectionsStore.TryApplyPersistedPenguinSkin(ref characterData, out var changed))
            {
                return false;
            }

            if (changed)
            {
                NormalizeCharacterData(ref characterData);
                networkPlayer.ChangeCharacterData(characterData);
            }
            UnlockEverythingRuntime.LogStartupSkinValidation(networkPlayer.InternalId, characterData, changed);
            return changed;
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Applying persisted skin to local network player failed", exception);
            return false;
        }
    }

    private static MyPlayerRegistry? GetMyPlayerRegistry(PlayerNewMetaInventory? preferredInventory = null)
    {
        if (preferredInventory is not null)
        {
            var preferredRegistry = preferredInventory._myPlayerRegistry;
            if (preferredRegistry is not null)
            {
                _currentInventory = preferredInventory;
                return preferredRegistry;
            }
        }

        if (_currentInventory is not null)
        {
            var currentRegistry = _currentInventory._myPlayerRegistry;
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

            var inventory = view._playerNewMetaInventory;
            if (inventory is null)
            {
                continue;
            }

            var registry = inventory._myPlayerRegistry;
            if (registry is null)
            {
                continue;
            }

            _currentInventory = inventory;
            return registry;
        }

        return null;
    }

    private static NetworkPlayerRegistryRuntime? GetNetworkPlayerRegistry()
    {
        if (_currentInventory is not null)
        {
            var currentRegistry = _currentInventory._networkPlayerRegistry;
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

            var inventory = view._playerNewMetaInventory;
            if (inventory is null)
            {
                continue;
            }

            _currentInventory = inventory;
            var registry = inventory._networkPlayerRegistry;
            if (registry is not null)
            {
                return registry;
            }
        }

        return null;
    }

    private static SpookedNetworkPlayerRuntime? GetCurrentNetworkPlayer()
    {
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
            else if (_currentNetworkPlayer.InternalId == internalId)
            {
                return _currentNetworkPlayer;
            }
            else
            {
                _currentNetworkPlayer = null;
            }
        }

        var registry = GetNetworkPlayerRegistry();
        var networkPlayer = registry?[internalId];
        if (networkPlayer is not null)
        {
            RememberNetworkPlayer(networkPlayer);
            return networkPlayer;
        }

        foreach (var candidate in UnityEngine.Resources.FindObjectsOfTypeAll<SpookedNetworkPlayerRuntime>())
        {
            if (candidate is null || candidate.GetType() != typeof(SpookedNetworkPlayerRuntime))
            {
                continue;
            }

            if (candidate.InternalId == internalId)
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
        if (player?.Characters is null)
        {
            return false;
        }

        var myPlayerRegistry = GetMyPlayerRegistry(preferredInventory);
        if (myPlayerRegistry is null)
        {
            UnlockEverythingRuntime.LogSkillUiEvent("UnlockEverythingSelections.SyncMyPlayerRegistryCharactersSkills", "noRegistry");
            return false;
        }

        myPlayerRegistry.CharactersSkills = CharactersSkillsRuntime.ToCharacterSkills(player.Characters);
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

        var characterData = myPlayerRegistry.CharacterData;
        var skinParts = character.SkinParts;
        characterData.HeadType = skinParts?.Head?.SkinPartType ?? SkinPartType.None;
        characterData.TorsoType = skinParts?.Chest?.SkinPartType ?? SkinPartType.None;
        characterData.ArmsType = skinParts?.Hands?.SkinPartType ?? SkinPartType.None;
        characterData.LegsType = skinParts?.Legs?.SkinPartType ?? SkinPartType.None;
        characterData.BackType = skinParts?.Back?.SkinPartType ?? SkinPartType.None;
        characterData.WholeType = skinParts?.Whole?.SkinPartType ?? SkinPartType.None;
        myPlayerRegistry.CharacterData = characterData;
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
            if (player?.Characters is null || networkPlayer is null)
            {
                UnlockEverythingRuntime.LogSkillUiEvent("UnlockEverythingSelections.SyncLivePlayerCharactersSkills", "missingSource");
                return;
            }

            networkPlayer.CharactersSkills = CharactersSkillsRuntime.ToCharacterSkills(player.Characters);
            networkPlayer.GetComponent<EntitySkillsComponentRuntime>()?.RefreshPlayerSkills();
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

                var inventory = view._playerNewMetaInventory;
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

            var avatarType = character.Avatar?.AvatarType ?? AvatarType.None;
            switch (character.Type)
            {
                case CharacterType.Penguin:
                    networkPlayer.VictimAvatarType = avatarType;
                    break;
                case CharacterType.Reaper:
                    networkPlayer.RipperAvatarType = avatarType;
                    break;
                case CharacterType.Dracula:
                    networkPlayer.DraculaAvatarType = avatarType;
                    break;
                case CharacterType.Scarecrow:
                    networkPlayer.ScarecrowAvatarType = avatarType;
                    break;
                case CharacterType.Butcher:
                    networkPlayer.ButcherAvatarType = avatarType;
                    break;
                case CharacterType.Clown:
                    networkPlayer.ClownAvatarType = avatarType;
                    break;
            }

            networkPlayer.AvatarFrameBorderType = character.AvatarFrame?.AvatarFrameType ?? AvatarFrameType.None;
            networkPlayer.DescriptionType = character.Description;
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
                || networkPlayer.CharacterType != runtimeCharacterType)
            {
                UnlockEverythingRuntime.LogSkinSelectionSnapshot("UnlockEverythingSelections.SyncLivePlayerCharacterData:characterMismatch", character);
                return;
            }

            var characterData = networkPlayer.CharacterData;
            var skinParts = character.SkinParts;
            characterData.HeadType = skinParts?.Head?.SkinPartType ?? SkinPartType.None;
            characterData.TorsoType = skinParts?.Chest?.SkinPartType ?? SkinPartType.None;
            characterData.ArmsType = skinParts?.Hands?.SkinPartType ?? SkinPartType.None;
            characterData.LegsType = skinParts?.Legs?.SkinPartType ?? SkinPartType.None;
            characterData.BackType = skinParts?.Back?.SkinPartType ?? SkinPartType.None;
            characterData.WholeType = skinParts?.Whole?.SkinPartType ?? SkinPartType.None;
            networkPlayer.ChangeCharacterData(characterData);
            UnlockEverythingRuntime.LogSkinSelectionSnapshot("UnlockEverythingSelections.SyncLivePlayerCharacterData:applied", character);
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend stabilizer live character data sync failed", exception);
        }
    }
}
