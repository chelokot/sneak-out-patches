import { access, chmod, mkdir, writeFile } from "node:fs/promises";
import { constants } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { spawn } from "node:child_process";

const libraryDirectory = dirname(fileURLToPath(import.meta.url));
export const repositoryRoot = resolve(libraryDirectory, "..", "..");
export const temporaryDirectory = resolve(repositoryRoot, ".tmp");
export const runtimeModDirectory = resolve(temporaryDirectory, "runtime-mod");
export const localDotnetDirectory = resolve(runtimeModDirectory, "dotnet");
export const localDownloadsDirectory = resolve(temporaryDirectory, "downloads");

export function localDotnetExecutablePath() {
  return resolve(localDotnetDirectory, process.platform === "win32" ? "dotnet.exe" : "dotnet");
}

export async function ensureDirectory(path) {
  await mkdir(path, { recursive: true });
}

export async function fileExists(path) {
  try {
    await access(path, constants.F_OK);
    return true;
  } catch {
    return false;
  }
}

export async function ensureExecutable(path) {
  if (process.platform === "win32") {
    return;
  }

  await chmod(path, 0o755);
}

export function runCommand(command, argumentsList, options = {}) {
  return new Promise((resolvePromise, rejectPromise) => {
    const child = spawn(command, argumentsList, {
      cwd: options.cwd ?? repositoryRoot,
      env: options.env ?? process.env,
      stdio: options.stdio ?? "inherit",
      shell: false
    });

    child.on("error", rejectPromise);
    child.on("close", (exitCode) => {
      if (exitCode === 0) {
        resolvePromise();
        return;
      }

      rejectPromise(new Error(`Command failed with exit code ${exitCode}: ${command}`));
    });
  });
}

export async function runAndCapture(command, argumentsList, options = {}) {
  return new Promise((resolvePromise, rejectPromise) => {
    let stdout = "";
    let stderr = "";
    const child = spawn(command, argumentsList, {
      cwd: options.cwd ?? repositoryRoot,
      env: options.env ?? process.env,
      stdio: ["ignore", "pipe", "pipe"],
      shell: false
    });

    child.stdout.on("data", (chunk) => {
      stdout += chunk.toString();
    });
    child.stderr.on("data", (chunk) => {
      stderr += chunk.toString();
    });
    child.on("error", rejectPromise);
    child.on("close", (exitCode) => {
      if (exitCode === 0) {
        resolvePromise({ stdout, stderr });
        return;
      }

      rejectPromise(new Error(`Command failed with exit code ${exitCode}: ${command}\n${stderr || stdout}`));
    });
  });
}

export async function resolvePythonCommand() {
  const candidates = process.platform === "win32"
    ? [
        { command: "py", argumentsList: ["-3", "--version"], prefix: ["-3"] },
        { command: "python", argumentsList: ["--version"], prefix: [] },
        { command: "python3", argumentsList: ["--version"], prefix: [] }
      ]
    : [
        { command: "python3", argumentsList: ["--version"], prefix: [] },
        { command: "python", argumentsList: ["--version"], prefix: [] }
      ];

  for (const candidate of candidates) {
    try {
      await runAndCapture(candidate.command, candidate.argumentsList);
      return { command: candidate.command, prefix: candidate.prefix };
    } catch {
      continue;
    }
  }

  throw new Error("Python 3 is required but was not found in PATH.");
}

export async function fetchToFile(url, destinationPath) {
  const response = await fetch(url);
  if (!response.ok) {
    throw new Error(`Failed to download ${url}: ${response.status} ${response.statusText}`);
  }

  const bytes = new Uint8Array(await response.arrayBuffer());
  await ensureDirectory(dirname(destinationPath));
  await writeFile(destinationPath, bytes);
}
