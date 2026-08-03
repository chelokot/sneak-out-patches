using BepInEx.Logging;
using Gameplay.Player.Components;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using System.Security.Cryptography;
using Steamworks;
using TMPro;
using Types;
using UI.Views;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace SneakOut.ProximityVoiceChat;

internal static class ProximityVoiceChatRuntime
{
    private const float HelloIntervalSeconds = 1.75f;
    private const float PlaybackExpirySeconds = 8f;
    private const int MaximumPeerPacketsPerSecond = 180;
    private const int MaximumPeerBytesPerSecond = 256 * 1024;
    private const int MaximumTrackedTrafficPeers = 64;

    private static readonly Dictionary<IntPtr, SpookedNetworkPlayer> ObservedPlayers = new();
    private static readonly Dictionary<ulong, RemoteVoicePlayback> Playbacks = new();
    private static readonly Dictionary<ulong, ulong> RemoteInstanceIds = new();
    private static readonly Dictionary<ulong, VoiceFragmentAssembler> FragmentAssemblers = new();
    private static readonly Dictionary<ulong, PeerTrafficWindow> PeerTrafficWindows = new();
    private static readonly HashSet<ulong> ConnectedPeers = new();
    private static readonly Queue<BufferedCapture> VoiceActivationPreRoll = new();
    private static readonly List<ulong> PlaybackRemovalBuffer = new();
    private static readonly List<IntPtr> PlayerRemovalBuffer = new();

    private static ManualLogSource? _logger;
    private static ProximityVoiceChatConfig? _configuration;
    private static Harmony? _harmony;
    private static SteamVoiceCapture? _capture;
    private static SteamVoiceTransport? _transport;
    private static VoicePeerDirectory? _peers;
    private static SpookedNetworkPlayer? _localPlayer;
    private static AudioListener? _audioListener;
    private static ulong _localSteamId;
    private static ulong _sessionHash;
    private static ulong _localInstanceId;
    private static int _localInternalId = -1;
    private static uint _audioSequence;
    private static uint _controlSequence;
    private static float _voiceActivationOpenUntil;
    private static float _nextHelloTime;
    private static bool _shutdown;
    private static bool _pushToTalkKeyResolved;
    private static KeyCode _cachedPushToTalkKeyCode;
    private static Key _cachedPushToTalkKey;

    public static void Initialize(ManualLogSource logger, ProximityVoiceChatConfig configuration)
    {
        _logger = logger;
        _configuration = configuration;
        ProximityVoiceSettingsUi.Initialize(configuration, logger);
        _shutdown = false;
        _harmony ??= new Harmony(ProximityVoiceChatPlugin.PluginGuid);
        _harmony.PatchAll();

        ClassInjector.RegisterTypeInIl2Cpp<ProximityVoiceWatcher>();
        var watcherObject = new GameObject("ProximityVoiceChatWatcher");
        watcherObject.hideFlags = HideFlags.HideAndDontSave;
        UnityEngine.Object.DontDestroyOnLoad(watcherObject);
        watcherObject.AddComponent<ProximityVoiceWatcher>();
    }

    public static void ObservePlayer(SpookedNetworkPlayer player)
    {
        try
        {
            if (_shutdown || player is null || player.Pointer == IntPtr.Zero)
            {
                return;
            }

            ObservedPlayers[player.Pointer] = player;
            if (player.HasInputAuthority && !player.IsBot)
            {
                _localPlayer = player;
            }
            _peers?.RegisterPlayer(player);
        }
        catch (Exception exception)
        {
            _logger?.LogWarning($"Proximity voice ignored an unavailable spawned player: {exception.Message}");
        }
    }

