import { rm } from "node:fs/promises";
import { join } from "node:path";
import {
  ensureDirectory,
  ensureExecutable,
  fetchToFile,
  fileExists,
  localBepInExDirectory,
  localDotnetDirectory,
  localDotnetExecutablePath,
  localDownloadsDirectory,
  resolvePythonCommand,
  runAndCapture,
  runCommand
} from "./lib/workspace-tools.mjs";

const dotnetVersion = process.env.SNEAKOUT_DOTNET_VERSION ?? "8.0.408";
const bepinexWindowsAssetUrl = process.env.SNEAKOUT_BEPINEX_WINDOWS_ASSET_URL
  ?? "https://builds.bepinex.dev/projects/bepinex_be/755/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.755%2B3fab71a.zip";

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

async function extractZipArchive(archivePath, destinationPath) {
  const { command, prefix } = await resolvePythonCommand();
  const extractorScript = [
    "from pathlib import Path",
    "import shutil",
    "import sys",
    "import zipfile",
    "archive_path = Path(sys.argv[1])",
    "destination_path = Path(sys.argv[2])",
    "if destination_path.exists():",
    "    shutil.rmtree(destination_path)",
    "destination_path.mkdir(parents=True, exist_ok=True)",
    "with zipfile.ZipFile(archive_path) as archive_file:",
    "    archive_file.extractall(destination_path)"
  ].join("\n");

  await runCommand(command, [
    ...prefix,
    "-c",
    extractorScript,
    archivePath,
    destinationPath
  ]);
}

async function ensureBepInEx() {
  const bepinexCorePath = join(localBepInExDirectory, "BepInEx", "core", "BepInEx.Unity.IL2CPP.dll");
  const bepinexBootstrapPath = join(localBepInExDirectory, "winhttp.dll");
  if (await fileExists(bepinexCorePath) && await fileExists(bepinexBootstrapPath)) {
    console.log("BepInEx IL2CPP bundle already installed");
    return;
  }

  await ensureDirectory(localDownloadsDirectory);
  const assetFileName = decodeURIComponent(new URL(bepinexWindowsAssetUrl).pathname.split("/").at(-1) ?? "bepinex-il2cpp-win-x64.zip");
  const archivePath = join(localDownloadsDirectory, assetFileName);
  await fetchToFile(bepinexWindowsAssetUrl, archivePath);
  await extractZipArchive(archivePath, localBepInExDirectory);

  if (!(await fileExists(bepinexCorePath)) || !(await fileExists(bepinexBootstrapPath))) {
    throw new Error(`BepInEx install did not produce ${bepinexCorePath}`);
  }

  console.log(`installed BepInEx IL2CPP bundle -> ${localBepInExDirectory}`);
}

async function main() {
  console.log("Bootstrapping local Sneak Out tooling");
  await ensureDotnet();
  await ensureBepInEx();
  console.log("Local tooling is ready");
}

main().catch((error) => {
  console.error(error instanceof Error ? error.message : String(error));
  process.exitCode = 1;
});
