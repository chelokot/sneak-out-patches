# Working Berek Patch

The final working set turned out to be smaller than the intermediate attempts.

## What is patched in the final version

`GameAssembly.dll`

- room and match startup are redirected into `Berek`
- state flow is redirected into `BerekSelectionState`
- a berek map is forced inside the host flow

`Sneak Out_Data/resources.assets`

- `SpookedNetworkPlayer.EntityBerekComponent` is pre-wired on the `UnityPlayer` prefab

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

- `get-the-crown`
  switches the default match flow into `Berek`
- `fix-private-party-first-invite`
  routes invitation joins through the explicit lobby id carried by `JoinLobbyEvent`, which fixes the stale-lobby-id handoff on the first accepted invite
- `uniform-hunter-random`
  expands the first default-mode seeker fairness threshold from `0.1` to `1.0`, which makes the normal hunter pick effectively uniform across the preferred-role candidate pool

## Current state

Confirmed:

- the match is created as `game_mode=Berek`
- the map switches into a berek map
- the mode really plays from start to finish

Remaining known issue:

- the crown visual is still not fully fixed
