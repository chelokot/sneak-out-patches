using TMPro;
using UI.Buttons;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SneakOut.LobbyTestBot;

internal sealed class LobbyTestBotUiState
{
    public LobbyTestBotUiState(
        GameObject rootObject,
        SpookedOutlineButton button,
        UnityAction clickAction,
        TMP_Text label)
    {
        RootObject = rootObject;
        Button = button;
        ClickAction = clickAction;
        Label = label;
    }

    public GameObject RootObject { get; }

    public SpookedOutlineButton Button { get; }

    public UnityAction ClickAction { get; }

    public TMP_Text Label { get; }

    public float NextRefreshTime { get; set; }

    public bool IsAlive =>
        RootObject is not null
        && RootObject.Pointer != IntPtr.Zero
        && Button is not null
        && Button.Pointer != IntPtr.Zero
        && Label is not null
        && Label.Pointer != IntPtr.Zero;
}
