import { readdir, readFile } from "node:fs/promises";
import { homedir, platform } from "node:os";
import { basename, dirname, join, resolve } from "node:path";
import { fileExists, runtimeModDirectory } from "./workspace-tools.mjs";

export const gameDirectoryName = "Sneak Out";
export const steamAppId = "2410490";
export const repositoryInteropDirectory = resolve(runtimeModDirectory, "interop");

function uniquePaths(paths) {
  return [...new Map(paths.map((path) => [resolve(path), resolve(path)])).values()];
}

async function childDirectories(directoryPath) {
  try {
    const entries = await readdir(directoryPath, { withFileTypes: true });
    return entries
      .filter((entry) => entry.isDirectory())
      .map((entry) => join(directoryPath, entry.name));
  } catch {
    return [];
  }
}

async function mountedSteamLibraries() {
  const libraries = [];
  for (const mountRoot of ["/run/media", "/media", "/mnt"]) {
    for (const firstLevelDirectory of await childDirectories(mountRoot)) {
      if (basename(firstLevelDirectory) === "SteamLibrary") {
        libraries.push(firstLevelDirectory);
      }

      for (const secondLevelDirectory of await childDirectories(firstLevelDirectory)) {
        if (basename(secondLevelDirectory) === "SteamLibrary") {
          libraries.push(secondLevelDirectory);
        }

        for (const thirdLevelDirectory of await childDirectories(secondLevelDirectory)) {
          if (basename(thirdLevelDirectory) === "SteamLibrary") {
            libraries.push(thirdLevelDirectory);
          }
        }
      }
    }
  }
  return libraries;
}

function defaultSteamRoots() {
  const homeDirectory = homedir();
  switch (platform()) {
    case "win32":
      return ["PROGRAMFILES(X86)", "PROGRAMFILES", "LOCALAPPDATA"]
        .map((variableName) => process.env[variableName])
        .filter((value) => value)
        .map((value) => join(value, "Steam"));
    case "darwin":
      return [join(homeDirectory, "Library", "Application Support", "Steam")];
    default:
      return [
        join(homeDirectory, ".local", "share", "Steam"),
        join(homeDirectory, ".steam", "steam"),
        join(homeDirectory, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam")
      ];
  }
}

async function configuredSteamLibraries(steamRoot) {
  const libraryFoldersPath = join(steamRoot, "steamapps", "libraryfolders.vdf");
  if (!(await fileExists(libraryFoldersPath))) {
    return [];
  }

  const libraryFolders = await readFile(libraryFoldersPath, "utf8");
  return [...libraryFolders.matchAll(/"path"\s+"([^"]+)"/g)]
    .map((match) => resolve(match[1].replaceAll("\\\\", "\\")));
}

export async function steamLibraryDirectories() {
  const steamRoots = defaultSteamRoots();
  const configuredLibraries = [];
  for (const steamRoot of steamRoots) {
    configuredLibraries.push(...await configuredSteamLibraries(steamRoot));
  }

  return uniquePaths([
    ...steamRoots,
    ...configuredLibraries,
    ...await mountedSteamLibraries()
  ]);
}

export async function isGameDirectory(gameDirectory) {
  const requiredFiles = await Promise.all([
    fileExists(join(gameDirectory, "GameAssembly.dll")),
    fileExists(join(gameDirectory, "Sneak Out.exe")),
    fileExists(join(gameDirectory, "Sneak Out_Data", "resources.assets"))
  ]);
  return requiredFiles.every(Boolean);
}

export async function detectGameDirectory() {
  for (const libraryDirectory of await steamLibraryDirectories()) {
    const gameDirectory = join(libraryDirectory, "steamapps", "common", gameDirectoryName);
    if (await isGameDirectory(gameDirectory)) {
      return resolve(gameDirectory);
    }
  }
  return null;
}

export async function resolveGameDirectory(explicitGameDirectory = process.env.SNEAKOUT_GAME_DIR) {
  if (explicitGameDirectory) {
    const gameDirectory = resolve(explicitGameDirectory);
    if (!(await isGameDirectory(gameDirectory))) {
      throw new Error(`Invalid Sneak Out game directory: ${gameDirectory}`);
    }
    return gameDirectory;
  }

  const detectedGameDirectory = await detectGameDirectory();
  if (detectedGameDirectory === null) {
    throw new Error("Sneak Out is not installed in a detected Steam library. Set SNEAKOUT_GAME_DIR.");
  }
  return detectedGameDirectory;
}

export function steamManifestPath(gameDirectory) {
  return join(dirname(dirname(gameDirectory)), `appmanifest_${steamAppId}.acf`);
}

export async function readSteamBuildId(gameDirectory) {
  const manifestPath = steamManifestPath(gameDirectory);
  if (!(await fileExists(manifestPath))) {
    throw new Error(`Steam app manifest is missing: ${manifestPath}`);
  }

  const manifest = await readFile(manifestPath, "utf8");
  const buildId = /"buildid"\s+"(\d+)"/.exec(manifest)?.[1];
  if (!buildId) {
    throw new Error(`Steam build ID is missing in ${manifestPath}`);
  }
  return buildId;
}

export async function resolveBuildInteropDirectory(options = {}) {
  const optionInteropDirectory = options.interopDirectory;
  const environmentInteropDirectory = process.env.SNEAKOUT_INTEROP_DIR;
  const explicitInteropDirectory = optionInteropDirectory ?? environmentInteropDirectory;
  if (explicitInteropDirectory) {
    const interopDirectory = resolve(explicitInteropDirectory);
    const source = optionInteropDirectory ? "--interop-dir" : "SNEAKOUT_INTEROP_DIR";
    if (!(await fileExists(join(interopDirectory, "Assembly-CSharp.dll")))) {
      throw new Error(`Assembly-CSharp.dll is missing from ${source}: ${interopDirectory}`);
    }
    return { interopDirectory, source };
  }

  if (await fileExists(join(repositoryInteropDirectory, "Assembly-CSharp.dll"))) {
    return { interopDirectory: repositoryInteropDirectory, source: "repo-local cache" };
  }

  const gameDirectory = await resolveGameDirectory(options.gameDirectory);
  const interopDirectory = join(gameDirectory, "BepInEx", "interop");
  if (!(await fileExists(join(interopDirectory, "Assembly-CSharp.dll")))) {
    throw new Error(
      `BepInEx interop assemblies are missing from ${interopDirectory}. Run the game once with BepInEx.`
    );
  }
  return { gameDirectory, interopDirectory, source: "Steam install" };
}
