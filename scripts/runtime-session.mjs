import { cp, mkdir, readFile, stat, writeFile } from "node:fs/promises";
import { basename, join, resolve } from "node:path";
import { repositoryRoot, runAndCapture, runCommand } from "./lib/workspace-tools.mjs";

const steamAppUri = "steam://rungameid/2410490";
const defaultGameDirectory = "/run/media/chelokot/second/SteamLibrary/steamapps/common/Sneak Out";
const defaultSteamLogPath = "/var/home/chelokot/.var/app/com.valvesoftware.Steam/steam-2410490.log";
const defaultWindowName = "Sneak Out";
const defaultWaitSeconds = 25;
const defaultHostTarget = "chelokot@localhost";
const defaultHostDisplay = ":0";
const defaultHostWaylandDisplay = "wayland-0";
const defaultHostDbusSessionBusAddress = "unix:path=/run/user/1000/bus";
const defaultHostXdgRuntimeDirectory = "/run/user/1000";
const defaultHostSteamLaunchCommand = "flatpak run --command=/app/bin/steam com.valvesoftware.Steam steam://rungameid/2410490";

function parseArguments(argv) {
  const options = {
    launch: false,
    hostLaunch: false,
    stopExisting: false,
    sessionName: "",
    waitSeconds: defaultWaitSeconds,
    gameDirectory: process.env.SNEAKOUT_GAME_DIR ?? defaultGameDirectory,
    windowName: defaultWindowName,
    clearLogs: true,
    hostTarget: process.env.SNEAKOUT_HOST_TARGET ?? defaultHostTarget,
    hostDisplay: process.env.SNEAKOUT_HOST_DISPLAY ?? defaultHostDisplay,
    hostWaylandDisplay: process.env.SNEAKOUT_HOST_WAYLAND_DISPLAY ?? defaultHostWaylandDisplay,
    hostDbusSessionBusAddress: process.env.SNEAKOUT_HOST_DBUS_SESSION_BUS_ADDRESS ?? defaultHostDbusSessionBusAddress,
    hostXdgRuntimeDirectory: process.env.SNEAKOUT_HOST_XDG_RUNTIME_DIR ?? defaultHostXdgRuntimeDirectory,
    hostSteamLaunchCommand: process.env.SNEAKOUT_HOST_STEAM_COMMAND ?? defaultHostSteamLaunchCommand
  };

  for (let index = 0; index < argv.length; index += 1) {
    const argument = argv[index];
    switch (argument) {
      case "--help":
        printHelpAndExit();
        break;
      case "--launch":
        options.launch = true;
        break;
      case "--host-launch":
        options.launch = true;
        options.hostLaunch = true;
        break;
      case "--stop-existing":
        options.stopExisting = true;
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
      case "--host":
        options.hostTarget = argv[++index] ?? options.hostTarget;
        break;
      case "--host-display":
        options.hostDisplay = argv[++index] ?? options.hostDisplay;
        break;
      case "--host-wayland-display":
        options.hostWaylandDisplay = argv[++index] ?? options.hostWaylandDisplay;
        break;
      case "--host-dbus-session-bus-address":
        options.hostDbusSessionBusAddress = argv[++index] ?? options.hostDbusSessionBusAddress;
        break;
      case "--host-xdg-runtime-dir":
        options.hostXdgRuntimeDirectory = argv[++index] ?? options.hostXdgRuntimeDirectory;
        break;
      case "--host-steam-command":
        options.hostSteamLaunchCommand = argv[++index] ?? options.hostSteamLaunchCommand;
        break;
      default:
        throw new Error(`Unknown argument: ${argument}`);
    }
  }

  return options;
}

