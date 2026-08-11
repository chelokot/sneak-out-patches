using System.Diagnostics;
using Gameplay.Camera;
using Gameplay.Enviro;
using Gameplay.Match.MatchState;
using Gameplay.Player.Components;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Networking.Photon;
using TMPro;
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

[HarmonyPatch(typeof(SceneCameraManager), "OnAwake")]
internal static class SceneCameraManagerTelemetryPatch
{
    [HarmonyPostfix]
    private static void Postfix(SceneCameraManager __instance)
    {
        PerformanceOptimizerRuntime.CaptureSceneCameraManager(__instance);
    }
}

[HarmonyPatch(typeof(SpookedNetworkPlayer), nameof(SpookedNetworkPlayer.Spawned))]
internal static class PerformancePlayerSpawnedPatch
{
    [HarmonyPostfix]
    private static void Postfix(SpookedNetworkPlayer __instance)
    {
        PerformanceOptimizerRuntime.CaptureLocalPlayer(__instance);
    }
}

[HarmonyPatch(typeof(SpookedNetworkPlayer), nameof(SpookedNetworkPlayer.Despawned))]
internal static class PerformancePlayerDespawnedPatch
{
    [HarmonyPrefix]
    private static void Prefix(SpookedNetworkPlayer __instance)
    {
        PerformanceOptimizerRuntime.ForgetLocalPlayer(__instance);
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

[HarmonyPatch(typeof(Room), nameof(Room.EnableOrDisableRoomLights))]
internal static class RoomLightTransitionTelemetryPatch
{
    [HarmonyPrefix]
    private static void Prefix(out long __state)
    {
        __state = PerformanceOptimizerRuntime.DetailedTelemetryEnabled
            ? Stopwatch.GetTimestamp()
            : 0L;
    }

    [HarmonyPostfix]
    private static void Postfix(Room __instance, bool enableLights, bool forTest, long __state)
    {
        if (__state == 0L)
        {
            return;
        }

        var elapsedMilliseconds = (Stopwatch.GetTimestamp() - __state) * 1000d / Stopwatch.Frequency;
        PerformanceOptimizerRuntime.ReportRoomLightTransition(
            __instance,
            enableLights,
            forTest,
            elapsedMilliseconds);
    }
}

[HarmonyPatch(typeof(RoomsLightsManager), "HandleLightsActivation")]
internal static class RoomsLightsManagerTelemetryPatch
{
    [HarmonyPrefix]
    private static void Prefix(out long __state)
    {
        __state = PerformanceOptimizerRuntime.DetailedTelemetryEnabled
            ? Stopwatch.GetTimestamp()
            : 0L;
    }

    [HarmonyPostfix]
    private static void Postfix(
        RoomsLightsManager __instance,
        Types.RoomType roomType,
        bool enableLights,
        bool forTest,
        long __state)
    {
        if (__state == 0L)
        {
            return;
        }

        var elapsedMilliseconds = (Stopwatch.GetTimestamp() - __state) * 1000d / Stopwatch.Frequency;
        PerformanceOptimizerRuntime.ReportRoomsLightsManagerTransition(
            __instance,
            roomType,
            enableLights,
            forTest,
            elapsedMilliseconds);
    }
}

[HarmonyPatch(typeof(MatchStateMachine), "OnMatchStateTypeChange")]
internal static class MatchStateTransitionTelemetryPatch
{
    [HarmonyPrefix]
    private static void Prefix(out long __state)
    {
        __state = PerformanceOptimizerRuntime.DetailedTelemetryEnabled
            ? Stopwatch.GetTimestamp()
            : 0L;
    }

    [HarmonyPostfix]
    private static void Postfix(MatchStateMachine __instance, long __state)
    {
        if (__state == 0L)
        {
            return;
        }

        var elapsedMilliseconds = (Stopwatch.GetTimestamp() - __state) * 1000d / Stopwatch.Frequency;
        PerformanceOptimizerRuntime.ReportMatchStateTransition(__instance, elapsedMilliseconds);
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

[HarmonyPatch(typeof(ResolutionSelector), "OnAwake")]
internal static class ResolutionSelectorAvailableResolutionsPatch
{
    [HarmonyPostfix]
    private static void Postfix(ResolutionSelector __instance)
    {
        var displayResolutions = Screen.resolutions;
        var dropdown = __instance._dropdown;
        if (displayResolutions is null || displayResolutions.Length == 0 || dropdown is null)
        {
            return;
        }

        var dimensions = new ResolutionDimensions[displayResolutions.Length];
        for (var index = 0; index < displayResolutions.Length; index++)
        {
            var resolution = displayResolutions[index];
            dimensions[index] = new ResolutionDimensions(resolution.width, resolution.height);
        }

        var uniqueIndices = ResolutionOptionPolicy.GetUniqueDimensionIndices(dimensions);
        var stockResolutions = __instance._availableResolutions;
        if (HasSameDimensions(stockResolutions, displayResolutions, uniqueIndices))
        {
            return;
        }

        var availableResolutions = new Il2CppStructArray<Resolution>(uniqueIndices.Count);
        var options = dropdown.options;
        options.Clear();
        for (var index = 0; index < uniqueIndices.Count; index++)
        {
            var resolution = displayResolutions[uniqueIndices[index]];
            availableResolutions[index] = resolution;
            options.Add(new TMP_Dropdown.OptionData($"{resolution.width}x{resolution.height}"));
        }

        __instance._availableResolutions = availableResolutions;
    }

    private static bool HasSameDimensions(
        Il2CppStructArray<Resolution>? stockResolutions,
        Il2CppStructArray<Resolution> displayResolutions,
        IReadOnlyList<int> uniqueIndices)
    {
        if (stockResolutions is null || stockResolutions.Length != uniqueIndices.Count)
        {
            return false;
        }

        for (var index = 0; index < uniqueIndices.Count; index++)
        {
            var stockResolution = stockResolutions[index];
            var displayResolution = displayResolutions[uniqueIndices[index]];
            if (stockResolution.width != displayResolution.width
                || stockResolution.height != displayResolution.height)
            {
                return false;
            }
        }

        return true;
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
