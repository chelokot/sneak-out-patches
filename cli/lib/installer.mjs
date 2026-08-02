import { createHash } from "node:crypto";
import { mkdir, readFile, rm, writeFile } from "node:fs/promises";
import { dirname, join, relative, resolve, sep } from "node:path";
import {
  copyFileAtomic,
  exists,
  listFiles,
  removeEmptyParents,
  sha256File,
  writeFileAtomic
} from "./io.mjs";
import { isProtonInstall, readInstalledBuildId, steamLocalConfigPaths, STEAM_APP_ID } from "./steam.mjs";

const stateFileName = ".sneakout-patches-install.json";
const backupDirectoryName = ".sneakout-patches-backup";
const legacyBackupSuffix = ".codex-sneak-out.bak";
const legacyAbsentSuffix = ".codex-sneak-out.absent";
const protonLaunchOptions = 'WINEDLLOVERRIDES="winhttp=n,b" %command%';
const loaderRootNames = [
  "BepInEx",
  "dotnet",
  ".doorstop_version",
  "doorstop_config.ini",
  "winhttp.dll",
  "changelog.txt"
];

function statePath(gameDirectory) {
  return join(gameDirectory, stateFileName);
}

function backupRoot(gameDirectory) {
  return join(gameDirectory, backupDirectoryName);
}

function portableRelative(gameDirectory, path) {
  const value = relative(gameDirectory, path);
  if (!value || value === ".." || value.startsWith(`..${sep}`)) {
    throw new Error(`Refusing to manage a path outside the game: ${path}`);
  }
  return value.split(sep).join("/");
}

function fromPortableRelative(gameDirectory, path) {
  const resolved = resolve(gameDirectory, ...path.split("/"));
  const root = resolve(gameDirectory);
  if (!resolved.startsWith(`${root}${sep}`)) {
    throw new Error(`Invalid managed path in install state: ${path}`);
  }
  return resolved;
}

async function loadState(gameDirectory) {
  try {
    const state = JSON.parse(await readFile(statePath(gameDirectory), "utf8"));
    if (state.schema !== 1 || !Array.isArray(state.files) || !Array.isArray(state.externalFiles)) {
      throw new Error("unsupported schema");
    }
    return state;
  } catch (error) {
    if (error.code === "ENOENT") {
      return { schema: 1, files: [], externalFiles: [], selectedMods: [] };
    }
    throw new Error(`Invalid installer state at ${statePath(gameDirectory)}: ${error.message}`);
  }
}

async function saveState(gameDirectory, state) {
  await writeFileAtomic(statePath(gameDirectory), `${JSON.stringify(state, null, 2)}\n`);
}

async function legacyOriginal(gameDirectory, destination) {
  const absentMarker = `${destination}${legacyAbsentSuffix}`;
  if (await exists(absentMarker)) {
    return { kind: "absent" };
  }
  for (const rootName of loaderRootNames) {
    const root = join(gameDirectory, rootName);
    if (
      (destination === root || destination.startsWith(`${root}${sep}`)) &&
      await exists(`${root}${legacyAbsentSuffix}`)
    ) {
      return { kind: "absent" };
    }
  }
  const backup = `${destination}${legacyBackupSuffix}`;
  if (await exists(backup)) {
    return { kind: "file", source: backup };
  }
  return null;
}

async function ensureTrackedOriginal(gameDirectory, state, destination) {
  const path = portableRelative(gameDirectory, destination);
  let record = state.files.find((entry) => entry.path === path);
  if (record) {
    return record;
  }

  const legacy = await legacyOriginal(gameDirectory, destination);
  if (legacy?.kind === "absent" || !(await exists(destination))) {
    record = { path, original: "absent", installedSha256: null };
  } else {
    const backupRelative = join("files", ...path.split("/"));
    const backup = join(backupRoot(gameDirectory), backupRelative);
    await mkdir(dirname(backup), { recursive: true });
    await copyFileAtomic(legacy?.source ?? destination, backup);
    record = {
      path,
      original: "backup",
      backup: backupRelative.split(sep).join("/"),
      installedSha256: null
    };
  }
  state.files.push(record);
  await saveState(gameDirectory, state);
  return record;
}

