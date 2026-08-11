## Runtime Mod Debugging Notes

These rules exist to prevent thrashing when debugging runtime mods against a live game client.

### 1. Protect the last known good baseline

Always keep one clearly identified working state.
Do not stack speculative fixes on top of a half-broken state.
If a hypothesis fails, return to the last clean baseline before trying the next one.

### 2. Change one layer at a time

Do not modify multiple layers in one step.
Typical layers are:
- backend/profile payload
- `ClientCache` overlay
- inventory/meta accessors
- shop/meta caches
- view-models
- UI views/buttons

A valid debugging step changes exactly one of them.

### 3. Localize first, fix second

Before editing behavior, answer one precise question:
- Is the source data empty?
- Is the source data present but filtered out?
- Is ownership false?
- Is item id missing?
- Is the UI receiving data but failing to initialize controls?

Do not implement a fix before one of these statements is proven.

### 4. Prefer binary search over broad probing

When a screen is broken, narrow the failure by halving the pipeline:
- real backend vs overlay
- source cache vs inventory accessor
- view-model vs view
- button state vs button rendering

Do not add logs everywhere at once.
Add the smallest probe that can eliminate half of the possibilities.

### 5. Separate data bugs from UI bugs

Do not patch the UI while the data path is still unproven.
Do not patch the data path while the UI path is still unproven.
First prove where the break occurs.

### 6. Treat interop assemblies as the source of truth

For IL2CPP patches, verify exact signatures against `BepInEx/interop` before patching.
Do not rely on `dump.cs` alone for:
- return types
- field vs property shape
- nested type names
- `Il2CppSystem` vs `System` types

### 7. Avoid mixed “fix + instrumentation” commits

If possible:
- first add a minimal probe
- observe
- then add the behavioral fix

This keeps causality obvious and makes rollback trivial.

### 8. Keep runtime overlays minimal and surgical

For profile stabilization:
- preserve real server data whenever possible
- merge into existing structures instead of replacing them wholesale
- never synthesize large object graphs unless that exact layer is proven to require it

Replacing full player/profile objects is a last resort.

### 9. Do not trust “counts look right”

A count being non-zero does not prove the screen is healthy.
For UI issues, inspect the concrete state that matters:
- current selected value
- stored product/item id
- blocked/unlocked state
- active/inactive state
- sprite or text payload actually bound to the button

### 10. Keep unrelated features isolated

If one mod breaks a screen, disable the other mods and prove the culprit first.
Do not debug cross-mod interactions by intuition.
Use isolation aggressively.

### 11. Prefer removing a bad patch over layering another one

When a patch is only “probably helping”, remove it until proven necessary.
Accumulated speculative patches destroy observability.

### 12. Production standard still applies during reverse engineering

Even exploratory fixes should be:
- minimal
- reversible
- typed correctly
- understandable on reread

A debugging shortcut that pollutes the final architecture is still a bad fix.

## Installer and repository tooling

- The maintained installer is Rust-only under `installer-rs/`. Do not add JavaScript,
  package-manager manifests, dependency directories, or JavaScript wrappers.
- Use `cargo test --manifest-path installer-rs/Cargo.toml --all-features` for installer
  coverage and `python3 tools/package_installer_payload.py` for release payloads.
- The primary noninteractive CLI actions are `--install-mods=<selection>` and
  `--remove-mods`. Keep the compatibility subcommands unless a migration explicitly
  removes them.
- Mod selections are authoritative. `default` expands to every current
  `default_enabled` mod, `all` expands to the entire current release manifest, and
  explicit ids are unioned with those expansions without duplicates.
- `--no-update` must make no GitHub update request. It uses the embedded payload in both
  the GUI and CLI, while local payload overrides used by tests remain allowed.
- Never replace a selected plugin when its local semantic version is newer than the
  release version; it may be a private build. Equal versions remain untouched. Copy a
  release DLL only when the plugin is missing or the release version is newer.
- Catalog-known plugins outside an installed selection must be removed. Plugins absent
  from the latest catalog remain visible as `LEGACY` in the GUI when known to the
  embedded catalog. They remain selectable and install from their embedded artifacts,
  because `LEGACY` may also represent an unreleased local development mod. Unrelated
  third-party DLLs remain untouched.