    public static void ForgetPlayer(SpookedNetworkPlayer player)
    {
        try
        {
            if (player is null)
            {
                return;
            }
            ObservedPlayers.Remove(player.Pointer);
            _peers?.UnregisterPlayer(player);
            if (_localPlayer is not null && _localPlayer.Pointer == player.Pointer)
            {
                _localPlayer = null;
                EndSession(sendGoodbye: true);
            }
        }
        catch (Exception exception)
        {
            _logger?.LogWarning($"Proximity voice ignored an unavailable despawned player: {exception.Message}");
        }
    }

    public static void ObserveSettingsMenu(GameMenuView view)
    {
        if (view is null
            || view.Pointer == IntPtr.Zero
            || view._audioPanel is null)
        {
            return;
        }

        try
        {
            ProximityVoiceSettingsUi.Attach(view);
            if (_configuration?.EnableLogging.Value == true)
            {
                _logger?.LogInfo("Proximity voice audio settings hierarchy:");
                LogSettingsHierarchy(view._audioPanel.transform, depth: 0, remaining: 100);
            }
        }
        catch (Exception exception)
        {
            _logger?.LogWarning($"Could not inspect the stock audio settings hierarchy: {exception.Message}");
        }
    }

    private static int LogSettingsHierarchy(Transform root, int depth, int remaining)
    {
        if (remaining <= 0 || root is null || root.Pointer == IntPtr.Zero)
        {
            return remaining;
        }

        var componentNames = root.gameObject.GetComponents<Component>()
            .Where(component => component is not null && component.Pointer != IntPtr.Zero)
            .Select(component => component.GetIl2CppType().Name)
            .ToArray();
        _logger?.LogInfo(
            $"VOICE_UI {new string(' ', depth * 2)}{root.name} "
            + $"active={root.gameObject.activeSelf} components=[{string.Join(',', componentNames)}]");
        remaining--;
        for (var childIndex = 0; childIndex < root.childCount && remaining > 0; childIndex++)
        {
            remaining = LogSettingsHierarchy(root.GetChild(childIndex), depth + 1, remaining);
        }
        return remaining;
    }

    private static void Tick()
    {
        if (_shutdown || _configuration is null)
        {
            return;
        }
        ProximityVoiceSettingsUi.Tick();
        if (!_configuration.EnableMod.Value)
        {
            EndSession(sendGoodbye: true);
            return;
        }
        if (!EnsureSteamServices())
        {
            return;
        }

        RefreshObservedPlayers();
        var localPlayer = _localPlayer;
        if (localPlayer is null
            || localPlayer.Pointer == IntPtr.Zero
            || !localPlayer.HasInputAuthority
            || localPlayer.IsBot
            || !VoiceSessionResolver.TryGetSessionName(localPlayer, out var sessionName))
        {
            EndSession(sendGoodbye: true);
            return;
        }

        var sessionHash = VoiceProtocol.HashSessionName(sessionName);
        if (_sessionHash != sessionHash || _localInternalId != localPlayer.InternalId)
        {
            BeginSession(sessionHash, localPlayer.InternalId);
        }

        var now = Time.unscaledTime;
        _peers!.Refresh(now);
        PrepareAndGreetPeers(now);
        _transport!.Poll(HandlePacketSafely);
        CaptureAndTransmit(now);
        TickPlaybacks(now, ResolveListener());
    }

    private static bool EnsureSteamServices()
    {
        if (_capture is not null && _transport is not null && _peers is not null)
        {
            return true;
        }
        if (!SteamAPI.IsSteamRunning())
        {
            return false;
        }

        _localSteamId = SteamUser.GetSteamID().m_SteamID;
        if (_localSteamId == 0)
        {
            return false;
        }

        _peers = new VoicePeerDirectory(_logger!, _configuration!);
        _capture = new SteamVoiceCapture();
        _transport = new SteamVoiceTransport(
            _logger!,
            _configuration!.EnableLogging.Value,
            steamId => _peers?.IsAllowed(steamId) == true);
        return true;
    }

