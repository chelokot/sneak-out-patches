using BepInEx.Logging;
using Fusion;
using Gameplay.Interactions;
using Gameplay.Player.Components;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Plugins.Easy_performant_outline.Scripts;
using UI.Interactions;
using UnityEngine;

namespace SneakOut.CommunityDiscord;

internal static class CommunityDiscordRuntime
{
    private const string StockStatueName = "DiscordStatue_a_prefab";
    private const string PortalStatueName = "CommunityDiscordStatue";
    private const string RecolorMaterialName = "DiscordStatue_a_Anim_mat";
    private const uint PortalInteractionIdRaw = 0x43444F52;
    private const float PortalSideRightOffset = -0.55f;
    private const float PortalSideTopOffset = -1.05f;
    private const float MinimumPortalInteractionDistance = 2.5f;
    private const float PortalSelectionTolerance = 0.35f;

    private static readonly Color PortalRed = new(0.82f, 0.045f, 0.025f, 1f);
    private static readonly Color PortalRedEmission = new(0.18f, 0.008f, 0.004f, 1f);
    private static readonly NetworkId PortalInteractionId = new(PortalInteractionIdRaw);

    private static ManualLogSource? _logger;
    private static CommunityDiscordConfig? _configuration;
    private static Harmony? _harmony;
    private static IntPtr _portalStatuePointer;
    private static GameObject? _portalStatueRoot;
    private static SocialsStatue? _portalStatue;
    private static bool _loggedDiscoveryFallback;
    private static bool _loggedForcedSelection;
    private static IntPtr _forcedInteractiveComponentPointer;
    private static ActionCircle? _actionCircle;
    private static bool _actionCircleHasPortalTarget;

    public static void Initialize(ManualLogSource logger, CommunityDiscordConfig configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _harmony ??= new Harmony(CommunityDiscordPlugin.PluginGuid);
        _harmony.PatchAll();
    }

    public static void EnsurePortalSideStatue(Interactable interactable)
    {
        var stockStatue = interactable?.TryCast<SocialsStatue>();
        if (!IsEnabled()
            || stockStatue is null
            || !IsStockDiscordStatue(stockStatue))
        {
            return;
        }

        if (_portalStatueRoot is not null && _portalStatueRoot)
        {
            return;
        }

        ResetPortalStatue();
        InteractableObjectsRegistry? registry = null;
        GameObject? cloneRoot = null;
        IntPtr cloneStatuePointer = IntPtr.Zero;
        try
        {
            registry = stockStatue.InteractableObjectsRegistry;
            if (registry is null)
            {
                Warn("The stock Discord statue has no interaction registry; the separate entry was not created");
                return;
            }
            if (!PortalInteractionId.IsValid || PortalInteractionId.IsReserved)
            {
                Warn($"Community interaction ID {PortalInteractionIdRaw} is not usable");
                return;
            }
            if (registry._interactables.ContainsKey(PortalInteractionId))
            {
                Warn($"Community interaction ID {PortalInteractionIdRaw} is already registered");
                return;
            }

            cloneRoot = UnityEngine.Object.Instantiate(stockStatue.gameObject);
            cloneRoot.name = PortalStatueName;
            cloneRoot.transform.position = ToVector(CommunityDiscordPolicy.MoveOnFloor(
                ToPoint(stockStatue.transform.position),
                PortalSideRightOffset,
                PortalSideTopOffset));
            cloneRoot.transform.rotation = stockStatue.transform.rotation;
            cloneRoot.transform.localScale = stockStatue.transform.localScale;

            var portalStatue = cloneRoot.GetComponent<SocialsStatue>();
            var portalCollider = cloneRoot.GetComponent<BoxCollider>();
            var portalOutline = cloneRoot.GetComponent<Outlinable>();
            if (portalStatue is null || portalCollider is null || portalOutline is null)
            {
                UnityEngine.Object.Destroy(cloneRoot);
                Warn("The cloned Discord statue is missing its interaction, collider, or outline component");
                return;
            }

            portalStatue.Transform = cloneRoot.transform;
            portalStatue.Collider = portalCollider;
            portalStatue.Settings = stockStatue.Settings;
            portalStatue.InteractableObjectsRegistry = registry;
            portalStatue._outlinable = portalOutline;
            portalStatue._redirectURL = _configuration!.InviteUrl.Value.Trim();
            portalStatue._canBeUsed = true;
            portalCollider.enabled = true;
            portalOutline.AddAllChildRenderersToRenderingList(RenderersAddingMode.All);
            portalStatue._darkOutlinable?.AddAllChildRenderersToRenderingList(RenderersAddingMode.All);
            ApplyRedMaterials(cloneRoot);

            cloneStatuePointer = portalStatue.Pointer;
            _portalStatuePointer = cloneStatuePointer;
            _portalStatueRoot = cloneRoot;
            _portalStatue = portalStatue;
            var reportedInteractionId = portalStatue.NetworkObjectId;
            if (reportedInteractionId.Raw != PortalInteractionId.Raw)
            {
                throw new InvalidOperationException(
                    $"the cloned statue reported interaction ID {reportedInteractionId.Raw} instead of {PortalInteractionId.Raw}");
            }
            registry.Register(PortalInteractionId, portalStatue);
            if (!registry._interactables.ContainsKey(PortalInteractionId)
                || registry[PortalInteractionId] is not { } registeredPortalStatue
                || registeredPortalStatue.Pointer != cloneStatuePointer)
            {
                throw new InvalidOperationException("the interaction registry rejected the community statue");
            }

            _logger?.LogInfo(
                $"Added separate red Discord statue toward the portal at {cloneRoot.transform.position} "
                + $"with interaction ID {PortalInteractionIdRaw}");
        }
        catch (Exception exception)
        {
            try
            {
                if (registry is not null
                    && cloneStatuePointer != IntPtr.Zero
                    && registry._interactables.ContainsKey(PortalInteractionId)
                    && registry[PortalInteractionId] is { } registered
                    && registered.Pointer == cloneStatuePointer)
                {
                    registry.Unregister(PortalInteractionId);
                }
            }
            catch (Exception cleanupException)
            {
                Warn($"Could not clean up the failed community interaction: {cleanupException.Message}");
            }
            ResetPortalStatue();
            if (cloneRoot is not null && cloneRoot)
            {
                UnityEngine.Object.Destroy(cloneRoot);
            }
            Warn($"Could not create the separate Discord statue: {exception.Message}");
        }
    }

