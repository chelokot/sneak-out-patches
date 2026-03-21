# Function Reference

This file tracks the key functions and offsets that mattered during the current reverse-engineering work.

## Match mode and startup flow

### `PrepareVictims`

Why it matters:

- it is one of the direct mode-sensitive startup points
- it can choose between default startup and berek startup paths

Practical role in patching:

- redirecting this logic away from the default coroutine was part of the working route into berek startup

### `BeforeSelectionState.Tick`

Why it matters:

- it decides which selection state the match moves into before actual gameplay startup

Practical role in patching:

- it needed to point at `BerekSelectionState` instead of the default selection path

### `HandleBerekModeStart`

Why it matters:

- this is the real berek startup coroutine
- reaching it meant the room and state flow were finally entering the crown-mode path

Observed behavior:

- once the room and map patches were correct, this coroutine really started executing

### `InitializeBerekComponents`

Why it matters:

- this was the key late-stage crash site

Observed failure:

- `NullReferenceException`

Practical interpretation:

- berek startup was no longer blocked at higher layers
- a berek-specific player component was missing or not wired

## Player component wiring

### `SpookedNetworkPlayer.AssignComponents()`

Why it matters:

- it populates many cached component fields on `SpookedNetworkPlayer`

Important finding:

- the runtime still expected `EntityBerekComponent`
- but the working path did not end up relying on extending this function directly

Practical lesson:

- even when code-level component assignment looks like the obvious fix, serialized prefab wiring can be the safer patch point

### `SpookedNetworkPlayer.EntityBerekComponent`

Why it matters:

- this field was the concrete null that broke berek startup

Confirmed fix path:

- patch the serialized slot on the player prefab in `resources.assets`

## Lobby and mode selection

### `GameModeView` and `GameModeViewV2`

Why they matter:

- they show that older mode-selection UI still exists in the client

Practical lesson:

- presence in the client does not imply safe reintegration into the live UI flow

### Current portal play flow

Why it matters:

- the modern portal flow is the entry point the current client actually uses

Practical lesson:

- patching the active host flow is more reliable than trying to revive disconnected UI

## Default seeker selection

### `RandomizeSeeker()` and `GetRandomSeeker()`

Why they matter:

- they define how the default mode picks the hunter

Confirmed behavior:

- only players with `CanBeSeeker = true` are considered
- the game uses `GamesPlayedAsSeeker / GamesFinishedCount`
- players are bucketed by thresholds
- the first non-empty bucket is chosen
- the final pick inside that bucket is uniform random

Known threshold set:

- `0.1`
- `0.2`
- `0.4`
- `0.6`

## Patch offsets used by the current working script

`GameAssembly.dll`

- `0x67FA02`
- `0x6971D7`
- `0x6972C3`
- `0x7E15B9`
- `0x7E15E2`
- `0x803726`
- `0x80373B`
- `0x803FBD`
- `0x823201`
- `0x823310`
- `0x8233EE`

`Sneak Out_Data/resources.assets`

- `0x4990E2C`

The authoritative source for the exact byte-level patch set is:

- `tools/patch_sneak_out_berek.py`
