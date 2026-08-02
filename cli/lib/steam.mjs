import { execFile } from "node:child_process";
import { access, readFile, readdir, realpath } from "node:fs/promises";
import { constants } from "node:fs";
import { homedir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { promisify } from "node:util";

export const STEAM_APP_ID = "2410490";
export const GAME_DIRECTORY_NAME = "Sneak Out";

const execFileAsync = promisify(execFile);

async function exists(path) {
  try {
    await access(path, constants.F_OK);
    return true;
  } catch {
    return false;
  }
}

function unique(paths) {
  return [...new Set(paths.filter(Boolean).map((path) => resolve(path)))];
}

async function windowsSteamPath() {
  if (process.platform !== "win32") {
    return null;
  }
  try {
    const { stdout } = await execFileAsync("reg.exe", [
      "query",
      "HKCU\\Software\\Valve\\Steam",
      "/v",
      "SteamPath"
    ], { windowsHide: true });
    const match = stdout.match(/SteamPath\s+REG_\w+\s+(.+)$/im);
    return match?.[1]?.trim() ?? null;
  } catch {
    return null;
  }
}

async function candidateSteamRoots() {
  if (process.env.SNEAKOUT_STEAM_ROOTS) {
    return unique(process.env.SNEAKOUT_STEAM_ROOTS.split(process.platform === "win32" ? ";" : ":"));
  }
  const userHome = homedir();
  if (process.platform === "win32") {
    return unique([
      await windowsSteamPath(),
      process.env["ProgramFiles(x86)"] && join(process.env["ProgramFiles(x86)"], "Steam"),
      process.env.ProgramFiles && join(process.env.ProgramFiles, "Steam"),
      "C:\\Program Files (x86)\\Steam",
      "C:\\Program Files\\Steam"
    ]);
  }
  return unique([
    join(userHome, ".steam", "steam"),
    join(userHome, ".local", "share", "Steam"),
    join(userHome, ".var", "app", "com.valvesoftware.Steam", "data", "Steam")
  ]);
}

async function parseLibraryFolders(steamRoot) {
  const libraryFile = join(steamRoot, "steamapps", "libraryfolders.vdf");
  try {
    const content = await readFile(libraryFile, "utf8");
    return [...content.matchAll(/"path"\s+"((?:\\.|[^"])*)"/g)].map((match) =>
      match[1].replace(/\\\\/g, "\\")
    );
  } catch {
    return [];
  }
}

async function mountedSteamLibraries() {
  if (process.platform === "win32") {
    return [];
  }
  const results = [];
  for (const base of ["/run/media", "/media", "/mnt", "/var/mnt"]) {
    let firstLevel;
    try {
      firstLevel = await readdir(base, { withFileTypes: true });
    } catch {
      continue;
    }
    for (const first of firstLevel.filter((entry) => entry.isDirectory())) {
      const direct = join(base, first.name, "SteamLibrary");
      if (await exists(direct)) {
        results.push(direct);
      }
      let secondLevel;
      try {
        secondLevel = await readdir(join(base, first.name), { withFileTypes: true });
      } catch {
        continue;
      }
      for (const second of secondLevel.filter((entry) => entry.isDirectory())) {
        const nested = join(base, first.name, second.name, "SteamLibrary");
        if (await exists(nested)) {
          results.push(nested);
        }
      }
    }
  }
  return results;
}

export async function isGameDirectory(path) {
  return Promise.all([
    exists(join(path, "GameAssembly.dll")),
    exists(join(path, "Sneak Out.exe")),
    exists(join(path, "Sneak Out_Data", "resources.assets"))
  ]).then((checks) => checks.every(Boolean));
}

export async function detectGameDirectories() {
  const steamRoots = await candidateSteamRoots();
  const libraries = [...steamRoots, ...(await mountedSteamLibraries())];
  for (const root of steamRoots) {
    libraries.push(...(await parseLibraryFolders(root)));
  }
  const candidates = unique(libraries).map((root) =>
    join(root, "steamapps", "common", GAME_DIRECTORY_NAME)
  );
  const valid = [];
  for (const candidate of candidates) {
    if (await isGameDirectory(candidate)) {
      valid.push(await realpath(candidate));
    }
  }
  return unique(valid);
}

export async function resolveGameDirectory(path) {
  const resolved = resolve(path);
  if (!(await isGameDirectory(resolved))) {
    throw new Error(
      `Invalid Sneak Out directory: ${resolved}\n` +
      "Expected GameAssembly.dll, Sneak Out.exe, and Sneak Out_Data/resources.assets."
    );
  }
  return resolved;
}

export function appManifestPath(gameDirectory) {
  return join(dirname(dirname(gameDirectory)), `appmanifest_${STEAM_APP_ID}.acf`);
}

export async function readInstalledBuildId(gameDirectory) {
  try {
    const content = await readFile(appManifestPath(gameDirectory), "utf8");
    return content.match(/"buildid"\s+"(\d+)"/)?.[1] ?? null;
  } catch {
    return null;
  }
}

export async function steamLocalConfigPaths() {
  const results = [];
  for (const steamRoot of await candidateSteamRoots()) {
    const userdata = join(steamRoot, "userdata");
    let users;
    try {
      users = await readdir(userdata, { withFileTypes: true });
    } catch {
      continue;
    }
    for (const user of users.filter((entry) => entry.isDirectory())) {
      const path = join(userdata, user.name, "config", "localconfig.vdf");
      if (await exists(path)) {
        results.push(path);
      }
    }
  }
  return unique(results);
}

export async function isSteamClientRunning() {
  if (process.env.SNEAKOUT_STEAM_RUNNING !== undefined) {
    return process.env.SNEAKOUT_STEAM_RUNNING === "1";
  }
  if (process.platform === "win32") {
    return false;
  }
  let processes;
  try {
    processes = await readdir("/proc", { withFileTypes: true });
  } catch {
    return false;
  }
  for (const processEntry of processes) {
    if (!processEntry.isDirectory() || !/^\d+$/.test(processEntry.name)) {
      continue;
    }
    try {
      const command = (await readFile(join("/proc", processEntry.name, "comm"), "utf8")).trim();
      if (command === "steam" || command === "steamwebhelper") {
        return true;
      }
    } catch {
      // Processes can exit while /proc is being scanned.
    }
  }
  return false;
}

export function isProtonInstall() {
  return process.platform !== "win32";
}
