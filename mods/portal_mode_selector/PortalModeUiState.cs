using TMPro;
using SneakOut.PortalSettings;
using UI.Buttons;
using UI.Views.Lobby;
using UnityEngine;
using UnityEngine.Events;

namespace SneakOut.PortalModeSelector;

internal sealed record PortalModeUiState(
    PortalPlayView View,
    GameObject ModeSection,
    TMP_Text ModeTitle,
    NativePortalSwitch ModeSwitch,
    UnityAction ModeClickAction,
    GameObject MapsSection,
    TMP_Text MapsTitle,
    PortalMapOptionUiState[] MapOptions)
{
    public bool IsAlive =>
        View is not null
        && View.Pointer != IntPtr.Zero
        && ModeSection is not null
        && ModeSection.Pointer != IntPtr.Zero
        && ModeTitle is not null
        && ModeTitle.Pointer != IntPtr.Zero
        && ModeSwitch.IsAlive
        && ModeClickAction is not null
        && MapsSection is not null
        && MapsSection.Pointer != IntPtr.Zero
        && MapsTitle is not null
        && MapsTitle.Pointer != IntPtr.Zero
        && MapOptions is not null
        && MapOptions.All(option => option.IsAlive);
}
