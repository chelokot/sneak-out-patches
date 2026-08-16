using BepInEx.Configuration;
using UnityEngine;

namespace SneakOut.ProximityVoiceChat;

internal enum VoiceTransmissionMode
{
    PushToTalk,
    VoiceActivation,
    AlwaysOn,
}

internal sealed class ProximityVoiceChatConfig
{
    public const string DefaultPushToTalkBinding = "<Keyboard>/v";

    private ProximityVoiceChatConfig(
        ConfigEntry<bool> enableMod,
        ConfigEntry<VoiceTransmissionMode> transmissionMode,
        ConfigEntry<string> microphoneDevice,
        ConfigEntry<string> pushToTalkBinding,
        ConfigEntry<bool> stopWhenGameIsUnfocused,
        ConfigEntry<float> voiceActivationThreshold,
        ConfigEntry<float> voiceActivationHangoverSeconds,
        ConfigEntry<float> voiceActivationPreRollSeconds,
        ConfigEntry<float> microphoneVolume,
        ConfigEntry<float> masterVolume,
        ConfigEntry<string> playerVolumes,
        ConfigEntry<bool> directionalVoice,
        ConfigEntry<float> jitterBufferMilliseconds,
        ConfigEntry<float> maximumJitterMilliseconds,
        ConfigEntry<string> mutedSteamIds,
        ConfigEntry<string> additionalPeerSteamIds,
        ConfigEntry<bool> captureSettingsScreenshot,
        ConfigEntry<bool> enableLogging)
    {
        EnableMod = enableMod;
        TransmissionMode = transmissionMode;
        MicrophoneDevice = microphoneDevice;
        PushToTalkBinding = pushToTalkBinding;
        StopWhenGameIsUnfocused = stopWhenGameIsUnfocused;
        VoiceActivationThreshold = voiceActivationThreshold;
        VoiceActivationHangoverSeconds = voiceActivationHangoverSeconds;
        VoiceActivationPreRollSeconds = voiceActivationPreRollSeconds;
        MicrophoneVolume = microphoneVolume;
        MasterVolume = masterVolume;
        PlayerVolumes = playerVolumes;
        DirectionalVoice = directionalVoice;
        JitterBufferMilliseconds = jitterBufferMilliseconds;
        MaximumJitterMilliseconds = maximumJitterMilliseconds;
        MutedSteamIds = mutedSteamIds;
        AdditionalPeerSteamIds = additionalPeerSteamIds;
        CaptureSettingsScreenshot = captureSettingsScreenshot;
        EnableLogging = enableLogging;
    }

    public ConfigEntry<bool> EnableMod { get; }
    public ConfigEntry<VoiceTransmissionMode> TransmissionMode { get; }
    public ConfigEntry<string> MicrophoneDevice { get; }
    public ConfigEntry<string> PushToTalkBinding { get; }
    public ConfigEntry<bool> StopWhenGameIsUnfocused { get; }
    public ConfigEntry<float> VoiceActivationThreshold { get; }
    public ConfigEntry<float> VoiceActivationHangoverSeconds { get; }
    public ConfigEntry<float> VoiceActivationPreRollSeconds { get; }
    public ConfigEntry<float> MicrophoneVolume { get; }
    public ConfigEntry<float> MasterVolume { get; }
    public ConfigEntry<string> PlayerVolumes { get; }
    public ConfigEntry<bool> DirectionalVoice { get; }
    public ConfigEntry<float> JitterBufferMilliseconds { get; }
    public ConfigEntry<float> MaximumJitterMilliseconds { get; }
    public ConfigEntry<string> MutedSteamIds { get; }
    public ConfigEntry<string> AdditionalPeerSteamIds { get; }
    public ConfigEntry<bool> CaptureSettingsScreenshot { get; }
    public ConfigEntry<bool> EnableLogging { get; }

    private readonly Dictionary<ulong, float> _playerVolumeCache = new();
    private string _cachedPlayerVolumesText = string.Empty;

    public float GetPlayerVolume(ulong steamId)
    {
        RefreshPlayerVolumeCache();
        return steamId != 0 && _playerVolumeCache.TryGetValue(steamId, out var volume)
            ? volume
            : Math.Clamp(
                MasterVolume.Value,
                VoicePlayerVolumePolicy.MinimumVolume,
                VoicePlayerVolumePolicy.MaximumVolume);
    }

