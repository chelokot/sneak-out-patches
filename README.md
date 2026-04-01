# Sneak Out Runtime Mods

Reverse-engineering notes, runtime mod code, and installation tooling for `Sneak Out`.

The repository is runtime-mod first:

- no `GameAssembly.dll` byte patches are currently shipped
- the patcher installs `BepInEx IL2CPP` and selected runtime mods
- committed DLL artifacts in `artifacts/runtime_mods/` let other people install with `--nobuild`

## Tooling bootstrap

```bash
npm install
```

On Linux you can use:

```bash
make install
```

That bootstraps into `.tmp/`:

- a repo-managed `.NET SDK`
- a repo-managed `BepInEx Unity.IL2CPP x64` bundle

External prerequisites:

- `Node.js 20+`
- `Python 3`

## Repository layout

- `mods/`
  Runtime BepInEx IL2CPP plugins.
- `runtime_mods_manifest.json`
  The single source of truth for runtime mod ids, labels, categories, build paths, and default install settings.
- `config_templates/runtime_mods/`
  Default config templates consumed by the patcher.
- `artifacts/runtime_mods/`
  Committed runtime mod DLLs for `--nobuild` installs.
- `scripts/`
  Repo bootstrap, build helpers, runtime automation, and profiler configuration.
- `tools/patch_sneak_out.py`
  Interactive installer, rollback tool, and validator for the game directory.
- `tools/interop_inspector/`
  Helper for inspecting BepInEx-generated interop assemblies.
- `docs/`
  Reverse-engineering notes, gameplay findings, and historical experiments.

## Runtime mod catalog

Core and gameplay mods:

- `double-party-invite-fix`
  Fixes the first accepted private-party invite flow.
- `uniform-seeker-random`
  Replaces the default seeker selection with a uniform random choice.
- `ui-crash-guards`
  Suppresses known crash-prone battlepass and daily-quest refresh handlers.
- `portal-mode-selector`
  Runtime portal mode and map selector hooks.
- `mummy-unlock`
  Restores Mummy-related selection hooks.
- `unlock-everything`
  Profile overlay, local apply hooks, persistence, and live sync work.
- `start-delay-reducer`
  Reduces host-side pre-match delays.
- `friend-invite-unlock`
  Keeps offline friends inviteable from the lobby list.
- `friend-join-button`
  Replaces the friend popup action with `JOIN` when the friend's presence exposes a joinable party.

Experimental and debug mods:

- `lobby-skill-sandbox`
  Lobby-only penguin skill sandbox.
- `free-fly`
  Vertical free-fly debugging controls.
- `runtime-profiler`
  Managed method profiler for narrow runtime investigations.

The patcher enables all mods except `runtime-profiler` and `free-fly` by default.

## Common commands

List runtime mods:

```bash
npm run mod:list
```

Build one runtime mod:

```bash
npm run mod:build -- unlock-everything
```

Build all runtime mods:

```bash
npm run mods:build
```

Start the interactive installer:

```bash
npm run patcher
```

Install specific runtime mods into an explicit game directory:

```bash
npm run patcher -- \
  --game-dir "/path/to/Sneak Out" \
  --mods double-party-invite-fix,unlock-everything,start-delay-reducer
```

Install from committed artifacts without local builds:

```bash
npm run patcher -- \
  --game-dir "/path/to/Sneak Out" \
  --mods double-party-invite-fix,unlock-everything \
  --nobuild
```

Rollback script-managed changes:

```bash
npm run patcher -- --rollback --game-dir "/path/to/Sneak Out"
```

Validate the current install:

```bash
npm run patcher -- --validate --game-dir "/path/to/Sneak Out"
```

## Patcher behavior

When started without `--game-dir`, the patcher:

1. detects the current operating system
2. looks for Steam library folders
3. tries to locate the `Sneak Out` install automatically
4. asks for confirmation before installing into that directory

The interactive selector is runtime-mod oriented and keyboard driven:

- `Up` and `Down`
  Move between runtime mod options.
- `Space`
  Toggle the highlighted runtime mod.
- `Enter`
  Apply the selected install set.

`--list-mods` prints ids, labels, and categories directly from `runtime_mods_manifest.json`.

## Linux and Proton

When the target install is the Proton Windows build, BepInEx also needs:

```text
WINEDLLOVERRIDES="winhttp=n,b" %command%
```

The patcher configures that launch option automatically on Linux Steam installs by updating Steam `localconfig.vdf` for app `2410490`.

## Runtime automation

Useful commands:

```bash
npm run runtime:session
npm run runtime:session:host
npm run runtime:profiler:configure -- ui-hotspots
npm run runtime:profiler:off
```

The runtime session tooling snapshots multiple real log channels because `BepInEx/LogOutput.log` is not always populated during automated launches.

## Interop inspection

Example:

```bash
npm run interop:inspect -- \
  "/path/to/Sneak Out/BepInEx/interop/Assembly-CSharp.dll" \
  "PortalPlayView"
```

## Documentation

See [docs/README.md](/var/home/chelokot/Documents/Projects/Sneakout/docs/README.md).

Historical patching notes are still kept under `docs/patching/`, but they describe previous binary-patch eras and should not be treated as the current install architecture.
