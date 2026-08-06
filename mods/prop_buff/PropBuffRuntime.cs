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

    public static void RestoreSerializedPropMovement(PlayerInputController inputController)
    {
        if (_configuration?.EnableMod.Value != true)
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

            var skills = player.EntitySkillsComponent;
            var registry = skills?._playerPropRegistry;
            var buffs = player.EntityBuffsComponent;
            if (registry is null
                || buffs is null
                || !registry.IsPlayerProp(player.InternalId)
                || !buffs.CanMove
                || HasNonPropInputBlocker(buffs))
            {
                return;
            }

            var multiplier = Mathf.Clamp(_configuration.MovementSpeedMultiplier.Value, 0.05f, 0.75f);
            var movement = inputController._moveDirection;
            if (movement.sqrMagnitude < 0.0001f)
            {
                movement = ReadPhysicalMovement();
            }

            movement = movement.sqrMagnitude > 1f
                ? movement.normalized * multiplier
                : movement * multiplier;

            // PropChange deliberately leaves BlockInputs set, so stock input serialization
            // neutralizes both movement axes. Restore only those axes; action and skill flags
            // remain stock-blocked while disguised.
            var accumulatedInput = inputController._accumulatedInput;
            accumulatedInput.XMoveDir = CompressMovementAxis(movement.x);
            accumulatedInput.YMoveDir = CompressMovementAxis(movement.y);
            inputController._accumulatedInput = accumulatedInput;
        }
        catch (Exception exception)
        {
            _logger?.LogError($"Prop movement serialization failed: {exception}");
        }
    }

    public static EntityBuffsComponent? BeginPropLocomotion(EntityLocomotionComponent locomotion)
    {
        if (_configuration?.EnableMod.Value != true)
        {
            return null;
        }

        try
        {
            var player = locomotion._spookedNetworkPlayer;
            var registry = locomotion._playerPropRegistry;
            var buffs = player?.EntityBuffsComponent;
            if (player is null
                || registry is null
                || buffs is null
                || !registry.IsPlayerProp(player.InternalId)
                || !buffs.CanMove
                || !buffs.BlockInputs
                || HasNonPropInputBlocker(buffs))
            {
                return null;
            }

            // CalculateLocomotion zeros Speed whenever BlockInputs is true. Suppress only the
            // persistent prop block for this synchronous calculation; input serialization still
            // blocks every action and supplies movement axes already scaled by the configured rate.
            buffs.BlockInputs = false;
            return buffs;
        }
        catch (Exception exception)
        {
            _logger?.LogError($"Prop locomotion setup failed: {exception}");
            return null;
        }
    }

    public static void EndPropLocomotion(EntityBuffsComponent? temporarilyUnblockedBuffs)
    {
        if (temporarilyUnblockedBuffs is not null)
        {
            temporarilyUnblockedBuffs.BlockInputs = true;
        }
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

    private static Vector2 ReadPhysicalMovement()
    {
        var keyboard = Keyboard.current;
        if (keyboard is null)
        {
            return Vector2.zero;
        }

        var horizontal = 0f;
        var vertical = 0f;
        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
        {
            horizontal -= 1f;
        }
        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
        {
            horizontal += 1f;
        }
        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
        {
            vertical -= 1f;
        }
        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
        {
            vertical += 1f;
        }

        var movement = new Vector2(horizontal, vertical);
        return movement.sqrMagnitude > 1f ? movement.normalized : movement;
    }

    private static byte CompressMovementAxis(float axis)
    {
        axis = Mathf.Clamp(axis, -1f, 1f);
        var encoded = (axis + 1f) * 100f;
        var compressed = Mathf.FloorToInt(encoded) + (axis < 0f ? 1 : 0);
        return (byte)Mathf.Clamp(compressed, 0, 200);
    }

    private static bool HasNonPropInputBlocker(EntityBuffsComponent buffs)
    {
        return IsNonPropInputBlocker(buffs.Buff1.BuffType)
            || IsNonPropInputBlocker(buffs.Buff2.BuffType)
            || IsNonPropInputBlocker(buffs.Buff3.BuffType)
            || IsNonPropInputBlocker(buffs.Buff4.BuffType);
    }

    private static bool IsNonPropInputBlocker(SpookedBuffType buffType)
    {
        // Mirrors EntityBuffsComponent.RefreshBlockInputs for the current interop build,
        // excluding only the persistent PropChange buff. The short transformation block and
        // every unrelated stun/interaction block continue to suppress movement normally.
        return buffType is SpookedBuffType.Trap
            or SpookedBuffType.BlockInputsForPortalSpawn
            or SpookedBuffType.BlockInputsForRipperBlink
            or SpookedBuffType.BlockInputsForDraculaBatChange
            or SpookedBuffType.PumpkinBombStun
            or SpookedBuffType.LockerStun
            or SpookedBuffType.Stun
            or SpookedBuffType.BananaFail
            or SpookedBuffType.GhostArmorStun
            or SpookedBuffType.BarrelExplosionStun
            or SpookedBuffType.BlockInputsForWand
            or SpookedBuffType.BlockInputsForLockerHide
            or SpookedBuffType.BlockInputsForLockerComeOut
            or SpookedBuffType.BlockInputsForPlayerDeath
            or SpookedBuffType.BlockInputsForScarecrowPumpkinBomb
            or SpookedBuffType.BlockInputsForButcherHook
            or SpookedBuffType.BlockInputsForButcherHookGrab
            or SpookedBuffType.BlockInputsForKick
            or SpookedBuffType.BlockInputsForShovelDig
            or SpookedBuffType.BlockInputsForArmorGetIn
            or SpookedBuffType.BlockInputsForArmorGetOut
            or SpookedBuffType.BlockInputsForMummySandTrap
            or SpookedBuffType.BlockInputsForPropChange
            or SpookedBuffType.BlockInputsForClownHammerHit
            or SpookedBuffType.BlockInputsForRipperFlames
            or SpookedBuffType.BlockInputsForMatchSelection
            or SpookedBuffType.BlockInputsForOpenPortal
            or SpookedBuffType.BlockInputsForBeingResurrected
            or SpookedBuffType.BlockInputsForEndGame
            or SpookedBuffType.BlockInputsForJugMaking
            or SpookedBuffType.BlockInputs
            or SpookedBuffType.BerekMatchStartStun
            or SpookedBuffType.BerekAfterCrownStun;
    }
}

[HarmonyPatch(typeof(EntityLocomotionComponent), "CalculateLocomotion")]
internal static class EntityLocomotionComponentCalculateLocomotionPatch
{
    private static void Prefix(EntityLocomotionComponent __instance, out EntityBuffsComponent? __state)
    {
        __state = PropBuffRuntime.BeginPropLocomotion(__instance);
    }

    private static Exception? Finalizer(EntityBuffsComponent? __state, Exception? __exception)
    {
        PropBuffRuntime.EndPropLocomotion(__state);
        return __exception;
    }
}

[HarmonyPatch(typeof(PlayerInputController), "ResolveLocalInputs")]
internal static class PlayerInputControllerResolveLocalInputsPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(PlayerInputController __instance)
    {
        PropBuffRuntime.TryCycleModel(__instance);
    }
}

[HarmonyPatch(typeof(PlayerInputController), "SaveLocalClientInputs")]
internal static class PlayerInputControllerSaveLocalClientInputsPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(PlayerInputController __instance)
    {
        PropBuffRuntime.RestoreSerializedPropMovement(__instance);
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