    public void SetPlayerVolume(ulong steamId, float volume)
    {
        if (steamId == 0 || !float.IsFinite(volume))
        {
            return;
        }

        RefreshPlayerVolumeCache();
        var clamped = Math.Clamp(
            volume,
            VoicePlayerVolumePolicy.MinimumVolume,
            VoicePlayerVolumePolicy.MaximumVolume);
        if (_playerVolumeCache.TryGetValue(steamId, out var existing)
            && Mathf.Approximately(existing, clamped))
        {
            return;
        }

        _playerVolumeCache[steamId] = clamped;
        var serialized = VoicePlayerVolumePolicy.Serialize(_playerVolumeCache);
        _cachedPlayerVolumesText = serialized;
        PlayerVolumes.Value = serialized;
    }

    private void RefreshPlayerVolumeCache()
    {
        var current = PlayerVolumes.Value ?? string.Empty;
        if (string.Equals(current, _cachedPlayerVolumesText, StringComparison.Ordinal))
        {
            return;
        }

        _playerVolumeCache.Clear();
        foreach (var pair in VoicePlayerVolumePolicy.Parse(current))
        {
            _playerVolumeCache[pair.Key] = pair.Value;
        }
        _cachedPlayerVolumesText = current;
    }

    public static ProximityVoiceChatConfig Bind(ConfigFile config)
    {
        var legacyPushToTalkKeyDefinition = new ConfigDefinition("Capture", "PushToTalkKey");
        var legacyTransmitWhileUnfocusedDefinition = new ConfigDefinition("Capture", "TransmitWhileUnfocused");
        var legacySuppressWhileTypingDefinition = new ConfigDefinition("Capture", "SuppressWhileTyping");
        var legacyMinimumDistanceDefinition = new ConfigDefinition("Playback", "MinimumDistance");
        var legacyMaximumDistanceDefinition = new ConfigDefinition("Playback", "MaximumDistance");
        var legacyEnableOcclusionDefinition = new ConfigDefinition("Playback", "EnableOcclusion");
        var legacyOccludedVolumeDefinition = new ConfigDefinition("Playback", "OccludedVolumeMultiplier");
        var legacyOccludedLowPassDefinition = new ConfigDefinition("Playback", "OccludedLowPassFrequency");

        var hasLegacyPushToTalkKey = HasConfigKey(config, legacyPushToTalkKeyDefinition);
        var hasLegacyTransmitWhileUnfocused = HasConfigKey(config, legacyTransmitWhileUnfocusedDefinition);
        var hasLegacySuppressWhileTyping = HasConfigKey(config, legacySuppressWhileTypingDefinition);
        var hasLegacyMinimumDistance = HasConfigKey(config, legacyMinimumDistanceDefinition);
        var hasLegacyMaximumDistance = HasConfigKey(config, legacyMaximumDistanceDefinition);
        var hasLegacyEnableOcclusion = HasConfigKey(config, legacyEnableOcclusionDefinition);
        var hasLegacyOccludedVolume = HasConfigKey(config, legacyOccludedVolumeDefinition);
        var hasLegacyOccludedLowPass = HasConfigKey(config, legacyOccludedLowPassDefinition);

        var migratedPushToTalkBinding = DefaultPushToTalkBinding;
        if (hasLegacyPushToTalkKey)
        {
            var legacyEntry = config.Bind(
                legacyPushToTalkKeyDefinition,
                KeyCode.V,
                new ConfigDescription("Legacy push-to-talk key; migrated to PushToTalkBinding."));
            migratedPushToTalkBinding = ToKeyboardBinding(legacyEntry.Value);
        }

        var migratedStopWhenUnfocused = true;
        if (hasLegacyTransmitWhileUnfocused)
        {
            var legacyEntry = config.Bind(
                legacyTransmitWhileUnfocusedDefinition,
                false,
                new ConfigDescription("Legacy focus setting; migrated to StopWhenGameIsUnfocused."));
            migratedStopWhenUnfocused = !legacyEntry.Value;
        }

        if (hasLegacySuppressWhileTyping)
        {
            config.Bind(
                legacySuppressWhileTypingDefinition,
                true,
                new ConfigDescription("Legacy text-input suppression setting; no longer used."));
        }
        if (hasLegacyMinimumDistance)
        {
            config.Bind(
                legacyMinimumDistanceDefinition,
                2.5f,
                new ConfigDescription("Legacy voice-distance setting; voice falloff is now fixed."));
        }
        if (hasLegacyMaximumDistance)
        {
            config.Bind(
                legacyMaximumDistanceDefinition,
                10f,
                new ConfigDescription("Legacy voice-distance setting; maximum range is now fixed at 20 metres."));
        }
        if (hasLegacyEnableOcclusion)
        {
            config.Bind(
                legacyEnableOcclusionDefinition,
                true,
                new ConfigDescription("Legacy occlusion toggle; corrected wall occlusion is always active."));
        }
        if (hasLegacyOccludedVolume)
        {
            config.Bind(
                legacyOccludedVolumeDefinition,
                0.68f,
                new ConfigDescription("Legacy occlusion tuning; material-aware profiles are now fixed."));
        }
        if (hasLegacyOccludedLowPass)
        {
            config.Bind(
                legacyOccludedLowPassDefinition,
                1800f,
                new ConfigDescription("Legacy occlusion tuning; material-aware profiles are now fixed."));
        }

        var enableMod = config.Bind("General", "Enabled", true, "Enable proximity voice chat.");
        var transmissionMode = config.Bind(
            "Capture",
            "TransmissionMode",
            VoiceTransmissionMode.PushToTalk,
            "PushToTalk, VoiceActivation, or AlwaysOn.");
        var microphoneDevice = config.Bind(
            "Capture",
            "MicrophoneDevice",
            string.Empty,
            "Unity microphone device name. Empty uses the current system default.");
        var pushToTalkBinding = config.Bind(
            "Capture",
            "PushToTalkBinding",
            migratedPushToTalkBinding,
            "Unity Input System keyboard binding used by push-to-talk. Record a key in Audio settings.");
        var stopWhenGameIsUnfocused = config.Bind(
            "Capture",
            "StopWhenGameIsUnfocused",
            migratedStopWhenUnfocused,
            "Stop microphone transmission while the game window is not focused.");
        var voiceActivationThreshold = config.Bind(
            "Capture",
            "VoiceActivationThreshold",
            0.018f,
            new ConfigDescription("RMS threshold for voice activation.", new AcceptableValueRange<float>(0.002f, 0.25f)));
        var voiceActivationHangoverSeconds = config.Bind(
            "Capture",
            "VoiceActivationHangoverSeconds",
            0.35f,
            new ConfigDescription("Keep transmitting after speech falls below the threshold.", new AcceptableValueRange<float>(0.05f, 2f)));
        var voiceActivationPreRollSeconds = config.Bind(
            "Capture",
            "VoiceActivationPreRollSeconds",
            0.16f,
            new ConfigDescription("Buffered audio sent before voice activation opens.", new AcceptableValueRange<float>(0f, 0.5f)));
        var microphoneVolume = config.Bind(
            "Capture",
            "MicrophoneVolume",
            1f,
            new ConfigDescription(
                "Outgoing microphone volume applied before Opus encoding.",
                new AcceptableValueRange<float>(
                    VoicePlayerVolumePolicy.MinimumVolume,
                    VoicePlayerVolumePolicy.MaximumVolume)));
        var masterVolume = config.Bind(
            "Playback",
            "MasterVolume",
            1f,
            new ConfigDescription(
                "Default voice volume for players without a saved per-player override; 100% is the original receive baseline.",
                new AcceptableValueRange<float>(
                    VoicePlayerVolumePolicy.MinimumVolume,
                    VoicePlayerVolumePolicy.MaximumVolume)));
        var playerVolumes = config.Bind(
            "Playback",
            "PlayerVolumes",
            string.Empty,
            "Saved per-player voice volumes as SteamID64=multiplier pairs.");
        var directionalVoice = config.Bind(
            "Playback",
            "DirectionalVoice",
            true,
            "Pan remote voices toward their world-space direction. Distance and occlusion volume remain active when disabled.");
        var jitterBufferMilliseconds = config.Bind(
            "Playback",
            "JitterBufferMilliseconds",
            110f,
            new ConfigDescription("Initial playout buffer. Higher values tolerate unstable connections at the cost of latency.", new AcceptableValueRange<float>(40f, 400f)));
        var maximumJitterMilliseconds = config.Bind(
            "Playback",
            "MaximumJitterMilliseconds",
            280f,
            new ConfigDescription("Maximum adaptive playout delay.", new AcceptableValueRange<float>(100f, 1000f)));
        var mutedSteamIds = config.Bind(
            "Privacy",
            "MutedSteamIds",
            string.Empty,
            "Comma-separated SteamID64 values that should never be played.");
        var additionalPeerSteamIds = config.Bind(
            "Networking",
            "AdditionalPeerSteamIds",
            string.Empty,
            "Optional comma-separated SteamID64 peers used when the current client cannot expose their platform id.");
        var captureSettingsScreenshot = config.Bind(
            "Diagnostics",
            "CaptureSettingsScreenshot",
            false,
            "Open the stock audio settings once and capture the proximity voice controls for unattended visual regression testing.");
        var enableLogging = config.Bind(
            "Diagnostics",
            "EnableLogging",
            false,
            "Write lifecycle and transport diagnostics without logging voice payloads.");

        if (hasLegacyPushToTalkKey
            || hasLegacyTransmitWhileUnfocused
            || hasLegacySuppressWhileTyping
            || hasLegacyMinimumDistance
            || hasLegacyMaximumDistance
            || hasLegacyEnableOcclusion
            || hasLegacyOccludedVolume
            || hasLegacyOccludedLowPass)
        {
            config.Remove(legacyPushToTalkKeyDefinition);
            config.Remove(legacyTransmitWhileUnfocusedDefinition);
            config.Remove(legacySuppressWhileTypingDefinition);
            config.Remove(legacyMinimumDistanceDefinition);
            config.Remove(legacyMaximumDistanceDefinition);
            config.Remove(legacyEnableOcclusionDefinition);
            config.Remove(legacyOccludedVolumeDefinition);
            config.Remove(legacyOccludedLowPassDefinition);
            config.Save();
        }

        return new ProximityVoiceChatConfig(
            enableMod,
            transmissionMode,
            microphoneDevice,
            pushToTalkBinding,
            stopWhenGameIsUnfocused,
            voiceActivationThreshold,
            voiceActivationHangoverSeconds,
            voiceActivationPreRollSeconds,
            microphoneVolume,
            masterVolume,
            playerVolumes,
            directionalVoice,
            jitterBufferMilliseconds,
            maximumJitterMilliseconds,
            mutedSteamIds,
            additionalPeerSteamIds,
            captureSettingsScreenshot,
            enableLogging);
    }

