# Berek Startup Flow

This document tracks the startup path that had to be restored before `Berek` became playable in the current retail build.

## Working target state

A healthy `Berek` startup needs all of the following to line up:

- the host room must be created with `game_mode=Berek`
- the selected scene must be a berek-compatible map
- the match state flow must move into berek-specific states instead of the default hunter-selection path
- berek-specific player components must be present when `HandleBerekModeStart` begins running

If any one of those layers stays on the default path, the result becomes a hybrid match.

## Room-level evidence

Confirmed live runtime evidence from `Player.log`:

- `StartGameArgs` can show `game_mode=Berek`
- `SessionInfo` can show `scene_type=Map05_TagGame`
- the room can remain in that state through player join and match close

That proved the mode and map were not just UI labels. They really reached the networking layer.

## Why room-level success was not enough

Even after the room started as `Berek`, the match could still log:

- `ShouldStartState`
- per-player seeker ratios
- `Chosen seeker: ...`

That showed a second problem:

- the room metadata could already be berek-correct
- while the local runtime state machine still entered default startup logic

## Practical startup chain

The important startup chain was:

1. host room creation
2. berek map selection
3. transition through `BeforeSelectionState`
4. transition into `BerekSelectionState`
5. startup coroutine branch into `HandleBerekModeStart`
6. berek-only initialization inside `HandleBerekModeStart`

The working patch route had to repair multiple layers in that order.

## `HandleBerekModeStart`

Observed call sequence inside the berek startup coroutine around `0x1806A4EBE`:

- remove the initial input block
- call `GivePlayerCrown`
- call `GiveStunToNotCrownedPlayers`
- call `InitializeBerekComponents`

This sequence matters because it separates three different classes of failure:

- room creation failures
- state-flow failures
- late berek-component failures

## `InitializeBerekComponents`

This was the key late-stage blocker.

Observed failure mode:

- the match reached berek startup
- `InitializeBerekComponents()` then failed because `SpookedNetworkPlayer.EntityBerekComponent` was null

Practical interpretation:

- by that stage the networking and mode-selection work was already mostly correct
- the remaining blocker lived in prefab wiring, not in matchmaking

## Why the `resources.assets` patch mattered

The final working patch set did not only touch `GameAssembly.dll`.

It also patched `Sneak Out_Data/resources.assets` so that the player prefab had a valid `EntityBerekComponent` reference cached on `SpookedNetworkPlayer`.

That change unlocked the first fully playable end-to-end berek match in the current retail build.

## Known remaining gap

The startup flow is now strong enough to:

- create a berek room
- load a berek map
- complete a full match

The remaining known issue is not startup anymore. It is the crown visual path.
