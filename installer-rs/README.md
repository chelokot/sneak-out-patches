# Native installer

The repository's installer is implemented entirely in Rust. It contains:

- `sneakout-installer`: shared Steam detection, payload verification, installation,
  validation, and rollback logic
- `sneakout-patches`: command-line installer
- `sneakout-patches-gui`: lightweight `egui` graphical installer
- `sneakout-patches-macos`: native Sikarugir wrapper discovery and launch helper

The GUI checks the latest GitHub Release for a newer GUI binary on startup. Installer
payload data is embedded in each binary.

## Command line

Running `sneakout-patches` without arguments opens the GUI. Pass an action to use the
noninteractive command-line flow.

Install an authoritative selection:

```bash
sneakout-patches --install-mods=default
sneakout-patches --install-mods=default,lobby-test-bot
sneakout-patches --install-mods=all
```

`default` and `all` expand in place and can be combined with explicit ids. Plugins in
the known catalog but outside the resulting selection are removed. Equal-version local
builds are left alone, and selected local builds newer than the release are preserved.

Remove all managed files:

```bash
sneakout-patches --remove-mods
```

Pass `--no-update` to skip GitHub checks and use only the embedded payload. The older
`install --mods ...`, `install --all`, and `uninstall` forms remain accepted.

## Development

Run the core and integration tests:

```bash
cargo test --manifest-path installer-rs/Cargo.toml
```

Build the CLI and GUI:

```bash
cargo build --manifest-path installer-rs/Cargo.toml --release --features gui --bins
```

Production GUI builds show only manifest entries with `stable: true`. To include
unstable entries in a development build, enable the `dev-mode` feature as well:

```bash
cargo build --manifest-path installer-rs/Cargo.toml --features gui,dev-mode --bins
```

Run the GUI from the repository:

```bash
cargo run --manifest-path installer-rs/Cargo.toml --features gui --bin sneakout-patches-gui
```

Build the macOS Sikarugir launcher on macOS:

```bash
cargo build --manifest-path installer-rs/Cargo.toml --release \
  --features macos-launcher --bin sneakout-patches-macos
```

The packaged macOS application places the Windows GUI at
`SneakOutPatches.app/Contents/Resources/SneakOutPatches.exe`. At runtime the helper
copies that executable into its cache before invoking Sikarugir, keeping the packaged
application bundle immutable. If the resource is absent, it downloads and verifies
the direct Windows release binary. `SNEAKOUT_SIKARUGIR_ROOTS` overrides wrapper search roots
and `SNEAKOUT_PATCHES_WINDOWS_INSTALLER` supplies a development Windows executable.

The release app is not Developer ID signed or notarized. If Control-clicking the app
and choosing **Open** does not clear Gatekeeper, users can remove quarantine after
verifying the official release download:

```bash
xattr -dr com.apple.quarantine "/path/to/SneakOutPatches.app"
```

The GUI queries the latest GitHub Release for binary self-updates. The GUI and CLI use
the runtime mod DLLs and configuration templates embedded at compile time. BepInEx
remains pinned and SHA-256 verified.

## Distribution

Release CI runs the test suite once, then builds only `sneakout-patches-gui` natively
on Windows and Linux and the Sikarugir GUI launcher for both macOS architectures. It
publishes the Windows and Linux executables directly with their SHA-256 files. The
universal macOS app and its bundled Windows GUI are published as a ZIP plus SHA-256
because an app bundle requires its directory structure. The CLI and standalone payload
are not published.

The Windows executable is currently unsigned. The macOS app is ad-hoc signed but not
Developer ID signed or notarized. Adding platform signing and notarization remains the
distribution-hardening step.
