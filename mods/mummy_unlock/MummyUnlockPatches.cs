using Collections;
using Collections.Skills;
using Gameplay.Player.Components;
using Gameplay.Skills;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Kinguinverse.WebServiceProvider.Types_v2;
using Localization;
using Scriptables;
using Types;
using UI.Views;
using UI.Views.Lobby;
using AvatarType = Kinguinverse.WebServiceProvider.Types_v2.AvatarType;
using CharactersSkillsRuntime = Types.Structs.CharactersSkills;
using Il2CppTasks = Il2CppSystem.Threading.Tasks;
using RuntimeCharacterType = Types.CharacterType;
using SimplifiedSkillsRuntime = Types.Structs.SimplifiedWebPlayerSkills;

namespace SneakOut.MummyUnlock;

[HarmonyPatch(typeof(SeekerSelectionViewModel), nameof(SeekerSelectionViewModel.Init))]
internal static class SeekerSelectionViewModelInitPatch
{
    private static void Postfix(SeekerSelectionViewModel __instance)
    {
        MummyUnlockRuntime.EnsureAvailableSeekersContainMummy(__instance);
        __instance.OnSelectionChange(0);
    }
}

[HarmonyPatch(typeof(PlayerNewMetaInventory), nameof(PlayerNewMetaInventory.LoadOwnedSeekers))]
internal static class PlayerNewMetaInventoryLoadOwnedSeekersMummyPatch
{
    private static void Postfix(PlayerNewMetaInventory __instance)
    {
        try
        {
            MummyUnlockRuntime.EnsureOwnedSeekersContainMummy(__instance);
        }
        catch (Exception exception)
        {
            MummyUnlockRuntime.LogError("Adding Mummy to owned hunters failed", exception);
        }
    }
}

[HarmonyPatch(typeof(SpookedNetworkPlayer), nameof(SpookedNetworkPlayer.GetCurrentAvatar))]
internal static class SpookedNetworkPlayerGetCurrentAvatarPatch
{
    private static bool Prefix(SpookedNetworkPlayer __instance, ref AvatarType __result)
    {
        if (!MummyUnlockRuntime.TryGetMummyAvatar(__instance, out var avatarType))
        {
            return true;
        }

        __result = avatarType;
        return false;
    }
}

[HarmonyPatch(typeof(SeekerSelectionView), nameof(SeekerSelectionView.ManagerAwake))]
internal static class SeekerSelectionViewManagerAwakePatch
{
    private static void Postfix(SeekerSelectionView __instance)
    {
        MummyAbilityIconRuntime.ApplyToSeekerSelectionView(__instance);
    }
}

[HarmonyPatch(
    typeof(SeekerSelectionView._ShiftCharactersPanel_d__27),
    nameof(SeekerSelectionView._ShiftCharactersPanel_d__27.MoveNext))]
internal static class SeekerSelectionViewShiftCharactersPanelMoveNextPatch
{
    private static void Postfix(SeekerSelectionView._ShiftCharactersPanel_d__27 __instance, bool __result)
    {
        if (__result)
        {
            return;
        }

        var view = __instance.__4__this;
        if (view is not null)
        {
            MummyAbilityIconRuntime.ApplyToSeekerSelectionView(view);
        }
    }
}

[HarmonyPatch(typeof(CharacterShopView), nameof(CharacterShopView.ManagerAwake))]
internal static class CharacterShopViewManagerAwakePatch
{
    private static void Prefix(CharacterShopView __instance)
    {
        MummyUnlockRuntime.PrepareCharacterShop(__instance);
    }

    private static void Postfix(CharacterShopView __instance)
    {
        MummyAbilityIconRuntime.ApplyToCharacterShopCarousel(__instance);
    }
}

[HarmonyPatch(
    typeof(CharacterShopView._ShiftCharacters_d__43),
    nameof(CharacterShopView._ShiftCharacters_d__43.MoveNext))]
internal static class CharacterShopViewShiftCharactersMoveNextPatch
{
    private static void Postfix(CharacterShopView._ShiftCharacters_d__43 __instance, bool __result)
    {
        if (__result)
        {
            return;
        }

        var view = __instance.__4__this;
        if (view is not null)
        {
            MummyAbilityIconRuntime.ApplyToCharacterShopCarousel(view);
        }
    }
}