    public static void EnsurePortalStatueIsDiscoverable(EntityInteractiveComponent interactiveComponent)
    {
        if (!IsEnabled()
            || interactiveComponent is null
            || interactiveComponent.Pointer == IntPtr.Zero
            || _portalStatue is null
            || _portalStatue.Pointer != _portalStatuePointer
            || _portalStatueRoot is null
            || !_portalStatueRoot
            || !_portalStatue.enabled
            || !_portalStatue.gameObject.activeInHierarchy)
        {
            return;
        }

        try
        {
            var interactables = interactiveComponent._interactablesAround;
            if (interactables is null || ContainsPortalStatue(interactables))
            {
                return;
            }

            var playerPosition = interactiveComponent.PlayerPosition;
            if (!TryGetPortalDistance(playerPosition, out var portalDistance)
                || !CommunityDiscordPolicy.ShouldOfferInteraction(
                    portalDistance,
                    GetNativeInteractionDistance(),
                    MinimumPortalInteractionDistance))
            {
                return;
            }

            var insertIndex = interactables.Count;
            for (var index = 0; index < interactables.Count; index++)
            {
                var candidate = interactables[index];
                if (candidate is null || candidate.Pointer == IntPtr.Zero)
                {
                    continue;
                }

                var candidateDistance = (candidate.GetClosestPoint(playerPosition) - playerPosition).magnitude;
                if (portalDistance < candidateDistance)
                {
                    insertIndex = index;
                    break;
                }
            }

            interactables.Insert(insertIndex, _portalStatue);
            if (!_loggedDiscoveryFallback)
            {
                _loggedDiscoveryFallback = true;
                _logger?.LogInfo("Added the separate Discord statue to the player's interaction discovery list");
            }
        }
        catch (Exception exception)
        {
            if (!_loggedDiscoveryFallback)
            {
                _loggedDiscoveryFallback = true;
                Warn($"Could not add the separate Discord statue to interaction discovery: {exception.Message}");
            }
        }
    }

    public static bool TryGetPortalInteractionId(Interactable interactable, out NetworkId interactionId)
    {
        interactionId = default;
        if (!IsEnabled()
            || interactable is null
            || interactable.Pointer == IntPtr.Zero
            || interactable.Pointer != _portalStatuePointer
            || _portalStatueRoot is null
            || !_portalStatueRoot)
        {
            return false;
        }

        interactionId = PortalInteractionId;
        return true;
    }