function printHelpAndExit() {
  console.log([
    "Usage: node scripts/runtime-session.mjs [options]",
    "",
    "Options:",
    "  --launch                             Launch the game before collecting artifacts.",
    "  --host-launch                        Launch through host Steam via ssh + flatpak.",
    "  --stop-existing                      Stop existing Sneak Out.exe host processes first.",
    "  --session <label>                    Append a label to the session directory name.",
    "  --wait-seconds <n>                   Wait for activity for up to n seconds.",
    "  --game-dir <path>                    Override the game directory.",
    "  --window-name <name>                 Window name to search for.",
    "  --no-log-clear                       Preserve existing BepInEx logs instead of truncating them.",
    "  --host <ssh-target>                  Host target for ssh launch.",
    "  --host-display <display>             Host DISPLAY value.",
    "  --host-wayland-display <display>     Host WAYLAND_DISPLAY value.",
    "  --host-dbus-session-bus-address <address>",
    "  --host-xdg-runtime-dir <path>",
    "  --host-steam-command <command>       Host command that launches Sneak Out through Steam."
  ].join("\n"));
  process.exit(0);
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

function getHostEnvironmentPrefix(options) {
  return [
    `export DISPLAY=${JSON.stringify(options.hostDisplay)}`,
    `export WAYLAND_DISPLAY=${JSON.stringify(options.hostWaylandDisplay)}`,
    `export DBUS_SESSION_BUS_ADDRESS=${JSON.stringify(options.hostDbusSessionBusAddress)}`,
    `export XDG_RUNTIME_DIR=${JSON.stringify(options.hostXdgRuntimeDirectory)}`
  ].join("; ");
}

async function runHostShell(options, command) {
  const remoteCommand = `${getHostEnvironmentPrefix(options)}; bash --noprofile --norc -lc ${JSON.stringify(command)}`;
  return runAndCapture("ssh", [options.hostTarget, remoteCommand]);
}

async function runHostShellDetached(options, command) {
  const remoteCommand = `${getHostEnvironmentPrefix(options)}; bash --noprofile --norc -lc ${JSON.stringify(`(${command}) >/tmp/sneakout-runtime-session.log 2>&1 & disown`)}`;
  await runCommand("ssh", [options.hostTarget, remoteCommand], {
    cwd: repositoryRoot,
    stdio: "ignore"
  });
}

async function getWindowIds(windowName, options) {
  const command = `xdotool search --name ${JSON.stringify(windowName)} 2>/dev/null || true`;
  const { stdout } = options.hostLaunch
    ? await runHostShell(options, command)
    : await runAndCapture("bash", [
        "--noprofile",
        "--norc",
        "-lc",
        command
      ]);

  return stdout
    .split(/\s+/)
    .map((value) => value.trim())
    .filter((value) => value.length > 0);
}

async function getWindowNames(windowIds, options) {
  const names = [];
  for (const windowId of windowIds) {
    const command = `xdotool getwindowname ${windowId} 2>/dev/null || true`;
    const { stdout } = options.hostLaunch
      ? await runHostShell(options, command)
      : await runAndCapture("bash", [
          "--noprofile",
          "--norc",
          "-lc",
          command
        ]);
    names.push({ windowId, name: stdout.trim() });
  }

  return names;
}

async function getSneakOutProcesses(options) {
  if (!options.hostLaunch) {
    return [];
  }

  const command = "ps -eo pid=,args= | grep -Ei 'Sneak Out\\.exe|UnityCrashHandler64\\.exe|proton waitforexitandrun' | grep -Ev 'ssh .*ps -eo pid=,args=|grep -Ei|grep -Ev' || true";
  const { stdout } = await runHostShell(options, command);
  return stdout
    .split("\n")
    .map((line) => line.trim())
    .filter((line) => line.length > 0);
}

async function stopExistingGameProcesses(options) {
  if (!options.hostLaunch) {
    return;
  }

  const command = [
    "ps -eo pid=,args= | grep -Ei 'Sneak Out\\.exe|UnityCrashHandler64\\.exe|proton waitforexitandrun' | grep -Ev 'ssh .*ps -eo pid=,args=|grep -Ei|grep -Ev' | awk '{ print $1 }' | xargs -r kill -TERM 2>/dev/null || true",
    "sleep 3",
    "ps -eo pid=,args= | grep -Ei 'Sneak Out\\.exe|UnityCrashHandler64\\.exe|proton waitforexitandrun' | grep -Ev 'ssh .*ps -eo pid=,args=|grep -Ei|grep -Ev' | awk '{ print $1 }' | xargs -r kill -KILL 2>/dev/null || true"
  ].join("; ");
  await runHostShell(options, command);
}

async function launchGame(options) {
  if (options.hostLaunch) {
    await runHostShellDetached(options, options.hostSteamLaunchCommand);
    return;
  }

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

function haveWatchedLogsChanged(initialMetadataByPath, currentMetadataByPath) {
  return Object.entries(currentMetadataByPath).some(([path, metadata]) => {
    const initialMetadata = initialMetadataByPath[path];
    return metadata.exists && (
      metadata.mtimeMs > initialMetadata.mtimeMs
      || metadata.size > initialMetadata.size
    );
  });
}

async function getWatchedLogMetadata(paths) {
  const metadataEntries = await Promise.all(
    paths.map(async (path) => [path, await getFileMetadata(path)])
  );
  return Object.fromEntries(metadataEntries);
}

async function waitForSessionActivity(watchedLogPaths, windowName, initialLogMetadataByPath, waitSeconds, options) {
  const deadline = Date.now() + waitSeconds * 1000;

  while (Date.now() < deadline) {
    const [logMetadataByPath, windowIds, processes] = await Promise.all([
      getWatchedLogMetadata(watchedLogPaths),
      getWindowIds(windowName, options),
      getSneakOutProcesses(options)
    ]);

    if (
      windowIds.length > 0
      || processes.length > 0
      || haveWatchedLogsChanged(initialLogMetadataByPath, logMetadataByPath)
    ) {
      return {
        logMetadataByPath,
        windowIds,
        processes
      };
    }

    await new Promise((resolvePromise) => setTimeout(resolvePromise, 1000));
  }

  return {
    logMetadataByPath: await getWatchedLogMetadata(watchedLogPaths),
    windowIds: await getWindowIds(windowName, options),
    processes: await getSneakOutProcesses(options)
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
  const unityOutputLogPath = join(gameDirectory, "output_log.txt");
  const steamLogPath = defaultSteamLogPath;
  const watchedLogPaths = [
    logOutputPath,
    errorLogPath,
    unityOutputLogPath,
    steamLogPath
  ];

  await ensureDirectory(sessionDirectory);
  await Promise.all(
    watchedLogPaths.map((path) => snapshotFile(path, join(sessionDirectory, "before")))
  );

  const initialLogMetadataByPath = await getWatchedLogMetadata(watchedLogPaths);
  if (options.clearLogs) {
    await truncateFile(logOutputPath);
    await truncateFile(errorLogPath);
  }

  const initialWindowIds = await getWindowIds(options.windowName, options);
  const initialProcesses = await getSneakOutProcesses(options);
  if (options.stopExisting) {
    await stopExistingGameProcesses(options);
  }
  if (options.launch) {
    await launchGame(options);
  }

  const activity = await waitForSessionActivity(watchedLogPaths, options.windowName, initialLogMetadataByPath, options.waitSeconds, options);
  const finalWindowNames = await getWindowNames(activity.windowIds, options);
  await Promise.all(
    watchedLogPaths.map((path) => snapshotFile(path, join(sessionDirectory, "after")))
  );

  const summary = {
    sessionLabel,
    launched: options.launch,
    gameDirectory,
    windowName: options.windowName,
    waitSeconds: options.waitSeconds,
    hostLaunch: options.hostLaunch,
    hostTarget: options.hostTarget,
    initialWindowIds,
    initialProcesses,
    finalWindowNames,
    finalProcesses: activity.processes,
    watchedLogPaths,
    initialLogMetadataByPath,
    finalLogMetadataByPath: activity.logMetadataByPath
  };

  await writeFile(join(sessionDirectory, "summary.json"), `${JSON.stringify(summary, null, 2)}\n`);
  console.log(sessionDirectory);
}

await main();
