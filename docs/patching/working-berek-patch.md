# Working Mode Patch

The current patch set no longer hardcodes `Berek`. It adds a live selector to the existing portal popup, and the former `GameAssembly.dll` helper fixes now belong in runtime mods instead of byte patches.

## What is patched in the final version

Runtime mods

- `Portal Mode Selector` wraps `PortalPlayView.OnChangeRoleButton()` so the original role control and the new mode control can coexist
- `Portal Mode Selector` loads `GameModeType` from a dedicated mode bit instead of a hardcoded `Default`
- the injected mode row reuses the `OnChangeRoleButton()` callback family, not the private/public callback path, so the real private-game toggle stays untouched
- startup-time selector setup hooks were removed after they proved too fragile in the live lobby flow
- `Core Fixes` replaces the former private-party invite join fix, uniform hunter-random fix, and battlepass refresh no-op

`Sneak Out_Data/level0`

- the original preferred-role row remains the live preferred-role control
- a cloned preferred-role row is inserted above it as the visual `Game mode` row
- the private-game row is shifted down to fit the extra control without changing the play button structure
- the visible popup patch must account for both `Background` and `GameSettings`, not just the decorative frame row
- the cloned row no longer owns cloned TMP text objects; it reuses existing text nodes from the hidden `GameModeViewV2` subtree because cloned TMP objects caused `TextMeshProUGUI.Awake()` crashes inside `PortalPlayView.Open()`

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
- supports `--validate`
- supports `--patches` for non-interactive selection
- runs static validation after apply before treating the result as successful
- validates ABI-style hook invariants on known `GameAssembly.dll` helper regions, not just byte equality

## Current runtime options

- `core-fixes`
  replaces the former byte patches with runtime fixes for first private-party invite join, uniform hunter random, and battlepass refresh suppression
- `portal-mode-selector`
  adds a real `Normal / Berek` selector to the current portal popup while preserving the separate preferred-role selector

## Current state

Confirmed:

- the live portal popup now has room for a separate mode selector without stealing the preferred-role control
- the script can deterministically rebuild that popup from a clean `level0`
- the runtime selector and runtime core fixes can be installed together from the same script run
- the crashy clone-based TMP approach has been replaced with a safer hybrid row that keeps cloned layout objects but reuses already-scene-valid TMP text nodes

Operational lesson:

- always validate the selector patch on a temporary clean copy before applying it to the retail build
- recent failures came from two different sources: fragile startup-time listener registration in `GameAssembly.dll` and crashy cloned TMP objects in `level0`
- the script-side validator is now good at catching malformed hook output in `GameAssembly.dll`, but it still cannot prove runtime-correct UI behavior on its own

Remaining known issue:

- the selector still needs runtime validation inside the game client
- the crown visual is still not fully fixed
