using BepInEx.Logging;
using Gameplay.Match.MatchState;
using HarmonyLib;
using Types;

namespace SneakOut.UniformSeekerRandom;

internal static class UniformSeekerRandomRuntime
{
    private static ManualLogSource? _logger;
    private static Harmony? _harmony;
    private static UniformSeekerRandomConfig? _configuration;

    public static void Initialize(ManualLogSource logger, UniformSeekerRandomConfig configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _harmony ??= new Harmony(UniformSeekerRandomPlugin.PluginGuid);
        _harmony.PatchAll();
    }

    public static bool TryHandleUniformHunterRandom(ShouldStartState shouldStartState, ref int result)
    {
        if (_configuration is null || !_configuration.EnableMod.Value)
        {
            return false;
        }

        if (shouldStartState._gameState.GameMode == GameModeType.Berek)
        {
            return false;
        }

        result = shouldStartState.RandomizeSeeker();

        if (_configuration.EnableLogging.Value)
        {
            _logger?.LogInfo($"Uniform seeker random override selected seeker index {result}");
        }

        return true;
    }
}
