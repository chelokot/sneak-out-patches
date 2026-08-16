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
using UnityEngine.InputSystem;

namespace SneakOut.ProximityVoiceChat;

internal readonly record struct VoicePartyMember(ulong SteamId, string DisplayName);

internal enum VoiceMicrophoneTestState
{
    Idle,
    Recording,
    Playing,
}

internal static class ProximityVoiceChatRuntime
{
    private const float HelloIntervalSeconds = 1.75f;
    private const float PlaybackExpirySeconds = 8f;
    private const int MaximumPeerPacketsPerSecond = 180;
    private const int MaximumPeerBytesPerSecond = 256 * 1024;
    private const int MaximumTrackedTrafficPeers = 64;
    private const float HandshakeWarningSeconds = 8f;
    private const float MicrophoneTestRecordingSeconds = 4f;
    private const int MaximumMicrophoneTestFrames = 250;

    private static readonly Dictionary<IntPtr, SpookedNetworkPlayer> ObservedPlayers = new();
    private static readonly Dictionary<ulong, RemoteVoicePlayback> Playbacks = new();
    private static readonly Dictionary<ulong, ulong> RemoteInstanceIds = new();
    private static readonly Dictionary<ulong, VoiceFragmentAssembler> FragmentAssemblers = new();
    private static readonly Dictionary<ulong, PeerTrafficWindow> PeerTrafficWindows = new();
    private static readonly HashSet<ulong> ConnectedPeers = new();
    private static readonly Queue<BufferedCapture> VoiceActivationPreRoll = new();
    private static readonly List<ulong> PlaybackRemovalBuffer = new();
    private static readonly List<IntPtr> PlayerRemovalBuffer = new();
    private static readonly Dictionary<ulong, float> HandshakeStartedAt = new();
    private static readonly HashSet<ulong> HandshakeTimeoutWarnings = new();
    private static readonly HashSet<string> PacketRejectionWarnings = new();
    private static readonly HashSet<string> PacketExceptionWarnings = new();
    private static readonly List<byte[]> MicrophoneTestFrames = new(MaximumMicrophoneTestFrames);

    private static ManualLogSource? _logger;
    private static ProximityVoiceChatConfig? _configuration;
    private static Harmony? _harmony;
    private static OpusVoiceCapture? _capture;
    private static SteamVoiceTransport? _transport;
    private static VoicePeerDirectory? _peers;
    private static LocalVoiceTestPlayback? _microphoneTestPlayback;
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
    private static string _cachedPushToTalkBinding = string.Empty;
    private static Key _cachedPushToTalkKey;
    private static string _lastRuntimeStatus = string.Empty;
    private static string _lastCaptureStatus = string.Empty;
    private static bool _loggedFirstTransmit;
    private static bool _microphoneTestRequested;
    private static float _microphoneTestVolume = 1f;
    private static float _microphoneTestRecordUntil;
    private static float _microphoneTestPeakRootMeanSquare;
    private static VoiceMicrophoneTestState _microphoneTestState;

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

    public static bool IsMicrophoneTestEnabled => _microphoneTestRequested;

    public static float MicrophoneTestVolume => _microphoneTestVolume;

    public static VoiceMicrophoneTestState MicrophoneTestState => _microphoneTestState;

    public static void SetMicrophoneTestEnabled(bool enabled)
    {
        if (_shutdown)
        {
            return;
        }
        if (enabled)
        {
            _microphoneTestRequested = true;
            return;
        }

        StopMicrophoneTest(logCancellation: _microphoneTestState != VoiceMicrophoneTestState.Idle);
    }

