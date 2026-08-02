using UI.Views.Lobby;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SneakOut.PortalModeSelector;

internal sealed record PortalModeUiState(
    PortalPlayView View,
    GameObject RootObject,
    Button ModeButton,
    Image ModeBackground,
    Text ModeLabel,
    UnityAction ModeClickAction,
    Button MapsButton,
    Image MapsBackground,
    Text MapsLabel,
    UnityAction MapsClickAction,
    UnityAction PlayClickAction,
    PortalMapOptionUiState[] MapOptions)
{
    public bool IsAlive =>
        View is not null
        && View.Pointer != IntPtr.Zero
        && RootObject is not null
        && RootObject.Pointer != IntPtr.Zero
        && ModeButton is not null
        && ModeButton.Pointer != IntPtr.Zero
        && ModeBackground is not null
        && ModeBackground.Pointer != IntPtr.Zero
        && ModeLabel is not null
        && ModeLabel.Pointer != IntPtr.Zero
        && ModeClickAction is not null
        && MapsButton is not null
        && MapsButton.Pointer != IntPtr.Zero
        && MapsBackground is not null
        && MapsBackground.Pointer != IntPtr.Zero
        && MapsLabel is not null
        && MapsLabel.Pointer != IntPtr.Zero
        && MapsClickAction is not null
        && PlayClickAction is not null
        && MapOptions is not null
        && MapOptions.All(option => option.IsAlive);
}