    private static string ToKeyboardBinding(KeyCode keyCode)
    {
        var controlName = keyCode.ToString() switch
        {
            "LeftControl" => "leftCtrl",
            "RightControl" => "rightCtrl",
            "Return" => "enter",
            "Alpha0" => "digit0",
            "Alpha1" => "digit1",
            "Alpha2" => "digit2",
            "Alpha3" => "digit3",
            "Alpha4" => "digit4",
            "Alpha5" => "digit5",
            "Alpha6" => "digit6",
            "Alpha7" => "digit7",
            "Alpha8" => "digit8",
            "Alpha9" => "digit9",
            var value when value.Length > 0 => char.ToLowerInvariant(value[0]) + value[1..],
            _ => "v",
        };
        return $"<Keyboard>/{controlName}";
    }

    private static bool HasConfigKey(ConfigFile config, ConfigDefinition definition)
    {
        try
        {
            var configPath = config.GetType()
                .GetProperty("ConfigFilePath")?
                .GetValue(config) as string;
            if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
            {
                return false;
            }

            var currentSection = string.Empty;
            foreach (var rawLine in File.ReadLines(configPath))
            {
                var line = rawLine.Trim();
                if (line.StartsWith("[", StringComparison.Ordinal)
                    && line.EndsWith("]", StringComparison.Ordinal))
                {
                    currentSection = line[1..^1].Trim();
                    continue;
                }
                if (!string.Equals(currentSection, definition.Section, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var equalsIndex = line.IndexOf('=');
                if (equalsIndex > 0
                    && string.Equals(
                        line[..equalsIndex].Trim(),
                        definition.Key,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch
        {
            // A missing or transiently unavailable config file just means there is nothing to migrate.
        }
        return false;
    }
}
