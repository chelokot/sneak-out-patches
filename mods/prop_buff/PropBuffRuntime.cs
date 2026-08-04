using BepInEx.Logging;
using HarmonyLib;
using Gameplay.Player;
using Gameplay.Player.Components;
using Types;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SneakOut.PropBuff;

internal static class PropBuffRuntime
{
    private static readonly PlayerPropType[] PropTypes =
    {
        PlayerPropType.Chair,
        PlayerPropType.LibraryChair,
        PlayerPropType.Scroll1,
        PlayerPropType.Scroll2,
        PlayerPropType.Book1,
        PlayerPropType.PotCactus,
        PlayerPropType.Pot0,
        PlayerPropType.Pot1,
        PlayerPropType.Pot2,
        PlayerPropType.ToyTeddyBear,
        PlayerPropType.ToyDragon,
        PlayerPropType.ToyRubikCube,
        PlayerPropType.GreenBag,
        PlayerPropType.RedBag,
        PlayerPropType.BlueBag,
    };

    private static ManualLogSource? _logger;
    private static PropBuffConfig? _configuration;
    private static Harmony? _harmony;
    private static float _nextCycleAt;

    public static void Initialize(ManualLogSource logger, PropBuffConfig configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _harmony ??= new Harmony(PropBuffPlugin.PluginGuid);
        _harmony.PatchAll();
    }

    public static bool TryGetPropSpeedMultiplier(SpookedBuffType buffType, out float multiplier)
    {
        multiplier = 0f;
        if (_configuration?.EnableMod.Value != true || buffType != SpookedBuffType.PropChange)
        {
            return false;
        }

        multiplier = Mathf.Clamp(_configuration.MovementSpeedMultiplier.Value, 0.05f, 0.75f);
        return true;
    }

    public static void TryCycleModel(PlayerInputController inputController)
    {
        if (_configuration?.EnableMod.Value != true
            || !_configuration.EnableModelCycling.Value
            || Time.unscaledTime < _nextCycleAt)
        {
            return;
        }

        var scroll = Mouse.current?.scroll.ReadValue().y ?? 0f;
        if (Mathf.Abs(scroll) < 0.01f)
        {
            return;
        }

        try
        {
            var player = inputController._spookedNetworkPlayer;
            if (player is null || !player.HasInputAuthority || player.IsBot)
            {
                return;
            }

            var skills = inputController.GetComponent<EntitySkillsComponent>();
            var registry = skills?._playerPropRegistry;
            if (skills is null || registry is null || !registry.IsPlayerProp(player.InternalId))
            {
                return;
            }

            var current = registry[player.InternalId];
            if (current is null)
            {
                return;
            }

            var next = NextPropType(current._playerPropType, scroll > 0f ? 1 : -1);
            _nextCycleAt = Time.unscaledTime + 0.12f;

            // This is the stock input-authority -> all-clients RPC used by the skill itself.
            // ChangeToProp is intercepted below only when a prop already exists, making this a
            // visual reroll instead of restarting the ability or its cooldown.
            skills.RPC_VictimPropChange(next);
        }
        catch (Exception exception)
        {
            _logger?.LogError($"Prop model cycle failed: {exception}");
        }
    }

    public static bool TryReplaceActiveProp(EntitySkillsComponent skills, PlayerPropType nextType)
    {
        if (_configuration?.EnableMod.Value != true || nextType == PlayerPropType.None)
        {
            return false;
        }

        var activePropConfirmed = false;
        try
        {
            var player = skills._spookedNetworkPlayer;
            var registry = skills._playerPropRegistry;
            var pool = skills._propPool;
            if (player is null || registry is null || pool is null || !registry.IsPlayerProp(player.InternalId))
            {
                return false;
            }

            activePropConfirmed = true;
            var current = registry[player.InternalId];
            if (current is null || current._playerPropType == nextType || current.PropObject is null)
            {
                return current is not null && current._playerPropType == nextType;
            }

            var position = current.PropObject.transform.position;
            var rotation = current.PropObject.transform.rotation;
            var replacement = pool.GetInstance(player.InternalId, nextType);
            if (replacement?.PropObject is null)
            {
                // Keep the current disguise if this map's stock pool does not contain the
                // requested model. Acquiring the replacement before returning the current
                // instance makes a missing-model reroll failure-safe.
                return true;
            }

            replacement.PropObject.transform.SetPositionAndRotation(position, rotation);
            registry[player.InternalId] = replacement;
            pool.ReturnInstance(current);
            if (_configuration.EnableLogging.Value)
            {
                _logger?.LogInfo($"Prop model changed: player={player.InternalId}, model={replacement._playerPropType}");
            }

            return true;
        }
        catch (Exception exception)
        {
            _logger?.LogError($"Prop model replacement failed: {exception}");
            // If this was an active disguise reroll, do not fall through to stock ChangeToProp:
            // that path assumes an initial transform and can leak/overwrite the current prop.
            return activePropConfirmed;
        }
    }

    private static PlayerPropType NextPropType(PlayerPropType current, int direction)
    {
        var index = Array.IndexOf(PropTypes, current);
        if (index < 0)
        {
            index = 0;
        }

        return PropTypes[(index + direction + PropTypes.Length) % PropTypes.Length];
    }
}

[HarmonyPatch(typeof(SpookedBuffTypeExtension), nameof(SpookedBuffTypeExtension.GetSpeedMultiplier))]
internal static class SpookedBuffTypeExtensionGetSpeedMultiplierPatch
{
    private static bool Prefix(SpookedBuffType buffType, ref float __result)
    {
        if (!PropBuffRuntime.TryGetPropSpeedMultiplier(buffType, out var multiplier))
        {
            return true;
        }

        __result = multiplier;
        return false;
    }
}

[HarmonyPatch(typeof(PlayerInputController), "ResolveLocalInputs")]
internal static class PlayerInputControllerResolveLocalInputsPatch
{
    private static void Postfix(PlayerInputController __instance)
    {
        PropBuffRuntime.TryCycleModel(__instance);
    }
}

[HarmonyPatch(typeof(EntitySkillsComponent), nameof(EntitySkillsComponent.ChangeToProp))]
internal static class EntitySkillsComponentChangeToPropPatch
{
    private static bool Prefix(EntitySkillsComponent __instance, PlayerPropType playerPropTypeToChangeInto)
    {
        return !PropBuffRuntime.TryReplaceActiveProp(__instance, playerPropTypeToChangeInto);
    }
}
