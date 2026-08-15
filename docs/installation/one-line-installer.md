# Native installer

The primary public installer is a lightweight Rust application distributed with each
GitHub Release:

```text
SneakOutPatches-windows-x86_64.exe
SneakOutPatches-windows-x86_64.exe.sha256
SneakOutPatches-linux-x86_64
SneakOutPatches-linux-x86_64.sha256
SneakOutPatches-macos-universal.zip
SneakOutPatches-macos-universal.zip.sha256
```

Windows and Linux are published as direct graphical installers with corresponding
SHA-256 files. No separate CLI or standalone mod payload is published. The GUI checks
the latest GitHub Release for a newer platform binary, verifies its SHA-256, and
replaces itself through a staged helper before restarting. The mod catalog and
artifacts are embedded in the GUI binary.

On Linux, make the downloaded file executable before opening it:

```sh
chmod +x SneakOutPatches-linux-x86_64
```

The macOS ZIP contains `SneakOutPatches.app`, a universal native launcher for
Sikarugir. An app bundle requires an archive to preserve its directory structure. The
launcher discovers wrappers under the normal user Applications locations and opens
the bundled Windows GUI through the wrapper's `WSS-installer` mode. If the bundled
executable is unavailable, it downloads the direct Windows release binary and accepts
it only after verifying the published SHA-256.

The macOS app is ad-hoc signed but not Developer ID signed or notarized. After
verifying the official release download, Control-click the app and choose **Open** if
Gatekeeper blocks the first launch.

The Windows installer detects when it is running under Wine. For Sneak Out only, it
sets Wine's `winhttp` DLL order to `native,builtin`, records the previous registry
value in the normal installation state, and restores that value during uninstall.
Users do not need to edit `winecfg` or Steam launch options.

```text
sneakout-patches --install-mods=default
sneakout-patches --install-mods=default,lobby-test-bot
sneakout-patches --install-mods=all
sneakout-patches --remove-mods
```

Pass `--no-update` to the graphical installer to skip the installer update check and
use its embedded catalog and artifacts.

## Distribution model

Each GUI binary contains the complete installer payload. On a normal online run, an
older GUI first downloads its platform binary and checksum, then stages a verified
replacement without overwriting the running process. BepInEx is downloaded from the
pinned upstream build and checked against a repository-pinned SHA-256.

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

On Linux, the original Steam `localconfig.vdf` content is retained before adding the
Proton `WINEDLLOVERRIDES` launch option and restored during uninstall. Under
Sikarugir/Wine, the game-scoped registry override is handled with the same ownership
rule. A value changed by the user after installation is preserved rather than
overwritten during uninstall.

## Publishing

`python3 tools/package_installer_payload.py` remains available for local payload
validation. The `Release installer` workflow runs the test suite once, builds only the
graphical installer on Windows and Linux, builds the universal macOS Sikarugir GUI
without repeating the tests, and creates the GitHub Release for `v*` tags. Windows and
Linux are direct binaries; only the macOS app bundle is zipped.
