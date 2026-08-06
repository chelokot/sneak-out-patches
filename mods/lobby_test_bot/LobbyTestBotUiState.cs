using SneakOut.PortalSettings;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace SneakOut.LobbyTestBot;

internal sealed class LobbyTestBotUiState
{
    public LobbyTestBotUiState(
        UI.Views.Lobby.PortalPlayView view,
        GameObject section,
        TMP_Text title,
        NativePortalSwitch dummySwitch,
        NativePortalSwitch roleSwitch,
        UnityAction dummyClickAction,
        UnityAction roleClickAction)
    {
        View = view;
        Section = section;
        Title = title;
        DummySwitch = dummySwitch;
        RoleSwitch = roleSwitch;
        DummyClickAction = dummyClickAction;
        RoleClickAction = roleClickAction;
    }

    public UI.Views.Lobby.PortalPlayView View { get; }

    public GameObject Section { get; }

    public TMP_Text Title { get; }

    public NativePortalSwitch DummySwitch { get; }

    public NativePortalSwitch RoleSwitch { get; }

    public UnityAction DummyClickAction { get; }

    public UnityAction RoleClickAction { get; }

    public float NextRefreshTime { get; set; }

    public bool IsAlive =>
        View is not null
        && View.Pointer != IntPtr.Zero
        && Section is not null
        && Section.Pointer != IntPtr.Zero
        && Title is not null
        && Title.Pointer != IntPtr.Zero
        && DummySwitch.IsAlive
        && RoleSwitch.IsAlive;
}
