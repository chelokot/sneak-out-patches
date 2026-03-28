import { copyFile, mkdir } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileExists, localDotnetExecutablePath, repositoryRoot, runCommand } from "./lib/workspace-tools.mjs";

const runtimeMods = {
  "runtime-profiler": {
    projectPath: resolve(repositoryRoot, "mods/runtime_profiler/RuntimeProfiler.csproj"),
    builtDllPath: resolve(repositoryRoot, "mods/runtime_profiler/bin/Release/net6.0/SneakOut.RuntimeProfiler.dll"),
    artifactDllPath: resolve(repositoryRoot, "artifacts/runtime_mods/SneakOut.RuntimeProfiler.dll")
  },
  "core-fixes": {
    projectPath: resolve(repositoryRoot, "mods/core_fixes/CoreFixes.csproj"),
    builtDllPath: resolve(repositoryRoot, "mods/core_fixes/bin/Release/net6.0/SneakOut.CoreFixes.dll"),
    artifactDllPath: resolve(repositoryRoot, "artifacts/runtime_mods/SneakOut.CoreFixes.dll")
  },
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
  "backend-stabilizer": {
    projectPath: resolve(repositoryRoot, "mods/backend_stabilizer/BackendStabilizer.csproj"),
    builtDllPath: resolve(repositoryRoot, "mods/backend_stabilizer/bin/Release/net6.0/SneakOut.BackendStabilizer.dll"),
    artifactDllPath: resolve(repositoryRoot, "artifacts/runtime_mods/SneakOut.BackendStabilizer.dll")
  },
  "start-delay-reducer": {
    projectPath: resolve(repositoryRoot, "mods/start_delay_reducer/StartDelayReducer.csproj"),
    builtDllPath: resolve(repositoryRoot, "mods/start_delay_reducer/bin/Release/net6.0/SneakOut.StartDelayReducer.dll"),
    artifactDllPath: resolve(repositoryRoot, "artifacts/runtime_mods/SneakOut.StartDelayReducer.dll")
  },
  "friend-invite-unlock": {
    projectPath: resolve(repositoryRoot, "mods/friend_invite_unlock/FriendInviteUnlock.csproj"),
    builtDllPath: resolve(repositoryRoot, "mods/friend_invite_unlock/bin/Release/net6.0/SneakOut.FriendInviteUnlock.dll"),
    artifactDllPath: resolve(repositoryRoot, "artifacts/runtime_mods/SneakOut.FriendInviteUnlock.dll")
  },
  "lobby-penguin-skills": {
    projectPath: resolve(repositoryRoot, "mods/lobby_penguin_skills/LobbyPenguinSkills.csproj"),
    builtDllPath: resolve(repositoryRoot, "mods/lobby_penguin_skills/bin/Release/net6.0/SneakOut.LobbyPenguinSkills.dll"),
    artifactDllPath: resolve(repositoryRoot, "artifacts/runtime_mods/SneakOut.LobbyPenguinSkills.dll")
  },
  "free-fly": {
    projectPath: resolve(repositoryRoot, "mods/free_fly/FreeFly.csproj"),
    builtDllPath: resolve(repositoryRoot, "mods/free_fly/bin/Release/net6.0/SneakOut.FreeFly.dll"),
    artifactDllPath: resolve(repositoryRoot, "artifacts/runtime_mods/SneakOut.FreeFly.dll")
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