async function installFile(gameDirectory, state, source, destination) {
  const record = await ensureTrackedOriginal(gameDirectory, state, destination);
  await copyFileAtomic(source, destination);
  record.installedSha256 = await sha256File(destination);
}

async function installBytes(gameDirectory, state, bytes, destination) {
  const record = await ensureTrackedOriginal(gameDirectory, state, destination);
  await writeFileAtomic(destination, bytes);
  record.installedSha256 = createHash("sha256").update(bytes).digest("hex");
}

async function restoreRecord(gameDirectory, record) {
  const destination = fromPortableRelative(gameDirectory, record.path);
  if (record.original === "backup") {
    await copyFileAtomic(join(backupRoot(gameDirectory), ...record.backup.split("/")), destination);
    process.stdout.write(`restored: ${destination}\n`);
  } else {
    await rm(destination, { force: true });
    await removeEmptyParents(destination, gameDirectory);
    process.stdout.write(`removed:  ${destination}\n`);
  }
}

export async function compatibilityIssues(gameDirectory, supportedBuild) {
  const issues = [];
  const installedBuildId = await readInstalledBuildId(gameDirectory);
  if (installedBuildId && installedBuildId !== String(supportedBuild.steam_build_id)) {
    issues.push(`Steam build ${installedBuildId}, supported build ${supportedBuild.steam_build_id}`);
  }
  const fingerprints = [
    [join(gameDirectory, "GameAssembly.dll"), supportedBuild.game_assembly_sha256],
    [
      join(gameDirectory, "Sneak Out_Data", "il2cpp_data", "Metadata", "global-metadata.dat"),
      supportedBuild.global_metadata_sha256
    ]
  ];
  for (const [path, expected] of fingerprints) {
    if (!(await exists(path))) {
      issues.push(`missing ${path}`);
      continue;
    }
    const actual = await sha256File(path);
    if (actual !== expected) {
      issues.push(`${path} has unsupported SHA-256 ${actual}`);
    }
  }
  return issues;
}

function mergeProtonLaunchOptions(current) {
  if (current.includes("WINEDLLOVERRIDES") && current.includes("winhttp")) {
    return current;
  }
  if (current.includes("%command%")) {
    return `${protonLaunchOptions.replace(" %command%", "")} ${current}`.trim();
  }
  return `${protonLaunchOptions} ${current}`.trim();
}

