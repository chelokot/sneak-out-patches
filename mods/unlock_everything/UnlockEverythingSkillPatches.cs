using Collections;
using HarmonyLib;
using Kinguinverse.WebServiceProvider.Types_v2;
using UI.Views;
using ClientCharacterType = Types.CharacterType;
using CharactersSkillsRuntime = Types.Structs.CharactersSkills;
using EntitySkillsComponentRuntime = Gameplay.Player.Components.EntitySkillsComponent;
using Il2CppTasks = Il2CppSystem.Threading.Tasks;
using PlayersActiveSkillsRuntime = Collections.Skills.PlayersActiveSkills;
using RuntimeCharacterType = Types.CharacterType;
using ScopeCleanerRuntime = Gameplay.ScopeCleaner;
using SpookedNetworkPlayerRuntime = Gameplay.Player.Components.SpookedNetworkPlayer;
using SpookedSkillType = Types.SpookedSkillType;

namespace SneakOut.UnlockEverything;

[HarmonyPatch(typeof(MainBoostersViewModel), "ChangeEquippedSkill")]
internal static class MainBoostersViewModelChangeEquippedSkillLoggingPatch
{
    private static void Prefix(SkillType nextSkill, int slotType)
    {
        UnlockEverythingRuntime.LogSkillUiEvent("MainBoostersViewModel.ChangeEquippedSkill", $"nextSkill={nextSkill}, slotType={slotType}");
    }
}

[HarmonyPatch(typeof(MainBoostersViewModel), "TreeSkillEquipped")]
internal static class MainBoostersViewModelTreeSkillEquippedLoggingPatch
{
    private static void Postfix(object sender, Il2CppSystem.EventArgs args)
    {
        UnlockEverythingRuntime.LogSkillUiEvent("MainBoostersViewModel.TreeSkillEquipped", $"senderType={sender?.GetType().Name ?? "null"}, argsType={args?.GetType().Name ?? "null"}");
        PlayerNewMetaInventoryOnTreeSkillChangePatch.RefreshSkillViews();
    }
}

[HarmonyPatch(typeof(MainBoostersView), "Open")]
internal static class MainBoostersViewOpenSkillSyncPatch
{
    private static void Postfix(MainBoostersView __instance)
    {
        if (!UnlockEverythingRuntime.UsePersistentSelections || __instance is null)
        {
            return;
        }

        try
        {
            var inventory = __instance._playerNewMetaInventory;
            if (inventory is null)
            {
                UnlockEverythingRuntime.LogSkillUiEvent("MainBoostersView.Open", "startupSkillSyncSkipped=noInventory");
                return;
            }

            UnlockEverythingSelections.SyncInventoryRegistryCharactersSkills(inventory);
            PlayerNewMetaInventoryOnTreeSkillChangePatch.RefreshSkillViews();
            UnlockEverythingRuntime.LogSkillUiEvent("MainBoostersView.Open", "startupSkillSyncApplied=1");
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend stabilizer MainBoostersView.Open skill sync failed", exception);
        }
    }
}

[HarmonyPatch(typeof(PlayerNewMetaInventory), nameof(PlayerNewMetaInventory.OnTreeSkillChange))]
internal static class PlayerNewMetaInventoryOnTreeSkillChangePatch
{
    private static bool Prefix(PlayerNewMetaInventory __instance, SkillType cardSkillType, int slotType, ClientCharacterType characterType)
    {
        UnlockEverythingSelections.RememberInventory(__instance);
        UnlockEverythingRuntime.LogSkillUiEvent("PlayerNewMetaInventory.OnTreeSkillChange:prefix", $"skill={cardSkillType}, slotType={slotType}, characterType={characterType}");
        return true;
    }

    private static void Postfix(ClientCharacterType characterType, Il2CppTasks.Task<bool> __result)
    {
        UnlockEverythingSelections.SaveAfterCompletion(__result, characterType);
    }

