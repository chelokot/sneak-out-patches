import { readFile } from "node:fs/promises";
import { resolve } from "node:path";
import { repositoryRoot } from "./workspace-tools.mjs";

const manifestPath = resolve(repositoryRoot, "runtime_mods_manifest.json");

let cachedRuntimeMods = null;

export async function loadRuntimeMods() {
  if (cachedRuntimeMods !== null) {
    return cachedRuntimeMods;
  }

  const manifestText = await readFile(manifestPath, "utf8");
  const manifestEntries = JSON.parse(manifestText);
  cachedRuntimeMods = manifestEntries.map((entry) => ({
    ...entry,
    projectPath: resolve(repositoryRoot, entry.project_relative_path),
    builtDllPath: resolve(
      repositoryRoot,
      entry.project_relative_path.replace(/[^/]+\.csproj$/, `bin/Release/net6.0/${entry.assembly_name}.dll`)
    ),
    artifactDllPath: resolve(repositoryRoot, "artifacts/runtime_mods", `${entry.assembly_name}.dll`)
  }));
  return cachedRuntimeMods;
}
