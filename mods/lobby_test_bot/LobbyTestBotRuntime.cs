using BepInEx.Logging;
using Events;
using Fusion;
using Gameplay.Player.Components;
using Gameplay.Spawn;
using HarmonyLib;
using Kinguinverse.DataUtils.Events;
using Networking;
using Types;
using UI.Views.Lobby;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SneakOut.LobbyTestBot;

internal static class LobbyTestBotRuntime
{
    private enum PendingOperation
    {
        None,
        Add,
        Remove
    }

    private const float ButtonSize = 62f;
    private const float ButtonGap = 9f;
    private const float PendingTimeout = 8f;
    private const float RefreshInterval = 0.4f;

    private static readonly Dictionary<IntPtr, LobbyTestBotUiState> UiStateByView = new();

    private static ManualLogSource? _logger;
    private static LobbyTestBotConfig? _configuration;
    private static Harmony? _harmony;
    private static PendingOperation _pendingOperation;
    private static float _pendingStartedAt;
    private static int _requestedPlayerRefId;
    private static IntPtr _requestedLobbySpawnerPointer;
    private static IntPtr _diagnosticPublishedLobbySpawnerPointer;
    private static int _diagnosticLocalPlayerRefId;
    private static IntPtr _managedLobbySpawnerPointer;
    private static IntPtr _managedPlayerPointer;
    private static int _managedPlayerRefId;

    public static void Initialize(ManualLogSource logger, LobbyTestBotConfig configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _harmony ??= new Harmony(LobbyTestBotPlugin.PluginGuid);
        _harmony.PatchAll();
    }

    private static bool Enabled => _configuration is not null && _configuration.EnableMod.Value;

    private static bool LoggingEnabled => _configuration is not null && _configuration.EnableLogging.Value;

    private static string BotNickname => _configuration!.BotNickname.Value.Trim();

    public static void OpenPortal(PortalPlayView view)
    {
        if (!Enabled)
        {
            ReleasePortal(view);
            return;
        }

        try
        {
            var state = EnsureButton(view);
            if (state is null)
            {
                return;
            }

            RefreshButton(state);
        }
        catch (Exception exception)
        {
            LogError("Lobby bot portal setup failed", exception);
        }
    }

    public static void TickPortal(PortalPlayView view)
    {
        if (!Enabled)
        {
            ReleasePortal(view);
            return;
        }

        try
        {
            ObservePendingOperation();
            if (!UiStateByView.TryGetValue(view.Pointer, out var state) || !state.IsAlive)
            {
                return;
            }

            var now = Time.unscaledTime;
            if (now < state.NextRefreshTime)
            {
                return;
            }

            state.NextRefreshTime = now + RefreshInterval;
            TryRunDiagnosticAutoAdd();
            LayoutButton(view, state);
            RefreshButton(state);
        }
        catch (Exception exception)
        {
            LogError("Lobby bot portal update failed", exception);
        }
    }

    public static void ReleasePortal(PortalPlayView view)
    {
        if (!UiStateByView.Remove(view.Pointer, out var state))
        {
            return;
        }

        if (state.IsAlive)
        {
            state.Button.onClick.RemoveListener(state.ClickAction);
            UnityEngine.Object.Destroy(state.RootObject);
        }
    }

    public static void ObservePlayerDespawned(SpookedNetworkPlayer player)
    {
        if (!Enabled || !IsManagedBot(player))
        {
            return;
        }

        if (_pendingOperation == PendingOperation.Remove
            && player.KinguinverseId == _requestedPlayerRefId)
        {
            if (LoggingEnabled)
            {
                _logger?.LogInfo(
                    $"Authoritative bot despawn completed: playerRef={player.KinguinverseId}, "
                    + $"internalId={player.InternalId}");
            }

            ClearPendingOperation();
        }
        else if (LoggingEnabled)
        {
            _logger?.LogInfo(
                $"Managed bot despawned outside a pending request: playerRef={player.KinguinverseId}, "
                + $"internalId={player.InternalId}");
        }

        ForgetManagedBot();
        RefreshAllButtons();
    }

