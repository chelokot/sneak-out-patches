using BepInEx.Configuration;

namespace SneakOut.FirstPersonExperiment;

internal sealed class FirstPersonExperimentConfig
{
    private FirstPersonExperimentConfig(
        ConfigEntry<bool> enableMod,
        ConfigEntry<float> headHeight,
        ConfigEntry<float> forwardOffset,
        ConfigEntry<float> eyeVerticalOffset,
        ConfigEntry<float> eyeForwardOffset,
        ConfigEntry<float> pitch,
        ConfigEntry<float> mouseYawSensitivity,
        ConfigEntry<float> mousePitchSensitivity,
        ConfigEntry<float> lookUpLimit,
        ConfigEntry<float> lookDownLimit,
        ConfigEntry<float> taskCameraPullback)
    {
        EnableMod = enableMod;
        HeadHeight = headHeight;
        ForwardOffset = forwardOffset;
        EyeVerticalOffset = eyeVerticalOffset;
        EyeForwardOffset = eyeForwardOffset;
        Pitch = pitch;
        MouseYawSensitivity = mouseYawSensitivity;
        MousePitchSensitivity = mousePitchSensitivity;
        LookUpLimit = lookUpLimit;
        LookDownLimit = lookDownLimit;
        TaskCameraPullback = taskCameraPullback;
    }

    public ConfigEntry<bool> EnableMod { get; }

    public ConfigEntry<float> HeadHeight { get; }

    public ConfigEntry<float> ForwardOffset { get; }

    public ConfigEntry<float> EyeVerticalOffset { get; }

    public ConfigEntry<float> EyeForwardOffset { get; }

    public ConfigEntry<float> Pitch { get; }

    public ConfigEntry<float> MouseYawSensitivity { get; }

    public ConfigEntry<float> MousePitchSensitivity { get; }

    public ConfigEntry<float> LookUpLimit { get; }

    public ConfigEntry<float> LookDownLimit { get; }

    public ConfigEntry<float> TaskCameraPullback { get; }

    public static FirstPersonExperimentConfig Bind(ConfigFile configFile)
    {
        var enableMod = configFile.Bind(
            "general",
            "EnableMod",
            true,
            "Move the stock gameplay camera to the local player's head.");
        var headHeight = configFile.Bind(
            "camera",
            "HeadHeight",
            1.8f,
            "Fallback camera height above the player root when the animated eye bones are unavailable.");
        var forwardOffset = configFile.Bind(
            "camera",
            "ForwardOffset",
            -0.6f,
            "Fallback camera offset along player facing when the animated eye bones are unavailable.");
        var eyeVerticalOffset = configFile.Bind(
            "camera",
            "EyeVerticalOffset",
            0f,
            "Vertical adjustment from the stabilized eye anchor.");
        var eyeForwardOffset = configFile.Bind(
            "camera",
            "EyeForwardOffset",
            0f,
            "Adjustment from the stabilized eye anchor along the current viewing direction.");
        var pitch = configFile.Bind(
            "camera",
            "Pitch",
            0f,
            "Initial first-person vertical viewing angle in degrees. Positive values look down.");
        var mouseYawSensitivity = configFile.Bind(
            "controls",
            "MouseYawSensitivity",
            0.15f,
            "Horizontal first-person rotation in degrees per mouse-delta unit.");
        var mousePitchSensitivity = configFile.Bind(
            "controls",
            "MousePitchSensitivity",
            0.15f,
            "Vertical first-person rotation in degrees per mouse-delta unit.");
        var lookUpLimit = configFile.Bind(
            "controls",
            "LookUpLimit",
            10f,
            "Maximum number of degrees the first-person camera can look upward.");
        var lookDownLimit = configFile.Bind(
            "controls",
            "LookDownLimit",
            85f,
            "Maximum number of degrees the first-person camera can look downward.");
        var taskCameraPullback = configFile.Bind(
            "camera",
            "TaskCameraPullback",
            2f,
            "Distance to pull dedicated task cameras backward so the task remains visible.");

        return new FirstPersonExperimentConfig(
            enableMod,
            headHeight,
            forwardOffset,
            eyeVerticalOffset,
            eyeForwardOffset,
            pitch,
            mouseYawSensitivity,
            mousePitchSensitivity,
            lookUpLimit,
            lookDownLimit,
            taskCameraPullback);
    }
}