function escapeVdfString(value) {
  return value.replace(/\\/g, "\\\\").replace(/"/g, '\\"');
}

function unescapeVdfString(value) {
  return value.replace(/\\"/g, '"').replace(/\\\\/g, "\\");
}

function appLaunchOptions(content) {
  const stack = [];
  let pendingKey = null;
  for (const line of content.split(/\r?\n/)) {
    const trimmed = line.trim();
    const keyValue = trimmed.match(/^"([^"]+)"\s+"((?:\\.|[^"])*)"/);
    if (keyValue) {
      if (
        stack.join("/") === `UserLocalConfigStore/Software/Valve/Steam/apps/${STEAM_APP_ID}` &&
        keyValue[1] === "LaunchOptions"
      ) {
        return unescapeVdfString(keyValue[2]);
      }
      pendingKey = null;
      continue;
    }
    const keyOnly = trimmed.match(/^"([^"]+)"\s*$/);
    if (keyOnly) {
      pendingKey = keyOnly[1];
    } else if (trimmed === "{") {
      if (pendingKey !== null) {
        stack.push(pendingKey);
        pendingKey = null;
      }
    } else if (trimmed === "}") {
      stack.pop();
      pendingKey = null;
    }
  }
  return null;
}

function hasRequiredProtonLaunchOptions(content) {
  const value = appLaunchOptions(content);
  return value !== null && value.includes("WINEDLLOVERRIDES") && value.includes("winhttp");
}

export async function protonLaunchConfigurationRequired() {
  if (!isProtonInstall()) {
    return false;
  }
  const paths = await steamLocalConfigPaths();
  if (paths.length === 0) {
    return true;
  }
  for (const path of paths) {
    const content = await readFile(path, "utf8").catch(() => "");
    if (!hasRequiredProtonLaunchOptions(content)) {
      return true;
    }
  }
  return false;
}

function updateLaunchOptions(content) {
  const lines = content.split(/\r?\n/);
  const stack = [];
  let pendingKey = null;
  let appClosingIndex = null;
  let appsClosingIndex = null;
  let launchOptionsIndex = null;
  for (let index = 0; index < lines.length; index += 1) {
    const trimmed = lines[index].trim();
    const keyValue = trimmed.match(/^"([^"]+)"\s+"((?:\\.|[^"])*)"/);
    if (keyValue) {
      if (
        stack.join("/") === `UserLocalConfigStore/Software/Valve/Steam/apps/${STEAM_APP_ID}` &&
        keyValue[1] === "LaunchOptions"
      ) {
        launchOptionsIndex = index;
      }
      pendingKey = null;
      continue;
    }
    const keyOnly = trimmed.match(/^"([^"]+)"\s*$/);
    if (keyOnly) {
      pendingKey = keyOnly[1];
      continue;
    }
    if (trimmed === "{") {
      if (pendingKey !== null) {
        stack.push(pendingKey);
        pendingKey = null;
      }
      continue;
    }
    if (trimmed === "}") {
      const current = stack.join("/");
      if (current === `UserLocalConfigStore/Software/Valve/Steam/apps/${STEAM_APP_ID}`) {
        appClosingIndex = index;
      } else if (current === "UserLocalConfigStore/Software/Valve/Steam/apps") {
        appsClosingIndex = index;
      }
      stack.pop();
      pendingKey = null;
    }
  }

  if (launchOptionsIndex !== null) {
    const match = lines[launchOptionsIndex].match(/^(\s*)"LaunchOptions"\s+"((?:\\.|[^"])*)"/);
    if (!match) {
      return content;
    }
    const current = unescapeVdfString(match[2]);
    const updated = escapeVdfString(mergeProtonLaunchOptions(current));
    lines[launchOptionsIndex] = `${match[1]}"LaunchOptions"\t\t"${updated}"`;
  } else if (appClosingIndex !== null) {
    const indent = lines[appClosingIndex].match(/^\s*/)?.[0] ?? "";
    lines.splice(
      appClosingIndex,
      0,
      `${indent}\t"LaunchOptions"\t\t"${escapeVdfString(protonLaunchOptions)}"`
    );
  } else if (appsClosingIndex !== null) {
    const indent = lines[appsClosingIndex].match(/^\s*/)?.[0] ?? "";
    lines.splice(
      appsClosingIndex,
      0,
      `${indent}"${STEAM_APP_ID}"`,
      `${indent}{`,
      `${indent}\t"LaunchOptions"\t\t"${escapeVdfString(protonLaunchOptions)}"`,
      `${indent}}`
    );
  } else {
    return content;
  }
  return `${lines.join("\n").replace(/\n+$/, "")}\n`;
}

async function configureProton(gameDirectory, state) {
  const paths = await steamLocalConfigPaths();
  if (paths.length === 0) {
    throw new Error("Steam localconfig.vdf was not found; cannot activate the Proton BepInEx loader.");
  }
  for (const path of paths) {
    const original = await readFile(path);
    const updated = updateLaunchOptions(original.toString("utf8"));
    if (updated === original.toString("utf8")) {
      continue;
    }
    if (!state.externalFiles.some((entry) => entry.path === path)) {
      state.externalFiles.push({ path, originalBase64: original.toString("base64") });
      await saveState(gameDirectory, state);
    }
    await writeFileAtomic(path, updated);
    process.stdout.write(`updated Proton launch options: ${path}\n`);
  }
}

