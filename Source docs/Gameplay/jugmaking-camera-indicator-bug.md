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

## Updated working explanation

The current best explanation is no longer "the indicator projects the hunter into the wrong camera basis."

The better-supported explanation is:

- `JugMakingTask` switches into a dedicated close task camera
- the interaction also blocks the player into a stationary task state
- while that task is active, the victim is likely replicated as `PlayerMobilityState.NotMoving`
- `HandleSeenBySeekerIndicator()` uses that mobility state to toggle the seeker warning object
- the visual result is perceived as an inverted side indicator, but the root trigger is the mobility-state branch, not a direct left/right computation inside this method

This matches the reported behavior better:

- the bug is stable, not noisy
- it appears only while the task camera interaction is active
- it feels like a binary wrong-side state, not a drifting projection error

## What is still not fully proven

Two details are still open:

- where exactly the player is switched into `PlayerMobilityState.NotMoving` during jug making
- whether the visible "wrong side" comes from a dedicated left/right child object, a mirrored canvas layout, or a sprite/layout state under the toggled `GameObject`

So the current evidence says the bug is tightly coupled to task-induced stationary state, but the final visual mapping still needs one more tracing pass.

## Practical takeaway

At the current evidence level, this should be treated as:

- a real bug
- specific to the jug-making close camera state
- most likely caused by the seeker-warning HUD switching through the `PlayerMobilityState.NotMoving` path while the task interaction is active

The highest-value next reverse-engineering target is the visual object structure behind `_seenBySeekerIndicator`, to see how that active/inactive state becomes the perceived left/right inversion on screen.
