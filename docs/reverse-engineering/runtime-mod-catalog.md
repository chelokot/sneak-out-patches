# Runtime Mod Catalog

## Current policy

Keep runtime mod ids and names aligned with what each mod actually does.

For this repo that means:

- use one stable slug per mod across tooling and docs
- keep display labels close to the actual plugin names
- centralize runtime-mod metadata in one manifest
- prefer structural refactors before gameplay refactors

## Stable groups

### Core

- `uniform-seeker-random`
- `portal-mode-selector`

These are narrow runtime replacements for old binary patches or scene edits.

### Gameplay

- `mummy-unlock`
- `start-delay-reducer`
- `background-loading-guard`
- `friend-invite-unlock`
- `minimap`
- `globe-launch`

These stay separate because each one owns a single gameplay or lobby concern and has a clear rollback boundary. `minimap` is enabled by default and remains local-player-only; it does not publish or reveal network-player positions. `globe-launch` changes only the lobby globe after the sixth distinct participant's third hit while all six remain registered by the vanilla interaction set, sends its child Rigidbody and referenced vanilla particles along a wide leftward horizontal curve toward each client's local camera, cleans up the launched globe, removes the empty stand from the raycastable interaction layer, and leaves the stock spin path intact until launch.

### Progression

- `unlock-everything`

This is the broadest mod in the repo. It touches:

- profile overlay
- local apply hooks
- persistence
- live sync

It keeps one install identity because those layers share profile state and must be enabled or rolled back together. Its implementation is split by responsibility into profile, skill, cosmetic, web-service, and live-player-sync modules.

## Sandbox and debug groups

- `lobby-skill-sandbox`
- `lobby-test-bot`
- `runtime-profiler`

The lobby skill panel, slide, and networked prop-change are part of the default install. The test bot and profiler are not enabled by default.

They stay separate because:

- `lobby-skill-sandbox` is a sandbox feature, not a normal progression fix, but its lobby-only skill use is an isolated default feature
- lobby prop-change uses the stock RPC only as transport, while every modded client renders a collider-free visual copied from lobby scenery; it never enters the map-only `PropPool`/registry path
- the lobby skill panel reuses an existing view model and stays unavailable when the normal UI graph is not initialized
- `lobby-test-bot` is a host-only match-start tool that uses the authoritative Fusion runner and the stock `SceneSpawner` bot initializer instead of faking player counts
- `runtime-profiler` is instrumentation, not gameplay behavior

## Why some mods are not merged

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
