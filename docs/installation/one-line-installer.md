# Native installer

The primary public installer is a lightweight Rust application distributed with each
GitHub Release:

```text
SneakOutPatches-windows-x86_64.zip
SneakOutPatches-linux-x86_64.tar.gz
```

Each archive contains the graphical `SneakOutPatches` installer and a native
`sneakout-patches` CLI. Both check the latest GitHub Release for a newer installer,
verify the platform archive's SHA-256, and replace themselves through a staged helper.
The GUI restarts immediately; an explicit CLI install or remove action finishes first
and applies the binary update as it exits. Both retain the mod payload embedded at
compile time as an offline fallback.
Running `sneakout-patches` without arguments opens the graphical installer; pass an
action to use it noninteractively.

```text
sneakout-patches --install-mods=default
sneakout-patches --install-mods=default,lobby-test-bot
sneakout-patches --install-mods=all
sneakout-patches --remove-mods
```

Pass `--no-update` to either binary to skip installer and mod update checks entirely
and use the embedded catalog and artifacts.

## Distribution model

The native binaries use one release payload and installation-state schema. On a normal
online run, an older installer first downloads its platform archive and checksum, then
stages a verified replacement without overwriting the running process. The current
installer downloads `sneakout-patches-payload.zip` plus its SHA-256 file. BepInEx is
downloaded from the pinned upstream build and checked against a repository-pinned
SHA-256.

End users do not build mods and do not need Python, .NET, Git, or an interop cache. BepInEx generates the runtime interop cache during the first modded game launch.

## Defaults and compatibility

The production GUI exposes only manifest entries marked `stable` and selects the
`default_enabled` entries among them. Development builds made with the `dev-mode`
Cargo feature also expose unstable entries. The CLI catalog and explicit selection
semantics are unchanged.
The CLI is deliberately noninteractive by default. `default` and `all` expand inside
`--install-mods`, explicit ids are unioned with those expansions, and the resulting
selection is authoritative. The compatibility `install --mods`, `install --all`, and
`uninstall` forms remain accepted.

Before writing anything, the installer compares the Steam build id, `GameAssembly.dll`, and `global-metadata.dat` with the release metadata. An unsupported client is rejected unless the user explicitly passes `--allow-unsupported-build` or confirms the override interactively.

## Rollback ownership

The installer records every file it creates or replaces in `.sneakout-patches-install.json`. Original files are retained under `.sneakout-patches-backup/`. Uninstall restores replacements and removes files that were absent before installation. The migration path also recognizes backups and absence markers created by the older Python patcher.

On Linux, the original Steam `localconfig.vdf` content is retained before adding the Proton `WINEDLLOVERRIDES` launch option and restored during uninstall.

## Publishing

`python3 tools/package_installer_payload.py` creates the payload assets under `dist/`.
The `Release installer` workflow builds and tests the native binaries on Windows and
Linux, packages them with SHA-256 files, and creates the GitHub Release for `v*` tags.
