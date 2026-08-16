namespace SneakOut.ProximityVoiceChat;

internal static class VoiceMicrophoneDevicePolicy
{
    public const string SystemDefaultLabel = "System default";

    public static string[] NormalizeDevices(IEnumerable<string> devices)
    {
        return devices
            .Where(device => !string.IsNullOrWhiteSpace(device))
            .Select(device => device.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    public static int GetSelectionIndex(
        string? selectedDevice,
        IReadOnlyList<string> availableDevices)
    {
        if (string.IsNullOrWhiteSpace(selectedDevice))
        {
            return 0;
        }

        for (var index = 0; index < availableDevices.Count; index++)
        {
            if (string.Equals(availableDevices[index], selectedDevice, StringComparison.Ordinal))
            {
                return index + 1;
            }
        }
        return 0;
    }

    public static string GetSelection(
        int dropdownIndex,
        IReadOnlyList<string> availableDevices)
    {
        var deviceIndex = dropdownIndex - 1;
        return deviceIndex >= 0 && deviceIndex < availableDevices.Count
            ? availableDevices[deviceIndex]
            : string.Empty;
    }

    public static string? ResolveCaptureDevice(
        string? selectedDevice,
        IReadOnlyList<string> availableDevices)
    {
        var selectionIndex = GetSelectionIndex(selectedDevice, availableDevices);
        return selectionIndex > 0 ? availableDevices[selectionIndex - 1] : null;
    }

    public static string GetDisplayName(string? device)
    {
        return string.IsNullOrWhiteSpace(device) ? SystemDefaultLabel : device;
    }
}
