using BepInEx.Logging;
using Gameplay.Match.MatchState;
using Gameplay.Player.Components;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using Types;

namespace SneakOut.UniformSeekerRandom;

internal static class UniformSeekerRandomRuntime
{
    private static readonly System.Random Random = new();
    private static readonly FieldInfo? NetworkPlayerRegistryComponentsField = AccessTools.Field(typeof(NetworkPlayerRegistry), "_components");
    private static readonly Type? ShouldStartStateClosureType = typeof(ShouldStartState).GetNestedType("<>c", BindingFlags.NonPublic);
    private static readonly FieldInfo? ShouldStartStateClosureInstanceField = ShouldStartStateClosureType?.GetField("<>9", BindingFlags.Public | BindingFlags.Static);
    private static readonly MethodInfo? ShouldStartStateGetRandomSeekerPredicateMethod = ShouldStartStateClosureType?.GetMethod("<GetRandomSeeker>b__9_0", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly Type? PortalModeSelectorRuntimeType = AccessTools.TypeByName("SneakOut.PortalModeSelector.PortalModeSelectorRuntime");
    private static readonly MethodInfo? PortalModeSelectorIsBerekRequestedMethod = PortalModeSelectorRuntimeType is null
        ? null
        : AccessTools.Method(PortalModeSelectorRuntimeType, "IsBerekRequested");
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

        if (IsBerekRequestedByPortalSelector())
        {
            if (_configuration.EnableLogging.Value)
            {
                _logger?.LogInfo("Uniform seeker random skipped because crown mode is requested by Portal Mode Selector");
            }

            return false;
        }

        var candidateInternalIds = CollectEligibleSeekerInternalIds(shouldStartState);
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

    private static List<int> CollectEligibleSeekerInternalIds(ShouldStartState shouldStartState)
    {
        var predicate = GetOriginalGetRandomSeekerPredicate();
        var candidateInternalIds = new List<int>();
        var networkPlayers = NetworkPlayerRegistryComponentsField?.GetValue(shouldStartState._networkPlayerRegistry) as SpookedNetworkPlayer[];
        if (networkPlayers is null)
        {
            return candidateInternalIds;
        }

        foreach (var networkPlayer in networkPlayers)
        {
            if (networkPlayer is null)
            {
                continue;
            }

            if (predicate is not null)
            {
                if (!predicate(networkPlayer))
                {
                    continue;
                }
            }
            else if (!networkPlayer.CanBeSeeker)
            {
                continue;
            }

            candidateInternalIds.Add(networkPlayer.InternalId);
        }

        return candidateInternalIds;
    }

    private static Func<SpookedNetworkPlayer, bool>? GetOriginalGetRandomSeekerPredicate()
    {
        if (ShouldStartStateClosureInstanceField is null || ShouldStartStateGetRandomSeekerPredicateMethod is null)
        {
            return null;
        }

        var closureInstance = ShouldStartStateClosureInstanceField.GetValue(null);
        if (closureInstance is null)
        {
            return null;
        }

        return networkPlayer => (bool)ShouldStartStateGetRandomSeekerPredicateMethod.Invoke(closureInstance, new object[] { networkPlayer })!;
    }

    private static bool IsBerekRequestedByPortalSelector()
    {
        if (PortalModeSelectorIsBerekRequestedMethod is null)
        {
            return false;
        }

        try
        {
            return PortalModeSelectorIsBerekRequestedMethod.Invoke(null, Array.Empty<object>()) is true;
        }
        catch (Exception exception)
        {
            if (_configuration?.EnableLogging.Value == true)
            {
                _logger?.LogWarning($"Uniform seeker random could not query Portal Mode Selector: {exception.GetType().Name}");
            }

            return false;
        }
    }
}
