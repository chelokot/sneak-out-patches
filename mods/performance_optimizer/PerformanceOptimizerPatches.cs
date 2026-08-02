using Gameplay.Enviro;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Networking.Photon;
using UnityEngine;

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