[HarmonyPatch(typeof(CharacterShopView), nameof(CharacterShopView.SetDescriptions))]
internal static class CharacterShopViewSetDescriptionsPatch
{
    private static bool Prefix(CharacterShopView __instance)
    {
        return !MummyUnlockRuntime.TryRenderCharacterShopDescription(__instance);
    }
}

[HarmonyPatch(typeof(MainBoostersView), nameof(MainBoostersView.ManagerAwake))]
internal static class MainBoostersViewManagerAwakeMummyPerkTreePatch
{
    private static void Prefix(MainBoostersView __instance)
    {
        try
        {
            MummyPerkShopRuntime.PrepareView(__instance);
        }
        catch (Exception exception)
        {
            MummyUnlockRuntime.LogError("Preparing the Mummy perk shop failed", exception);
        }
    }

    private static void Postfix(MainBoostersView __instance)
    {
        try
        {
            MummyPerkShopRuntime.UpdateSelectionContext(__instance);
            MummyPerkShopRuntime.ApplyCarouselIcons(__instance);
        }
        catch (Exception exception)
        {
            MummyUnlockRuntime.LogError("Applying the Mummy perk-shop icon failed", exception);
        }
    }
}

[HarmonyPatch(
    typeof(MainBoostersView._ShiftCharactersPanel_d__115),
    nameof(MainBoostersView._ShiftCharactersPanel_d__115.MoveNext))]
internal static class MainBoostersViewShiftCharactersPanelMoveNextMummyIconPatch
{
    private static void Postfix(MainBoostersView._ShiftCharactersPanel_d__115 __instance, bool __result)
    {
        if (__result || __instance.__4__this is not { } view)
        {
            return;
        }

        try
        {
            MummyPerkShopRuntime.UpdateSelectionContext(view);
            MummyPerkShopRuntime.ApplyCarouselIcons(view);
        }
        catch (Exception exception)
        {
            MummyUnlockRuntime.LogError("Refreshing the Mummy perk-shop presentation failed", exception);
        }
    }
}

[HarmonyPatch(typeof(MainBoostersView), nameof(MainBoostersView.SetSkillTree))]
internal static class MainBoostersViewSetSkillTreeMummyPresentationPatch
{
    private static void Prefix(MainBoostersView __instance)
    {
        MummyPerkShopRuntime.UpdateSelectionContext(__instance);
    }

    private static void Postfix(MainBoostersView __instance)
    {
        MummyPerkShopRuntime.ApplyCharacterName(__instance);
        MummyPerkShopRuntime.ApplyCarouselIcons(__instance);
    }
}

[HarmonyPatch(typeof(MainBoostersView), nameof(MainBoostersView.SetEquippedSkills))]
internal static class MainBoostersViewSetEquippedSkillsMummyContextPatch
{
    private static void Prefix(MainBoostersView __instance)
    {
        MummyPerkShopRuntime.UpdateSelectionContext(__instance);
    }
}

[HarmonyPatch(typeof(MainBoostersView), nameof(MainBoostersView.GetDescriptionParams))]
internal static class MainBoostersViewGetDescriptionParamsMummyDefinitionPatch
{
    private static void Prefix(SkillType cardSkillType, ref RuntimeCharacterType characterType)
    {
        characterType = MummySkillsRegistry.GetDefinitionCharacter(cardSkillType, characterType);
    }
}

[HarmonyPatch(typeof(SpookedSkillSettings), nameof(SpookedSkillSettings.GetTitle))]
internal static class SpookedSkillSettingsGetTitleMummyDefinitionPatch
{
    private static void Prefix(SkillType cardSkillType, ref RuntimeCharacterType characterType)
    {
        characterType = MummySkillsRegistry.GetDefinitionCharacter(cardSkillType, characterType);
    }
}

