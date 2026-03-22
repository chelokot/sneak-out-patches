import { rm } from "node:fs/promises";
import { join } from "node:path";
import {
  ensureDirectory,
  ensureExecutable,
  fetchToFile,
  fileExists,
  localDotnetDirectory,
  localDotnetExecutablePath,
  localDownloadsDirectory,
  runAndCapture,
  runCommand
} from "./lib/workspace-tools.mjs";

const dotnetVersion = process.env.SNEAKOUT_DOTNET_VERSION ?? "8.0.408";

async function ensureDotnet() {
  const dotnetExecutablePath = localDotnetExecutablePath();
  if (await fileExists(dotnetExecutablePath)) {
    const { stdout } = await runAndCapture(dotnetExecutablePath, ["--version"]);
    if (stdout.trim() === dotnetVersion) {
      console.log(`dotnet ${dotnetVersion} already installed`);
      return;
    }
  }

  await ensureDirectory(localDownloadsDirectory);
  await rm(localDotnetDirectory, { recursive: true, force: true });
  if (process.platform === "win32") {
    const installerPath = join(localDownloadsDirectory, "dotnet-install.ps1");
    await fetchToFile("https://dot.net/v1/dotnet-install.ps1", installerPath);
    await runCommand("powershell.exe", [
      "-NoProfile",
      "-ExecutionPolicy",
      "Bypass",
      "-File",
      installerPath,
      "-Version",
      dotnetVersion,
      "-InstallDir",
      localDotnetDirectory
    ]);
  } else {
    const installerPath = join(localDownloadsDirectory, "dotnet-install.sh");
    await fetchToFile("https://dot.net/v1/dotnet-install.sh", installerPath);
    await ensureExecutable(installerPath);
    await runCommand("bash", [
      installerPath,
      "--version",
      dotnetVersion,
      "--install-dir",
      localDotnetDirectory
    ]);
  }

  if (!(await fileExists(dotnetExecutablePath))) {
    throw new Error(`dotnet install did not produce ${dotnetExecutablePath}`);
  }

  console.log(`installed dotnet ${dotnetVersion} -> ${localDotnetDirectory}`);
}

async function main() {
  console.log("Bootstrapping local Sneak Out tooling");
  await ensureDotnet();
  console.log("Local tooling is ready");
}

main().catch((error) => {
  console.error(error instanceof Error ? error.message : String(error));
  process.exitCode = 1;
});
