import { copyFile, mkdir } from "node:fs/promises";
import { dirname } from "node:path";
import { fileExists, localDotnetExecutablePath, runCommand } from "./lib/workspace-tools.mjs";
import { loadRuntimeMods } from "./lib/runtime-mod-manifest.mjs";

async function buildRuntimeMod(runtimeMod) {
  await runCommand(localDotnetExecutablePath(), ["build", runtimeMod.projectPath, "-c", "Release"]);

  if (!(await fileExists(runtimeMod.builtDllPath))) {
    throw new Error(`Missing built runtime mod DLL: ${runtimeMod.builtDllPath}`);
  }

  await mkdir(dirname(runtimeMod.artifactDllPath), { recursive: true });
  await copyFile(runtimeMod.builtDllPath, runtimeMod.artifactDllPath);
  console.log(`updated artifact: ${runtimeMod.artifactDllPath}`);
}

const runtimeMods = await loadRuntimeMods();
const runtimeModsById = new Map(runtimeMods.map((runtimeMod) => [runtimeMod.option_id, runtimeMod]));
const selectedArgument = process.argv[2];

if (selectedArgument === "--list") {
  for (const runtimeMod of runtimeMods) {
    console.log(`${runtimeMod.option_id}: ${runtimeMod.label}`);
  }
  process.exit(0);
}

if (selectedArgument === "--all") {
  for (const runtimeMod of runtimeMods) {
    await buildRuntimeMod(runtimeMod);
  }
  process.exit(0);
}

if (!selectedArgument) {
  throw new Error(`Usage: node scripts/build-runtime-mod.mjs <--all|--list|${runtimeMods.map((runtimeMod) => runtimeMod.option_id).join("|")}>`);
}

const runtimeMod = runtimeModsById.get(selectedArgument);
if (!runtimeMod) {
  throw new Error(`Unknown runtime mod: ${selectedArgument}`);
}

await buildRuntimeMod(runtimeMod);
