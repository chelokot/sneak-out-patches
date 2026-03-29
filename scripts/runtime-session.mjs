import { cp, mkdir, readFile, stat, writeFile } from "node:fs/promises";
import { basename, join, resolve } from "node:path";
import { repositoryRoot, runAndCapture, runCommand } from "./lib/workspace-tools.mjs";

const steamAppUri = "steam://rungameid/2410490";
const defaultGameDirectory = "/run/media/chelokot/second/SteamLibrary/steamapps/common/Sneak Out";
const defaultWindowName = "Sneak Out";
const defaultWaitSeconds = 25;

function parseArguments(argv) {
  const options = {
    launch: false,
    sessionName: "",
    waitSeconds: defaultWaitSeconds,
    gameDirectory: process.env.SNEAKOUT_GAME_DIR ?? defaultGameDirectory,
    windowName: defaultWindowName,
    clearLogs: true
  };

  for (let index = 0; index < argv.length; index += 1) {
    const argument = argv[index];
    switch (argument) {
      case "--launch":
        options.launch = true;
        break;
      case "--session":
        options.sessionName = argv[++index] ?? "";
        break;
      case "--wait-seconds":
        options.waitSeconds = Number.parseInt(argv[++index] ?? `${defaultWaitSeconds}`, 10);
        break;
      case "--game-dir":
        options.gameDirectory = argv[++index] ?? options.gameDirectory;
        break;
      case "--window-name":
        options.windowName = argv[++index] ?? options.windowName;
        break;
      case "--no-log-clear":
        options.clearLogs = false;
        break;
      default:
        throw new Error(`Unknown argument: ${argument}`);
    }
  }

  return options;
}

function getTimestampLabel() {
  return new Date().toISOString().replaceAll(":", "-").replaceAll(".", "-");
}

async function ensureDirectory(path) {
  await mkdir(path, { recursive: true });
}

async function snapshotFile(sourcePath, destinationDirectory) {
  try {
    await stat(sourcePath);
  } catch {
    return;
  }

  await ensureDirectory(destinationDirectory);
  await cp(sourcePath, join(destinationDirectory, basename(sourcePath)));
}

async function truncateFile(path) {
  try {
    await stat(path);
  } catch {
    return;
  }

  await writeFile(path, "");
}

async function getWindowIds(windowName) {
  const { stdout } = await runAndCapture("bash", [
    "--noprofile",
    "--norc",
    "-lc",
    `xdotool search --name ${JSON.stringify(windowName)} 2>/dev/null || true`
  ]);

  return stdout
    .split(/\s+/)
    .map((value) => value.trim())
    .filter((value) => value.length > 0);
}

async function getWindowNames(windowIds) {
  const names = [];
  for (const windowId of windowIds) {
    const { stdout } = await runAndCapture("bash", [
      "--noprofile",
      "--norc",
      "-lc",
      `xdotool getwindowname ${windowId} 2>/dev/null || true`
    ]);
    names.push({ windowId, name: stdout.trim() });
  }

  return names;
}

async function launchGame() {
  await runCommand("xdg-open", [steamAppUri], {
    cwd: repositoryRoot,
    stdio: "ignore"
  });
}

async function getFileMetadata(path) {
  try {
    const fileStat = await stat(path);
    return {
      exists: true,
      mtimeMs: fileStat.mtimeMs,
      size: fileStat.size
    };
  } catch {
    return {
      exists: false,
      mtimeMs: 0,
      size: 0
    };
  }
}

async function waitForSessionActivity(logPath, windowName, initialLogMetadata, waitSeconds) {
  const deadline = Date.now() + waitSeconds * 1000;

  while (Date.now() < deadline) {
    const [logMetadata, windowIds] = await Promise.all([
      getFileMetadata(logPath),
      getWindowIds(windowName)
    ]);

    if (
      windowIds.length > 0
      || (logMetadata.exists && (
        logMetadata.mtimeMs > initialLogMetadata.mtimeMs
        || logMetadata.size > initialLogMetadata.size
      ))
    ) {
      return {
        logMetadata,
        windowIds
      };
    }

    await new Promise((resolvePromise) => setTimeout(resolvePromise, 1000));
  }

  return {
    logMetadata: await getFileMetadata(logPath),
    windowIds: await getWindowIds(windowName)
  };
}

async function main() {
  const options = parseArguments(process.argv.slice(2));
  const sessionLabel = `${getTimestampLabel()}${options.sessionName ? `-${options.sessionName}` : ""}`;
  const sessionDirectory = resolve(repositoryRoot, ".tmp", "runtime-sessions", sessionLabel);
  const gameDirectory = resolve(options.gameDirectory);
  const bepinexDirectory = join(gameDirectory, "BepInEx");
  const logOutputPath = join(bepinexDirectory, "LogOutput.log");
  const errorLogPath = join(bepinexDirectory, "ErrorLog.log");

  await ensureDirectory(sessionDirectory);
  await snapshotFile(logOutputPath, join(sessionDirectory, "before"));
  await snapshotFile(errorLogPath, join(sessionDirectory, "before"));

  const initialLogMetadata = await getFileMetadata(logOutputPath);
  if (options.clearLogs) {
    await truncateFile(logOutputPath);
    await truncateFile(errorLogPath);
  }

  const initialWindowIds = await getWindowIds(options.windowName);
  if (options.launch) {
    await launchGame();
  }

  const activity = await waitForSessionActivity(logOutputPath, options.windowName, initialLogMetadata, options.waitSeconds);
  const finalWindowNames = await getWindowNames(activity.windowIds);
  await snapshotFile(logOutputPath, join(sessionDirectory, "after"));
  await snapshotFile(errorLogPath, join(sessionDirectory, "after"));

  const summary = {
    sessionLabel,
    launched: options.launch,
    gameDirectory,
    windowName: options.windowName,
    waitSeconds: options.waitSeconds,
    initialWindowIds,
    finalWindowNames,
    initialLogMetadata,
    finalLogMetadata: activity.logMetadata
  };

  await writeFile(join(sessionDirectory, "summary.json"), `${JSON.stringify(summary, null, 2)}\n`);
  console.log(sessionDirectory);
}

await main();