[HarmonyPatch(typeof(SpookedSkillSettings), nameof(SpookedSkillSettings.GetDescriptionKey))]
internal static class SpookedSkillSettingsGetDescriptionKeyMummyDefinitionPatch
{
    private static void Prefix(SkillType cardSkillType, ref RuntimeCharacterType characterType)
    {
        characterType = MummySkillsRegistry.GetDefinitionCharacter(cardSkillType, characterType);
    }
}

[HarmonyPatch(typeof(SpookedSkillSettings), nameof(SpookedSkillSettings.GetAllModifiers))]
internal static class SpookedSkillSettingsGetAllModifiersMummyDefinitionPatch
{
    private static void Prefix(SkillType cardSkillType, ref RuntimeCharacterType characterType)
    {
        characterType = MummySkillsRegistry.GetDefinitionCharacter(cardSkillType, characterType);
    }
}

[HarmonyPatch(typeof(CharactersSkillsRuntime), "GetSkillsForCharacterType")]
internal static class CharactersSkillsGetMummySkillsPatch
{
    private static bool Prefix(RuntimeCharacterType characterType, ref SimplifiedSkillsRuntime __result)
    {
        return !MummySkillsRegistry.TryGetSkills(characterType, out __result);
    }
}

[HarmonyPatch(typeof(CharactersSkillsRuntime), "SaveSkillsForCharacterType")]
internal static class CharactersSkillsSaveMummySkillsPatch
{
    private static bool Prefix(RuntimeCharacterType characterType, SimplifiedSkillsRuntime skills)
    {
        return !MummySkillsRegistry.TrySaveSkills(characterType, skills);
    }
}

[HarmonyPatch(typeof(PlayerNewMetaInventory), nameof(PlayerNewMetaInventory.GetSkillCard))]
internal static class PlayerNewMetaInventoryGetMummySkillCardPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(SkillType skillType, ref SkillCard __result)
    {
        if (MummyPerkShopRuntime.ShouldProvideMummySkillCard(skillType))
        {
            __result = MummyPerkRuntime.GetSyntheticCard(skillType);
        }
    }
}

[HarmonyPatch(typeof(PlayerNewMetaInventory), nameof(PlayerNewMetaInventory.DoIOwnThisItem))]
internal static class PlayerNewMetaInventoryOwnsMummySkillPatch
{
    private static bool Prefix(Il2CppSystem.Enum itemType, ref bool __result)
    {
        if (itemType is null
            || !System.Enum.TryParse(itemType.ToString(), out SkillType skillType)
            || !MummyPerkShopRuntime.ShouldProvideMummySkillCard(skillType))
        {
            return true;
        }

        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(PlayerNewMetaInventory), nameof(PlayerNewMetaInventory.OnTreeSkillChange))]
internal static class PlayerNewMetaInventoryOnMummyTreeSkillChangePatch
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(
        SkillType cardSkillType,
        TreeSkillSlotType slotType,
        RuntimeCharacterType characterType,
        ref Il2CppTasks.Task<bool> __result)
    {
        if (characterType != MummyUnlockRuntime.MummyCharacterType)
        {
            return true;
        }

        var applied = MummyPerkRuntime.ApplySelection(cardSkillType, slotType);
        __result = Il2CppTasks.Task.FromResult(applied);
        return false;
    }
}

[HarmonyPatch(typeof(MainBoostersViewModel), "TreeSkillEquipped")]
internal static class MainBoostersViewModelTreeSkillEquippedMummyRefreshPatch
{
    private static void Postfix()
    {
        MummyPerkShopRuntime.RefreshCurrentView();
    }
}

[HarmonyPatch(typeof(PlayersActiveSkills), nameof(PlayersActiveSkills.HaveSkillEquipped))]
internal static class PlayersActiveSkillsHaveMummySkillEquippedPatch
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(
        int internalId,
        SkillType cardSkillType,
        RuntimeCharacterType characterType,
        ref bool __result)
    {
        if (!MummyPerkRuntime.TryHaveSkillEquipped(
                internalId,
                cardSkillType,
                characterType,
                out var equipped))
        {
            return true;
        }

        __result = equipped;
        return false;
    }
}