    private static void BeginSession(ulong sessionHash, int localInternalId)
    {
        EndSession(sendGoodbye: true);
        _sessionHash = sessionHash;
        _localInternalId = localInternalId;
        _audioSequence = 0;
        _controlSequence = 0;
        _localInstanceId = CreateInstanceId();
        _nextHelloTime = 0f;
        _peers!.BeginSession(sessionHash, localInternalId, _localSteamId);
        foreach (var player in ObservedPlayers.Values)
        {
            if (player is not null && player.Pointer != IntPtr.Zero)
            {
                _peers.RegisterPlayer(player);
            }
        }
        Log($"Voice session started: room={sessionHash:X16}, internalId={localInternalId}");
    }

    private static void PrepareAndGreetPeers(float now)
    {
        if (now < _nextHelloTime)
        {
            return;
        }
        _nextHelloTime = now + HelloIntervalSeconds;
        var allowed = _peers!.AllowedPeers.ToHashSet();
        foreach (var staleSteamId in ConnectedPeers.Where(steamId => !allowed.Contains(steamId)).ToArray())
        {
            _transport!.ClosePeer(staleSteamId);
            ConnectedPeers.Remove(staleSteamId);
            RemoteInstanceIds.Remove(staleSteamId);
            PeerTrafficWindows.Remove(staleSteamId);
            RemovePlayback(staleSteamId);
        }
        foreach (var steamId in allowed)
        {
            _transport!.AllowPeer(steamId);
            ConnectedPeers.Add(steamId);
        }
        BroadcastControl(VoicePacketKind.Hello, reliable: false, confirmedOnly: false);
    }

    private static void CaptureAndTransmit(float now)
    {
        var mode = _configuration!.TransmissionMode.Value;
        var pushToTalkPressed = mode == VoiceTransmissionMode.PushToTalk && IsPushToTalkPressed();
        var capturePermitted = (_configuration.TransmitWhileUnfocused.Value || Application.isFocused)
            && (!_configuration.SuppressWhileTyping.Value || !IsTextInputFocused());
        var shouldRecord = _peers!.ConfirmedPeers.Count > 0
            && capturePermitted
            && (mode switch
            {
                VoiceTransmissionMode.PushToTalk => pushToTalkPressed,
                VoiceTransmissionMode.VoiceActivation => true,
                VoiceTransmissionMode.AlwaysOn => true,
                _ => false,
            });
        _capture!.SetRecording(shouldRecord);
        if (!shouldRecord)
        {
            VoiceActivationPreRoll.Clear();
            _voiceActivationOpenUntil = 0f;
            return;
        }

        var captureBudget = 6;
        while (captureBudget-- > 0
               && _capture.TryCapture(mode == VoiceTransmissionMode.VoiceActivation, out var frame))
        {
            var timestamp = unchecked((uint)Environment.TickCount64);
            if (mode != VoiceTransmissionMode.VoiceActivation)
            {
                SendAudio(frame, timestamp);
                continue;
            }

            if (now <= _voiceActivationOpenUntil)
            {
                SendAudio(frame, timestamp);
                if (frame.RootMeanSquare >= _configuration.VoiceActivationThreshold.Value)
                {
                    _voiceActivationOpenUntil = now + _configuration.VoiceActivationHangoverSeconds.Value;
                }
                continue;
            }

            VoiceActivationPreRoll.Enqueue(new BufferedCapture(frame, timestamp, now));
            TrimPreRoll(now);
            if (frame.RootMeanSquare < _configuration.VoiceActivationThreshold.Value)
            {
                continue;
            }

            _voiceActivationOpenUntil = now + _configuration.VoiceActivationHangoverSeconds.Value;
            while (VoiceActivationPreRoll.TryDequeue(out var buffered))
            {
                SendAudio(buffered.Frame, buffered.TimestampMilliseconds);
            }
        }
    }

    private static void TrimPreRoll(float now)
    {
        var cutoff = now - _configuration!.VoiceActivationPreRollSeconds.Value;
        while (VoiceActivationPreRoll.TryPeek(out var buffered) && buffered.CapturedAt < cutoff)
        {
            VoiceActivationPreRoll.Dequeue();
        }
    }

