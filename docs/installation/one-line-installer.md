# One-line installer

The public installer is the npm CLI package `@chelokot/sneak-out-patches`.

```text
npx -y @chelokot/sneak-out-patches install
npx -y @chelokot/sneak-out-patches uninstall
npx -y @chelokot/sneak-out-patches install --interactive
```

## Distribution model

The npm package contains the cross-platform Node CLI and a fallback copy of the committed runtime-mod artifacts. On a normal online install, the CLI queries the latest GitHub Release and downloads `sneakout-patches-payload.zip` plus its SHA-256 file. BepInEx is downloaded from the pinned upstream build and checked against a repository-pinned SHA-256.

End users do not build mods and do not need Python, .NET, Git, or an interop cache. BepInEx generates the runtime interop cache during the first modded game launch.

## Defaults and compatibility

`install` is deliberately noninteractive. It finds Steam library folders, chooses the detected Sneak Out installation, and installs every manifest entry with `default_enabled: true`, including the host-only lobby test bot. Experimental and debug plugins are not stable defaults. `--all`, `--mods`, and `--interactive` provide explicit opt-in paths.

Before writing anything, the installer compares the Steam build id, `GameAssembly.dll`, and `global-metadata.dat` with the release metadata. An unsupported client is rejected unless the user explicitly passes `--allow-unsupported-build` or confirms the override interactively.

## Rollback ownership

The installer records every file it creates or replaces in `.sneakout-patches-install.json`. Original files are retained under `.sneakout-patches-backup/`. Uninstall restores replacements and removes files that were absent before installation. The migration path also recognizes backups and absence markers created by the older Python patcher.

On Linux, the original Steam `localconfig.vdf` content is retained before adding the Proton `WINEDLLOVERRIDES` launch option and restored during uninstall.

## Publishing

`npm run installer:payload` creates the two GitHub Release assets under `dist/`. The `Release installer` GitHub Actions workflow tests the installer, creates the release assets for `v*` tags, and publishes the public npm package. npm publication requires the repository `NPM_TOKEN` secret or an equivalent trusted-publishing setup.
