using BepInEx.Logging;
using Gameplay.Skills;
using HarmonyLib;
using Types;
using UnityEngine;

namespace SneakOut.PumpkinRadiusIndicatorFix;

internal static class PumpkinRadiusIndicatorFixRuntime
{
    private static ManualLogSource? _logger;
    private static PumpkinRadiusIndicatorFixConfig? _configuration;
    private static Harmony? _harmony;
    private static bool _loggedFailure;

    public static void Initialize(ManualLogSource logger, PumpkinRadiusIndicatorFixConfig configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _harmony ??= new Harmony(PumpkinRadiusIndicatorFixPlugin.PluginGuid);
        _harmony.PatchAll();
    }

    [HarmonyPatch(typeof(PumpkinBomb), nameof(PumpkinBomb.Init))]
    private static class PumpkinBombInitPatch
    {
        [HarmonyPostfix]
        private static void Postfix(PumpkinBomb __instance)
        {
            AlignIndicatorFailOpen(__instance);
        }
    }

    [HarmonyPatch(typeof(PumpkinBomb), nameof(PumpkinBomb.ShowRange))]
    private static class PumpkinBombShowRangePatch
    {
        [HarmonyPrefix]
        private static void Prefix(PumpkinBomb __instance)
        {
            AlignIndicatorFailOpen(__instance);
        }
    }

    private static void AlignIndicatorFailOpen(PumpkinBomb pumpkin)
    {
        try
        {
            AlignIndicator(pumpkin);
        }
        catch (Exception exception)
        {
            if (!_loggedFailure)
            {
                _loggedFailure = true;
                _logger?.LogWarning($"Pumpkin indicator alignment failed; preserving the stock visual: {exception}");
            }
        }
    }

    private static void AlignIndicator(PumpkinBomb pumpkin)
    {
        if (_configuration is null || !_configuration.EnableMod.Value)
        {
            return;
        }

        var rangeObject = pumpkin._bombRange;
        var settings = pumpkin._spookedSettings?.Gameplay?.GetSkillSettings(SpookedSkillType.ScarecrowPumpkinBomb);
        var rangeTransform = rangeObject?.transform;
        var parent = rangeTransform?.parent;
        if (settings is null || rangeTransform is null || parent is null)
        {
            return;
        }

        var parentScale = parent.lossyScale;
        if (!PumpkinIndicatorScalePolicy.TryCalculate(
                settings.Range,
                new Scale3(parentScale.x, parentScale.y, parentScale.z),
                out var scale))
        {
            return;
        }

        var desiredScale = new Vector3(scale.X, scale.Y, scale.Z);
        if ((rangeTransform.localScale - desiredScale).sqrMagnitude <= 0.000001f)
        {
            return;
        }

        rangeTransform.localScale = desiredScale;
        if (_configuration.EnableLogging.Value)
        {
            _logger?.LogInfo($"Aligned pumpkin indicator to kill radius {settings.Range:0.###} m (local scale {desiredScale}).");
        }
    }
}