    private static void SendAudio(in CapturedVoiceFrame frame, uint timestamp)
    {
        if (frame.EncodedAudio.Length == 0
            || _sessionHash == 0
            || _peers is null
            || _peers.ConfirmedPeers.Count == 0)
        {
            return;
        }
        var sequence = _audioSequence++;
        var fragmentCount = checked((ushort)Math.Max(
            1,
            (frame.EncodedAudio.Length + VoiceProtocol.MaximumFragmentPayloadLength - 1)
            / VoiceProtocol.MaximumFragmentPayloadLength));
        for (ushort fragmentIndex = 0; fragmentIndex < fragmentCount; fragmentIndex++)
        {
            var offset = fragmentIndex * VoiceProtocol.MaximumFragmentPayloadLength;
            var length = Math.Min(
                VoiceProtocol.MaximumFragmentPayloadLength,
                frame.EncodedAudio.Length - offset);
            var payload = new byte[length];
            Buffer.BlockCopy(frame.EncodedAudio, offset, payload, 0, length);
            var packet = VoiceProtocol.Encode(new VoicePacket(
                VoicePacketKind.Audio,
                _sessionHash,
                _localSteamId,
                _localInstanceId,
                _localInternalId,
                sequence,
                timestamp,
                fragmentIndex,
                fragmentCount,
                payload));
            foreach (var steamId in _peers!.ConfirmedPeers)
            {
                _transport!.Send(steamId, packet, reliable: false);
            }
        }
    }

    private static void BroadcastControl(
        VoicePacketKind kind,
        bool reliable,
        bool confirmedOnly)
    {
        if (_sessionHash == 0 || _peers is null || _transport is null)
        {
            return;
        }
        var packet = VoiceProtocol.Encode(new VoicePacket(
            kind,
            _sessionHash,
            _localSteamId,
            _localInstanceId,
            _localInternalId,
            _controlSequence++,
            unchecked((uint)Environment.TickCount64),
            0,
            1,
            Array.Empty<byte>()));
        var recipients = confirmedOnly ? _peers.ConfirmedPeers : _peers.AllowedPeers;
        foreach (var steamId in recipients)
        {
            _transport.Send(steamId, packet, reliable);
        }
    }

    private static void HandlePacket(CSteamID remoteSteamId, byte[] rawPacket)
    {
        if (_sessionHash == 0
            || !VoiceProtocol.TryDecode(rawPacket, out var packet)
            || remoteSteamId.m_SteamID != packet.SenderSteamId
            || packet.SenderInstanceId == 0
            || packet.SessionHash != _sessionHash
            || !_peers!.TryBindPacketIdentity(packet.SenderSteamId, packet.SenderInternalId))
        {
            return;
        }

        if (packet.Kind == VoicePacketKind.Hello)
        {
            var isNewInstance = !RemoteInstanceIds.TryGetValue(packet.SenderSteamId, out var oldInstance)
                || oldInstance != packet.SenderInstanceId;
            _peers.ConfirmHandshake(packet.SenderSteamId);
            if (oldInstance != 0 && oldInstance != packet.SenderInstanceId)
            {
                RemovePlayback(packet.SenderSteamId);
            }
            RemoteInstanceIds[packet.SenderSteamId] = packet.SenderInstanceId;
            if (isNewInstance)
            {
                SendControlTo(VoicePacketKind.Hello, packet.SenderSteamId, reliable: false);
            }
            return;
        }
        if (!RemoteInstanceIds.TryGetValue(packet.SenderSteamId, out var instanceId)
            || instanceId != packet.SenderInstanceId)
        {
            // Audio is admitted only after a Hello for this exact process/session. This
            // rejects stale relay packets left over from an earlier run of the same lobby.
            return;
        }

        if (packet.Kind == VoicePacketKind.Audio)
        {
            if (!FragmentAssemblers.TryGetValue(packet.SenderSteamId, out var assembler))
            {
                assembler = new VoiceFragmentAssembler();
                FragmentAssemblers.Add(packet.SenderSteamId, assembler);
            }
            var fragment = packet;
            if (!assembler.TryAdd(fragment, Time.unscaledTime, out packet))
            {
                return;
            }
        }

        if (packet.Kind == VoicePacketKind.Goodbye)
        {
            RemovePlayback(packet.SenderSteamId);
            RemoteInstanceIds.Remove(packet.SenderSteamId);
            _transport!.ClosePeer(packet.SenderSteamId);
            return;
        }
        if (packet.Kind != VoicePacketKind.Audio || _peers.IsMuted(packet.SenderSteamId))
        {
            return;
        }
        if (!_peers.TryGetPlayer(packet.SenderSteamId, out var player))
        {
            return;
        }

        if (!Playbacks.TryGetValue(packet.SenderSteamId, out var playback))
        {
            playback = new RemoteVoicePlayback(
                player.transform,
                _capture!.SampleRate,
                _configuration!,
                packet.SenderSteamId.ToString());
            Playbacks.Add(packet.SenderSteamId, playback);
        }
        else if (playback.Anchor != player.transform)
        {
            playback.Rebind(player.transform);
        }
        playback.Enqueue(packet, Time.unscaledTime);
    }

