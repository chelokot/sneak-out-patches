using BepInEx.Logging;
using Gameplay.Player.Components;
using HarmonyLib;
using Kinguinverse.WebServiceProvider.Types_v2;
using Types;
using UnityEngine;
using UnityEngine.AI;

namespace SneakOut.RipperCornerBlinkFix;

internal static class RipperCornerBlinkFixRuntime
{
    private const float GroundProbeDistance = 10f;
    private const float PathOriginHeight = 0.5f;
    private const float NavMeshValidationRadius = 0.2f;
    private const string IntersectionLayerName = "Intersections";

    private static ManualLogSource? _logger;
    private static RipperCornerBlinkFixConfig? _configuration;
    private static Harmony? _harmony;
    private static bool _loggedFailure;

    public static void Initialize(ManualLogSource logger, RipperCornerBlinkFixConfig configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _harmony ??= new Harmony(RipperCornerBlinkFixPlugin.PluginGuid);
        _harmony.PatchAll();
    }

    [HarmonyPatch(typeof(EntitySkillsComponent), nameof(EntitySkillsComponent.OnRipperBlink))]
    private static class EntitySkillsComponentOnRipperBlinkPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(EntitySkillsComponent __instance)
        {
            try
            {
                return !TryBlinkThroughSharedCorner(__instance);
            }
            catch (Exception exception)
            {
                if (!_loggedFailure)
                {
                    _loggedFailure = true;
                    _logger?.LogWarning($"Shared-corner classification failed; using stock blink: {exception}");
                }

                return true;
            }
        }
    }

    private static bool TryBlinkThroughSharedCorner(EntitySkillsComponent skills)
    {
        if (_configuration is null || !_configuration.EnableMod.Value)
        {
            return false;
        }

        var player = skills._spookedNetworkPlayer;
        var transformComponent = player?.EntityTransformComponent;
        var activeSkills = skills._playersActiveSkills;
        var gameplaySettings = skills._spookedSettings?.Gameplay;
        if (player is null || transformComponent is null || activeSkills is null || gameplaySettings is null)
        {
            return false;
        }

        var baseSettings = gameplaySettings.GetSkillSettings(SpookedSkillType.RipperBlink);
        if (baseSettings is null)
        {
            return false;
        }

        var hasWallBlinkPerk = activeSkills.HaveSkillEquipped(
            player.InternalId,
            SkillType.ReaperHelloThere,
            player.CharacterType);
        if (!hasWallBlinkPerk)
        {
            return false;
        }

        var range = baseSettings.Range + activeSkills.GetPlayerSkillModifier(
            player.InternalId,
            SkillType.ReaperHelloThere,
            SkillModifierType.Range,
            0f);
        var forward = transformComponent.Forward;
        if (!float.IsFinite(range) || range <= 0f || forward.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        forward.Normalize();
        var mask = (int)skills._skillMaskForReaperBlink;

        var target = transformComponent.Position + forward * range;
        if (Physics.Raycast(
                target,
                Vector3.down,
                out var floorHit,
                GroundProbeDistance,
                mask,
                QueryTriggerInteraction.UseGlobal))
        {
            target = floorHit.point;
        }

        var origin = transformComponent.Position + Vector3.up * PathOriginHeight;
        var path = target - origin;
        var pathDistance = path.magnitude;
        if (pathDistance <= 0.001f)
        {
            return false;
        }

        var raycastHits = Physics.RaycastAll(
            origin,
            path / pathDistance,
            pathDistance,
            mask,
            QueryTriggerInteraction.UseGlobal);
        var solidHits = new List<PathBlocker>(raycastHits.Length);
        for (var index = 0; index < raycastHits.Length; index++)
        {
            var hit = raycastHits[index];
            var collider = hit.collider;
            if (collider is null || collider.isTrigger)
            {
                continue;
            }

            solidHits.Add(new PathBlocker(hit.distance, collider.gameObject.layer));
        }

        var intersectionLayer = LayerMask.NameToLayer(IntersectionLayerName);
        if (!SharedCornerPolicy.ShouldBypass(
                hasWallBlinkPerk,
                pathDistance,
                intersectionLayer,
                solidHits)
            || !NavMesh.SamplePosition(target, out _, NavMeshValidationRadius, NavMesh.AllAreas))
        {
            return false;
        }

        skills.RPC_RipperBlink(target);
        if (_configuration.EnableLogging.Value)
        {
            _logger?.LogInfo($"Accepted wall-blink perk through an Intersections-layer room junction to {target}.");
        }

        return true;
    }
}
