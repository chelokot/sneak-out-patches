using BepInEx.Logging;
using Gameplay.Interactions;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Photon.Voice;
using Photon.Voice.Unity;
using UnityEngine;
using UnityEngine.AI;

namespace SneakOut.ProximityVoiceChat;

internal enum VoicePropagationKind
{
    Direct,
    Routed,
    Blocked,
}

internal sealed class RemoteVoicePlayback : IDisposable
{
    private const float PropagationProbeIntervalSeconds = 0.20f;
    private const float PlaybackEffectBlendSpeed = 7f;
    private const float SourcePositionBlendSpeed = 10f;
    private const float OcclusionProbeRadiusMetres = 0.03f;
    private const float DoorProbeRadiusMetres = 0.08f;
    private const float NavMeshSampleRadiusMetres = 2.5f;
    private const float VoiceHeightAboveNavMeshMetres = 1.25f;
    private const float MinimumCornerDistanceMetres = 0.35f;
    private const float MinimumRouteDetourMetres = 0.15f;
    private const float FlatRolloffMaximumDistanceMetres = 1000f;
    private const int OcclusionHitCapacity = 32;

    private static int _wallLayer = int.MinValue;
    private static int _playerLayer = int.MinValue;
    private static int _additivePlayerLayer = int.MinValue;

    private readonly ProximityVoiceChatConfig _configuration;
    private readonly ManualLogSource _logger;
    private readonly ulong _peerSteamId;
    private readonly string _peerLabel;
    private readonly AdaptiveJitterBuffer _jitterBuffer;
    private readonly OpusVoiceDecoder _decoder;
    private readonly VoiceGainProcessor _gainProcessor = new(limitPeaks: false);
    private readonly Photon.Voice.Unity.Logger _photonLogger;
    private readonly UnityAudioOut _audioOutput;
    private readonly GameObject _host;
    private readonly AudioSource _audioSource;
    private readonly AudioLowPassFilter _lowPassFilter;
    private readonly Il2CppStructArray<RaycastHit> _occlusionHits = new(OcclusionHitCapacity);
    private Transform _anchor;
    private NavMeshPath? _navMeshPath;
    private Vector3 _targetSourcePosition;
    private float _routeDistance;
    private float _nextPropagationProbe;
    private float _nextAudioDiagnostic;
    private float _nextDecodeWarning;
    private float _currentDistanceVolumeMultiplier = 1f;
    private float _currentOcclusionVolumeMultiplier = 1f;
    private float _currentLowPassFrequency = VoiceOcclusionPolicy.UnoccludedLowPassFrequency;
    private VoiceOcclusionKind _occlusionKind;
    private VoicePropagationKind _propagationKind;
    private float _lastPacketTime;
    private long _decodedFrames;
    private long _decodeFailures;
    private long _concealedFrames;
    private uint _nextDecodeSequence;
    private bool _hasDecodeSequence;
    private bool _outOfRange;
    private bool _suppressedByPlayerState;
    private bool _pathingUnavailable;
    private bool _disposed;

