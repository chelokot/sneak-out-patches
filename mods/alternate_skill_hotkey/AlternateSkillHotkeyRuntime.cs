using BepInEx.Logging;
using HarmonyLib;
using Gameplay.Player;
using Gameplay.Player.Components;
using UnityEngine.InputSystem;

namespace SneakOut.AlternateSkillHotkey;

internal static class AlternateSkillHotkeyRuntime
{
    private static ManualLogSource? _logger;
    private static AlternateSkillHotkeyConfig? _configuration;
    private static Harmony? _harmony;

    public static void Initialize(ManualLogSource logger, AlternateSkillHotkeyConfig configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _harmony ??= new Harmony(AlternateSkillHotkeyPlugin.PluginGuid);
        _harmony.PatchAll();
    }

    public static void TryActivate(PlayerInputController inputController)
    {
        if (_configuration?.EnableMod.Value != true
            || Keyboard.current?.leftAltKey.wasPressedThisFrame != true)
        {
            return;
        }

        try
        {
            var networkPlayer = inputController._spookedNetworkPlayer;
            if (networkPlayer is null
                || networkPlayer.Pointer == IntPtr.Zero
                || !networkPlayer.HasInputAuthority
                || networkPlayer.IsBot)
            {
                return;
            }

            var skills = inputController.GetComponent<EntitySkillsComponent>();
            if (skills is null || skills.Pointer == IntPtr.Zero || !skills.HasInputAuthority)
            {
                return;
            }

            skills.OnSecondSkillStartButton();
        }
        catch (Exception exception)
        {
            _logger?.LogError($"Alternate skill dispatch failed: {exception}");
        }
    }
}

[HarmonyPatch(typeof(PlayerInputController), "ResolveLocalInputs")]
internal static class PlayerInputControllerResolveLocalInputsPatch
{
    private static void Postfix(PlayerInputController __instance)
    {
        AlternateSkillHotkeyRuntime.TryActivate(__instance);
    }
}