    internal static void RefreshSkillViews()
    {
        try
        {
            foreach (var view in UnityEngine.Resources.FindObjectsOfTypeAll<MainBoostersView>())
            {
                if (view is null)
                {
                    continue;
                }

                view.SetSkillTree();
                view.SetEquippedSkills();
                UnlockEverythingRuntime.LogSkillUiEvent(
                    "MainBoostersView.SetEquippedSkills",
                    $"equippedSkillsCount={view._equippedSkills?.Length ?? 0}");
            }
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend stabilizer skill view refresh failed", exception);
        }
    }
}

[HarmonyPatch(typeof(PlayersActiveSkillsRuntime), nameof(PlayersActiveSkillsRuntime.HaveSkillEquipped))]
internal static class PlayersActiveSkillsHaveSkillEquippedPatch
{
    private static bool Prefix(int internalId, SkillType cardSkillType, RuntimeCharacterType characterType, ref bool __result)
    {
        if (!UnlockEverythingSelections.TryGetLocalSkillEquipped(internalId, cardSkillType, characterType, out var equipped))
        {
            if (!UnlockEverythingSelections.IsCurrentInternalIdForLogging(internalId))
            {
                UnlockEverythingRuntime.LogSkillUiEvent(
                    "PlayersActiveSkills.HaveSkillEquipped:original",
                    $"internalId={internalId}, skill={cardSkillType}, characterType={characterType}, reason=noOverride");
            }

            return true;
        }

        __result = equipped;
        if (!UnlockEverythingSelections.IsCurrentInternalIdForLogging(internalId))
        {
            UnlockEverythingRuntime.LogSkillUiEvent(
                "PlayersActiveSkills.HaveSkillEquipped:remote",
                $"internalId={internalId}, skill={cardSkillType}, characterType={characterType}, after={__result}");
        }

        return false;
    }
}

[HarmonyPatch(typeof(PlayersActiveSkillsRuntime), nameof(PlayersActiveSkillsRuntime.GetPlayerSkillModifier))]
internal static class PlayersActiveSkillsGetPlayerSkillModifierPatch
{
    private static bool Prefix(PlayersActiveSkillsRuntime __instance, int internalId, SkillType cardSkillType, Types.SkillModifierType skillModifierType, ref float __result)
    {
        if (!UnlockEverythingSelections.TryGetLocalSkillTier(internalId, cardSkillType, out var characterType, out var tier))
        {
            if (!UnlockEverythingSelections.IsCurrentInternalIdForLogging(internalId))
            {
                UnlockEverythingRuntime.LogSkillUiEvent(
                    "PlayersActiveSkills.GetPlayerSkillModifier:original",
                    $"internalId={internalId}, skill={cardSkillType}, modifierType={skillModifierType}, reason=noTierOverride");
            }

            return true;
        }

        if (!UnlockEverythingSelections.TryGetDirectSkillModifier(__instance, cardSkillType, skillModifierType, characterType, tier, out var modifier))
        {
            if (!UnlockEverythingSelections.IsCurrentInternalIdForLogging(internalId))
            {
                UnlockEverythingRuntime.LogSkillUiEvent(
                    "PlayersActiveSkills.GetPlayerSkillModifier:original",
                    $"internalId={internalId}, skill={cardSkillType}, modifierType={skillModifierType}, tier={tier}, characterType={characterType}, reason=directLookupFailed");
            }

            return true;
        }

        __result = modifier;
        if (!UnlockEverythingSelections.IsCurrentInternalIdForLogging(internalId))
        {
            UnlockEverythingRuntime.LogSkillUiEvent(
                "PlayersActiveSkills.GetPlayerSkillModifier:remote",
                $"internalId={internalId}, skill={cardSkillType}, modifierType={skillModifierType}, tier={tier}, characterType={characterType}, after={__result}");
        }

        return false;
    }
}

