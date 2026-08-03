using BepInEx.Logging;
using Gameplay.Interactions;
using Gameplay.Player.Components;
using Gameplay.Skills;
using HarmonyLib;
using UnityEngine;

namespace SneakOut.MagicWardrobeHookFix;

internal static class MagicWardrobeHookFixRuntime
{
    private static readonly MagicWardrobeHookPolicy HookPolicy = new();

    private static ManualLogSource? _logger;
    private static Harmony? _harmony;
    private static MagicWardrobeHookFixConfig? _configuration;

    public static void Initialize(ManualLogSource logger, MagicWardrobeHookFixConfig configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _harmony ??= new Harmony(MagicWardrobeHookFixPlugin.PluginGuid);
        _harmony.PatchAll();
    }

    public static void MarkHookedWardrobeUser(ButcherHook hook)
    {
        if (_configuration?.EnableMod.Value != true || hook.Pointer == IntPtr.Zero)
        {
            return;
        }

        var playerId = hook._hookedPlayerId;
        if (playerId < 0 || playerId == hook._butcherInternalId)
        {
            return;
        }

        if (!HookPolicy.RecordHook(playerId, Time.unscaledTime, 4f))
        {
            return;
        }

        // The hook owns movement from this point. The wardrobe interaction coroutine can
        // otherwise continue its cached lerp and move the player back after the pull.
        LogInfo($"Tracked Butcher hook interruption during magic-wardrobe entry for player {playerId}");
    }

    public static bool BeginWardrobeStep(
        EntityInteractiveComponent._InteractWithMagicWardrobe_d__76 coroutine)
    {
        if (_configuration?.EnableMod.Value != true
            || coroutine.Pointer == IntPtr.Zero
            || coroutine.wardrobe is null
            || coroutine.wardrobe.Pointer == IntPtr.Zero
            || coroutine.__4__this is null
            || coroutine.__4__this.Pointer == IntPtr.Zero)
        {
            return false;
        }

        var playerId = coroutine.__4__this.InternalId;
        if (!HookPolicy.BeginStep(
                playerId,
                coroutine.interactionType == Types.InteractionType.Hide,
                Time.unscaledTime))
        {
            return false;
        }

        LogInfo($"Cancelled interrupted magic-wardrobe entry movement for hooked player {playerId}");
        return true;
    }

    public static void EndWardrobeStep(
        EntityInteractiveComponent._InteractWithMagicWardrobe_d__76 coroutine,
        bool hasNextStep)
    {
        if (hasNextStep
            || coroutine.Pointer == IntPtr.Zero
            || coroutine.__4__this is null
            || coroutine.__4__this.Pointer == IntPtr.Zero)
        {
            return;
        }

        var playerId = coroutine.__4__this.InternalId;
        HookPolicy.End(playerId);
    }

    private static void LogInfo(string message)
    {
        if (_configuration?.EnableLogging.Value == true)
        {
            _logger?.LogInfo(message);
        }
    }
}
