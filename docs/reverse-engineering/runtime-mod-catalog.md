# Runtime Mod Catalog

## Current policy

Keep runtime mod ids and names aligned with what each mod actually does.

For this repo that means:

- use one stable slug per mod across tooling and docs
- keep display labels close to the actual plugin names
- centralize runtime-mod metadata in one manifest
- prefer structural refactors before gameplay refactors

## Audit result

No current runtime mod looks completely dead.

Each shipped mod currently has:

- a build path
- an installer entry
- an artifact path
- at least one code path or documentation reference

That does not prove every mod is equally healthy, but it does mean the current surface is actively wired into the repo.

## Stable groups

### Core

- `double-party-invite-fix`
- `uniform-seeker-random`
- `ui-crash-guards`
- `portal-mode-selector`

These are narrow runtime replacements for old binary patches or scene edits.

### Gameplay

- `mummy-unlock`
- `start-delay-reducer`
- `friend-invite-unlock`
- `friend-join-button`

These stay separate because each one owns a single gameplay or lobby concern and has a clear rollback boundary.

### Progression

- `unlock-everything`

This is the broadest mod in the repo. It touches:

- profile overlay
- local apply hooks
- persistence
- live sync

It is a good candidate for a future split, but not during active sync debugging. Right now the safer refactor is to document its layering rules and keep one clear identity for it across tooling, code, and docs.

## Experimental and debug groups

- `lobby-skill-sandbox`
- `free-fly`
- `runtime-profiler`

These are intentionally not enabled together with the same confidence level as the core install set.

They stay separate because:

- `lobby-skill-sandbox` is a sandbox feature, not a normal progression fix
- `free-fly` is a debugging tool
- `runtime-profiler` is instrumentation, not gameplay behavior

## Why some mods are not merged

### `double-party-invite-fix` and `friend-invite-unlock`

Do not merge them just because both touch lobby behavior.

`double-party-invite-fix` is a network-join fix for the first accepted party invite.
`friend-invite-unlock` is a social UI behavior change with its own config and rollback story.

### `friend-invite-unlock` and `friend-join-button`

Do not merge them.

`friend-invite-unlock` keeps otherwise disabled invite paths available.
`friend-join-button` is a separate presence-driven join affordance with a different network source and failure mode.

### `unlock-everything` and `lobby-skill-sandbox`

Do not merge them.

`Unlock Everything` is already too broad and should not absorb sandbox or debug behavior.
`Lobby Skill Sandbox` is easier to disable and reason about as a separate mod.

### `runtime-profiler` and other mods

Instrumentation should stay isolated.
Mixing it into gameplay mods makes stability investigations harder.

## Refactor direction that is safe now

The safe refactor is structural, not behavioral:

1. keep one stable slug per mod
2. centralize runtime-mod metadata in one manifest
3. centralize default config templates outside installer code
4. classify mods clearly in docs and CLI output
5. keep gameplay refactors separate from repo/tooling cleanup