    private static void HandlePacketSafely(CSteamID remoteSteamId, byte[] rawPacket)
    {
        try
        {
            if (!TryConsumePeerTraffic(remoteSteamId.m_SteamID, rawPacket.Length, Time.unscaledTime))
            {
                return;
            }
            HandlePacket(remoteSteamId, rawPacket);
        }
        catch (Exception exception)
        {
            if (_configuration?.EnableLogging.Value == true)
            {
                _logger?.LogWarning(
                    $"Ignored malformed or unavailable proximity voice peer state for {remoteSteamId.m_SteamID}: {exception.Message}");
            }
        }
    }

    private static void TickPlaybacks(float now, Transform? listener)
    {
        PlaybackRemovalBuffer.Clear();
        foreach (var pair in Playbacks)
        {
            if (_peers!.IsMuted(pair.Key)
                || !_peers.TryGetPlayer(pair.Key, out var remotePlayer)
                || now - pair.Value.LastPacketTime > PlaybackExpirySeconds)
            {
                PlaybackRemovalBuffer.Add(pair.Key);
                continue;
            }
            try
            {
                pair.Value.Tick(now, listener, CanLocalPlayerHear(remotePlayer));
            }
            catch (Exception exception)
            {
                if (_configuration?.EnableLogging.Value == true)
                {
                    _logger?.LogWarning($"Removed failed proximity playback for {pair.Key}: {exception.Message}");
                }
                PlaybackRemovalBuffer.Add(pair.Key);
            }
        }
        foreach (var steamId in PlaybackRemovalBuffer)
        {
            RemovePlayback(steamId);
        }
    }

    private static bool CanLocalPlayerHear(SpookedNetworkPlayer remotePlayer)
    {
        try
        {
            // Ghosts remain in the same Fusion player registry as living players. A dead local
            // player may listen to both groups; a living (or lobby) player must never receive
            // buffered ghost speech. Character/prop form deliberately does not participate in
            // this decision, so hunters, penguins and possessed props can all talk normally.
            return VoiceAudibilityPolicy.CanHear(
                _localPlayer?.GamePlayerState == GamePlayerState.Dead,
                remotePlayer.GamePlayerState == GamePlayerState.Dead);
        }
        catch
        {
            // During a scene transition the conservative choice is to suppress an unavailable
            // remote state rather than briefly leak ghost voice to a living player.
            return false;
        }
    }