    public static void SetMicrophoneTestVolume(float volume)
    {
        if (float.IsFinite(volume))
        {
            _microphoneTestVolume = Math.Clamp(
                volume,
                VoicePlayerVolumePolicy.MinimumVolume,
                VoicePlayerVolumePolicy.MaximumVolume);
        }
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

    public static IReadOnlyList<VoicePartyMember> GetRemotePartyMembers()
    {
        RefreshObservedPlayers();
        var members = new Dictionary<ulong, VoicePartyMember>();
        foreach (var player in ObservedPlayers.Values)
        {
            try
            {
                if (player is null
                    || player.Pointer == IntPtr.Zero
                    || player.HasInputAuthority
                    || player.IsBot
                    || !VoiceIdentityResolver.TryResolveSteamId(player, out var steamId)
                    || steamId == 0
                    || steamId == _localSteamId)
                {
                    continue;
                }

                var displayName = player.Nickname?.Trim();
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    displayName = $"Player {player.InternalId}";
                }
                members[steamId] = new VoicePartyMember(steamId, displayName);
            }
            catch
            {
                // A replicated player can disappear during the settings-frame snapshot.
            }
        }

        return members.Values
            .OrderBy(member => member.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(member => member.SteamId)
            .ToArray();
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
        if (_configuration is null)
        {
            return;
        }
        ProximityVoiceSettingsUi.Tick();
        if (_shutdown)
        {
            return;
        }
        if (!_configuration.EnableMod.Value)
        {
            EndSession(sendGoodbye: true);
            ReportStatus("disabled", "Enabled=false");
            return;
        }
        if (!EnsureSteamServices(out var steamFailure))
        {
            ReportStatus("waiting-for-steam", steamFailure);
            return;
        }

        RefreshObservedPlayers();
        var localPlayer = _localPlayer;
        if (localPlayer is null || localPlayer.Pointer == IntPtr.Zero)
        {
            EndSession(sendGoodbye: true);
            ReportStatus("waiting-for-player", $"observedPlayers={ObservedPlayers.Count}");
            return;
        }
        if (!localPlayer.HasInputAuthority || localPlayer.IsBot)
        {
            EndSession(sendGoodbye: true);
            ReportStatus(
                "waiting-for-player-authority",
                $"internalId={localPlayer.InternalId}, inputAuthority={localPlayer.HasInputAuthority}, bot={localPlayer.IsBot}");
            return;
        }
        if (!VoiceSessionResolver.TryGetSessionName(localPlayer, out var sessionName, out var sessionFailure))
        {
            EndSession(sendGoodbye: true);
            ReportStatus("waiting-for-session", sessionFailure);
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
        ReportPeerState(now);
        if (!TickMicrophoneTest(now))
        {
            CaptureAndTransmit(now);
        }
        TickPlaybacks(now, ResolveListener());
    }

    private static bool EnsureSteamServices(out string failureReason)
    {
        failureReason = string.Empty;
        if (_capture is not null && _transport is not null && _peers is not null)
        {
            return true;
        }
        if (!SteamAPI.IsSteamRunning())
        {
            failureReason = "SteamAPI.IsSteamRunning=false";
            return false;
        }

        _localSteamId = SteamUser.GetSteamID().m_SteamID;
        if (_localSteamId == 0)
        {
            failureReason = "SteamUser.GetSteamID returned 0";
            return false;
        }

        _peers = new VoicePeerDirectory(_logger!, _configuration!);
        _capture = new OpusVoiceCapture(_logger!, _configuration!.EnableLogging.Value);
        _transport = new SteamVoiceTransport(
            _logger!,
            _configuration!.EnableLogging.Value,
            steamId => _peers?.IsAllowed(steamId) == true);
        _logger!.LogInfo(
            $"Proximity voice services ready: localSteamId={_localSteamId}, "
            + $"sampleRate={OpusVoiceCapture.SampleRate}, codec=Opus");
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
        _loggedFirstTransmit = false;
        _logger?.LogInfo($"Voice session started: room={sessionHash:X16}, internalId={localInternalId}");
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
            HandshakeStartedAt.Remove(staleSteamId);
            HandshakeTimeoutWarnings.Remove(staleSteamId);
        }
        foreach (var steamId in allowed)
        {
            _transport!.AllowPeer(steamId);
            ConnectedPeers.Add(steamId);
            HandshakeStartedAt.TryAdd(steamId, now);
        }
        BroadcastControl(VoicePacketKind.Hello, confirmedOnly: false);
    }

    private static void ReportPeerState(float now)
    {
        var allowed = _peers!.AllowedPeers.ToArray();
        var confirmed = _peers.ConfirmedPeers.ToHashSet();
        foreach (var steamId in confirmed)
        {
            HandshakeStartedAt.Remove(steamId);
            HandshakeTimeoutWarnings.Remove(steamId);
        }

        if (allowed.Length == 0)
        {
            ReportStatus(
                "waiting-for-peers",
                $"observedPlayers={ObservedPlayers.Count}, identifiedRemotePeers=0");
            return;
        }
        var pending = allowed.Where(steamId => !confirmed.Contains(steamId)).ToArray();
        if (pending.Length > 0)
        {
            ReportStatus(
                "handshake-pending",
                $"candidates={allowed.Length}, confirmed={confirmed.Count}, pending={pending.Length}");
            foreach (var steamId in pending)
            {
                if (HandshakeStartedAt.TryGetValue(steamId, out var startedAt)
                    && now - startedAt >= HandshakeWarningSeconds
                    && HandshakeTimeoutWarnings.Add(steamId))
                {
                    _logger?.LogWarning(
                        $"Proximity voice handshake timed out for Steam peer {steamId} after "
                        + $"{now - startedAt:F1}s: {_transport!.DescribePeerState(steamId)}");
                }
            }
            return;
        }
        ReportStatus(
            "ready",
            $"candidates={allowed.Length}, confirmed={confirmed.Count}, mode={_configuration!.TransmissionMode.Value}");
    }

    private static void CaptureAndTransmit(float now)
    {
        var mode = _configuration!.TransmissionMode.Value;
        var pushToTalkPressed = mode == VoiceTransmissionMode.PushToTalk && IsPushToTalkPressed();
        var capturePermitted = !_configuration.StopWhenGameIsUnfocused.Value || Application.isFocused;
        var hasConfirmedPeers = _peers!.ConfirmedPeers.Count > 0;
        // Keep the microphone session warm while a peer is connected. Starting capture only when
        // PTT is pressed loses the beginning of an utterance to device initialization latency.
        var shouldCapture = hasConfirmedPeers && capturePermitted;
        var shouldTransmit = shouldCapture
            && (mode switch
            {
                VoiceTransmissionMode.PushToTalk => pushToTalkPressed,
                VoiceTransmissionMode.VoiceActivation => true,
                VoiceTransmissionMode.AlwaysOn => true,
                _ => false,
            });
        var captureStatus = !capturePermitted
            ? "paused-unfocused"
            : !hasConfirmedPeers
                ? "waiting-for-handshake"
                : mode == VoiceTransmissionMode.PushToTalk && !pushToTalkPressed
                    ? "armed-push-to-talk"
                    : shouldTransmit
                        ? "transmitting"
                        : "capture-idle";
        ReportCaptureStatus(captureStatus);
        _capture!.SetRecording(shouldCapture);
        if (!shouldCapture)
        {
            VoiceActivationPreRoll.Clear();
            _voiceActivationOpenUntil = 0f;
            return;
        }

        var captureBudget = 6;
        while (captureBudget-- > 0
               && _capture.TryCapture(mode == VoiceTransmissionMode.VoiceActivation, out var frame))
        {
            if (!shouldTransmit)
            {
                continue;
            }

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

    private static bool TickMicrophoneTest(float now)
    {
        if (!_microphoneTestRequested)
        {
            if (_microphoneTestState != VoiceMicrophoneTestState.Idle)
            {
                StopMicrophoneTest(logCancellation: false);
            }
            return false;
        }

        if (_microphoneTestState == VoiceMicrophoneTestState.Idle)
        {
            MicrophoneTestFrames.Clear();
            _microphoneTestPeakRootMeanSquare = 0f;
            _microphoneTestRecordUntil = now + MicrophoneTestRecordingSeconds;
            _microphoneTestState = VoiceMicrophoneTestState.Recording;
            _capture!.SetRecording(true);
            _logger?.LogInfo(
                $"Proximity voice microphone test recording started for {MicrophoneTestRecordingSeconds:F0} seconds");
        }

        if (_microphoneTestState == VoiceMicrophoneTestState.Recording)
        {
            _capture!.SetRecording(true);
            var captureBudget = 6;
            while (captureBudget-- > 0 && _capture.TryCapture(analyzeLevel: true, out var frame))
            {
                _microphoneTestPeakRootMeanSquare = Math.Max(
                    _microphoneTestPeakRootMeanSquare,
                    frame.RootMeanSquare);
                if (MicrophoneTestFrames.Count < MaximumMicrophoneTestFrames)
                {
                    MicrophoneTestFrames.Add(frame.EncodedAudio);
                }
            }

            if (now < _microphoneTestRecordUntil)
            {
                ReportCaptureStatus("microphone-test-recording");
                return true;
            }

            _capture.SetRecording(false);
            if (MicrophoneTestFrames.Count == 0)
            {
                _logger?.LogWarning(
                    "Proximity voice microphone test captured no audio; check the selected system microphone");
                StopMicrophoneTest(logCancellation: false);
                return true;
            }

            try
            {
                _microphoneTestPlayback = new LocalVoiceTestPlayback(
                    MicrophoneTestFrames,
                    _configuration!,
                    _logger!);
                _microphoneTestState = VoiceMicrophoneTestState.Playing;
                _logger?.LogInfo(
                    $"Proximity voice microphone test playback started: "
                    + $"frames={MicrophoneTestFrames.Count}, peakRms={_microphoneTestPeakRootMeanSquare:F4}, "
                    + $"volume={_microphoneTestVolume:F2}");
                MicrophoneTestFrames.Clear();
            }
            catch (Exception exception)
            {
                _logger?.LogWarning(
                    $"Proximity voice microphone test could not start playback: "
                    + $"{exception.GetType().Name}: {exception.Message}");
                StopMicrophoneTest(logCancellation: false);
            }
            return true;
        }

        ReportCaptureStatus("microphone-test-playback");
        try
        {
            if (_microphoneTestPlayback?.Tick(now, _microphoneTestVolume) != true)
            {
                return true;
            }
            _logger?.LogInfo("Proximity voice microphone test completed");
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(
                $"Proximity voice microphone test playback failed: "
                + $"{exception.GetType().Name}: {exception.Message}");
        }
        StopMicrophoneTest(logCancellation: false);
        return true;
    }

    private static void StopMicrophoneTest(bool logCancellation)
    {
        if (logCancellation)
        {
            _logger?.LogInfo("Proximity voice microphone test canceled");
        }

        _microphoneTestRequested = false;
        _microphoneTestState = VoiceMicrophoneTestState.Idle;
        _microphoneTestRecordUntil = 0f;
        _microphoneTestPeakRootMeanSquare = 0f;
        MicrophoneTestFrames.Clear();
        _capture?.SetRecording(false);
        if (_microphoneTestPlayback is not null)
        {
            try
            {
                _microphoneTestPlayback.Dispose();
            }
            catch
            {
                // Scene teardown can invalidate the test AudioSource before the UI is detached.
            }
            _microphoneTestPlayback = null;
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
                _transport!.Send(steamId, packet, VoiceTransportSendMode.Realtime);
            }
        }
        if (!_loggedFirstTransmit)
        {
            _loggedFirstTransmit = true;
            _logger?.LogInfo(
                $"Proximity voice transmitted first audio frame: bytes={frame.EncodedAudio.Length}, "
                + $"fragments={fragmentCount}, peers={_peers.ConfirmedPeers.Count}");
        }
    }

    private static void BroadcastControl(
        VoicePacketKind kind,
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
        var mode = VoiceTransportSendPolicy.ForControlPacket(kind);
        foreach (var steamId in recipients)
        {
            _transport.Send(steamId, packet, mode);
        }
    }

    private static void HandlePacket(CSteamID remoteSteamId, byte[] rawPacket)
    {
        var authenticatedSteamId = remoteSteamId.m_SteamID;
        if (_sessionHash == 0)
        {
            LogPacketRejection(authenticatedSteamId, "no-active-fusion-session");
            return;
        }
        if (!VoiceProtocol.TryDecode(rawPacket, out var packet))
        {
            LogPacketRejection(authenticatedSteamId, "invalid-protocol-packet");
            return;
        }
        if (authenticatedSteamId != packet.SenderSteamId)
        {
            LogPacketRejection(authenticatedSteamId, "authenticated-sender-mismatch");
            return;
        }
        if (packet.SenderInstanceId == 0)
        {
            LogPacketRejection(authenticatedSteamId, "missing-process-instance");
            return;
        }
        if (packet.SessionHash != _sessionHash)
        {
            LogPacketRejection(authenticatedSteamId, "fusion-session-mismatch");
            return;
        }
        if (!_peers!.TryBindPacketIdentity(packet.SenderSteamId, packet.SenderInternalId))
        {
            LogPacketRejection(authenticatedSteamId, "steam-to-player-identity-mismatch");
            return;
        }

        if (packet.Kind == VoicePacketKind.Hello)
        {
            var isNewInstance = !RemoteInstanceIds.TryGetValue(packet.SenderSteamId, out var oldInstance)
                || oldInstance != packet.SenderInstanceId;
            var newlyConfirmed = _peers.ConfirmHandshake(packet.SenderSteamId);
            if (oldInstance != 0 && oldInstance != packet.SenderInstanceId)
            {
                RemovePlayback(packet.SenderSteamId);
            }
            RemoteInstanceIds[packet.SenderSteamId] = packet.SenderInstanceId;
            if (newlyConfirmed || isNewInstance)
            {
                _logger?.LogInfo(
                    $"Proximity voice handshake confirmed: peer={packet.SenderSteamId}, "
                    + $"internalId={packet.SenderInternalId}, newProcess={isNewInstance}");
            }
            if (isNewInstance)
            {
                SendControlTo(VoicePacketKind.Hello, packet.SenderSteamId);
            }
            return;
        }
        if (!RemoteInstanceIds.TryGetValue(packet.SenderSteamId, out var instanceId)
            || instanceId != packet.SenderInstanceId)
        {
            // Audio is admitted only after a Hello for this exact process/session. This
            // rejects stale relay packets left over from an earlier run of the same lobby.
            LogPacketRejection(authenticatedSteamId, "hello-required-for-current-process");
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
            LogPacketRejection(authenticatedSteamId, "replicated-player-not-available");
            return;
        }

        if (!Playbacks.TryGetValue(packet.SenderSteamId, out var playback))
        {
            playback = new RemoteVoicePlayback(
                player.transform,
                _configuration!,
                _logger!,
                packet.SenderSteamId,
                packet.SenderSteamId.ToString());
            Playbacks.Add(packet.SenderSteamId, playback);
            _logger?.LogInfo(
                $"Proximity voice playback started: peer={packet.SenderSteamId}, internalId={packet.SenderInternalId}");
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
            var key = $"{remoteSteamId.m_SteamID}:{exception.GetType().FullName}";
            if (PacketExceptionWarnings.Add(key))
            {
                _logger?.LogWarning(
                    $"Proximity voice packet handler failed once for peer {remoteSteamId.m_SteamID}: "
                    + $"{exception.GetType().Name}: {exception.Message}");
            }
        }
    }

    private static void LogPacketRejection(ulong steamId, string reason)
    {
        var key = $"{steamId}:{reason}";
        if (PacketRejectionWarnings.Add(key))
        {
            _logger?.LogWarning($"Proximity voice rejected packet: peer={steamId}, reason={reason}");
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

    private static void SendControlTo(
        VoicePacketKind kind,
        ulong steamId)
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
        _transport.Send(steamId, packet, VoiceTransportSendPolicy.ForControlPacket(kind));
    }

    private static bool IsPushToTalkPressed()
    {
        var configuredBinding = _configuration!.PushToTalkBinding.Value;
        if (!_pushToTalkKeyResolved
            || !string.Equals(configuredBinding, _cachedPushToTalkBinding, StringComparison.OrdinalIgnoreCase))
        {
            _cachedPushToTalkBinding = configuredBinding;
            _pushToTalkKeyResolved = TryResolveKey(configuredBinding, out _cachedPushToTalkKey);
        }
        return _pushToTalkKeyResolved
            && Keyboard.current?[_cachedPushToTalkKey].isPressed == true;
    }

    private static bool TryResolveKey(string binding, out Key key)
    {
        var slashIndex = binding.LastIndexOf('/');
        var keyName = slashIndex >= 0 && slashIndex + 1 < binding.Length
            ? binding[(slashIndex + 1)..]
            : binding;
        return Enum.TryParse(keyName, ignoreCase: true, out key);
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
        StopMicrophoneTest(logCancellation: _microphoneTestState != VoiceMicrophoneTestState.Idle);
        if (_sessionHash == 0)
        {
            return;
        }
        if (sendGoodbye)
        {
            BroadcastControl(VoicePacketKind.Goodbye, confirmedOnly: true);
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
        HandshakeStartedAt.Clear();
        HandshakeTimeoutWarnings.Clear();
        PacketRejectionWarnings.Clear();
        PacketExceptionWarnings.Clear();
        _transport?.CloseAll();
        _peers?.EndSession();
        _logger?.LogInfo($"Voice session ended: room={_sessionHash:X16}");
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

    private static void ReportStatus(string stage, string detail)
    {
        var status = $"{stage}|{detail}";
        if (string.Equals(status, _lastRuntimeStatus, StringComparison.Ordinal))
        {
            return;
        }
        _lastRuntimeStatus = status;
        _logger?.LogInfo($"Proximity voice state: {stage} ({detail})");
    }

    private static void ReportCaptureStatus(string status)
    {
        if (string.Equals(status, _lastCaptureStatus, StringComparison.Ordinal))
        {
            return;
        }
        _lastCaptureStatus = status;
        _logger?.LogInfo($"Proximity voice capture gate: {status}");
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
        _microphoneTestPlayback = null;
        ObservedPlayers.Clear();
        _localPlayer = null;
        _lastRuntimeStatus = string.Empty;
        _lastCaptureStatus = string.Empty;
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
