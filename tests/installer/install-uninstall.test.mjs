import assert from "node:assert/strict";
import { execFile } from "node:child_process";
import { mkdtemp, mkdir, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";
import { promisify } from "node:util";

const execFileAsync = promisify(execFile);
const testDirectory = dirname(fileURLToPath(import.meta.url));
const repositoryRoot = resolve(testDirectory, "..", "..");
const cliPath = join(repositoryRoot, "cli", "sneakout-patches.mjs");

async function fixture() {
  const root = await mkdtemp(join(tmpdir(), "sneakout-installer-test-"));
  const steamRoot = join(root, "Steam");
  const gameDirectory = join(steamRoot, "steamapps", "common", "Sneak Out");
  const metadataDirectory = join(gameDirectory, "Sneak Out_Data", "il2cpp_data", "Metadata");
  const userdataConfig = join(steamRoot, "userdata", "123", "config", "localconfig.vdf");
  const bepinexRoot = join(root, "BepInExSource");
  await mkdir(metadataDirectory, { recursive: true });
  await mkdir(dirname(userdataConfig), { recursive: true });
  await mkdir(join(bepinexRoot, "BepInEx", "core"), { recursive: true });
  await Promise.all([
    writeFile(join(gameDirectory, "GameAssembly.dll"), "unsupported-game-assembly"),
    writeFile(join(gameDirectory, "Sneak Out.exe"), "game"),
    writeFile(join(gameDirectory, "Sneak Out_Data", "resources.assets"), "resources"),
    writeFile(join(metadataDirectory, "global-metadata.dat"), "unsupported-metadata"),
    writeFile(
      join(steamRoot, "steamapps", "appmanifest_2410490.acf"),
      '"AppState"\n{\n\t"appid" "2410490"\n\t"buildid" "1"\n}\n'
    ),
    writeFile(
      userdataConfig,
      '"UserLocalConfigStore"\n{\n\t"Software"\n\t{\n\t\t"Valve"\n\t\t{\n\t\t\t"Steam"\n\t\t\t{\n\t\t\t\t"apps"\n\t\t\t\t{\n\t\t\t\t}\n\t\t\t}\n\t\t}\n\t}\n}\n'
    ),
    writeFile(join(bepinexRoot, "BepInEx", "core", "BepInEx.Unity.IL2CPP.dll"), "core"),
    writeFile(join(bepinexRoot, "winhttp.dll"), "loader"),
    writeFile(join(bepinexRoot, "doorstop_config.ini"), "doorstop")
  ]);
  return { root, steamRoot, gameDirectory, userdataConfig, bepinexRoot };
}

async function runCli(argumentsList, paths) {
  return execFileAsync(process.execPath, [cliPath, ...argumentsList], {
    cwd: repositoryRoot,
    env: {
      ...process.env,
      SNEAKOUT_PATCHES_PAYLOAD_DIR: repositoryRoot,
      SNEAKOUT_BEPINEX_DIR: paths.bepinexRoot,
      SNEAKOUT_STEAM_ROOTS: paths.steamRoot
    }
  });
}

test("noninteractive install selects stable defaults and uninstall restores clean state", async () => {
  const paths = await fixture();
  try {
    const originalLocalConfig = await readFile(paths.userdataConfig, "utf8");
    const { stdout, stderr } = await runCli([
      "install",
      "--allow-unsupported-build",
      "--offline"
    ], paths);
    assert.match(stdout, /Installation complete/);
    assert.match(stderr, /Unsupported game installation/);

    const manifest = JSON.parse(await readFile(join(repositoryRoot, "runtime_mods_manifest.json"), "utf8"));
    for (const mod of manifest) {
      const installed = join(paths.gameDirectory, "BepInEx", "plugins", `${mod.assembly_name}.dll`);
      if (mod.default_enabled) {
        assert.equal(await readFile(installed).then(() => true, () => false), true, mod.option_id);
      } else {
        assert.equal(await readFile(installed).then(() => true, () => false), false, mod.option_id);
      }
    }
    if (process.platform !== "win32") {
      assert.match(await readFile(paths.userdataConfig, "utf8"), /WINEDLLOVERRIDES/);
    }
    assert.equal(await readFile(join(paths.gameDirectory, "winhttp.dll"), "utf8"), "loader");

    const uninstallResult = await runCli([
      "uninstall",
      "--offline"
    ], paths);
    assert.match(uninstallResult.stdout, /patches removed/);
    assert.equal(await readFile(paths.userdataConfig, "utf8"), originalLocalConfig);
    assert.equal(await readFile(join(paths.gameDirectory, "winhttp.dll")).then(() => true, () => false), false);
    assert.equal(
      await readFile(join(paths.gameDirectory, ".sneakout-patches-install.json")).then(() => true, () => false),
      false
    );
  } finally {
    await rm(paths.root, { recursive: true, force: true });
  }
});

test("uninstall restores files that existed before installation", async () => {
  const paths = await fixture();
  try {
    await writeFile(join(paths.gameDirectory, "winhttp.dll"), "original-loader");
    await runCli([
      "install",
      "--game-dir",
      paths.gameDirectory,
      "--mods",
      "keyboard-layout-fix",
      "--allow-unsupported-build",
      "--offline"
    ], paths);
    assert.equal(await readFile(join(paths.gameDirectory, "winhttp.dll"), "utf8"), "loader");
    await runCli(["uninstall", "--game-dir", paths.gameDirectory, "--offline"], paths);
    assert.equal(await readFile(join(paths.gameDirectory, "winhttp.dll"), "utf8"), "original-loader");
  } finally {
    await rm(paths.root, { recursive: true, force: true });
  }
});