    private static Transform? ResolveListener()
    {
        try
        {
            if (_audioListener is not null
                && _audioListener.Pointer != IntPtr.Zero
                && _audioListener.enabled
                && _audioListener.gameObject.activeInHierarchy)
            {
                return _audioListener.transform;
            }
        }
        catch
        {
            _audioListener = null;
        }
        foreach (var listener in Resources.FindObjectsOfTypeAll<AudioListener>())
        {
            try
            {
                if (listener is not null && listener.enabled && listener.gameObject.activeInHierarchy)
                {
                    _audioListener = listener;
                    return listener.transform;
                }
            }
            catch
            {
                // Ignore a listener destroyed during the scene transition snapshot.
            }
        }
        return _localPlayer?.transform;
    }

    private static void SendControlTo(VoicePacketKind kind, ulong steamId, bool reliable)
    {
        if (_sessionHash == 0 || _transport is null)
        {
            return;
        }
        var packet = VoiceProtocol.Encode(new VoicePacket(
            kind,
            _sessionHash,
            _localSteamId,
            _localInstanceId,
            _localInternalId,
            _controlSequence++,
            unchecked((uint)Environment.TickCount64),
            0,
            1,
            Array.Empty<byte>()));
        _transport.Send(steamId, packet, reliable);
    }

    private static bool IsPushToTalkPressed()
    {
        var configuredKey = _configuration!.PushToTalkKey.Value;
        if (!_pushToTalkKeyResolved || configuredKey != _cachedPushToTalkKeyCode)
        {
            _cachedPushToTalkKeyCode = configuredKey;
            _pushToTalkKeyResolved = TryResolveKey(configuredKey, out _cachedPushToTalkKey);
        }
        return _pushToTalkKeyResolved
            && Keyboard.current?[_cachedPushToTalkKey].isPressed == true;
    }

    private static bool TryResolveKey(KeyCode configuredKey, out Key key)
    {
        var keyName = configuredKey.ToString() switch
        {
            "LeftControl" => "LeftCtrl",
            "RightControl" => "RightCtrl",
            "Return" => "Enter",
            "Alpha0" => "Digit0",
            "Alpha1" => "Digit1",
            "Alpha2" => "Digit2",
            "Alpha3" => "Digit3",
            "Alpha4" => "Digit4",
            "Alpha5" => "Digit5",
            "Alpha6" => "Digit6",
            "Alpha7" => "Digit7",
            "Alpha8" => "Digit8",
            "Alpha9" => "Digit9",
            var value => value,
        };
        return Enum.TryParse(keyName, ignoreCase: true, out key);
    }

    private static bool IsTextInputFocused()
    {
        var selected = EventSystem.current?.currentSelectedGameObject;
        return selected is not null && selected.GetComponent<TMP_InputField>() is not null;
    }

    private static bool TryConsumePeerTraffic(ulong steamId, int bytes, float now)
    {
        if (!PeerTrafficWindows.TryGetValue(steamId, out var window))
        {
            if (PeerTrafficWindows.Count >= MaximumTrackedTrafficPeers)
            {
                return false;
            }
            window = new PeerTrafficWindow(now);
            PeerTrafficWindows.Add(steamId, window);
        }
        if (now - window.StartTime >= 1f)
        {
            window.StartTime = now;
            window.PacketCount = 0;
            window.ByteCount = 0;
        }
        if (window.PacketCount >= MaximumPeerPacketsPerSecond
            || window.ByteCount + bytes > MaximumPeerBytesPerSecond)
        {
            return false;
        }
        window.PacketCount++;
        window.ByteCount += bytes;
        return true;
    }

    private static void RefreshObservedPlayers()
    {
        PlayerRemovalBuffer.Clear();
        foreach (var pair in ObservedPlayers)
        {
            var player = pair.Value;
            if (player is null || player.Pointer == IntPtr.Zero)
            {
                PlayerRemovalBuffer.Add(pair.Key);
                continue;
            }
            try
            {
                if (player.HasInputAuthority && !player.IsBot)
                {
                    _localPlayer = player;
                }
            }
            catch
            {
                PlayerRemovalBuffer.Add(pair.Key);
            }
        }
        foreach (var pointer in PlayerRemovalBuffer)
        {
            ObservedPlayers.Remove(pointer);
        }
    }

