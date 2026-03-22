import { copyFile, mkdir } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileExists, localDotnetExecutablePath, repositoryRoot, runCommand } from "./lib/workspace-tools.mjs";

const runtimeMods = {
  "portal-mode-selector": {
    projectPath: resolve(repositoryRoot, "mods/portal_mode_selector/PortalModeSelector.csproj"),
    builtDllPath: resolve(repositoryRoot, "mods/portal_mode_selector/bin/Release/net6.0/SneakOut.PortalModeSelector.dll"),
    artifactDllPath: resolve(repositoryRoot, "artifacts/runtime_mods/SneakOut.PortalModeSelector.dll")
  },
  "mummy-unlock": {
    projectPath: resolve(repositoryRoot, "mods/mummy_unlock/MummyUnlock.csproj"),
    builtDllPath: resolve(repositoryRoot, "mods/mummy_unlock/bin/Release/net6.0/SneakOut.MummyUnlock.dll"),
    artifactDllPath: resolve(repositoryRoot, "artifacts/runtime_mods/SneakOut.MummyUnlock.dll")
  },
  "backend-redirector": {
    projectPath: resolve(repositoryRoot, "mods/backend_redirector/BackendRedirector.csproj"),
    builtDllPath: resolve(repositoryRoot, "mods/backend_redirector/bin/Release/net6.0/SneakOut.BackendRedirector.dll"),
    artifactDllPath: resolve(repositoryRoot, "artifacts/runtime_mods/SneakOut.BackendRedirector.dll")
  },
  "start-delay-reducer": {
    projectPath: resolve(repositoryRoot, "mods/start_delay_reducer/StartDelayReducer.csproj"),
    builtDllPath: resolve(repositoryRoot, "mods/start_delay_reducer/bin/Release/net6.0/SneakOut.StartDelayReducer.dll"),
    artifactDllPath: resolve(repositoryRoot, "artifacts/runtime_mods/SneakOut.StartDelayReducer.dll")
  }
};

const runtimeModId = process.argv[2];
if (!runtimeModId || !(runtimeModId in runtimeMods)) {
  throw new Error(`Usage: node scripts/build-runtime-mod.mjs <${Object.keys(runtimeMods).join("|")}>`);
}

const runtimeMod = runtimeMods[runtimeModId];
await runCommand(localDotnetExecutablePath(), ["build", runtimeMod.projectPath, "-c", "Release"]);

if (!(await fileExists(runtimeMod.builtDllPath))) {
  throw new Error(`Missing built runtime mod DLL: ${runtimeMod.builtDllPath}`);
}

await mkdir(dirname(runtimeMod.artifactDllPath), { recursive: true });
await copyFile(runtimeMod.builtDllPath, runtimeMod.artifactDllPath);
console.log(`updated artifact: ${runtimeMod.artifactDllPath}`);