    public static bool TryGetSelectedPortalStatue(out Interactable? portalStatue)
    {
        portalStatue = null;
        if (!IsEnabled()
            || _forcedInteractiveComponentPointer == IntPtr.Zero
            || _portalStatue is null
            || _portalStatue.Pointer != _portalStatuePointer
            || _portalStatueRoot is null
            || !_portalStatueRoot
            || !_portalStatue.enabled
            || !_portalStatue.gameObject.activeInHierarchy)
        {
            return false;
        }

        portalStatue = _portalStatue;
        return true;
    }

    public static void AnchorPortalActionCircleView(ActionCircle actionCircle)
    {
        if (!IsEnabled()
            || actionCircle is null
            || actionCircle.Pointer == IntPtr.Zero
            || _forcedInteractiveComponentPointer == IntPtr.Zero
            || _portalStatue is null
            || _portalStatue.Pointer != _portalStatuePointer
            || _portalStatueRoot is null
            || !_portalStatueRoot
            || actionCircle._interactable is not { } target
            || target.Pointer != _portalStatuePointer
            || actionCircle._activeView is not { } activeView
            || !activeView.Active
            || activeView.TryCast<InteractionView>() is not { } interactionView)
        {
            return;
        }

        var expectedPosition = _portalStatue.Position + actionCircle._offset;
        if ((interactionView.Transform.position - expectedPosition).sqrMagnitude > 0.0001f)
        {
            interactionView.Transform.position = expectedPosition;
        }
    }

    public static void ResolvePortalStatueSelection(EntityInteractiveComponent interactiveComponent)
    {
        if (interactiveComponent is null
            || interactiveComponent.Pointer == IntPtr.Zero
            || _portalStatue is null
            || _portalStatue.Pointer != _portalStatuePointer
            || !interactiveComponent.HasInputAuthority)
        {
            return;
        }

        try
        {
            var internalId = interactiveComponent.InternalId;
            var playerPosition = interactiveComponent.PlayerPosition;
            var shouldSelectPortal = ShouldSelectPortalStatue(
                interactiveComponent._interactablesAround,
                internalId,
                playerPosition);
            var selectedId = interactiveComponent.SelectedInteractiveNetworkId;

            if (!shouldSelectPortal)
            {
                ClearForcedSelection(interactiveComponent, selectedId);
                return;
            }
            if (selectedId.IsValid && selectedId.Raw != PortalInteractionId.Raw)
            {
                ClearSelectedOutline(interactiveComponent, selectedId);
            }

            _portalStatue.Outline(true);
            interactiveComponent.SelectedInteractiveNetworkId = PortalInteractionId;
            _forcedInteractiveComponentPointer = interactiveComponent.Pointer;
            if (!_actionCircleHasPortalTarget)
            {
                _actionCircleHasPortalTarget = RefreshActionCircleTarget();
            }
            if (!_loggedForcedSelection)
            {
                _loggedForcedSelection = true;
                _logger?.LogInfo("Selected and highlighted the separate Discord statue with its independent interaction ID");
            }
        }
        catch (Exception exception)
        {
            if (!_loggedForcedSelection)
            {
                _loggedForcedSelection = true;
                Warn($"Could not select the separate Discord statue: {exception.Message}");
            }
        }
    }

    private static bool ShouldSelectPortalStatue(
        Il2CppSystem.Collections.Generic.List<Interactable>? interactables,
        int internalId,
        Vector3 playerPosition)
    {
        if (interactables is null
            || !ContainsPortalStatue(interactables)
            || _portalStatue is null
            || !_portalStatue.IsAvailableInteraction(internalId)
            || !TryGetPortalDistance(playerPosition, out var portalDistance))
        {
            return false;
        }

        var nearestOtherDistance = float.PositiveInfinity;
        for (var index = 0; index < interactables.Count; index++)
        {
            var candidate = interactables[index];
            if (candidate is null
                || candidate.Pointer == IntPtr.Zero
                || candidate.Pointer == _portalStatuePointer
                || !candidate.IsAvailableInteraction(internalId))
            {
                continue;
            }

            var candidateDistance = (candidate.GetClosestPoint(playerPosition) - playerPosition).magnitude;
            if (candidateDistance < nearestOtherDistance)
            {
                nearestOtherDistance = candidateDistance;
            }
        }

        return CommunityDiscordPolicy.ShouldPreferPortal(
            portalDistance,
            nearestOtherDistance,
            GetNativeInteractionDistance(),
            MinimumPortalInteractionDistance,
            PortalSelectionTolerance);
    }

    private static bool TryGetPortalDistance(Vector3 playerPosition, out float distance)
    {
        distance = 0f;
        if (_portalStatue is null || _portalStatue.Pointer != _portalStatuePointer)
        {
            return false;
        }

        distance = (_portalStatue.GetClosestPoint(playerPosition) - playerPosition).magnitude;
        return float.IsFinite(distance);
    }

