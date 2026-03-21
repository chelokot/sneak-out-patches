# Working Mode Patch

The current patch set no longer hardcodes `Berek`. It adds a live selector to the existing portal popup and keeps the older invite / random fixes alongside it.

## What is patched in the final version

`GameAssembly.dll`

- `PortalPlayView.OnChangeRoleButton()` is wrapped so the original role control and the new mode control can coexist
- `PortalPlayView.OnPlay()` loads `GameModeType` from a dedicated mode bit instead of a hardcoded `Default`
- the first private-party invite fix and the uniform hunter-random fix remain as separate binary patches

`Sneak Out_Data/level0`

- the original preferred-role row is repurposed into a `Berek / Normal` mode row
- a cloned preferred-role row is inserted below it and all `PortalPlayView` role references are moved to that clone
- the private-game row is shifted down to fit the extra control without changing the play button structure

`Sneak Out_Data/resources.assets`

- `SpookedNetworkPlayer.EntityBerekComponent` remains pre-wired on the `UnityPlayer` prefab so the crown-mode startup still has the component it expects

## Why the resources.assets patch was required

The critical late bug was not in matchmaking itself, but in `InitializeBerekComponents()`.

Confirmed facts:

- the `UnityPlayer` prefab already contains `EntityBerekComponent`
- at runtime, `SpookedNetworkPlayer` expects a reference to that component in `EntityBerekComponent`
- `AssignComponents()` fills many player component fields, but does not fill this slot
- as a result, the berek flow reached `HandleBerekModeStart` and then crashed with `NullReferenceException`

Practical fix:

- in `resources.assets`, `SpookedNetworkPlayer` had a zeroed serialized slot
- that slot was bound to the prefab's `EntityBerekComponent`
- after that, the berek match could run to completion

## Script

Working patcher:

- `tools/patch_sneak_out.py`

Properties:

- detects the OS and tries to find the Steam install and `Sneak Out` automatically
- accepts an explicit game path through `--game-dir` or a positional path
- shows an interactive checkbox menu for patch selection
- restores script-managed backups before every apply and then reapplies the selected patch set on top of a clean baseline
- creates local `.codex-sneak-out.bak` backups when no trusted backup exists yet
- supports `--rollback`
- supports `--patches` for non-interactive selection

## Current patch options

- `mode-selector`
  adds a real `Normal / Berek` selector to the current portal popup while preserving the separate preferred-role selector
- `fix-private-party-first-invite`
  routes invitation joins through the explicit lobby id carried by `JoinLobbyEvent`, which fixes the stale-lobby-id handoff on the first accepted invite
- `uniform-hunter-random`
  retargets the first `GetRandomSeeker()` threshold load to `1.0` without touching the shared global `0.1` literal, which makes the normal hunter pick effectively uniform across the preferred-role candidate pool

## Current state

Confirmed:

- the live portal popup now has room for a separate mode selector without stealing the preferred-role control
- the script can deterministically rebuild that popup from a clean `level0`
- the selector patch, invite fix, and uniform hunter-random patch can be applied together from the same script run

Remaining known issue:

- the selector still needs runtime validation inside the game client
- the crown visual is still not fully fixed