    public static void ObservePlayerInitialized(SpookedNetworkPlayer player)
    {
        if (!Enabled
            || _configuration is null
            || !_configuration.AutoAddBotWhenLobbyReady.Value
            || player.IsBot
            || !player.HasInputAuthority)
        {
            return;
        }

        try
        {
            _diagnosticLocalPlayerRefId = player.KinguinverseId;
            var sceneSpawner = ResolveAuthoritativeLobbySpawner();
            if (sceneSpawner is null)
            {
                _logger?.LogWarning(
                    "Diagnostic auto-add skipped: lobby state authority was unavailable when the local player initialized");
                return;
            }

            TryRunDiagnosticAutoAdd();
        }
        catch (Exception exception)
        {
            LogError("Diagnostic lobby bot auto-add failed", exception);
        }
    }

    public static void ObserveBotSpawnCompleted(NetworkObject networkObject)
    {
        if (!Enabled || _pendingOperation != PendingOperation.Add)
        {
            return;
        }

        try
        {
            var bot = networkObject.GetComponent<SpookedNetworkPlayer>();
            var sceneSpawner = ResolveAuthoritativeLobbySpawner();
            if (sceneSpawner is null || sceneSpawner.Pointer != _requestedLobbySpawnerPointer)
            {
                return;
            }

            if (bot is null
                || !bot.IsBot
                || bot.KinguinverseId != _requestedPlayerRefId)
            {
                return;
            }

            TrackManagedBot(bot);
            LogAuthoritativeSpawn(bot);
            ClearPendingOperation();
            RefreshAllButtons();
        }
        catch (Exception exception)
        {
            LogError("Authoritative lobby bot completion observation failed", exception);
        }
    }

    private static LobbyTestBotUiState? EnsureButton(PortalPlayView view)
    {
        if (view.Pointer == IntPtr.Zero || view._playButton is null)
        {
            return null;
        }

        if (UiStateByView.TryGetValue(view.Pointer, out var existingState) && existingState.IsAlive)
        {
            LayoutButton(view, existingState);
            return existingState;
        }

        var playSection = view._playButton.transform.parent?.GetComponent<RectTransform>();
        if (playSection is null || playSection.parent is null)
        {
            _logger?.LogWarning("Lobby bot button setup skipped: play-button panel was not found");
            return null;
        }

        var buttonObject = new GameObject("LobbyTestBotButton");
        buttonObject.transform.SetParent(playSection.parent, false);
        var buttonRect = buttonObject.AddComponent<RectTransform>();
        var background = buttonObject.AddComponent<Image>();
        var button = buttonObject.AddComponent<Button>();

        CopyButtonStyle(view._playButton, button, background);

        var iconImages = new[]
        {
            CreateIconPart(buttonObject.transform, "Head", new Vector2(-8f, 9f), new Vector2(10f, 10f)),
            CreateIconPart(buttonObject.transform, "Body", new Vector2(-8f, -7f), new Vector2(18f, 14f)),
            CreateIconPart(buttonObject.transform, "SignHorizontal", new Vector2(11f, 0f), new Vector2(17f, 4f)),
            CreateIconPart(buttonObject.transform, "SignVertical", new Vector2(11f, 0f), new Vector2(4f, 17f))
        };

        var clickAction = (UnityAction)ToggleBot;
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(clickAction);
        buttonRect.SetAsLastSibling();

        var state = new LobbyTestBotUiState(
            buttonObject,
            button,
            clickAction,
            iconImages[3],
            iconImages);
        UiStateByView[view.Pointer] = state;

        LayoutButton(view, state);
        return state;
    }

