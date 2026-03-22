using TMPro;
using Types;
using UI.Buttons;
using UI.Views.Lobby;
using UnityEngine;
using UnityEngine.Events;

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
    UnityAction ModeClickAction,
    TMP_Text LabelText,
    TMP_Text LeftText,
    TMP_Text RightText,
    GameObject LeftObject,
    GameObject RightObject,
    RectTransform LeftMovingPanel,
    RectTransform RightMovingPanel,
    float LeftClassicX,
    float LeftCrownX,
    float RightClassicX,
    float RightCrownX,
    GameObject PlaySectionObject,
    GameObject ContentRootObject,
    GameObject PopupRootObject,
    Vector2 OriginalRoleSectionPosition,
    Vector2 OriginalPrivateSectionPosition,
    Vector2 OriginalPlaySectionPosition,
    int OriginalRoleSectionSiblingIndex,
    Vector2 OriginalContentPosition,
    Vector2 OriginalContentSize,
    Vector2 OriginalPopupPosition,
    Vector2 OriginalPopupSize
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
        && ModeClickAction is not null
        && LabelText is not null
        && LabelText.Pointer != IntPtr.Zero
        && LeftText is not null
        && LeftText.Pointer != IntPtr.Zero
        && RightText is not null
        && RightText.Pointer != IntPtr.Zero
        && LeftObject is not null
        && LeftObject.Pointer != IntPtr.Zero
        && RightObject is not null
        && RightObject.Pointer != IntPtr.Zero
        && LeftMovingPanel is not null
        && LeftMovingPanel.Pointer != IntPtr.Zero
        && RightMovingPanel is not null
        && RightMovingPanel.Pointer != IntPtr.Zero
        && PlaySectionObject is not null
        && PlaySectionObject.Pointer != IntPtr.Zero
        && ContentRootObject is not null
        && ContentRootObject.Pointer != IntPtr.Zero
        && PopupRootObject is not null
        && PopupRootObject.Pointer != IntPtr.Zero;

    public GameModeType SelectedMode => PortalModeSelectorRuntime.GetSelectedMode(View);
}