[HarmonyPatch(typeof(PlayersActiveSkills), nameof(PlayersActiveSkills.GetPlayerSkillModifier))]
internal static class PlayersActiveSkillsGetMummySkillModifierPatch
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(
        PlayersActiveSkills __instance,
        int internalId,
        SkillType cardSkillType,
        SkillModifierType skillModifierType,
        ref float __result)
    {
        if (!MummyPerkRuntime.TryGetModifier(
                __instance,
                internalId,
                cardSkillType,
                skillModifierType,
                out var modifier))
        {
            return true;
        }

        __result = modifier;
        return false;
    }
}

[HarmonyPatch(typeof(SpookedNetworkPlayer), nameof(SpookedNetworkPlayer.Init))]
internal static class SpookedNetworkPlayerInitMummyRuntimePatch
{
    private static void Postfix(SpookedNetworkPlayer __instance)
    {
        MummyPerkRuntime.RememberNetworkPlayer(__instance);
    }
}

[HarmonyPatch(typeof(SpookedNetworkPlayer), nameof(SpookedNetworkPlayer.Spawned))]
internal static class SpookedNetworkPlayerSpawnedMummyRuntimePatch
{
    private static void Postfix(SpookedNetworkPlayer __instance)
    {
        MummyPerkRuntime.RememberNetworkPlayer(__instance);
    }
}

[HarmonyPatch(typeof(SpookedNetworkPlayer), nameof(SpookedNetworkPlayer.RPC_SpawnedReady))]
internal static class SpookedNetworkPlayerSpawnedReadyMummyRuntimePatch
{
    private static void Postfix(SpookedNetworkPlayer __instance)
    {
        MummyPerkRuntime.RememberNetworkPlayer(__instance);
    }
}

[HarmonyPatch(typeof(GameTranslator), nameof(GameTranslator.ReloadDictionary))]
internal static class GameTranslatorReloadMummyDictionaryPatch
{
    private static void Postfix(GameTranslator __instance)
    {
        MummyPerkShopRuntime.AddCharacterNameTranslation(__instance);
    }
}

[HarmonyPatch(typeof(Gameplay.ScopeCleaner), nameof(Gameplay.ScopeCleaner.Clean))]
internal static class ScopeCleanerCleanMummyRuntimePatch
{
    private static void Postfix()
    {
        MummyPerkRuntime.Clear();
        MummySarcophagusTeleportRuntime.Clear();
    }
}

[HarmonyPatch(typeof(Gameplay.ScopeCleaner), nameof(Gameplay.ScopeCleaner.GameplayClean))]
internal static class ScopeCleanerGameplayCleanMummyRuntimePatch
{
    private static void Postfix()
    {
        MummyPerkRuntime.Clear();
        MummySarcophagusTeleportRuntime.Clear();
    }
}

[HarmonyPatch(typeof(MummySarcophagusManager), nameof(MummySarcophagusManager.Init))]
internal static class MummySarcophagusManagerInitPatch
{
    private static void Postfix(Il2CppReferenceArray<Sarcophagus> sarcophagi)
    {
        MummySarcophagusVisualRuntime.ApplyToSarcophagi(sarcophagi);
    }
}

[HarmonyPatch(typeof(SarcophagusInitializer), "Start")]
internal static class SarcophagusInitializerStartPatch
{
    private static void Postfix(SarcophagusInitializer __instance)
    {
        MummySarcophagusVisualRuntime.ApplyToSarcophagi(__instance._sarcophagi);
    }
}

[HarmonyPatch(typeof(PlayerActionsView), "SetFirstSkillSprite")]
internal static class PlayerActionsViewSetFirstSkillSpritePatch
{
    private static void Postfix(PlayerActionsView __instance, SpookedSkillType firstSkill)
    {
        MummyAbilityIconRuntime.ApplyToPlayerActionsView(__instance, firstSkill, false);
    }
}

[HarmonyPatch(typeof(PlayerActionsView), "SetSecondSkillSprite")]
internal static class PlayerActionsViewSetSecondSkillSpritePatch
{
    private static void Postfix(PlayerActionsView __instance, SpookedSkillType secondSkill)
    {
        MummyAbilityIconRuntime.ApplyToPlayerActionsView(__instance, secondSkill, true);
    }
}