    private static void CopyButtonStyle(Button source, Button target, Image targetImage)
    {
        target.transition = source.transition;
        target.colors = source.colors;
        target.spriteState = source.spriteState;
        target.navigation = source.navigation;
        target.targetGraphic = targetImage;

        if (source.targetGraphic is not Image sourceImage)
        {
            targetImage.color = new Color(0.08627451f, 0.5372549f, 0.654902f, 1f);
            return;
        }

        targetImage.sprite = sourceImage.sprite;
        targetImage.overrideSprite = sourceImage.overrideSprite;
        targetImage.type = sourceImage.type;
        targetImage.preserveAspect = sourceImage.preserveAspect;
        targetImage.fillCenter = sourceImage.fillCenter;
        targetImage.fillMethod = sourceImage.fillMethod;
        targetImage.fillAmount = sourceImage.fillAmount;
        targetImage.fillClockwise = sourceImage.fillClockwise;
        targetImage.fillOrigin = sourceImage.fillOrigin;
        targetImage.pixelsPerUnitMultiplier = sourceImage.pixelsPerUnitMultiplier;
        targetImage.material = sourceImage.material;
        targetImage.color = sourceImage.color;
    }

    private static Image CreateIconPart(Transform parent, string name, Vector2 position, Vector2 size)
    {
        var iconObject = new GameObject(name);
        iconObject.transform.SetParent(parent, false);
        var rect = iconObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        var image = iconObject.AddComponent<Image>();
        image.color = Color.white;
        image.raycastTarget = false;
        return image;
    }

    private static void LayoutButton(PortalPlayView view, LobbyTestBotUiState state)
    {
        var playSection = view._playButton?.transform.parent?.GetComponent<RectTransform>();
        var buttonRect = state.RootObject.GetComponent<RectTransform>();
        if (playSection is null || buttonRect is null)
        {
            return;
        }

        if (state.RootObject.transform.parent != playSection.parent)
        {
            state.RootObject.transform.SetParent(playSection.parent, false);
        }

        buttonRect.anchorMin = playSection.anchorMin;
        buttonRect.anchorMax = playSection.anchorMax;
        buttonRect.pivot = playSection.pivot;
        buttonRect.localScale = playSection.localScale;
        buttonRect.localRotation = playSection.localRotation;
        buttonRect.sizeDelta = new Vector2(ButtonSize, ButtonSize);
        buttonRect.anchoredPosition = playSection.anchoredPosition
            + new Vector2((playSection.rect.width + ButtonSize) * 0.5f + ButtonGap, 0f);
        buttonRect.SetAsLastSibling();
    }

    private static void ToggleBot()
    {
        try
        {
            if (!Enabled || _pendingOperation != PendingOperation.None)
            {
                return;
            }

            var bot = FindManagedBot();
            if (bot is null)
            {
                TryAddBot();
            }
            else
            {
                TryRemoveBot(bot);
            }

            RefreshAllButtons();
        }
        catch (Exception exception)
        {
            LogError("Lobby bot toggle failed", exception);
            ClearPendingOperation();
            RefreshAllButtons();
        }
    }

    private static bool TryAddBot()
    {
        var sceneSpawner = ResolveAuthoritativeLobbySpawner();
        if (sceneSpawner is null)
        {
            _logger?.LogWarning("Lobby bot was not added: this client is not the active lobby state authority");
            return false;
        }

        if (FindManagedBot() is not null)
        {
            return false;
        }

        var playerRefId = FindAvailablePlayerRefId(sceneSpawner);
        if (playerRefId == 0)
        {
            _logger?.LogWarning("Lobby bot was not added: the lobby player registry has no free slot");
            return false;
        }

        _pendingOperation = PendingOperation.Add;
        _pendingStartedAt = Time.unscaledTime;
        _requestedPlayerRefId = playerRefId;
        _requestedLobbySpawnerPointer = sceneSpawner.Pointer;

        try
        {
            GameEventsManager.Publish<SpawnActorEvent>(
                null,
                new SpawnActorEvent(playerRefId, BotNickname, CharacterType.victim_penguin));
        }
        catch
        {
            ClearPendingOperation();
            throw;
        }

        if (LoggingEnabled)
        {
            _logger?.LogInfo($"Published native SpawnActorEvent for playerRef={playerRefId}");
        }

        return true;
    }