    public RemoteVoicePlayback(
        Transform anchor,
        ProximityVoiceChatConfig configuration,
        ManualLogSource logger,
        ulong peerSteamId,
        string peerLabel)
    {
        _configuration = configuration;
        _logger = logger;
        _peerSteamId = peerSteamId;
        _peerLabel = peerLabel;
        _anchor = anchor;
        _jitterBuffer = new AdaptiveJitterBuffer(
            20f,
            120f);

        _host = new GameObject($"ProximityVoice-{peerLabel}");
        _host.hideFlags = HideFlags.HideAndDontSave;
        _targetSourcePosition = GetVoiceOrigin(anchor);
        _host.transform.position = _targetSourcePosition;
        _audioSource = _host.AddComponent<AudioSource>();
        _lowPassFilter = _host.AddComponent<AudioLowPassFilter>();
        _audioSource.loop = true;
        _audioSource.playOnAwake = false;
        _audioSource.spatialBlend = configuration.DirectionalVoice.Value ? 1f : 0f;
        _audioSource.dopplerLevel = 0f;
        _audioSource.spread = 0f;
        _audioSource.rolloffMode = AudioRolloffMode.Custom;
        ConfigureFlatDistanceCurve();
        _audioSource.volume = 1f;
        _lowPassFilter.cutoffFrequency = VoiceOcclusionPolicy.UnoccludedLowPassFrequency;

        var maximumDelayMilliseconds = Mathf.RoundToInt(Math.Max(
            configuration.JitterBufferMilliseconds.Value,
            configuration.MaximumJitterMilliseconds.Value));
        var targetDelayMilliseconds = Mathf.RoundToInt(Math.Clamp(
            configuration.JitterBufferMilliseconds.Value,
            40,
            maximumDelayMilliseconds));
        var playDelay = new AudioOutDelayControl.PlayDelayConfig
        {
            Low = targetDelayMilliseconds,
            High = Math.Min(maximumDelayMilliseconds, targetDelayMilliseconds + 40),
            Max = maximumDelayMilliseconds,
            SpeedUpPerc = 5,
        };
        _photonLogger = new Photon.Voice.Unity.Logger(
            configuration.EnableLogging.Value
                ? Photon.Voice.LogLevel.Info
                : Photon.Voice.LogLevel.Warning);
        _audioOutput = new UnityAudioOut(
            _audioSource,
            playDelay,
            new Photon.Voice.ILogger(_photonLogger.Pointer),
            $"[ProximityVoice:{peerLabel}]",
            configuration.EnableLogging.Value);
        _audioOutput.Start(OpusVoiceCapture.SampleRate, 1, OpusVoiceCapture.FrameSamples);
        _decoder = new OpusVoiceDecoder(OnDecodedFrame);
    }

    public Transform Anchor => _anchor;

    public float LastPacketTime => _lastPacketTime;

    public void Rebind(Transform anchor)
    {
        _anchor = anchor;
        _targetSourcePosition = GetVoiceOrigin(anchor);
    }

    public void Enqueue(in VoicePacket packet, float arrivalTime)
    {
        if (_disposed || packet.Kind != VoicePacketKind.Audio)
        {
            return;
        }

        _lastPacketTime = arrivalTime;
        _jitterBuffer.Enqueue(new EncodedVoiceFrame(
            packet.Sequence,
            packet.CaptureTimestampMilliseconds,
            arrivalTime,
            packet.Payload));
    }

    public void Tick(float nowSeconds, Transform? listener, bool audibleForPlayerState)
    {
        if (_disposed)
        {
            return;
        }

        _audioOutput.Service();
        if (!audibleForPlayerState)
        {
            _jitterBuffer.Reset();
            if (!_suppressedByPlayerState)
            {
                ResetPlayback();
                _suppressedByPlayerState = true;
            }
            return;
        }
        _suppressedByPlayerState = false;
        UpdatePropagation(nowSeconds, listener);
        var isInAudibleRange = listener is null
            || VoiceDistancePolicy.IsAudible(_routeDistance);
        if (!isInAudibleRange)
        {
            DiscardBufferedAudio(nowSeconds);
            if (!_outOfRange)
            {
                ResetPlayback();
                _outOfRange = true;
            }
            return;
        }
        _outOfRange = false;

        var decodeBudget = 8;
        while (decodeBudget-- > 0 && _jitterBuffer.TryDequeue(nowSeconds, out var frame))
        {
            try
            {
                var missingFrames = _hasDecodeSequence
                    ? VoicePacketLossPolicy.CountMissingFrames(_nextDecodeSequence, frame.Sequence)
                    : 0;
                _nextDecodeSequence = frame.Sequence + 1;
                _hasDecodeSequence = true;
                _concealedFrames += missingFrames;
                if (!_decoder.TryDecode(frame.Payload, missingFrames))
                {
                    _decodeFailures++;
                }
            }
            catch (Exception exception)
            {
                _decodeFailures++;
                if (nowSeconds >= _nextDecodeWarning)
                {
                    _nextDecodeWarning = nowSeconds + 10f;
                    _logger.LogWarning(
                        $"Proximity voice Opus decode failed for peer {_peerLabel}: "
                        + $"{exception.GetType().Name}: {exception.Message}; "
                        + $"totalFailures={_decodeFailures}");
                }
            }
        }

        ApplyPlaybackEffects(listener);
        ReportAudioDiagnostics(nowSeconds);
    }

