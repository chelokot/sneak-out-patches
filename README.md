# Sneak Out Patches

Reverse-engineering notes, patching tools, and runtime mod work for `Sneak Out`.

## Repository layout

- `docs/`
  Reverse-engineering notes, patch history, gameplay findings, and UI investigations.
- `tools/patch_sneak_out.py`
  The main patcher CLI for retail file patches.
- `tools/inspect_level0.py`
  Small helper for inspecting `level0` scene data.
- `tools/interop_inspector/`
  A .NET inspection utility for BepInEx-generated interop assemblies.
- `mods/portal_mode_selector/`
  The runtime BepInEx IL2CPP mod that is replacing fragile raw UI scene surgery.
- `mods/mummy_unlock/`
  A dedicated runtime research mod for restoring Mummy as a selectable hunter.
- `mods/backend_redirector/`
  The runtime BepInEx IL2CPP mod that redirects the dead web-service layer toward a community backend.
- `backend/community_server/`
  A `Hono + Deno` MVP backend for private-lobby recovery and maxed-profile testing.

## What currently exists

The Python patcher supports these retail file patches:

- `mode-selector`
  Current work-in-progress path for adding a `Classic / Crown` selector to the live portal popup.
- `fix-private-party-first-invite`
  Fixes the stale lobby id bug when joining private parties from the first invite.
- `uniform-hunter-random`
  Makes the default-mode hunter pick effectively uniform inside the preferred-role candidate pool.
- `fix-battlepass-refresh-crash`
  Temporarily disables a crashy battlepass refresh handler that was interfering with lobby testing.

The runtime mod path is now active through `BepInEx` and is the preferred direction for further UI work.

## Community backend development

The community backend lives in:

- `backend/community_server/`

Run it with:

```bash
cd backend/community_server
deno task dev
```

Validate it with:

```bash
cd backend/community_server
deno task check
deno task test
```

## Clone the repository

```bash
git clone https://github.com/chelokot/sneak-out-patches.git
cd sneak-out-patches
```

## Run the patcher

Interactive mode:

```bash
python3 tools/patch_sneak_out.py
```

Explicit game path:

```bash
python3 tools/patch_sneak_out.py --game-dir "/path/to/Sneak Out"
```

Non-interactive patch selection:

```bash
python3 tools/patch_sneak_out.py \
  --game-dir "/path/to/Sneak Out" \
  --patches mode-selector,fix-private-party-first-invite,uniform-hunter-random,fix-battlepass-refresh-crash
```

List runtime mod ids:

```bash
python3 tools/patch_sneak_out.py --list-mods
```

Install runtime mods through the same script:

```bash
python3 tools/patch_sneak_out.py \
  --game-dir "/path/to/Sneak Out" \
  --patches "" \
  --mods backend-redirector
```

Rollback:

```bash
python3 tools/patch_sneak_out.py --rollback --game-dir "/path/to/Sneak Out"
```

Validation only:

```bash
python3 tools/patch_sneak_out.py --validate --game-dir "/path/to/Sneak Out"
```

## How the interactive CLI behaves

When started without `--game-dir`, the patcher:

1. detects the current operating system
2. looks for Steam library folders
3. tries to locate the `Sneak Out` install automatically
4. asks for confirmation before patching the detected directory

If the detected directory is wrong, you can reject it and enter the path manually.

The patch selection screen is keyboard-driven:

- `Up` / `Down`
  Move between patch options.
- `Space`
  Toggle the currently highlighted option.
- `Enter`
  Apply the selected patch set.

After the patch screen, the same interactive flow opens a second screen for runtime mods.

The UI prints usage hints in green at the bottom of the selector.

The patcher always restores its clean managed backup first and then reapplies the chosen patch set on top of that baseline. This keeps repeated runs deterministic.

## Runtime mod development

The runtime selector mod lives in:

- `mods/portal_mode_selector/PortalModeSelector.csproj`

Build it with the local portable SDK:

```bash
./.tmp/runtime-mod/dotnet/dotnet build mods/portal_mode_selector/PortalModeSelector.csproj -c Release
```

Copy the built plugin into the game:

```bash
cp -f \
  mods/portal_mode_selector/bin/Release/net6.0/SneakOut.PortalModeSelector.dll \
  "/path/to/Sneak Out/BepInEx/plugins/SneakOut.PortalModeSelector.dll"
```

When running the game through Proton, `BepInEx` also needs the Steam launch option:

```text
WINEDLLOVERRIDES="winhttp=n,b" %command%
```

Without that override, `winhttp.dll` is usually not picked up and the runtime plugin will not load.

The backend redirector mod lives in:

- `mods/backend_redirector/BackendRedirector.csproj`

Build it with:

```bash
./.tmp/runtime-mod/dotnet/dotnet build mods/backend_redirector/BackendRedirector.csproj -c Release
```

Copy it into the game:

```bash
cp -f \
  mods/backend_redirector/bin/Release/net6.0/SneakOut.BackendRedirector.dll \
  "/path/to/Sneak Out/BepInEx/plugins/SneakOut.BackendRedirector.dll"
```

Its generated config file is:

```text
/path/to/Sneak Out/BepInEx/config/chelokot.sneakout.backend-redirector.cfg
```

The generated config stays fully disabled by default:

- `EnableResearchLogging=false`
- `EnableLocalStub=false`
- `EnableRedirect=false`

Enable it explicitly in:

```text
/path/to/Sneak Out/BepInEx/config/chelokot.sneakout.backend-redirector.cfg
```

For a first live test, start the backend first:

```bash
cd backend/community_server
COMMUNITY_BACKEND_LOG_REQUESTS=true deno task dev
```

Then launch the game with the same Proton `winhttp` override used by the other runtime plugins.

The helper inspector project lives in:

- `tools/interop_inspector/InteropInspector.csproj`

Example:

```bash
./.tmp/runtime-mod/dotnet/dotnet run \
  --project tools/interop_inspector/InteropInspector.csproj -- \
  "/path/to/Sneak Out/BepInEx/interop/Assembly-CSharp.dll" \
  "PortalPlayView"
```

## Important notes

- The repo intentionally contains research tooling and work-in-progress experiments.
- Raw `level0` UI surgery proved fragile; the runtime plugin path is now the safer route for portal UI work.
- `mode-selector` is still under active development.
- The visible crown visual is still not fully solved.

## Documentation index

See:

- `docs/README.md`