    private static void TryRemoveBot(SpookedNetworkPlayer bot)
    {
        var sceneSpawner = ResolveAuthoritativeLobbySpawner();
        if (sceneSpawner is null
            || sceneSpawner.Pointer != _managedLobbySpawnerPointer
            || bot.Object is null
            || !bot.Object.IsValid)
        {
            _logger?.LogWarning("Lobby bot was not removed: its authoritative network object is unavailable");
            return;
        }

        _pendingOperation = PendingOperation.Remove;
        _pendingStartedAt = Time.unscaledTime;
        _requestedPlayerRefId = bot.KinguinverseId;
        sceneSpawner.Runner.Despawn(bot.Object);

        if (LoggingEnabled)
        {
            _logger?.LogInfo(
                $"Requested native bot despawn: playerRef={bot.KinguinverseId}, networkObject={bot.Object.Id}");
        }
    }

    private static void ObservePendingOperation()
    {
        if (_pendingOperation == PendingOperation.None)
        {
            return;
        }

        if (_pendingOperation == PendingOperation.Add)
        {
            var requestedBot = FindRequestedBot();
            if (requestedBot is not null)
            {
                TrackManagedBot(requestedBot);
                LogAuthoritativeSpawn(requestedBot);
                ClearPendingOperation();
                RefreshAllButtons();
                return;
            }
        }
        else if (FindManagedBot() is null)
        {
            if (LoggingEnabled)
            {
                _logger?.LogInfo($"Authoritative bot despawn completed: playerRef={_requestedPlayerRefId}");
            }

            ForgetManagedBot();
            ClearPendingOperation();
            RefreshAllButtons();
            return;
        }

        if (Time.unscaledTime - _pendingStartedAt <= PendingTimeout)
        {
            return;
        }

        _logger?.LogError(
            $"Lobby bot {_pendingOperation.ToString().ToLowerInvariant()} timed out for playerRef={_requestedPlayerRefId}");
        ClearPendingOperation();
        RefreshAllButtons();
    }

    private static void TryRunDiagnosticAutoAdd()
    {
        if (_configuration is null || !_configuration.AutoAddBotWhenLobbyReady.Value)
        {
            return;
        }

        var sceneSpawner = ResolveAuthoritativeLobbySpawner();
        if (sceneSpawner is null
            || sceneSpawner._networkPlayerRegistry.Count == 0
            || _diagnosticPublishedLobbySpawnerPointer == sceneSpawner.Pointer
            || _pendingOperation != PendingOperation.None)
        {
            return;
        }

        if (FindManagedBot() is not null)
        {
            _diagnosticPublishedLobbySpawnerPointer = sceneSpawner.Pointer;
            return;
        }

        _logger?.LogInfo(
            "Diagnostic lobby readiness reached: "
            + $"localPlayerRef={_diagnosticLocalPlayerRefId}, "
            + $"networkPlayerCount={sceneSpawner._networkPlayerRegistry.Count}");
        if (TryAddBot())
        {
            _diagnosticPublishedLobbySpawnerPointer = sceneSpawner.Pointer;
        }
    }

    private static void LogAuthoritativeSpawn(SpookedNetworkPlayer bot)
    {
        var sceneSpawner = ResolveAuthoritativeLobbySpawner();
        var registeredInternalId = -1;
        var playersRegistryContainsBot = sceneSpawner is not null
            && sceneSpawner._players.Exists(bot.KinguinverseId, out registeredInternalId);
        var registryCount = sceneSpawner?._networkPlayerRegistry.Count ?? 0;
        var networkObject = bot.Object;

        _logger?.LogInfo(
            "Authoritative lobby bot spawned: "
            + $"playerRef={bot.KinguinverseId}, internalId={bot.InternalId}, "
            + $"registeredInternalId={(playersRegistryContainsBot ? registeredInternalId : -1)}, "
            + $"networkObject={networkObject.Id}, valid={networkObject.IsValid}, "
            + $"inSimulation={networkObject.IsInSimulation}, stateAuthority={networkObject.HasStateAuthority}, "
            + $"networkPlayerCount={registryCount}");
    }

    private static int FindAvailablePlayerRefId(SceneSpawner sceneSpawner)
    {
        var registry = sceneSpawner._players._registry;
        for (var playerRefId = 1; playerRefId < registry.Length; playerRefId++)
        {
            if (!sceneSpawner._players.Exists(playerRefId, out _))
            {
                return playerRefId;
            }
        }

        return 0;
    }

