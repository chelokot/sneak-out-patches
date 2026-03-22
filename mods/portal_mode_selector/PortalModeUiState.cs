using TMPro;
using Types;
using UI.Buttons;
using UI.Views.Lobby;
using UnityEngine;

namespace SneakOut.PortalModeSelector;

internal sealed record PortalModeUiState(
    PortalPlayView View,
    GameObject RoleSectionObject,
    GameObject RoleRowObject,
    GameObject PrivateSectionObject,
    GameObject PrivateRowObject,
    GameObject ModeSectionObject,
    GameObject ModeRowObject,
    SpookedOutlineButton ModeButton,
    TMP_Text LabelText,
    TMP_Text LeftText,
    TMP_Text RightText
)
{
    public bool IsAlive =>
        View is not null
        && View.Pointer != IntPtr.Zero
        && RoleSectionObject is not null
        && RoleSectionObject.Pointer != IntPtr.Zero
        && RoleRowObject is not null
        && RoleRowObject.Pointer != IntPtr.Zero
        && PrivateSectionObject is not null
        && PrivateSectionObject.Pointer != IntPtr.Zero
        && PrivateRowObject is not null
        && PrivateRowObject.Pointer != IntPtr.Zero
        && ModeSectionObject is not null
        && ModeSectionObject.Pointer != IntPtr.Zero
        && ModeRowObject is not null
        && ModeRowObject.Pointer != IntPtr.Zero
        && ModeButton is not null
        && ModeButton.Pointer != IntPtr.Zero
        && LabelText is not null
        && LabelText.Pointer != IntPtr.Zero
        && LeftText is not null
        && LeftText.Pointer != IntPtr.Zero
        && RightText is not null
        && RightText.Pointer != IntPtr.Zero;

    public GameModeType SelectedMode => PortalModeSelectorRuntime.GetSelectedMode(View);
}
