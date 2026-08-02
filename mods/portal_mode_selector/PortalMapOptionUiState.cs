using TMPro;
using Types;
using UI.Buttons;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SneakOut.PortalModeSelector;

internal sealed record PortalMapOptionUiState(
    SceneType SceneType,
    GameModeType GameModeType,
    GameObject RootObject,
    Image Background,
    TMP_Text Label,
    SpookedOutlineButton Button,
    UnityAction ClickAction)
{
    public bool IsAlive =>
        RootObject is not null
        && RootObject.Pointer != IntPtr.Zero
        && Background is not null
        && Background.Pointer != IntPtr.Zero
        && Label is not null
        && Label.Pointer != IntPtr.Zero
        && Button is not null
        && Button.Pointer != IntPtr.Zero
        && ClickAction is not null;
}
