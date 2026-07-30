using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SneakOut.LobbyTestBot;

internal sealed class LobbyTestBotUiState
{
    public LobbyTestBotUiState(
        GameObject rootObject,
        Button button,
        UnityAction clickAction,
        Image verticalSign,
        Image[] iconImages)
    {
        RootObject = rootObject;
        Button = button;
        ClickAction = clickAction;
        VerticalSign = verticalSign;
        IconImages = iconImages;
    }

    public GameObject RootObject { get; }

    public Button Button { get; }

    public UnityAction ClickAction { get; }

    public Image VerticalSign { get; }

    public Image[] IconImages { get; }

    public float NextRefreshTime { get; set; }

    public bool IsAlive =>
        RootObject is not null
        && RootObject.Pointer != IntPtr.Zero
        && Button is not null
        && Button.Pointer != IntPtr.Zero;
}
