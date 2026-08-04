using BepInEx.Logging;
using Events;
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
    private static readonly Dictionary<IntPtr, ExplosionIndicators> ExplosionIndicatorsByPumpkin = new();
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
            PrepareIndicatorsFailOpen(__instance);
        }
    }

    [HarmonyPatch(typeof(PumpkinBomb), nameof(PumpkinBomb.ShowRange))]
    private static class PumpkinBombShowRangePatch
    {
        [HarmonyPrefix]
        private static void Prefix(PumpkinBomb __instance)
        {
            PrepareIndicatorsFailOpen(__instance);
        }

        [HarmonyPostfix]
        private static void Postfix(PumpkinBomb __instance, bool isSeeker)
        {
            try
            {
                if (_configuration?.EnableMod.Value == true && __instance._bombRange is not null)
                {
                    __instance._bombRange.SetActive(isSeeker && IsOwnedByLocalPlayer(__instance));
                }
            }
            catch (Exception exception)
            {
                LogFailureOnce("Pumpkin owner-only trigger indicator failed", exception);
            }
        }
    }

    [HarmonyPatch(typeof(PumpkinBomb), "OnPumpkinBombExplodingEvent")]
    private static class PumpkinBombExplodingEventPatch
    {
        [HarmonyPostfix]
        private static void Postfix(PumpkinBomb __instance, Il2CppSystem.EventArgs arg)
        {
            if (arg is not PumpkinBombExplodingEvent explodingEvent
                || explodingEvent.RemoveBecauseSeekerSpawnerNewOne
                || explodingEvent.PumpkinBombId != __instance.InternalId
                || explodingEvent.PumpkinBombIndex != __instance.Index)
            {
                return;
            }

            ShowExplosionIndicatorsFailOpen(__instance);
        }
    }

    private static void PrepareIndicatorsFailOpen(PumpkinBomb pumpkin)
    {
        try
        {
            PrepareIndicators(pumpkin);
        }
        catch (Exception exception)
        {
            if (!_loggedFailure)
            {
                _loggedFailure = true;
                _logger?.LogWarning($"Pumpkin indicator preparation failed; preserving the stock visual: {exception}");
            }
        }
    }

    private static void ShowExplosionIndicatorsFailOpen(PumpkinBomb pumpkin)
    {
        try
        {
            PrepareIndicators(pumpkin);
            if (pumpkin.Pointer == IntPtr.Zero
                || !ExplosionIndicatorsByPumpkin.TryGetValue(pumpkin.Pointer, out var indicators)
                || !indicators.IsAlive)
            {
                return;
            }

            RestartEffect(indicators.Kill);
            RestartEffect(indicators.Stun);
            DetachAndExpire(indicators.Kill);
            DetachAndExpire(indicators.Stun);
        }
        catch (Exception exception)
        {
            if (!_loggedFailure)
            {
                _loggedFailure = true;
                _logger?.LogWarning($"Pumpkin explosion indicators failed; preserving the stock explosion: {exception}");
            }
        }
    }

    private static void PrepareIndicators(PumpkinBomb pumpkin)
    {
        if (_configuration is null || !_configuration.EnableMod.Value)
        {
            return;
        }

        var rangeObject = pumpkin._bombRange;
        var gameplay = pumpkin._spookedSettings?.Gameplay;
        var settings = gameplay?.GetSkillSettings(SpookedSkillType.ScarecrowPumpkinBomb);
        var rangeTransform = rangeObject?.transform;
        var parent = rangeTransform?.parent;
        if (settings is null || gameplay is null || rangeObject is null || rangeTransform is null || parent is null
            || !PumpkinIndicatorScalePolicy.TryResolveRadii(
                settings.Range,
                gameplay.ScarecrowBombStunRange,
                out var radii))
        {
            return;
        }

        AlignScale(rangeTransform, parent, radii.Trigger);

        if (pumpkin.Pointer == IntPtr.Zero)
        {
            return;
        }

        var created = false;
        if (!ExplosionIndicatorsByPumpkin.TryGetValue(pumpkin.Pointer, out var indicators) || !indicators.IsAlive)
        {
            var kill = CloneRangeEffect(rangeObject, parent, "PumpkinKillRadius");
            var stun = CloneRangeEffect(rangeObject, parent, "PumpkinStunRadius20Percent");
            SetOpacity(stun, PumpkinIndicatorScalePolicy.StunIndicatorOpacity);
            indicators = new ExplosionIndicators(kill, stun);
            ExplosionIndicatorsByPumpkin[pumpkin.Pointer] = indicators;
            created = true;
        }

        AlignScale(indicators.Kill.transform, parent, radii.Kill);
        AlignScale(indicators.Stun.transform, parent, radii.Stun);
        if (created)
        {
            indicators.Kill.SetActive(false);
            indicators.Stun.SetActive(false);
        }

        if (_configuration.EnableLogging.Value)
        {
            _logger?.LogInfo(
                $"Aligned pumpkin radii: trigger={radii.Trigger:0.###} m, kill={radii.Kill:0.###} m, "
                + $"stun={radii.Stun:0.###} m at {PumpkinIndicatorScalePolicy.StunIndicatorOpacity:P0} opacity.");
        }
    }

    private static GameObject CloneRangeEffect(GameObject source, Transform parent, string name)
    {
        var clone = UnityEngine.Object.Instantiate(source, parent, false);
        clone.name = name;
        clone.hideFlags = HideFlags.HideAndDontSave;
        clone.transform.localPosition = source.transform.localPosition;
        clone.transform.localRotation = source.transform.localRotation;
        clone.SetActive(false);
        foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
        {
            // The duplicate is a visual ruler only. Never duplicate PumpkinBomb helpers,
            // event listeners, or gameplay behaviours together with the stock range VFX.
            behaviour.enabled = false;
        }
        foreach (var collider in clone.GetComponentsInChildren<Collider>(true))
        {
            collider.enabled = false;
        }
        return clone;
    }

    private static void AlignScale(Transform target, Transform parent, float radius)
    {
        var parentScale = parent.lossyScale;
        if (!PumpkinIndicatorScalePolicy.TryCalculate(
                radius,
                new Scale3(parentScale.x, parentScale.y, parentScale.z),
                out var scale))
        {
            return;
        }

        target.localScale = new Vector3(scale.X, scale.Y, scale.Z);
    }

    private static void SetOpacity(GameObject effect, float opacity)
    {
        foreach (var renderer in effect.GetComponentsInChildren<Renderer>(true))
        {
            var materials = renderer.materials;
            for (var index = 0; index < materials.Length; index++)
            {
                var material = materials[index];
                if (material is null)
                {
                    continue;
                }

                if (material.HasProperty("_BaseColor"))
                {
                    var color = material.GetColor("_BaseColor");
                    color.a = opacity;
                    material.SetColor("_BaseColor", color);
                }
                if (material.HasProperty("_Color"))
                {
                    var color = material.GetColor("_Color");
                    color.a = opacity;
                    material.SetColor("_Color", color);
                }
            }
        }
    }

    private static void RestartEffect(GameObject effect)
    {
        effect.SetActive(false);
        foreach (var particleSystem in effect.GetComponentsInChildren<ParticleSystem>(true))
        {
            particleSystem.Clear(true);
        }
        effect.SetActive(true);
        foreach (var particleSystem in effect.GetComponentsInChildren<ParticleSystem>(true))
        {
            particleSystem.Play(true);
        }
    }

    private static void DetachAndExpire(GameObject effect)
    {
        effect.transform.SetParent(null, true);
        UnityEngine.Object.Destroy(effect, PumpkinIndicatorScalePolicy.ExplosionIndicatorDurationSeconds);
    }

    private static bool IsOwnedByLocalPlayer(PumpkinBomb pumpkin)
    {
        var player = pumpkin._networkPlayerRegistry?[pumpkin.InternalId];
        return player is not null && player.HasInputAuthority;
    }

    private static void LogFailureOnce(string message, Exception exception)
    {
        if (_loggedFailure)
        {
            return;
        }

        _loggedFailure = true;
        _logger?.LogWarning($"{message}; preserving the stock visual for this call: {exception}");
    }

    private sealed class ExplosionIndicators
    {
        public ExplosionIndicators(GameObject kill, GameObject stun)
        {
            Kill = kill;
            Stun = stun;
        }

        public GameObject Kill { get; }
        public GameObject Stun { get; }
        public bool IsAlive => Kill && Stun;
    }
}
