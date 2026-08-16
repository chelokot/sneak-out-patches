using BepInEx.Logging;
using Gameplay.Match.MatchState;
using HarmonyLib;
using System.Collections.Generic;
using Types;

namespace SneakOut.UniformSeekerRandom;

internal static class UniformSeekerRandomRuntime
{
    private static readonly System.Random Random = new();
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

        var candidateInternalIds = CollectSeekerCandidateInternalIds(shouldStartState);
        if (candidateInternalIds.Count == 0)
        {
            return false;
        }

        result = candidateInternalIds[Random.Next(candidateInternalIds.Count)];

        if (_configuration.EnableLogging.Value)
        {
            _logger?.LogInfo($"Uniform seeker random override selected seeker id {result} from {candidateInternalIds.Count} candidates");
        }

        return true;
    }

    private static List<int> CollectSeekerCandidateInternalIds(ShouldStartState shouldStartState)
    {
        var preferredCandidateInternalIds = new List<int>();
        var fallbackCandidateInternalIds = new List<int>();
        var networkPlayers = shouldStartState._networkPlayerRegistry._components;
        for (var playerIndex = 0; playerIndex < networkPlayers.Length; playerIndex++)
        {
            var networkPlayer = networkPlayers[playerIndex];
            if (networkPlayer is null)
            {
                continue;
            }

            if (!networkPlayer.IsBot)
            {
                fallbackCandidateInternalIds.Add(networkPlayer.InternalId);
            }

            if (networkPlayer.CanBeSeeker)
            {
                preferredCandidateInternalIds.Add(networkPlayer.InternalId);
            }
        }

        return preferredCandidateInternalIds.Count > 0
            ? preferredCandidateInternalIds
            : fallbackCandidateInternalIds;
    }
}
