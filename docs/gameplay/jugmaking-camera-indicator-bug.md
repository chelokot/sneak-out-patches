# JugMaking Camera And Seeker Indicator Bug

## Symptom

During the `JugMaking` task, the camera moves into the close task view as expected, but the "seen by seeker" side indicator appears inverted:

- seeker is physically on the left
- indicator points to the right

The reported behavior is that this happens consistently or almost consistently while the pottery wheel / jug-making interaction camera is active.

## Confirmed task-side facts

### The task has a dedicated task camera point

`JugMakingTask` contains a dedicated serialized transform:

- `_cameraTaskPointTransform` at field offset `0x1F0`

Relevant class:

- `Gameplay.Interactions.Tasks.JugMaking.JugMakingTask`
- RVA references in `dump.cs`:
  - `StartInteraction(int internalId)` at `0x1806F22D0`
  - `StopInteraction(int internalId)` at `0x1806F2530`

### StartInteraction explicitly activates a task camera

Inside `JugMakingTask.StartInteraction()`:

- the task opens its minigame UI
- it applies buff `BlockInputsForJugMaking` (`SpookedBuffType 188`)
- it activates the task camera through `CameraManager.ActivateTaskCamera(_cameraTaskPointTransform)`

Relevant call chain:

- `JugMakingTask.StartInteraction()` at `0x1806F22D0`
- `CameraManager.ActivateTaskCamera(Transform taskCameraPoint)` at `0x1805F6C00`

`CameraManager.ActivateTaskCamera()` is only a thin forwarder:

- it takes the current `SceneCameraManager`
- forwards the provided task camera transform into scene-camera logic

This means the pottery task does not fake a zoom inside the normal follow camera. It really switches to a dedicated task camera target/path.

### StopInteraction deactivates that camera again

`JugMakingTask.StopInteraction()` calls the inverse camera path:

- `CameraManager.DeactivateTaskCamera()` at `0x1805F6D20`

So the close-up state is a distinct camera mode, not just a temporary offset.

## Confirmed indicator-side facts

The victim HUD / entity canvas owns the seeker warning image:

- `_seenBySeekerIndicator` field exists on the player canvas object
- the update method is `HandleSeenBySeekerIndicator()` at `0x18063EBE0`

What matters here is more specific than the earlier broad camera-space hypothesis.

### `HandleSeenBySeekerIndicator()` does not compute a left/right angle directly

The key call chain inside `EntityCanvasComponent.HandleSeenBySeekerIndicator()` is:

- `UnityEngine.Object` null/equality helper at `0x183219EB0`
- `UnityEngine.Component::get_gameObject()` at `0x18320FB30`
- `SpookedNetworkPlayer.get_PlayerMobilityState()` at `0x1806886F0`
- `UnityEngine.GameObject::SetActive(System.Boolean)` at `0x183213DF0`

The relevant logic shape is:

- if the indicator component is valid, get its `GameObject`
- read `SpookedNetworkPlayer.PlayerMobilityState`
- compare it to `2`
- call `GameObject.SetActive(al == 2)`

That comparison value is confirmed from `dump.cs`:

- `PlayerMobilityState.None = 0`
- `PlayerMobilityState.Moving = 1`
- `PlayerMobilityState.NotMoving = 2`

So the seeker warning path is not taking a hunter position vector and deciding "left" vs "right" inside this method. It is toggling a dedicated warning object based on whether the player is in the `NotMoving` mobility state.

### `PlayerMobilityState` is not generic noise here

`SpookedNetworkPlayer.get_PlayerMobilityState()` is a thin getter over the network state byte at offset `0x188`, and the matching setter writes that same byte:

- `get_PlayerMobilityState()` at `0x1806886F0`
- `set_PlayerMobilityState()` at `0x180689640`

In the current binary, the getter is referenced only from this seeker-indicator path. That makes the dependency look intentional rather than incidental.

### The indicator object itself does not contain left/right substructure

Asset inspection of `Sneak Out_Data/resources.assets` confirms that:

- `SeenBySeekerIndicator` is a single `GameObject`
- it has no child transforms
- it sits under `TopPanel -> PlayerCanvas -> UnityPlayer`
- its `RectTransform` is static rather than procedural

Observed prefab layout:

- `UnityPlayer` (`path_id 2143`)
  - `PlayerCanvas` (`path_id 3093`)
    - `TopPanel` (`path_id 2225`)
      - `SeenBySeekerIndicator` (`path_id 2799`)