    private static void EndSession(bool sendGoodbye)
    {
        if (_sessionHash == 0)
        {
            _capture?.SetRecording(false);
            return;
        }
        if (sendGoodbye)
        {
            BroadcastControl(VoicePacketKind.Goodbye, reliable: true, confirmedOnly: true);
        }
        _capture?.SetRecording(false);
        VoiceActivationPreRoll.Clear();
        _voiceActivationOpenUntil = 0f;
        foreach (var playback in Playbacks.Values)
        {
            try
            {
                playback.Dispose();
            }
            catch
            {
                // Scene teardown may destroy the Unity host before the Fusion despawn callback.
            }
        }
        Playbacks.Clear();
        RemoteInstanceIds.Clear();
        foreach (var assembler in FragmentAssemblers.Values)
        {
            assembler.Reset();
        }
        FragmentAssemblers.Clear();
        PeerTrafficWindows.Clear();
        ConnectedPeers.Clear();
        _transport?.CloseAll();
        _peers?.EndSession();
        Log($"Voice session ended: room={_sessionHash:X16}");
        _sessionHash = 0;
        _localInstanceId = 0;
        _localInternalId = -1;
        _audioListener = null;
    }

    private static void RemovePlayback(ulong steamId)
    {
        if (Playbacks.Remove(steamId, out var playback))
        {
            try
            {
                playback.Dispose();
            }
            catch
            {
                // The remote avatar may already have destroyed its audio child during scene load.
            }
        }
        if (FragmentAssemblers.Remove(steamId, out var assembler))
        {
            assembler.Reset();
        }
    }

    private static void Log(string message)
    {
        if (_configuration?.EnableLogging.Value == true)
        {
            _logger?.LogInfo(message);
        }
    }

    private static ulong CreateInstanceId()
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        do
        {
            RandomNumberGenerator.Fill(bytes);
        }
        while (BitConverter.ToUInt64(bytes) == 0);
        return BitConverter.ToUInt64(bytes);
    }

    public static void Shutdown()
    {
        if (_shutdown)
        {
            return;
        }
        _shutdown = true;
        EndSession(sendGoodbye: true);
        _capture?.Dispose();
        _transport?.Dispose();
        _peers?.Dispose();
        _capture = null;
        _transport = null;
        _peers = null;
        ObservedPlayers.Clear();
        _localPlayer = null;
    }

    private readonly record struct BufferedCapture(
        CapturedVoiceFrame Frame,
        uint TimestampMilliseconds,
        float CapturedAt);

    private sealed class PeerTrafficWindow
    {
        public PeerTrafficWindow(float startTime)
        {
            StartTime = startTime;
        }

        public float StartTime { get; set; }
        public int PacketCount { get; set; }
        public int ByteCount { get; set; }
    }

    private sealed class ProximityVoiceWatcher : MonoBehaviour
    {
        private int _consecutiveFailures;

        public ProximityVoiceWatcher(IntPtr pointer) : base(pointer)
        {
        }

        public ProximityVoiceWatcher() : base(ClassInjector.DerivedConstructorPointer<ProximityVoiceWatcher>())
        {
            ClassInjector.DerivedConstructorBody(this);
        }

        private void Update()
        {
            try
            {
                Tick();
                _consecutiveFailures = 0;
            }
            catch (Exception exception)
            {
                _consecutiveFailures++;
                if (_consecutiveFailures == 1)
                {
                    _logger?.LogError($"Proximity voice update failed: {exception}");
                }
                if (_consecutiveFailures >= 3)
                {
                    _logger?.LogError("Proximity voice disabled itself after three consecutive update failures");
                    Shutdown();
                }
            }
        }

        private void OnApplicationQuit()
        {
            Shutdown();
        }
    }
}
