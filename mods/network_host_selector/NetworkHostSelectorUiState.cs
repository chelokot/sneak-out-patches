using TMPro;
using UI.Buttons;
using UI.Views.Lobby;
using UnityEngine;
using UnityEngine.Events;

namespace SneakOut.NetworkHostSelector;

internal sealed record NetworkHostSelectorUiState(
    PortalPlayView View,
    GameObject RootObject,
    SpookedOutlineButton Button,
    TMP_Text Label,
    UnityAction ClickAction)
{
    public bool IsAlive => View is not null
        && View.Pointer != IntPtr.Zero
        && RootObject is not null
        && RootObject.Pointer != IntPtr.Zero
        && Button is not null
        && Button.Pointer != IntPtr.Zero
        && Label is not null
        && Label.Pointer != IntPtr.Zero;
}