Observed `RectTransform` values for `SeenBySeekerIndicator`:

- anchored position `(2.4, -0.318)`
- size `(0.3, 0.3)`
- anchor min `(0.0, 1.0)`
- anchor max `(0.0, 1.0)`
- no child transforms

This matters because it rules out one simple explanation: the object itself is not swapping between left and right child arrows. It is a single fixed-position world-space UI element.

### The player canvas is a world-space billboard

Asset inspection of the `PlayerCanvas` object shows:

- `Canvas.m_RenderMode = 2`
- `Canvas.m_Camera = null`

So this is a world-space canvas attached to the player object, not a screen-space overlay.

`EntityCanvasComponent.LateUpdate()` then drives that canvas through Unity transform helpers:

- `UnityEngine.Transform::get_position_Injected` at `0x183241820`
- `UnityEngine.Quaternion::LookRotation_Injected` at `0x18321B7A0`
- `UnityEngine.Transform::SetPositionAndRotation_Injected` at `0x18323FB30`

The important detail is that the canvas is not positioned in camera-relative space. The function builds a new world-space target position by adding a hard-coded offset on the global `X` axis:

- most character types: `+2.2f`
- `dracula_bat`: `+5.5f`

Those constants come from:

- `0x18357FE70 = 2.2f`
- `0x18357FE74 = 5.5f`

The late-update logic is therefore approximately:

```text
playerPos = _playerTransform.position
targetPos = playerPos + (2.2, 0.0, 0.0)
if characterType == dracula_bat:
    targetPos = playerPos + (5.5, 0.0, 0.0)

canvasPos = _canvasTransform.position
cameraPos = _camera.transform.position
rotation = Quaternion.LookRotation(canvasPos - cameraPos)
_canvasTransform.SetPositionAndRotation(targetPos, rotation)
```

That means the visible HUD marker is produced in two stages:

- `HandleSeenBySeekerIndicator()` decides whether the fixed warning object is active
- `LateUpdate()` billboards and repositions the whole canvas relative to the currently active camera

This is the strongest current explanation for why the bug feels geometric: the indicator object itself is static, but the canvas that contains it is explicitly rotated to face the active camera every frame.

## Updated working explanation

The current best explanation is no longer "the indicator projects the hunter into the wrong camera basis."

The better-supported explanation is:

- `JugMakingTask` switches into a dedicated close task camera
- the interaction also blocks the player into a stationary task state
- while that task is active, the victim is likely replicated as `PlayerMobilityState.NotMoving`
- `HandleSeenBySeekerIndicator()` uses that mobility state to toggle the seeker warning object
- the visual result is perceived as an inverted side indicator because the warning object is static inside the world-space `PlayerCanvas`, while the whole canvas is billboarded to the active camera and positioned with a fixed global-`X` world offset rather than a player-relative or camera-relative side offset

This matches the reported behavior better:

- the bug is stable, not noisy
- it appears only while the task camera interaction is active
- it feels like a binary wrong-side state, not a drifting projection error
- the indicator object itself is fixed, so the apparent "side" must come from the canvas/camera relationship rather than from a dedicated arrow-direction algorithm inside the object
- the task camera is exactly the condition that changes that canvas/camera relationship
- a fixed global-`X` offset can look intuitive from the normal camera but obviously wrong from a close task camera with a different angle around the player

## What is still not fully proven

Two details are still open:

- where exactly the player is switched into `PlayerMobilityState.NotMoving` during jug making
- which exact step in `EntityCanvasComponent.LateUpdate()` makes the world-space canvas appear mirrored or opposite to player intuition during the task camera

So the current evidence says the bug is tightly coupled to task-induced stationary state plus world-space canvas geometry under the task camera, but the final canvas-space mapping still needs one more tracing pass.

## Practical takeaway

At the current evidence level, this should be treated as:

- a real bug
- specific to the jug-making close camera state
- most likely caused by the seeker-warning HUD switching through the `PlayerMobilityState.NotMoving` path while the task interaction is active, with the visible wrong-side effect created by a fixed indicator object inside a world-space player canvas that is billboarded to the camera but placed with a hard-coded global-`X` world offset

The highest-value next reverse-engineering target would now be confirming whether the fixed global-`X` offset is intentional everywhere or whether some missing task-specific override was supposed to replace it during close interaction cameras.
