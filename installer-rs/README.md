# Native installer

The repository's installer is implemented entirely in Rust. It contains:

- `sneakout-installer`: shared Steam detection, payload verification, installation,
  validation, and rollback logic
- `sneakout-patches`: command-line installer
- `sneakout-patches-gui`: lightweight `egui` graphical installer

The GUI checks the latest GitHub Release on startup. The CLI performs the same release
resolution when installing or removing mods.

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

Pass `--no-update` to either native binary to skip GitHub checks and use only its
embedded payload. The older `install --mods ...`, `install --all`, and `uninstall`
forms remain accepted.

The GUI labels embedded-catalog mods absent from the latest public release as `LEGACY`.
They remain selectable and install from the embedded payload; the label can therefore
also identify a new local development mod that has not been released yet.

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

The GUI and CLI query the latest GitHub Release by default and fall back to the runtime
mod DLLs and configuration templates embedded at compile time if the check fails.
BepInEx remains pinned and SHA-256 verified.

## Distribution

Release CI builds both binaries natively on Windows and Linux. Windows artifacts are a
ZIP containing `SneakOutPatches.exe` and `sneakout-patches.exe`; Linux artifacts are a
compressed tarball containing `SneakOutPatches` and `sneakout-patches`.

Windows artifacts are currently unsigned. Adding a code-signing certificate to release
CI is the remaining distribution-hardening step.
