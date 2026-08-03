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
    private ProximityVoiceChatConfig(
        ConfigEntry<bool> enableMod,
        ConfigEntry<VoiceTransmissionMode> transmissionMode,
        ConfigEntry<KeyCode> pushToTalkKey,
        ConfigEntry<bool> transmitWhileUnfocused,
        ConfigEntry<bool> suppressWhileTyping,
        ConfigEntry<float> voiceActivationThreshold,
        ConfigEntry<float> voiceActivationHangoverSeconds,
        ConfigEntry<float> voiceActivationPreRollSeconds,
        ConfigEntry<float> masterVolume,
        ConfigEntry<float> minimumDistance,
        ConfigEntry<float> maximumDistance,
        ConfigEntry<float> jitterBufferMilliseconds,
        ConfigEntry<float> maximumJitterMilliseconds,
        ConfigEntry<bool> enableOcclusion,
        ConfigEntry<float> occludedVolumeMultiplier,
        ConfigEntry<float> occludedLowPassFrequency,
        ConfigEntry<string> mutedSteamIds,
        ConfigEntry<string> additionalPeerSteamIds,
        ConfigEntry<bool> captureSettingsScreenshot,
        ConfigEntry<bool> enableLogging)
    {
        EnableMod = enableMod;
        TransmissionMode = transmissionMode;
        PushToTalkKey = pushToTalkKey;
        TransmitWhileUnfocused = transmitWhileUnfocused;
        SuppressWhileTyping = suppressWhileTyping;
        VoiceActivationThreshold = voiceActivationThreshold;
        VoiceActivationHangoverSeconds = voiceActivationHangoverSeconds;
        VoiceActivationPreRollSeconds = voiceActivationPreRollSeconds;
        MasterVolume = masterVolume;
        MinimumDistance = minimumDistance;
        MaximumDistance = maximumDistance;
        JitterBufferMilliseconds = jitterBufferMilliseconds;
        MaximumJitterMilliseconds = maximumJitterMilliseconds;
        EnableOcclusion = enableOcclusion;
        OccludedVolumeMultiplier = occludedVolumeMultiplier;
        OccludedLowPassFrequency = occludedLowPassFrequency;
        MutedSteamIds = mutedSteamIds;
        AdditionalPeerSteamIds = additionalPeerSteamIds;
        CaptureSettingsScreenshot = captureSettingsScreenshot;
        EnableLogging = enableLogging;
    }

    public ConfigEntry<bool> EnableMod { get; }
    public ConfigEntry<VoiceTransmissionMode> TransmissionMode { get; }
    public ConfigEntry<KeyCode> PushToTalkKey { get; }
    public ConfigEntry<bool> TransmitWhileUnfocused { get; }
    public ConfigEntry<bool> SuppressWhileTyping { get; }
    public ConfigEntry<float> VoiceActivationThreshold { get; }
    public ConfigEntry<float> VoiceActivationHangoverSeconds { get; }
    public ConfigEntry<float> VoiceActivationPreRollSeconds { get; }
    public ConfigEntry<float> MasterVolume { get; }
    public ConfigEntry<float> MinimumDistance { get; }
    public ConfigEntry<float> MaximumDistance { get; }
    public ConfigEntry<float> JitterBufferMilliseconds { get; }
    public ConfigEntry<float> MaximumJitterMilliseconds { get; }
    public ConfigEntry<bool> EnableOcclusion { get; }
    public ConfigEntry<float> OccludedVolumeMultiplier { get; }
    public ConfigEntry<float> OccludedLowPassFrequency { get; }
    public ConfigEntry<string> MutedSteamIds { get; }
    public ConfigEntry<string> AdditionalPeerSteamIds { get; }
    public ConfigEntry<bool> CaptureSettingsScreenshot { get; }
    public ConfigEntry<bool> EnableLogging { get; }

    public static ProximityVoiceChatConfig Bind(ConfigFile config)
    {
        return new ProximityVoiceChatConfig(
            config.Bind("General", "Enabled", true, "Enable proximity voice chat."),
            config.Bind(
                "Capture",
                "TransmissionMode",
                VoiceTransmissionMode.PushToTalk,
                "PushToTalk, VoiceActivation, or AlwaysOn."),
            config.Bind(
                "Capture",
                "PushToTalkKey",
                KeyCode.V,
                "Physical key used by push-to-talk, independent of the active keyboard layout."),
            config.Bind(
                "Capture",
                "TransmitWhileUnfocused",
                false,
                "Allow microphone transmission while the game window is not focused."),
            config.Bind(
                "Capture",
                "SuppressWhileTyping",
                true,
                "Never transmit while a text input field is selected."),
            config.Bind(
                "Capture",
                "VoiceActivationThreshold",
                0.018f,
                new ConfigDescription("RMS threshold for voice activation.", new AcceptableValueRange<float>(0.002f, 0.25f))),
            config.Bind(
                "Capture",
                "VoiceActivationHangoverSeconds",
                0.35f,
                new ConfigDescription("Keep transmitting after speech falls below the threshold.", new AcceptableValueRange<float>(0.05f, 2f))),
            config.Bind(
                "Capture",
                "VoiceActivationPreRollSeconds",
                0.16f,
                new ConfigDescription("Buffered audio sent before voice activation opens.", new AcceptableValueRange<float>(0f, 0.5f))),
            config.Bind(
                "Playback",
                "MasterVolume",
                1f,
                new ConfigDescription("Voice volume multiplier.", new AcceptableValueRange<float>(0f, 2f))),
            config.Bind(
                "Playback",
                "MinimumDistance",
                2.5f,
                new ConfigDescription("Distance at which voices play at full volume.", new AcceptableValueRange<float>(0.5f, 20f))),
            config.Bind(
                "Playback",
                "MaximumDistance",
                18f,
                new ConfigDescription("Distance at which voices become inaudible.", new AcceptableValueRange<float>(2f, 80f))),
            config.Bind(
                "Playback",
                "JitterBufferMilliseconds",
                110f,
                new ConfigDescription("Initial playout buffer. Higher values tolerate unstable connections at the cost of latency.", new AcceptableValueRange<float>(40f, 400f))),
            config.Bind(
                "Playback",
                "MaximumJitterMilliseconds",
                280f,
                new ConfigDescription("Maximum adaptive playout delay.", new AcceptableValueRange<float>(100f, 1000f))),
            config.Bind(
                "Playback",
                "EnableOcclusion",
                true,
                "Muffle voices when level geometry blocks the direct path."),
            config.Bind(
                "Playback",
                "OccludedVolumeMultiplier",
                0.68f,
                new ConfigDescription("Volume multiplier behind geometry.", new AcceptableValueRange<float>(0f, 1f))),
            config.Bind(
                "Playback",
                "OccludedLowPassFrequency",
                1800f,
                new ConfigDescription("Low-pass cutoff behind geometry.", new AcceptableValueRange<float>(500f, 8000f))),
            config.Bind(
                "Privacy",
                "MutedSteamIds",
                string.Empty,
                "Comma-separated SteamID64 values that should never be played."),
            config.Bind(
                "Networking",
                "AdditionalPeerSteamIds",
                string.Empty,
                "Optional comma-separated SteamID64 peers used when the current client cannot expose their platform id."),
            config.Bind(
                "Diagnostics",
                "CaptureSettingsScreenshot",
                false,
                "Open the stock audio settings once and capture the proximity voice controls for unattended visual regression testing."),
            config.Bind("Diagnostics", "EnableLogging", false, "Write lifecycle and transport diagnostics without logging voice payloads."));
    }
}