    private void ConfigureFlatDistanceCurve()
    {
        // Position now carries only direction and reverb-zone placement. Route distance owns the
        // fixed 20-metre volume curve, so Unity's transform distance must not attenuate it again.
        _audioSource.minDistance = 1f;
        _audioSource.maxDistance = FlatRolloffMaximumDistanceMetres;
        _audioSource.SetCustomCurve(
            AudioSourceCurveType.CustomRolloff,
            new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(1f, 1f)));
    }

    private void DiscardBufferedAudio(float nowSeconds)
    {
        var discardBudget = 32;
        while (discardBudget-- > 0 && _jitterBuffer.TryDequeue(nowSeconds, out _))
        {
        }
    }

    private void OnDecodedFrame(Il2CppArrayBase<float> samples)
    {
        if (_disposed || samples.Length == 0)
        {
            return;
        }
        _gainProcessor.Process(samples, _configuration.GetPlayerVolume(_peerSteamId));
        _audioOutput.Push(samples);
        _decodedFrames++;
    }

    private void UpdatePropagation(float nowSeconds, Transform? listener)
    {
        var voiceOrigin = GetVoiceOrigin(_anchor);
        if (listener is null)
        {
            _propagationKind = VoicePropagationKind.Direct;
            _occlusionKind = VoiceOcclusionKind.None;
            _routeDistance = 0f;
            _targetSourcePosition = voiceOrigin;
            UpdateSourcePosition();
            return;
        }

        if (nowSeconds >= _nextPropagationProbe)
        {
            _nextPropagationProbe = nowSeconds + PropagationProbeIntervalSeconds;
            var previousPropagation = _propagationKind;
            var previousOcclusion = _occlusionKind;
            var directDistance = Vector3.Distance(listener.position, voiceOrigin);
            var probe = directDistance <= VoiceDistancePolicy.MaximumAudibleDistanceMetres
                ? ProbeDirectPath(listener, voiceOrigin)
                : new DirectPathProbe(VoiceOcclusionKind.None, false, string.Empty, string.Empty);

            _propagationKind = probe.Occlusion == VoiceOcclusionKind.Wall
                ? VoicePropagationKind.Blocked
                : VoicePropagationKind.Direct;
            _occlusionKind = probe.Occlusion;
            _routeDistance = directDistance;
            _targetSourcePosition = voiceOrigin;
            var routeCornerCount = 0;
            var routedDoorName = string.Empty;

            if (probe.Occlusion == VoiceOcclusionKind.Wall
                && !probe.BlockedByDoor
                && TryCalculateRoute(listener, voiceOrigin, out var route))
            {
                _propagationKind = VoicePropagationKind.Routed;
                _occlusionKind = route.BlockedByDoor
                    ? VoiceOcclusionKind.Wall
                    : VoiceOcclusionKind.None;
                _routeDistance = route.DistanceMetres;
                _targetSourcePosition = route.FirstCorner;
                routeCornerCount = route.CornerCount;
                routedDoorName = route.BlockingDoorName;
            }

            if ((previousPropagation != _propagationKind || previousOcclusion != _occlusionKind)
                && _configuration.EnableLogging.Value)
            {
                var blockerName = string.IsNullOrEmpty(routedDoorName)
                    ? probe.BlockerName
                    : routedDoorName;
                _logger.LogInfo(
                    $"Proximity voice propagation: peer={_peerLabel}, mode={_propagationKind}, "
                    + $"occlusion={_occlusionKind}, direct={directDistance:F2}, "
                    + $"route={_routeDistance:F2}, corners={routeCornerCount}, "
                    + $"blocker={blockerName}, layer={probe.BlockerLayerName}");
            }
        }
        else if (_propagationKind != VoicePropagationKind.Routed)
        {
            // Direct and fallback-blocked playback should continue following a moving speaker in
            // between the more expensive physics/NavMesh route probes.
            _routeDistance = Vector3.Distance(listener.position, voiceOrigin);
            _targetSourcePosition = voiceOrigin;
        }

        UpdateSourcePosition();
    }

    private DirectPathProbe ProbeDirectPath(Transform listener, Vector3 voiceOrigin)
    {
        var from = listener.position;
        var offset = voiceOrigin - from;
        var distance = offset.magnitude;
        var hitCount = distance > 0.01f
            ? Physics.SphereCastNonAlloc(
                from,
                OcclusionProbeRadiusMetres,
                offset / distance,
                _occlusionHits,
                distance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore)
            : 0;
        var kind = VoiceOcclusionKind.None;
        var blockedByDoor = false;
        var blockerName = string.Empty;
        var blockerLayerName = string.Empty;
        for (var index = 0; index < hitCount; index++)
        {
            var collider = _occlusionHits[index].collider;
            if (ShouldIgnoreCollider(collider, listener))
            {
                continue;
            }

            var door = collider.GetComponentInParent<Door>();
            var candidateKind = IsWall(collider)
                ? VoiceOcclusionKind.Wall
                : VoiceOcclusionKind.Item;
            var combinedKind = VoiceOcclusionPolicy.Combine(kind, candidateKind);
            if (combinedKind != kind)
            {
                kind = combinedKind;
                blockerName = collider.gameObject.name;
                blockerLayerName = LayerMask.LayerToName(collider.gameObject.layer);
            }
            if (candidateKind == VoiceOcclusionKind.Wall && door is not null)
            {
                blockedByDoor = true;
                blockerName = collider.gameObject.name;
                blockerLayerName = LayerMask.LayerToName(collider.gameObject.layer);
            }
        }
        return new DirectPathProbe(kind, blockedByDoor, blockerName, blockerLayerName);
    }

    private bool TryCalculateRoute(Transform listener, Vector3 voiceOrigin, out RoutedVoicePath route)
    {
        route = default;
        if (_pathingUnavailable)
        {
            return false;
        }

        try
        {
            if (!NavMesh.SamplePosition(
                    listener.position,
                    out var listenerHit,
                    NavMeshSampleRadiusMetres,
                    NavMesh.AllAreas)
                || !NavMesh.SamplePosition(
                    _anchor.position,
                    out var speakerHit,
                    NavMeshSampleRadiusMetres,
                    NavMesh.AllAreas))
            {
                return false;
            }

            _navMeshPath ??= new NavMeshPath();
            if (!NavMesh.CalculatePath(
                    listenerHit.position,
                    speakerHit.position,
                    NavMesh.AllAreas,
                    _navMeshPath)
                || _navMeshPath.status != NavMeshPathStatus.PathComplete)
            {
                return false;
            }

            var corners = _navMeshPath.corners;
            if (corners is null || corners.Length < 2)
            {
                return false;
            }

            var navMeshDistance = 0f;
            for (var index = 1; index < corners.Length; index++)
            {
                navMeshDistance += Vector3.Distance(corners[index - 1], corners[index]);
            }
            var navMeshDirectDistance = Vector3.Distance(corners[0], corners[corners.Length - 1]);
            if (corners.Length < 3
                && navMeshDistance < navMeshDirectDistance + MinimumRouteDetourMetres)
            {
                // A straight baked path through a physics blocker is usually an openable closed
                // door or geometry absent from the NavMesh. It is not evidence of a sound route.
                return false;
            }

            var routeDistance = navMeshDistance
                + PlanarDistance(listener.position, corners[0])
                + PlanarDistance(_anchor.position, corners[corners.Length - 1]);
            routeDistance = Math.Max(routeDistance, Vector3.Distance(listener.position, voiceOrigin));

            var firstCornerIndex = 1;
            while (firstCornerIndex < corners.Length - 1
                   && PlanarDistance(listener.position, corners[firstCornerIndex])
                   < MinimumCornerDistanceMetres)
            {
                firstCornerIndex++;
            }
            var firstCorner = corners[firstCornerIndex];
            firstCorner.y += VoiceHeightAboveNavMeshMetres;

            var blockedByDoor = TryFindDoorOnRoute(corners, listener, out var blockingDoorName);
            route = new RoutedVoicePath(
                firstCorner,
                routeDistance,
                corners.Length - 2,
                blockedByDoor,
                blockingDoorName);
            return true;
        }
        catch (Exception exception)
        {
            _pathingUnavailable = true;
            _logger.LogWarning(
                $"Proximity voice NavMesh propagation unavailable for peer {_peerLabel}; "
                + $"using direct wall occlusion: {exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    private bool TryFindDoorOnRoute(
        Il2CppStructArray<Vector3> corners,
        Transform listener,
        out string blockingDoorName)
    {
        blockingDoorName = string.Empty;
        var heightOffset = Vector3.up * VoiceHeightAboveNavMeshMetres;
        for (var cornerIndex = 1; cornerIndex < corners.Length; cornerIndex++)
        {
            var from = corners[cornerIndex - 1] + heightOffset;
            var offset = corners[cornerIndex] + heightOffset - from;
            var distance = offset.magnitude;
            var hitCount = distance > 0.01f
                ? Physics.SphereCastNonAlloc(
                    from,
                    DoorProbeRadiusMetres,
                    offset / distance,
                    _occlusionHits,
                    distance,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore)
                : 0;
            for (var hitIndex = 0; hitIndex < hitCount; hitIndex++)
            {
                var collider = _occlusionHits[hitIndex].collider;
                if (ShouldIgnoreCollider(collider, listener))
                {
                    continue;
                }
                if (collider.GetComponentInParent<Door>() is not null)
                {
                    blockingDoorName = collider.gameObject.name;
                    return true;
                }
            }
        }
        return false;
    }

    private bool ShouldIgnoreCollider(Collider? collider, Transform listener)
    {
        return collider is null
            || collider.Pointer == IntPtr.Zero
            || IsPlayerLayer(collider.gameObject.layer)
            || BelongsToPlayer(collider.transform, listener)
            || BelongsToPlayer(collider.transform, _anchor);
    }

    private void UpdateSourcePosition()
    {
        var blend = 1f - Mathf.Exp(-SourcePositionBlendSpeed * Time.unscaledDeltaTime);
        _host.transform.position = Vector3.Lerp(_host.transform.position, _targetSourcePosition, blend);
    }

    private void ApplyPlaybackEffects(Transform? listener)
    {
        var profile = VoiceOcclusionPolicy.GetProfile(_occlusionKind);
        var blend = 1f - Mathf.Exp(-PlaybackEffectBlendSpeed * Time.unscaledDeltaTime);
        _audioSource.spatialBlend = _configuration.DirectionalVoice.Value ? 1f : 0f;
        var distanceVolume = listener is null
            ? 1f
            : VoiceDistancePolicy.EvaluateVolume(_routeDistance);
        _currentDistanceVolumeMultiplier = Mathf.Lerp(
            _currentDistanceVolumeMultiplier,
            distanceVolume,
            blend);
        _currentOcclusionVolumeMultiplier = Mathf.Lerp(
            _currentOcclusionVolumeMultiplier,
            profile.VolumeMultiplier,
            blend);
        _currentLowPassFrequency = Mathf.Lerp(
            _currentLowPassFrequency,
            profile.LowPassFrequency,
            blend);
        _audioSource.volume = _currentDistanceVolumeMultiplier
            * _currentOcclusionVolumeMultiplier;
        _lowPassFilter.cutoffFrequency = _currentLowPassFrequency;
    }

    private void ReportAudioDiagnostics(float nowSeconds)
    {
        if (!_configuration.EnableLogging.Value || nowSeconds < _nextAudioDiagnostic)
        {
            return;
        }
        _nextAudioDiagnostic = nowSeconds + 10f;
        _logger.LogInfo(
            $"Proximity voice playback metrics: peer={_peerLabel}, decodedFrames={_decodedFrames}, "
            + $"decodeFailures={_decodeFailures}, concealedFrames={_concealedFrames}, "
            + $"packetFrames={_jitterBuffer.BufferedFrameCount}, "
            + $"packetDelayMs={_jitterBuffer.TargetDelaySeconds * 1000f:F1}, "
            + $"playoutLagMs={_audioOutput.Lag}, inputPeak={_gainProcessor.LastInputPeak:F4}, "
            + $"gain={_gainProcessor.CurrentGain:F2}, playing={_audioOutput.IsPlaying}");
    }

    private static Vector3 GetVoiceOrigin(Transform anchor)
    {
        return anchor.position + Vector3.up * VoiceHeightAboveNavMeshMetres;
    }

    private static float PlanarDistance(Vector3 left, Vector3 right)
    {
        var x = left.x - right.x;
        var z = left.z - right.z;
        return Mathf.Sqrt(x * x + z * z);
    }

    private readonly record struct DirectPathProbe(
        VoiceOcclusionKind Occlusion,
        bool BlockedByDoor,
        string BlockerName,
        string BlockerLayerName);

    private readonly record struct RoutedVoicePath(
        Vector3 FirstCorner,
        float DistanceMetres,
        int CornerCount,
        bool BlockedByDoor,
        string BlockingDoorName);

    private static bool IsWall(Collider collider)
    {
        if (_wallLayer == int.MinValue)
        {
            _wallLayer = LayerMask.NameToLayer("Wall");
        }
        if (_wallLayer >= 0 && collider.gameObject.layer == _wallLayer)
        {
            return true;
        }
        if (collider.GetComponentInParent<Door>() is not null)
        {
            return true;
        }

        // The authored maps mix walls and furniture on Environment, SeeThroughEnvironment, and
        // Room_* layers. Wall and door objects consistently retain a structural name somewhere in
        // their ancestry, while ordinary props keep item-specific names.
        var current = collider.transform;
        for (var depth = 0; depth < 8 && current is not null; depth++)
        {
            if (VoiceOcclusionPolicy.IsStructuralName(current.name))
            {
                return true;
            }
            current = current.parent;
        }
        return false;
    }

    private static bool IsPlayerLayer(int layer)
    {
        if (_playerLayer == int.MinValue)
        {
            _playerLayer = LayerMask.NameToLayer("Player");
            _additivePlayerLayer = LayerMask.NameToLayer("AdditivePlayer");
        }
        return layer == _playerLayer || layer == _additivePlayerLayer;
    }

    private static bool BelongsToPlayer(Transform? candidate, Transform playerTransform)
    {
        if (candidate is null || candidate.Pointer == IntPtr.Zero)
        {
            return false;
        }

        var candidateRoot = candidate.root;
        var playerRoot = playerTransform.root;
        return candidateRoot is not null
            && playerRoot is not null
            && candidateRoot.Pointer == playerRoot.Pointer;
    }

    private void ResetPlayback()
    {
        _hasDecodeSequence = false;
        _audioOutput.Flush();
        _audioOutput.Service();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        var playbackClip = _audioOutput.clip;
        _audioOutput.Stop();
        _decoder.Dispose();
        if (playbackClip is not null && playbackClip.Pointer != IntPtr.Zero)
        {
            UnityEngine.Object.Destroy(playbackClip);
        }
        UnityEngine.Object.Destroy(_host);
        _jitterBuffer.Reset();
    }
}