    private static SceneSpawner? ResolveAuthoritativeLobbySpawner()
    {
        var sceneSpawner = UnityEngine.Object.FindObjectOfType<SceneSpawner>();
        if (sceneSpawner is null
            || sceneSpawner._gameState is null
            || sceneSpawner._gameState.CurrentScene != SceneType.Lobby
            || sceneSpawner._gameState.GameStateType != GameStateType.Lobby
            || sceneSpawner.Object is null
            || !sceneSpawner.Object.IsValid
            || !sceneSpawner.HasStateAuthority
            || sceneSpawner.Runner is null
            || !sceneSpawner.Runner.IsRunning)
        {
            return null;
        }

        return sceneSpawner;
    }

    private static SpookedNetworkPlayer? FindManagedBot()
    {
        if (_managedPlayerPointer == IntPtr.Zero || _managedPlayerRefId == 0)
        {
            return null;
        }

        var sceneSpawner = ResolveAuthoritativeLobbySpawner();
        if (sceneSpawner is null || sceneSpawner.Pointer != _managedLobbySpawnerPointer)
        {
            return null;
        }

        foreach (var player in Resources.FindObjectsOfTypeAll<SpookedNetworkPlayer>())
        {
            if (IsManagedBot(player)
                && player.Object is not null
                && player.Object.IsValid
                && player.Object.IsInSimulation)
            {
                return player;
            }
        }

        return null;
    }

    private static SpookedNetworkPlayer? FindRequestedBot()
    {
        var sceneSpawner = ResolveAuthoritativeLobbySpawner();
        if (sceneSpawner is null || sceneSpawner.Pointer != _requestedLobbySpawnerPointer)
        {
            return null;
        }

        foreach (var player in Resources.FindObjectsOfTypeAll<SpookedNetworkPlayer>())
        {
            if (player is not null
                && player.IsBot
                && player.KinguinverseId == _requestedPlayerRefId
                && player.Object is not null
                && player.Object.IsValid
                && player.Object.IsInSimulation)
            {
                return player;
            }
        }

        return null;
    }

    private static bool IsManagedBot(SpookedNetworkPlayer? player)
    {
        return player is not null
            && player.IsBot
            && player.Pointer == _managedPlayerPointer
            && player.KinguinverseId == _managedPlayerRefId;
    }

    private static void TrackManagedBot(SpookedNetworkPlayer player)
    {
        _managedLobbySpawnerPointer = _requestedLobbySpawnerPointer;
        _managedPlayerPointer = player.Pointer;
        _managedPlayerRefId = player.KinguinverseId;
    }

    private static void ForgetManagedBot()
    {
        _managedLobbySpawnerPointer = IntPtr.Zero;
        _managedPlayerPointer = IntPtr.Zero;
        _managedPlayerRefId = 0;
    }

    private static void RefreshAllButtons()
    {
        foreach (var state in UiStateByView.Values)
        {
            if (state.IsAlive)
            {
                RefreshButton(state);
            }
        }
    }

    private static void RefreshButton(LobbyTestBotUiState state)
    {
        var canManageBot = ResolveAuthoritativeLobbySpawner() is not null;
        state.RootObject.SetActive(canManageBot);
        if (!canManageBot)
        {
            return;
        }

        var hasBot = FindManagedBot() is not null;
        var pending = _pendingOperation != PendingOperation.None;
        state.Button.interactable = !pending;
        state.VerticalSign.gameObject.SetActive(!hasBot);

        var iconAlpha = pending ? 0.45f : 1f;
        foreach (var image in state.IconImages)
        {
            var color = image.color;
            color.a = iconAlpha;
            image.color = color;
        }
    }

    private static void ClearPendingOperation()
    {
        _pendingOperation = PendingOperation.None;
        _pendingStartedAt = 0f;
        _requestedPlayerRefId = 0;
        _requestedLobbySpawnerPointer = IntPtr.Zero;
    }

    private static void LogError(string context, Exception exception)
    {
        _logger?.LogError($"{context}: {exception}");
    }
}
