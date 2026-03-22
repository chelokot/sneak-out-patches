using TMPro;
using Types;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SneakOut.PortalModeSelector;

internal sealed record PortalMapOptionUiState(
    SceneType SceneType,
    GameModeType GameModeType,
    GameObject RootObject,
    Image RowBackgroundImage,
    Image CheckboxOutlineImage,
    Image CheckboxFillImage,
    TMP_Text LabelText,
    Button Button,
    UnityAction ClickAction
)
{
    public bool IsAlive =>
        RootObject is not null
        && RootObject.Pointer != IntPtr.Zero
        && RowBackgroundImage is not null
        && RowBackgroundImage.Pointer != IntPtr.Zero
        && CheckboxOutlineImage is not null
        && CheckboxOutlineImage.Pointer != IntPtr.Zero
        && CheckboxFillImage is not null
        && CheckboxFillImage.Pointer != IntPtr.Zero
        && LabelText is not null
        && LabelText.Pointer != IntPtr.Zero
        && Button is not null
        && Button.Pointer != IntPtr.Zero
        && ClickAction is not null;
}
