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
        TMP_Text label,
        GameObject roleRootObject,
        SpookedOutlineButton roleButton,
        UnityAction roleClickAction,
        TMP_Text roleLabel)
    {
        RootObject = rootObject;
        Button = button;
        ClickAction = clickAction;
        Label = label;
        RoleRootObject = roleRootObject;
        RoleButton = roleButton;
        RoleClickAction = roleClickAction;
        RoleLabel = roleLabel;
    }

    public GameObject RootObject { get; }

    public SpookedOutlineButton Button { get; }

    public UnityAction ClickAction { get; }

    public TMP_Text Label { get; }

    public GameObject RoleRootObject { get; }

    public SpookedOutlineButton RoleButton { get; }

    public UnityAction RoleClickAction { get; }

    public TMP_Text RoleLabel { get; }

    public float NextRefreshTime { get; set; }

    public bool IsAlive =>
        RootObject is not null
        && RootObject.Pointer != IntPtr.Zero
        && Button is not null
        && Button.Pointer != IntPtr.Zero
        && Label is not null
        && Label.Pointer != IntPtr.Zero
        && RoleRootObject is not null
        && RoleRootObject.Pointer != IntPtr.Zero
        && RoleButton is not null
        && RoleButton.Pointer != IntPtr.Zero
        && RoleLabel is not null
        && RoleLabel.Pointer != IntPtr.Zero;
}