function updateIniSetting(content, section, key, value) {
  const lines = content.split(/\r?\n/);
  let sectionStart = lines.findIndex((line) => line.trim() === `[${section}]`);
  if (sectionStart < 0) {
    const separator = lines.length && lines.at(-1) !== "" ? "" : null;
    if (separator !== null) {
      lines.push(separator);
    }
    lines.push(`[${section}]`, "", `${key} = ${value}`);
    return `${lines.join("\n").replace(/\n+$/, "")}\n`;
  }
  let sectionEnd = lines.findIndex((line, index) => index > sectionStart && /^\s*\[.+\]\s*$/.test(line));
  if (sectionEnd < 0) {
    sectionEnd = lines.length;
  }
  const setting = new RegExp(`^\\s*${key.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}\\s*=`);
  const settingIndex = lines.findIndex((line, index) =>
    index > sectionStart && index < sectionEnd && setting.test(line)
  );
  if (settingIndex >= 0) {
    lines[settingIndex] = `${key} = ${value}`;
  } else {
    lines.splice(sectionStart + 1, 0, "", `${key} = ${value}`);
  }
  return `${lines.join("\n").replace(/\n+$/, "")}\n`;
}

async function configureBepInExForProton(gameDirectory, state) {
  const path = join(gameDirectory, "BepInEx", "config", "BepInEx.cfg");
  const original = await readFile(path, "utf8").catch(() => "");
  let updated = updateIniSetting(original, "Logging.Console", "Enabled", "false");
  updated = updateIniSetting(updated, "IL2CPP", "PreloadIL2CPPInteropAssemblies", "false");
  if (updated !== original) {
    await installBytes(gameDirectory, state, updated, path);
  }
}

async function restoreDeselectedMods(gameDirectory, state, manifest, selectedIds) {
  const desired = new Set();
  for (const mod of manifest.filter((entry) => selectedIds.includes(entry.option_id))) {
    desired.add(`BepInEx/plugins/${mod.assembly_name}.dll`);
    if (mod.config_relative_path) {
      desired.add(mod.config_relative_path.replace(/\\/g, "/"));
    }
  }
  const managedModPaths = new Set();
  for (const mod of manifest) {
    managedModPaths.add(`BepInEx/plugins/${mod.assembly_name}.dll`);
    if (mod.config_relative_path) {
      managedModPaths.add(mod.config_relative_path.replace(/\\/g, "/"));
    }
  }
  const removed = [];
  for (const record of state.files) {
    if (managedModPaths.has(record.path) && !desired.has(record.path)) {
      await restoreRecord(gameDirectory, record);
      removed.push(record);
    }
  }
  state.files = state.files.filter((record) => !removed.includes(record));
  if (removed.length) {
    await saveState(gameDirectory, state);
  }
}

export async function install({
  gameDirectory,
  payloadRoot,
  bepinexRoot,
  manifest,
  selectedIds
}) {
  const state = await loadState(gameDirectory);
  await restoreDeselectedMods(gameDirectory, state, manifest, selectedIds);

  for (const source of await listFiles(bepinexRoot)) {
    const destination = join(gameDirectory, relative(bepinexRoot, source));
    await installFile(gameDirectory, state, source, destination);
  }

  for (const mod of manifest.filter((entry) => selectedIds.includes(entry.option_id))) {
    const source = join(payloadRoot, "artifacts", "runtime_mods", `${mod.assembly_name}.dll`);
    if (!(await exists(source))) {
      throw new Error(`Release is missing ${mod.assembly_name}.dll`);
    }
    await installFile(
      gameDirectory,
      state,
      source,
      join(gameDirectory, "BepInEx", "plugins", `${mod.assembly_name}.dll`)
    );
    if (mod.config_relative_path && mod.default_config_template_path) {
      const destination = join(gameDirectory, ...mod.config_relative_path.split("/"));
      if (!(await exists(destination))) {
        await installFile(
          gameDirectory,
          state,
          join(payloadRoot, ...mod.default_config_template_path.split("/")),
          destination
        );
      }
    }
  }

  if (isProtonInstall()) {
    await configureProton(gameDirectory, state);
    await configureBepInExForProton(gameDirectory, state);
  }
  state.selectedMods = selectedIds;
  await saveState(gameDirectory, state);
  return state;
}