    private static float GetNativeInteractionDistance()
    {
        try
        {
            return _portalStatue?.Settings?.Gameplay?.LocalDistanceToInteract ?? 0f;
        }
        catch
        {
            return 0f;
        }
    }

    private static void ClearSelectedOutline(
        EntityInteractiveComponent interactiveComponent,
        NetworkId selectedId)
    {
        try
        {
            var registry = interactiveComponent._interactableObjectsRegistry;
            if (registry is not null
                && registry._interactables.ContainsKey(selectedId)
                && registry[selectedId] is { } selected)
            {
                selected.Outline(false);
            }
        }
        catch
        {
            // The native resolver will also clear the old outline on its next pass.
        }
    }

    private static void ClearForcedSelection(
        EntityInteractiveComponent interactiveComponent,
        NetworkId selectedId)
    {
        if (_forcedInteractiveComponentPointer != interactiveComponent.Pointer)
        {
            return;
        }

        _portalStatue?.Outline(false);
        if (selectedId.Raw == PortalInteractionId.Raw)
        {
            interactiveComponent.SelectedInteractiveNetworkId = default;
        }
        _forcedInteractiveComponentPointer = IntPtr.Zero;
        if (_actionCircleHasPortalTarget)
        {
            RefreshActionCircleTarget();
            _actionCircleHasPortalTarget = false;
        }
    }

    private static bool RefreshActionCircleTarget()
    {
        try
        {
            if (_actionCircle is null
                || _actionCircle.Pointer == IntPtr.Zero
                || !_actionCircle
                || !_actionCircle.gameObject.activeInHierarchy)
            {
                _actionCircle = UnityEngine.Object.FindObjectOfType<ActionCircle>();
            }

            if (_actionCircle is null
                || _actionCircle.Pointer == IntPtr.Zero
                || !_actionCircle
                || !_actionCircle.gameObject.activeInHierarchy)
            {
                return false;
            }

            _actionCircle.SetTarget();
            return true;
        }
        catch (Exception exception)
        {
            Warn($"Could not refresh the stock interaction key prompt: {exception.Message}");
            return false;
        }
    }

    private static void ApplyRedMaterials(GameObject root)
    {
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            foreach (var material in renderer.materials)
            {
                if (!material.name.StartsWith(RecolorMaterialName, StringComparison.Ordinal))
                {
                    continue;
                }
                if (material.HasProperty("_BaseColor"))
                {
                    material.SetColor("_BaseColor", PortalRed);
                }
                if (material.HasProperty("_Color"))
                {
                    material.SetColor("_Color", PortalRed);
                }
                if (material.HasProperty("_EmissionColor"))
                {
                    material.SetColor("_EmissionColor", PortalRedEmission);
                    material.EnableKeyword("_EMISSION");
                }
            }
        }
    }

    private static bool ContainsPortalStatue(Il2CppSystem.Collections.Generic.List<Interactable> interactables)
    {
        for (var index = 0; index < interactables.Count; index++)
        {
            var candidate = interactables[index];
            if (candidate is not null && candidate.Pointer == _portalStatuePointer)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsStockDiscordStatue(SocialsStatue statue)
    {
        return statue.Pointer != IntPtr.Zero
            && statue.gameObject is { } gameObject
            && string.Equals(gameObject.name, StockStatueName, StringComparison.Ordinal)
            && gameObject.scene.IsValid()
            && string.Equals(gameObject.scene.name, "Lobby", StringComparison.Ordinal);
    }

    private static StatuePoint ToPoint(Vector3 value)
    {
        return new StatuePoint(value.x, value.y, value.z);
    }

    private static Vector3 ToVector(StatuePoint value)
    {
        return new Vector3(value.X, value.Y, value.Z);
    }

    private static bool IsEnabled()
    {
        return _configuration?.EnableMod.Value == true
            && !string.IsNullOrWhiteSpace(_configuration.InviteUrl.Value);
    }

    private static void ResetPortalStatue()
    {
        _portalStatuePointer = IntPtr.Zero;
        _portalStatueRoot = null;
        _portalStatue = null;
        _loggedDiscoveryFallback = false;
        _loggedForcedSelection = false;
        _forcedInteractiveComponentPointer = IntPtr.Zero;
        _actionCircle = null;
        _actionCircleHasPortalTarget = false;
    }

    private static void Warn(string message)
    {
        _logger?.LogWarning(message);
    }
}
