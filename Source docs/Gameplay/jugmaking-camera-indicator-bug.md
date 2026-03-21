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

What matters here:

- this code path is generic player-HUD logic
- there is no visible jug-making-specific branch in `HandleSeenBySeekerIndicator()`
- there is also no obvious check there for `BlockInputsForJugMaking`

That makes this look like a generic camera-space issue rather than a bespoke bug in `JugMakingTaskView`.

## Most likely explanation

The most likely cause is that the seeker-direction indicator is calculated relative to the currently active camera / canvas orientation, and the `JugMaking` task camera changes that orientation in a way the indicator code does not compensate for.

In other words:

- world-space seeker position is still correct
- indicator logic is still running
- but its left/right projection uses the task camera basis instead of the normal gameplay camera basis
- that task camera basis is effectively mirrored or rotated relative to the player's expected left/right reference

This fits all currently confirmed facts:

- `JugMakingTask` definitely activates a dedicated camera mode
- the seeker indicator is handled by shared HUD logic
- the indicator code does not appear to disable itself or switch into a task-aware mode during jug making

## What is not yet proven

The exact sign error is not fully proven yet.

The remaining unknown is whether the inversion comes from:

- the task camera transform itself facing the opposite direction
- a mirrored canvas-space conversion
- a helper inside `HandleSeenBySeekerIndicator()` that classifies the side from a camera-relative value

That part needs deeper tracing of the helper calls used by `HandleSeenBySeekerIndicator()`.

## Practical takeaway

At the current evidence level, this should be treated as:

- a real bug
- specific to the jug-making close camera state
- most likely caused by the indicator using the wrong camera-space basis while the task camera is active

The highest-value next reverse-engineering target is the helper chain under `HandleSeenBySeekerIndicator()` to identify the exact left/right sign decision.
