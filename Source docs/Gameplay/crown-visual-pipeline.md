# Crown Visual Pipeline

This document describes what is currently confirmed about the visible crown in `Berek`.

## Current state

Confirmed:

- `Berek` can now be hosted and played end-to-end
- the room really starts as `game_mode=Berek`
- the map can really switch into a berek map

Known remaining issue:

- the visible crown still does not appear reliably on the crowned player

## Asset-level wiring

The current retail `UnityPlayer` prefab already contains the crown-related objects.

Confirmed objects in `resources.assets`:

- `UnityPlayer` at path id `2143`
- `BerekCrown` at path id `3257`
- `CrownSteal_VFX` at path id `2034`
- `Kinguin_Crown_a_Head_mesh` at path id `1756`

Confirmed object state:

- `BerekCrown` exists
- `BerekCrown` starts inactive
- its transform is parented under the `UnityPlayer` transform

Practical implication:

- the missing crown is not explained by the crown object being absent from the prefab
- it is also not explained by the crown object living on the wrong prefab root

## `EntityBerekComponent`

The visible crown is managed through `EntityBerekComponent`.

Confirmed relevant object:

- `EntityBerekComponent` at path id `7811`

Confirmed role:

- it owns the cached crown object reference
- it also depends on a remote collision resolver during berek initialization

This component was part of the successful `resources.assets` patch because `SpookedNetworkPlayer` needed a valid serialized reference to it.

## Runtime control path

Two methods are the key visual controls:

### `EntityBerekComponent.HandleCrown` at `0x180639490`

Observed behavior from disassembly:

- it checks whether the player has buff type `0xC0`
- if the buff is absent, it disables the cached crown object when that object is active
- if the buff is present, it enables the cached crown object when that object is inactive

Practical conclusion:

- the visible crown is controlled as a normal active or inactive GameObject toggle
- the toggle is driven by crown-buff state, not by a separate dedicated crown actor

### `EntityBerekComponent.HasCrown` at `0x180639520`

Observed behavior from disassembly:

- it checks the same buff type `0xC0`

Practical conclusion:

- gameplay crown ownership and visual crown ownership are expected to agree through the same buff

## Crown assignment path

Inside the berek startup coroutine, `HandleBerekModeStart` calls `GivePlayerCrown`.

Observed behavior in `GivePlayerCrown` at `0x18067F690`:

- it routes through the host buff path
- it applies buff type `0xC0` to the selected player

Practical conclusion:

- the intended flow is simple:
- startup picks the crowned player
- host applies crown buff `0xC0`
- `EntityBerekComponent` sees the buff
- `HandleCrown` activates `BerekCrown`

## What the missing crown does not seem to be

The current evidence argues against several simpler explanations.

It does not look like:

- a missing `BerekCrown` object
- a missing crown GameObject parent under `UnityPlayer`
- a missing `EntityBerekComponent` on the player prefab

Those three layers are all now present.

## Current best hypothesis

The remaining bug is more likely in one of these areas:

- the crowned player does not keep the expected buff state on the local visual owner
- `HandleCrown` is not being triggered at the right time for the visible player entity
- the gameplay owner and the rendered player entity disagree about who should display the crown

This is why the next investigation should stay focused on buff flow and component update timing, not on prefab existence.

## Log interpretation

`Player.log` does not currently show explicit crown-visual messages.

The useful runtime signal is indirect:

- the room stays `Berek`
- the map stays on a berek scene
- unrelated UI `NullReferenceException` entries still appear in `BattlepassView` and finish-screen views

Practical lesson:

- not every runtime exception in the log is part of the crown problem
- the crown investigation should stay anchored to berek-specific code paths and the buff-driven visual toggle
