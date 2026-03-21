# Experiment Log

## Goal

Make `Sneak Out` reliably host and play `Berek` in a current retail build.

## Early stage

### Inspect the installed game and find the binary

Outcome:

- the game installation location was confirmed
- the main binary and related files were identified

### Validate that `Berek` still exists in the client

Outcome:

- `Berek` was confirmed in the code as a real match mode
- crown-specific states and logic were still present
- the older mode selector UI was still present, but hidden

## Failed UI-first route

### Enable hidden mode selector objects in the scene

Outcome:

- Steam still launched the modified build
- interaction behavior changed
- but the UI flow was unstable and could collapse into a black screen

Conclusion:

- scene-only toggles were not a reliable path

### Redirect portal flow into old mode selector UI

Outcome:

- sometimes produced an empty or black screen
- sometimes broke client initialization altogether

Conclusion:

- the selector exists, but restoring it safely is not a one-line fix

## Network and state route

### Force room creation into `Berek`

Outcome:

- the room eventually started reporting `game_mode=Berek`

### Force map selection into berek maps

Outcome:

- the room eventually started launching on berek-compatible maps

### Redirect selection flow into berek states

Outcome:

- the client started showing more berek-specific behavior
- but there were still hybrid states where a seeker was selected

## Wrong-layer character forcing

### Force the selected character into penguin form

Outcome:

- this created distorted mixed states such as two penguins without a proper crown flow

Conclusion:

- forcing character type too early was the wrong abstraction layer

## Critical runtime crash

### Match reaches `HandleBerekModeStart` and crashes

Observed symptom:

- `NullReferenceException` in `GameStartController.InitializeBerekComponents()`

Interpretation:

- the mode was no longer failing at matchmaking or map selection
- it was now failing inside berek-specific startup

## Root cause that unlocked the working patch

### `SpookedNetworkPlayer.EntityBerekComponent` was null

Confirmed facts:

- `UnityPlayer` prefab already had `EntityBerekComponent`
- `SpookedNetworkPlayer` expected a reference to that component
- the relevant slot was not initialized correctly

Fix:

- patch the serialized player prefab wiring in `resources.assets`

Outcome:

- the match ran from start to finish
- the mode became playable end-to-end

## Current known remaining issue

The crown visual still does not appear correctly, even though the mode is now functionally playable.