async function rollbackLegacyInstall(gameDirectory, manifest) {
  for (const mod of manifest) {
    const paths = [join(gameDirectory, "BepInEx", "plugins", `${mod.assembly_name}.dll`)];
    if (mod.config_relative_path) {
      paths.push(join(gameDirectory, ...mod.config_relative_path.split("/")));
    }
    for (const path of paths) {
      if (await exists(`${path}${legacyBackupSuffix}`)) {
        await copyFileAtomic(`${path}${legacyBackupSuffix}`, path);
      } else if (await exists(`${path}${legacyAbsentSuffix}`)) {
        await rm(path, { force: true });
      }
      await rm(`${path}${legacyBackupSuffix}`, { force: true });
      await rm(`${path}${legacyAbsentSuffix}`, { force: true });
    }
  }
  for (const name of [...loaderRootNames].reverse()) {
    const path = join(gameDirectory, name);
    if (await exists(`${path}${legacyBackupSuffix}`)) {
      await rm(path, { recursive: true, force: true });
      await copyFileAtomic(`${path}${legacyBackupSuffix}`, path);
    } else if (await exists(`${path}${legacyAbsentSuffix}`)) {
      await rm(path, { recursive: true, force: true });
    }
    await rm(`${path}${legacyBackupSuffix}`, { recursive: true, force: true });
    await rm(`${path}${legacyAbsentSuffix}`, { force: true });
  }
  for (const path of await steamLocalConfigPaths()) {
    if (await exists(`${path}${legacyBackupSuffix}`)) {
      await copyFileAtomic(`${path}${legacyBackupSuffix}`, path);
      await rm(`${path}${legacyBackupSuffix}`, { force: true });
    }
  }
}

export async function uninstall({ gameDirectory, manifest }) {
  const state = await loadState(gameDirectory);
  for (const record of [...state.files].sort((left, right) => right.path.length - left.path.length)) {
    await restoreRecord(gameDirectory, record);
  }
  for (const external of state.externalFiles) {
    await writeFileAtomic(external.path, Buffer.from(external.originalBase64, "base64"));
    process.stdout.write(`restored: ${external.path}\n`);
  }
  await rollbackLegacyInstall(gameDirectory, manifest);
  await rm(statePath(gameDirectory), { force: true });
  await rm(backupRoot(gameDirectory), { recursive: true, force: true });
}

export async function validateInstalled(gameDirectory, manifest, selectedIds, payloadRoot) {
  const problems = [];
  for (const relativePath of [
    "winhttp.dll",
    "doorstop_config.ini",
    "BepInEx/core/BepInEx.Unity.IL2CPP.dll"
  ]) {
    const installed = join(gameDirectory, ...relativePath.split("/"));
    if (!(await exists(installed))) {
      problems.push(`missing loader file ${installed}`);
    }
  }
  for (const mod of manifest.filter((entry) => selectedIds.includes(entry.option_id))) {
    const expected = join(payloadRoot, "artifacts", "runtime_mods", `${mod.assembly_name}.dll`);
    const installed = join(gameDirectory, "BepInEx", "plugins", `${mod.assembly_name}.dll`);
    if (!(await exists(installed))) {
      problems.push(`missing ${installed}`);
    } else if (await sha256File(expected) !== await sha256File(installed)) {
      problems.push(`hash mismatch ${installed}`);
    }
  }
  if (isProtonInstall()) {
    const paths = await steamLocalConfigPaths();
    if (paths.length === 0) {
      problems.push("Steam localconfig.vdf was not found; Proton loader override is inactive");
    }
    for (const path of paths) {
      const content = await readFile(path, "utf8").catch(() => "");
      if (!hasRequiredProtonLaunchOptions(content)) {
        problems.push(`Proton winhttp loader override is inactive in ${path}`);
      }
    }
  }
  return problems;
}
