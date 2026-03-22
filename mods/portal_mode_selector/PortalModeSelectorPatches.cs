using HarmonyLib;
using Networking.PGOS;
using Types;
using UI.Views.Lobby;

namespace SneakOut.PortalModeSelector;

[HarmonyPatch(typeof(PortalPlayView), nameof(PortalPlayView.Open))]
internal static class PortalPlayViewOpenPatch
{
    private static void Postfix(PortalPlayView __instance)
    {
        try
        {
            PortalModeSelectorRuntime.TryEnsureModeRow(__instance);
        }
        catch (Exception exception)
        {
            PortalModeSelectorRuntime.LogError("Portal selector Open postfix failed", exception);
        }
    }
}

[HarmonyPatch(typeof(PortalPlayView), nameof(PortalPlayView.OnChangeRoleButton))]
internal static class PortalPlayViewOnChangeRoleButtonPatch
{
    private static bool Prefix(PortalPlayView __instance)
    {
        try
        {
            if (PortalModeSelectorRuntime.TryHandleModeToggle(__instance))
            {
                return false;
            }
        }
        catch (Exception exception)
        {
            PortalModeSelectorRuntime.LogError("Portal selector role-button prefix failed", exception);
        }

        return true;
    }
}

[HarmonyPatch(typeof(PortalPlayView), nameof(PortalPlayView.OnPlay))]
internal static class PortalPlayViewOnPlayPatch
{
    private static void Prefix(PortalPlayView __instance)
    {
        PortalModeSelectorRuntime.RememberPendingPlayView(__instance);
    }
}

[HarmonyPatch(typeof(Matchmaker), nameof(Matchmaker.PrepareMatch))]
internal static class MatchmakerPrepareMatchPatch
{
    private static void Prefix(ref GameModeType gameModeType)
    {
        PortalModeSelectorRuntime.TryOverrideMatchMode(ref gameModeType);
    }
}
