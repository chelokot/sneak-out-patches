using UnityEngine;
using UnityEngine.InputSystem;

namespace SneakOut.FirstPersonExperiment;

internal static class FirstPersonCursorRuntime
{
    private static bool _ownsCursor;
    private static CursorLockMode _previousLockMode;
    private static bool _previousVisibility;

    public static bool IsReleaseHeld
    {
        get
        {
            var xKey = Keyboard.current?.xKey;
            return xKey is not null && xKey.isPressed;
        }
    }

    public static bool ShouldSuspendLook
    {
        get
        {
            var xKey = Keyboard.current?.xKey;
            return xKey is not null && (xKey.isPressed || xKey.wasReleasedThisFrame);
        }
    }

    public static void Update(bool uiOpen)
    {
        if (!_ownsCursor)
        {
            _previousLockMode = Cursor.lockState;
            _previousVisibility = Cursor.visible;
            _ownsCursor = true;
        }

        if (uiOpen || IsReleaseHeld)
        {
            if (Cursor.lockState != CursorLockMode.None)
            {
                Cursor.lockState = CursorLockMode.None;
            }

            if (!Cursor.visible)
            {
                Cursor.visible = true;
            }
            return;
        }

        if (Cursor.lockState != CursorLockMode.Locked)
        {
            Cursor.lockState = CursorLockMode.Locked;
        }

        if (Cursor.visible)
        {
            Cursor.visible = false;
        }
    }

    public static void Restore()
    {
        if (!_ownsCursor)
        {
            return;
        }

        Cursor.lockState = _previousLockMode;
        Cursor.visible = _previousVisibility;
        _ownsCursor = false;
    }
}
