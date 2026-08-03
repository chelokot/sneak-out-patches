using Gameplay.Enviro;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Networking.Photon;
using UI.Views;
using UI.VideoSettings;
using UnityEngine;
using SneakOutGame = Game.Game;

namespace SneakOut.PerformanceOptimizer;

[HarmonyPatch(typeof(PhotonPingServer), nameof(PhotonPingServer.SaveAndRefreshPing))]
internal static class PhotonPingServerRefreshPatch
{
    [HarmonyPrefix]
    private static void Prefix(PhotonPingServer __instance)
    {
        PerformanceOptimizerRuntime.CapturePhotonPingServer(__instance);
    }
}

[HarmonyPatch(typeof(Room), "OnAwake")]
internal static class RoomNullLightsPatch
{
    [HarmonyPrefix]
    private static void Prefix(Room __instance)
    {
        var lights = __instance.Lights;
        if (lights is null || lights.Length == 0)
        {
            return;
        }

        var validLightCount = 0;
        for (var index = 0; index < lights.Length; index++)
        {
            if (lights[index] is not null)
            {
                validLightCount++;
            }
        }

        if (validLightCount == lights.Length)
        {
            return;
        }

        var filteredLights = new Il2CppReferenceArray<Light>(validLightCount);
        var targetIndex = 0;
        for (var index = 0; index < lights.Length; index++)
        {
            var light = lights[index];
            if (light is not null)
            {
                filteredLights[targetIndex++] = light;
            }
        }

        __instance.Lights = filteredLights;
        PerformanceOptimizerRuntime.ReportSanitizedRoomLights(lights.Length - validLightCount);
    }
}

[HarmonyPatch(typeof(ResolutionSelector), "RefreshShownValue")]
internal static class ResolutionSelectorRefreshShownValuePatch
{
    [HarmonyPrefix]
    private static bool Prefix(ResolutionSelector __instance)
    {
        var availableResolutions = __instance._availableResolutions;
        var gameSettings = __instance._gameSettingsManager;
        var dropdown = __instance._dropdown;
        if (availableResolutions is null
            || availableResolutions.Length == 0
            || gameSettings is null
            || dropdown is null)
        {
            return true;
        }

        var currentResolution = gameSettings.CurrentResolution;
        var stockMatcher = new ResolutionSelector.__c__DisplayClass8_0
        {
            currentResolution = currentResolution,
        };
        for (var index = 0; index < availableResolutions.Length; index++)
        {
            if (stockMatcher._RefreshShownValue_b__0(availableResolutions[index]))
            {
                return true;
            }
        }

        var closestIndex = 0;
        var closestDistance = long.MaxValue;
        for (var index = 0; index < availableResolutions.Length; index++)
        {
            var candidate = availableResolutions[index];
            var widthDelta = (long)candidate.width - currentResolution.width;
            var heightDelta = (long)candidate.height - currentResolution.height;
            var distance = widthDelta * widthDelta + heightDelta * heightDelta;
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestIndex = index;
            }
        }

        dropdown.SetValueWithoutNotify(closestIndex);
        dropdown.RefreshShownValue();
        var closestResolution = availableResolutions[closestIndex];
        PerformanceOptimizerRuntime.ReportResolutionSelectorRecovery(
            currentResolution.width,
            currentResolution.height,
            closestResolution.width,
            closestResolution.height);
        return false;
    }
}

[HarmonyPatch(typeof(FinishBattlepassProgressView), "SetProgress")]
internal static class FinishBattlepassMissingPlayerPatch
{
    [HarmonyPrefix]
    private static bool Prefix(FinishBattlepassProgressView __instance)
    {
        var records = __instance._endMatchPlayerRecords?._matchPlayerResults;
        if (records is null)
        {
            PerformanceOptimizerRuntime.ReportMissingEndMatchRecord(SneakOutGame.InternalId, 0);
            return false;
        }

        var localInternalId = SneakOutGame.InternalId;
        for (var index = 0; index < records.Length; index++)
        {
            if (records[index].InternalId == localInternalId)
            {
                return true;
            }
        }

        // The stock method indexes EndMatchPlayerRecords without checking that the server sent
        // a record for the local player. During the observed disconnect it threw from the event
        // bus and stalled the end screen for several seconds. Skip only this optional battlepass
        // subview; the main results view and reconnect flow continue normally.
        PerformanceOptimizerRuntime.ReportMissingEndMatchRecord(localInternalId, records.Length);
        return false;
    }
}