[HarmonyPatch(typeof(EntitySkillsComponentRuntime), nameof(EntitySkillsComponentRuntime.GetSkill))]
internal static class EntitySkillsComponentGetSkillPatch
{
    private static void Postfix(EntitySkillsComponentRuntime __instance, bool firstSkill, ref SpookedSkillType __result)
    {
        if (!firstSkill)
        {
            return;
        }

        if (UnlockEverythingSelections.TryGetLocalFirstSkill(__instance, out var firstSkillType))
        {
            __result = firstSkillType;
        }
    }
}

[HarmonyPatch(typeof(SpookedNetworkPlayerRuntime), nameof(SpookedNetworkPlayerRuntime.Spawned))]
internal static class SpookedNetworkPlayerSpawnedRememberPatch
{
    private static void Postfix(SpookedNetworkPlayerRuntime __instance)
    {
        UnlockEverythingSelections.RememberNetworkPlayer(__instance);
        UnlockEverythingSelections.ApplyPersistedSkinToLocalNetworkPlayer(__instance);
    }
}

[HarmonyPatch(typeof(SpookedNetworkPlayerRuntime), nameof(SpookedNetworkPlayerRuntime.RPC_SpawnedReady))]
internal static class SpookedNetworkPlayerSpawnedReadyStartupSkinPatch
{
    private static void Postfix(SpookedNetworkPlayerRuntime __instance)
    {
        UnlockEverythingSelections.RememberNetworkPlayer(__instance);
        UnlockEverythingSelections.ApplyPersistedSkinToLocalNetworkPlayer(__instance);
    }
}

[HarmonyPatch(typeof(SpookedNetworkPlayerRuntime), nameof(SpookedNetworkPlayerRuntime.Init))]
internal static class SpookedNetworkPlayerInitSkillPayloadPatch
{
    private static void Prefix(int networkId, string nickname, ref CharactersSkillsRuntime charactersSkills)
    {
        if (!UnlockEverythingRuntime.UseProfileOverlay)
        {
            return;
        }

        try
        {
            var before = UnlockEverythingSelections.DescribeCharactersSkillsPayload(charactersSkills);
            var applied = UnlockEverythingSelections.TryMaxCharactersSkillsPayload(ref charactersSkills);
            UnlockEverythingRuntime.LogSkillUiEvent(
                "SpookedNetworkPlayer.Init:skillsPayload",
                $"networkId={networkId}, nickname={nickname}, applied={applied}, before={before}, after={UnlockEverythingSelections.DescribeCharactersSkillsPayload(charactersSkills)}");
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend stabilizer SpookedNetworkPlayer.Init skills payload fix failed", exception);
        }
    }

    private static void Postfix(SpookedNetworkPlayerRuntime __instance)
    {
        UnlockEverythingSelections.RememberNetworkPlayer(__instance);
        UnlockEverythingSelections.ApplyPersistedSkinToLocalNetworkPlayer(__instance);
        if (!UnlockEverythingRuntime.UseProfileOverlay)
        {
            return;
        }

        try
        {
            UnlockEverythingSelections.RememberLoadedCharactersSkillsFromNetworkPlayer(__instance);
            var internalId = UnlockEverythingSelections.GetNetworkPlayerInternalId(__instance);
            UnlockEverythingRuntime.LogSkillUiEvent(
                "SpookedNetworkPlayer.Init:liveCharactersSkills",
                $"internalId={internalId}, payload={UnlockEverythingSelections.DescribeLiveSpookedNetworkPlayerCharactersSkills(__instance)}");
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend stabilizer SpookedNetworkPlayer.Init live skill snapshot failed", exception);
        }
    }
}

[HarmonyPatch(typeof(ScopeCleanerRuntime), nameof(ScopeCleanerRuntime.Clean))]
internal static class ScopeCleanerCleanPatch
{
    private static void Postfix()
    {
        UnlockEverythingSelections.ClearLoadedCharactersSkills();
    }
}

[HarmonyPatch(typeof(ScopeCleanerRuntime), nameof(ScopeCleanerRuntime.GameplayClean))]
internal static class ScopeCleanerGameplayCleanPatch
{
    private static void Postfix()
    {
        UnlockEverythingSelections.ClearLoadedCharactersSkills();
    }
}
