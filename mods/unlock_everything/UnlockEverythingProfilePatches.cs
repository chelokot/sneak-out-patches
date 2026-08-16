using Base;
using Collections;
using HarmonyLib;
using Kinguinverse.WebServiceProvider.Types_v2;
using Il2CppTasks = Il2CppSystem.Threading.Tasks;

namespace SneakOut.UnlockEverything;

[HarmonyPatch(typeof(ClientCache), nameof(ClientCache.OnClientConfirmed))]
internal static class ClientCacheOnClientConfirmedPatch
{
    private static void Prefix(ClientCache __instance)
    {
        try
        {
            UnlockEverythingRuntime.TrackClientCache(__instance);
            // The original method synchronously notifies PlayerNewMetaInventory, which snapshots
            // OwnedSeekers for the perk-shop picker. Populate the profile before that notification.
            UnlockEverythingOverlay.EnsureClientCache(__instance);
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend stabilizer ClientCache.OnClientConfirmed prefix failed", exception);
        }
    }

    private static void Postfix(ClientCache __instance)
    {
        try
        {
            UnlockEverythingRuntime.LogClientCacheState("ClientCache.OnClientConfirmed", __instance);
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend stabilizer ClientCache.OnClientConfirmed diagnostics failed", exception);
        }
    }
}

[HarmonyPatch(typeof(PlayerNewMetaInventory), nameof(PlayerNewMetaInventory.GetSkillCard))]
internal static class PlayerNewMetaInventoryGetSkillCardPatch
{
    private static void Postfix(SkillType skillType, ref SkillCard __result)
    {
        if (!UnlockEverythingRuntime.UseProfileOverlay && !UnlockEverythingRuntime.UseLocalStub)
        {
            return;
        }

        if (skillType == SkillType.None || __result is not null)
        {
            return;
        }

        try
        {
            __result = UnlockEverythingStub.CreateMaxSkillCard(skillType);
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend stabilizer PlayerNewMetaInventory.GetSkillCard postfix failed", exception);
        }
    }
}

[HarmonyPatch(typeof(ClientCache), nameof(ClientCache.RefreshPlayer))]
internal static class ClientCacheRefreshPlayerPatch
{
    private static void Prefix(ClientCache __instance)
    {
        try
        {
            UnlockEverythingRuntime.TrackClientCache(__instance);
            if (UnlockEverythingRuntime.UseLocalStub)
            {
                UnlockEverythingStub.PopulateClientCache(__instance);
            }

            UnlockEverythingRuntime.LogClientCacheState("ClientCache.RefreshPlayer", __instance);
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Backend stabilizer ClientCache.RefreshPlayer prefix failed", exception);
        }
    }

    private static void Postfix(ClientCache __instance, Il2CppTasks.Task __result)
    {
        if (!UnlockEverythingRuntime.UseProfileOverlay || UnlockEverythingRuntime.UseLocalStub)
        {
            return;
        }

        try
        {
            if (__result is null)
            {
                return;
            }

            if (__result.IsCompletedSuccessfully)
            {
                ApplyProfileOverlayAndLiveSelections(__instance);
                UnlockEverythingRuntime.LogClientCacheState("ClientCache.RefreshPlayer:completed", __instance);
                return;
            }

            UnlockEverythingRuntime.ContinueOnMainThread(
                __result,
                () =>
                {
                    ApplyProfileOverlayAndLiveSelections(__instance);
                    UnlockEverythingRuntime.LogClientCacheState("ClientCache.RefreshPlayer:completed", __instance);
                },
                "Unlock Everything ClientCache.RefreshPlayer completion overlay failed");
        }
        catch (Exception exception)
        {
            UnlockEverythingRuntime.LogError("Unlock Everything ClientCache.RefreshPlayer completion overlay failed", exception);
        }
    }

    private static void ApplyProfileOverlayAndLiveSelections(ClientCache clientCache)
    {
        UnlockEverythingStub.ApplyProfileOverlay(clientCache);
        // RefreshPlayer can finish while the previous lobby player is being destroyed and before
        // its replacement is initialized. Only a player captured by the Fusion lifecycle hooks is
        // eligible here; if none exists yet, those hooks apply the persisted skin when it is safe.
        UnlockEverythingSelections.ApplyPersistedSkinToCurrentNetworkPlayer();
    }
}
