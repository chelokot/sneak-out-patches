using BepInEx.Logging;
using Fusion;
using Gameplay.Match.MatchState;
using HarmonyLib;

namespace SneakOut.StartDelayReducer;

internal static class StartDelayReducerRuntime
{
    private static ManualLogSource? _logger;
    private static Harmony? _harmony;
    private static StartDelayReducerConfig? _configuration;

    public static void Initialize(ManualLogSource logger, StartDelayReducerConfig configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _harmony ??= new Harmony(StartDelayReducerPlugin.PluginGuid);
        _harmony.PatchAll();
    }

    public static void ReduceBeforeStartDelay(MatchStateMachine stateMachine, ref int stateEndTick)
    {
        ReduceStateDelay("BeforeStart", stateMachine, _configuration!.BeforeStartSeconds.Value, ref stateEndTick);
    }

    public static void ReduceCountingToStartDelay(MatchStateMachine stateMachine, ref int stateEndTick)
    {
        ReduceStateDelay("CountingToStart", stateMachine, _configuration!.CountingToStartSeconds.Value, ref stateEndTick);
    }

    private static void ReduceStateDelay(string stateName, MatchStateMachine stateMachine, float configuredSeconds, ref int stateEndTick)
    {
        if (_configuration is null || !_configuration.EnableMod.Value)
        {
            return;
        }

        if (!stateMachine.HasStateAuthority)
        {
            return;
        }

        var runner = stateMachine.Runner;
        if (runner is null)
        {
            return;
        }

        var tickRate = runner.TickRate;
        if (tickRate <= 0)
        {
            return;
        }

        var currentTick = runner.Tick.Raw;
        var targetDurationTicks = Math.Max(1, (int)Math.Ceiling(configuredSeconds * tickRate));
        var targetEndTick = currentTick + targetDurationTicks;
        if (targetEndTick >= stateEndTick)
        {
            return;
        }

        var originalEndTick = stateEndTick;
        stateEndTick = targetEndTick;

        if (_configuration.EnableLogging.Value)
        {
            _logger?.LogInfo(
                $"{stateName}: tickRate={tickRate}, currentTick={currentTick}, targetDurationTicks={targetDurationTicks}, originalEndTick={originalEndTick}, patchedEndTick={stateEndTick}");
        }
    }
}
