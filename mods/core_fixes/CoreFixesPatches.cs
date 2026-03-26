using Events;
using Collections;
using Gameplay.Match.MatchState;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppSystem;
using Kinguinverse.WebServiceProvider.Types_v2;
using Networking.PGOS;
using System.Reflection;
using System.Linq;
using Types;
using UI.Buttons;
using UI.Views;
using UnityEngine;

namespace SneakOut.CoreFixes;

[HarmonyPatch(typeof(PgosLobby), "OnJoinLobbyEvent")]
internal static class PgosLobbyOnJoinLobbyEventPatch
{
    private static bool Prefix(PgosLobby __instance, Il2CppSystem.Object? sender, Il2CppSystem.EventArgs? args)
    {
        return !CoreFixesRuntime.TryHandleJoinLobbyEvent(__instance, args);
    }
}

[HarmonyPatch(typeof(ShouldStartState), "GetRandomSeeker")]
internal static class ShouldStartStateGetRandomSeekerPatch
{
    private static bool Prefix(ShouldStartState __instance, ref int __result)
    {
        return !CoreFixesRuntime.TryHandleUniformHunterRandom(__instance, ref __result);
    }
}

[HarmonyPatch(typeof(BattlepassView), "OnOnWebplayerRefreshEvent")]
internal static class BattlepassViewOnOnWebplayerRefreshEventPatch
{
    private static bool Prefix()
    {
        if (!CoreFixesRuntime.ShouldDisableCrashyBattlepassRefreshHandler)
        {
            return true;
        }

        CoreFixesRuntime.LogBattlepassRefreshSuppressed();
        return false;
    }
}

[HarmonyPatch(typeof(BattlepassView), "SetEndTime")]
internal static class BattlepassViewSetEndTimePatch
{
    private static bool Prefix()
    {
        if (!CoreFixesRuntime.ShouldDisableCrashyBattlepassRefreshHandler)
        {
            return true;
        }

        CoreFixesRuntime.LogLoopSuppressed("BattlepassView.SetEndTime");
        return false;
    }
}

[HarmonyPatch(typeof(DailyQuestsView), "Refresh")]
internal static class DailyQuestsViewRefreshPatch
{
    private static bool Prefix()
    {
        if (!CoreFixesRuntime.ShouldDisableCrashyBattlepassRefreshHandler)
        {
            return true;
        }

        CoreFixesRuntime.LogLoopSuppressed("DailyQuestsView.Refresh");
        return false;
    }
}

[HarmonyPatch]
internal static class BackendStabilizerAvatarAndFrameViewSetProductsPatch
{
    private static MethodBase? TargetMethod()
    {
        return CoreFixesRuntime.FindBackendStabilizerMethod(
            "SneakOut.BackendStabilizer.AvatarAndFrameViewSetProductsPatch",
            "Postfix");
    }

    private static bool Prefix()
    {
        return !CoreFixesRuntime.ShouldSuppressBackendStabilizerAvatarViewPatches();
    }
}

[HarmonyPatch]
internal static class BackendStabilizerAvatarAndFrameViewOpenPatch
{
    private static MethodBase? TargetMethod()
    {
        return CoreFixesRuntime.FindBackendStabilizerMethod(
            "SneakOut.BackendStabilizer.AvatarAndFrameViewOpenPatch",
            "Postfix");
    }

    private static bool Prefix()
    {
        return !CoreFixesRuntime.ShouldSuppressBackendStabilizerAvatarViewPatches();
    }
}

[HarmonyPatch(typeof(AvatarAndFrameView), "SetProducts")]
internal static class AvatarAndFrameViewSetProductsPatch
{
    private static void Postfix(AvatarAndFrameView __instance)
    {
        if (!CoreFixesRuntime.Enabled || __instance._currentCategorySelected != 0)
        {
            return;
        }

        var avatars = __instance._spookedShopNewMeta?.Avatars;
        var buttons = __instance._avatarModyfiRecordButtons;
        if (avatars is null || buttons is null)
        {
            return;
        }

        var avatarSprites = __instance._spookedSettings?.Sprites?.Avatars;
        var pickAction = __instance._avatarModyficationPicked;
        if (avatarSprites is null || pickAction is null)
        {
            return;
        }

        var orderedAvatars = avatars
            .Where(product => product is not null && product.AvatarType != AvatarType.None)
            .OrderBy(product => (int)product.AvatarType)
            .ToArray();

        var selectedProductName = (__instance._currentSelectedProduct as Il2CppSystem.Object)?.ToString();

        for (var index = 0; index < ((Il2CppArrayBase<AvatarModyfiRecordButton>)(object)buttons).Count; index++)
        {
            var button = ((Il2CppArrayBase<AvatarModyfiRecordButton>)(object)buttons)[index];
            if (button is null)
            {
                continue;
            }

            if (index >= orderedAvatars.Length)
            {
                ((Component)button).gameObject.SetActive(false);
                continue;
            }

            var avatarType = orderedAvatars[index].AvatarType;
            var storedProduct = Il2CppSystem.Enum
                .ToObject(Il2CppType.Of<AvatarType>(), (byte)avatarType)
                .TryCast<Il2CppSystem.Enum>();
            var raritySprite = GetAvatarRaritySprite(__instance, avatarType);
            var avatarSprite = avatarSprites.GetSprite(avatarType);

            if (storedProduct is null)
            {
                continue;
            }

            button.Init(avatarSprite, pickAction, storedProduct, raritySprite, __instance._gameTranslator);
            button.Unblock();
            button.SetEquipped(string.Equals(selectedProductName, avatarType.ToString(), System.StringComparison.OrdinalIgnoreCase));
            ((Component)button).gameObject.SetActive(true);
        }
    }

    private static Sprite GetAvatarRaritySprite(AvatarAndFrameView view, AvatarType avatarType)
    {
        return InventoryExtensions.GetAvatarRarity(avatarType) switch
        {
            1 => view._tierOneRarity,
            2 => view._tierTwoRarity,
            3 => view._tierThreeRarity,
            4 => view._tierFourRarity,
            5 => view._tierFiveRarity,
            _ => view._tierOneRarity
        };
    }
}
