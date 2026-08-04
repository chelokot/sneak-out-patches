import { cp, mkdir, open, readdir, readFile, rm, stat, writeFile } from "node:fs/promises";
import { spawn } from "node:child_process";
import { basename, dirname, join, resolve } from "node:path";
import { resolveGameDirectory, steamAppId } from "./lib/game-install.mjs";
import { repositoryRoot, runAndCapture } from "./lib/workspace-tools.mjs";

const sampleIntervalMs = 1000;

function parseArguments(argv) {
  const options = {
    durationSeconds: 90,
    sessionName: "profile",
    gameDirectory: process.env.SNEAKOUT_GAME_DIR,
    launch: true,
    leaveRunning: false,
    focusWindow: true,
    diagnosticReports: true
  };

  for (let index = 0; index < argv.length; index += 1) {
    const argument = argv[index];
    switch (argument) {
      case "--duration-seconds":
        options.durationSeconds = Number.parseInt(argv[++index] ?? "90", 10);
        break;
      case "--session":
        options.sessionName = argv[++index] ?? "profile";
        break;
      case "--game-dir":
        options.gameDirectory = argv[++index] ?? options.gameDirectory;
        break;
      case "--no-launch":
        options.launch = false;
        break;
      case "--leave-running":
        options.leaveRunning = true;
        break;
      case "--no-focus":
        options.focusWindow = false;
        break;
      case "--no-interval-reports":
        options.diagnosticReports = false;
        break;
      case "--help":
        console.log("Usage: node scripts/performance-session.mjs [--duration-seconds N] [--session NAME] [--game-dir PATH] [--no-launch] [--leave-running] [--no-focus] [--no-interval-reports]");
        process.exit(0);
        break;
      default:
        throw new Error(`Unknown argument: ${argument}`);
    }
  }

  if (!Number.isFinite(options.durationSeconds) || options.durationSeconds < 5) {
    throw new Error("--duration-seconds must be at least 5");
  }
  return options;
}

function sleep(milliseconds) {
  return new Promise((resolvePromise) => setTimeout(resolvePromise, milliseconds));
}

function timestampLabel() {
  return new Date().toISOString().replaceAll(":", "-").replaceAll(".", "-");
}

function enableDiagnosticIntervalReports(content) {
  if (/^WriteReportsDuringGameplay\s*=/m.test(content)) {
    return content.replace(
      /^WriteReportsDuringGameplay\s*=.*$/m,
      "WriteReportsDuringGameplay = true"
    );
  }
  if (/^\[telemetry\]\s*$/m.test(content)) {
    return content.replace(
      /^(\[telemetry\]\s*\r?\n)/m,
      "$1WriteReportsDuringGameplay = true\n"
    );
  }
  return `${content.trimEnd()}\n\n[telemetry]\nWriteReportsDuringGameplay = true\n`;
}

async function runHost(command, argumentsList) {
  return runAndCapture("flatpak-spawn", ["--host", command, ...argumentsList]);
}

async function runHostDetached(command) {
  const child = spawn(
    "flatpak-spawn",
    ["--host", "/bin/sh", "-lc", `${command} >/tmp/sneakout-performance-session.log 2>&1 &`],
    { cwd: repositoryRoot, detached: true, stdio: "ignore" }
  );
  child.unref();
}

async function findSneakOutPid() {
  for (const entry of await readdir("/proc", { withFileTypes: true })) {
    if (!entry.isDirectory() || !/^\d+$/.test(entry.name)) {
      continue;
    }
    try {
      const processName = (await readFile(`/proc/${entry.name}/comm`, "utf8")).trim();
      if (processName !== "Sneak Out.exe") {
        continue;
      }
      const status = await readFile(`/proc/${entry.name}/status`, "utf8");
      if (/^State:\s+Z/m.test(status)) {
        continue;
      }
      const commandLine = (await readFile(`/proc/${entry.name}/cmdline`))
        .toString("utf8")
        .replaceAll("\0", " ");
      if (/Sneak Out[\\/]Sneak Out\.exe/i.test(commandLine)) {
        return Number.parseInt(entry.name, 10);
      }
    } catch {
      // Processes can disappear while /proc is scanned.
    }
  }
  return 0;
}

async function waitForSneakOutPid(timeoutSeconds = 90) {
  const deadline = Date.now() + timeoutSeconds * 1000;
  while (Date.now() < deadline) {
    const pid = await findSneakOutPid();
    if (pid > 0) {
      return pid;
    }
    await sleep(500);
  }
  throw new Error("Sneak Out process did not appear within the startup timeout");
}

async function activateGameWindowOnce(timeoutSeconds = 30) {
  const deadline = Date.now() + timeoutSeconds * 1000;
  while (Date.now() < deadline) {
    try {
      const { stdout } = await runHost("wmctrl", ["-lx"]);
      const gameWindow = stdout
        .split("\n")
        .find((line) => /(?:sneak out|steam_app_2410490)/i.test(line));
      if (gameWindow) {
        const windowId = gameWindow.trim().split(/\s+/, 1)[0];
        await runHost("wmctrl", ["-i", "-a", windowId]);
        return;
      }
    } catch {
      // The game can render and be captured even if the host has no EWMH helper.
      return;
    }
    await sleep(250);
  }
}

async function getGameWindowId() {
  try {
    const { stdout } = await runAndCapture("xprop", ["-root", "_NET_CLIENT_LIST"]);
    const windowIds = stdout.match(/0x[0-9a-f]+/gi) ?? [];
    for (const windowId of windowIds) {
      const properties = await runAndCapture("xprop", ["-id", windowId, "_NET_WM_NAME", "WM_CLASS"]);
      if (properties.stdout.includes("Sneak Out") || properties.stdout.includes("steam_app_2410490")) {
        return windowId;
      }
    }
  } catch {
    // A window is optional during early startup.
  }
  return "";
}

function parseKeyValueFile(content) {
  return Object.fromEntries(
    content.split("\n")
      .map((line) => line.match(/^([^:]+):\s*(.*)$/))
      .filter(Boolean)
      .map((match) => [match[1], match[2]])
  );
}

async function readProcessSample(pid, previousSample, clockTicks) {
  const [statusText, statText] = await Promise.all([
    readFile(`/proc/${pid}/status`, "utf8"),
    readFile(`/proc/${pid}/stat`, "utf8")
  ]);
  let openFileDescriptorCount = null;
  try {
    openFileDescriptorCount = (await readdir(`/proc/${pid}/fd`)).length;
  } catch (error) {
    if (error?.code !== "EACCES" && error?.code !== "EPERM") {
      throw error;
    }
  }
  let ioText = "";
  try {
    ioText = await readFile(`/proc/${pid}/io`, "utf8");
  } catch (error) {
    if (error?.code !== "EACCES" && error?.code !== "EPERM") {
      throw error;
    }
  }
  const status = parseKeyValueFile(statusText);
  const io = parseKeyValueFile(ioText);
  const statTail = statText.slice(statText.lastIndexOf(")") + 2).trim().split(/\s+/);
  const processTicks = Number.parseInt(statTail[11], 10) + Number.parseInt(statTail[12], 10);
  const now = Date.now();
  const elapsedSeconds = previousSample ? (now - previousSample.timestamp_ms) / 1000 : 0;
  const cpuPercent = previousSample && elapsedSeconds > 0
    ? (processTicks - previousSample.process_ticks) / clockTicks / elapsedSeconds * 100
    : 0;
  return {
    timestamp_ms: now,
    elapsed_s: 0,
    pid,
    process_ticks: processTicks,
    cpu_pct_one_core_100: Number(cpuPercent.toFixed(2)),
    rss_mb: Number((Number.parseInt(status.VmRSS ?? "0", 10) / 1024).toFixed(2)),
    peak_rss_mb: Number((Number.parseInt(status.VmHWM ?? "0", 10) / 1024).toFixed(2)),
    virtual_mb: Number((Number.parseInt(status.VmSize ?? "0", 10) / 1024).toFixed(2)),
    threads: Number.parseInt(status.Threads ?? "0", 10),
    voluntary_context_switches: Number.parseInt(status.voluntary_ctxt_switches ?? "0", 10),
    involuntary_context_switches: Number.parseInt(status.nonvoluntary_ctxt_switches ?? "0", 10),
    process_io_available: ioText.length > 0,
    rchar_mb: ioText ? Number((Number.parseInt(io.rchar ?? "0", 10) / 1048576).toFixed(2)) : null,
    wchar_mb: ioText ? Number((Number.parseInt(io.wchar ?? "0", 10) / 1048576).toFixed(2)) : null,
    disk_read_mb: ioText ? Number((Number.parseInt(io.read_bytes ?? "0", 10) / 1048576).toFixed(2)) : null,
    disk_write_mb: ioText ? Number((Number.parseInt(io.write_bytes ?? "0", 10) / 1048576).toFixed(2)) : null,
    open_fds: openFileDescriptorCount
  };
}

async function readGpuSample() {
  try {
    const { stdout } = await runHost("nvidia-smi", [
      "--query-gpu=utilization.gpu,memory.used,power.draw",
      "--format=csv,noheader,nounits"
    ]);
    const [utilization, memory, power] = stdout.trim().split(",").map((value) => Number.parseFloat(value.trim()));
    return { gpu_pct: utilization, gpu_memory_mb: memory, gpu_power_w: power };
  } catch {
    return { gpu_pct: null, gpu_memory_mb: null, gpu_power_w: null };
  }
}

async function readCgroupIoSample(pid) {
  try {
    const cgroupText = await readFile(`/proc/${pid}/cgroup`, "utf8");
    const unifiedEntry = cgroupText.split("\n").find((line) => line.startsWith("0::"));
    if (!unifiedEntry) {
      return {};
    }
    const cgroupPath = unifiedEntry.slice(3);
    const ioText = await readFile(join("/sys/fs/cgroup", cgroupPath, "io.stat"), "utf8");
    const totals = { cgroup_read_mb: 0, cgroup_write_mb: 0, cgroup_read_ops: 0, cgroup_write_ops: 0 };
    for (const line of ioText.trim().split("\n")) {
      const values = Object.fromEntries(
        line.split(/\s+/).slice(1).map((entry) => entry.split("="))
      );
      totals.cgroup_read_mb += Number.parseInt(values.rbytes ?? "0", 10) / 1048576;
      totals.cgroup_write_mb += Number.parseInt(values.wbytes ?? "0", 10) / 1048576;
      totals.cgroup_read_ops += Number.parseInt(values.rios ?? "0", 10);
      totals.cgroup_write_ops += Number.parseInt(values.wios ?? "0", 10);
    }
    totals.cgroup_read_mb = Number(totals.cgroup_read_mb.toFixed(2));
    totals.cgroup_write_mb = Number(totals.cgroup_write_mb.toFixed(2));
    return totals;
  } catch {
    return {};
  }
}

async function snapshotPath(sourcePath, destinationDirectory) {
  try {
    await stat(sourcePath);
  } catch {
    return;
  }
  await cp(sourcePath, join(destinationDirectory, basename(sourcePath)), { recursive: true });
}

async function snapshotDirectoryState(directoryPath) {
  const state = new Map();
  try {
    for (const entry of await readdir(directoryPath, { withFileTypes: true })) {
      if (!entry.isFile()) {
        continue;
      }
      const metadata = await stat(join(directoryPath, entry.name));
      state.set(entry.name, `${metadata.size}:${metadata.mtimeMs}`);
    }
  } catch (error) {
    if (error?.code !== "ENOENT") {
      throw error;
    }
  }
  return state;
}

async function snapshotChangedFiles(sourceDirectory, destinationDirectory, initialState) {
  let entries;
  try {
    entries = await readdir(sourceDirectory, { withFileTypes: true });
  } catch (error) {
    if (error?.code === "ENOENT") {
      return;
    }
    throw error;
  }

  const reportDestination = join(destinationDirectory, basename(sourceDirectory));
  for (const entry of entries) {
    if (!entry.isFile()) {
      continue;
    }
    const sourcePath = join(sourceDirectory, entry.name);
    const metadata = await stat(sourcePath);
    const signature = `${metadata.size}:${metadata.mtimeMs}`;
    if (initialState.get(entry.name) === signature) {
      continue;
    }
    await mkdir(reportDestination, { recursive: true });
    await cp(sourcePath, join(reportDestination, entry.name));
  }
}

async function closeGame(pid) {
  const windowId = await getGameWindowId();
  if (windowId) {
    try {
      await runHost("xdotool", ["windowclose", windowId]);
    } catch {
      // SIGTERM below is the deterministic fallback.
    }
  }
  const deadline = Date.now() + 10_000;
  while (Date.now() < deadline) {
    try {
      const status = await readFile(`/proc/${pid}/status`, "utf8");
      if (/^State:\s+Z/m.test(status)) {
        return;
      }
    } catch {
      return;
    }
    await sleep(250);
  }
  try {
    process.kill(pid, "SIGTERM");
  } catch {
    // It exited between the final probe and signal.
  }
}

async function cleanupSneakOutLaunchers() {
  const processIds = [];
  for (const entry of await readdir("/proc", { withFileTypes: true })) {
    if (!entry.isDirectory() || !/^\d+$/.test(entry.name)) {
      continue;
    }
    try {
      const commandLine = (await readFile(`/proc/${entry.name}/cmdline`))
        .toString("utf8")
        .replaceAll("\0", " ");
      if (/Sneak Out[\\/]Sneak Out\.exe/i.test(commandLine)
          && !/^Z:/i.test(commandLine)
          && /(?:proton|steam-runtime-launch|pressure-vessel|pv-adverb|bwrap|c:\\windows\\system32\\steam\.exe)/i.test(commandLine)) {
        processIds.push(Number.parseInt(entry.name, 10));
      }
    } catch {
      // Processes can disappear while /proc is scanned.
    }
  }
  for (const processId of processIds.reverse()) {
    try {
      process.kill(processId, "SIGTERM");
    } catch {
      // It exited between discovery and signal.
    }
  }
}

async function main() {
  const options = parseArguments(process.argv.slice(2));
  const gameDirectory = await resolveGameDirectory(options.gameDirectory);
  const sessionDirectory = resolve(repositoryRoot, ".tmp", "performance-sessions", `${timestampLabel()}-${options.sessionName}`);
  await mkdir(sessionDirectory, { recursive: true });
  const sampleHandle = await open(join(sessionDirectory, "process-samples.jsonl"), "w");
  const performanceReportDirectory = join(gameDirectory, "BepInEx", "performance-reports");
  const profileReportDirectory = join(gameDirectory, "BepInEx", "profile-reports");
  const performanceConfigPath = join(
    gameDirectory,
    "BepInEx",
    "config",
    "chelokot.sneakout.performance-optimizer.cfg"
  );
  let originalPerformanceConfig = null;
  if (options.launch && options.diagnosticReports) {
    try {
      originalPerformanceConfig = await readFile(performanceConfigPath, "utf8");
    } catch (error) {
      if (error?.code !== "ENOENT") {
        throw error;
      }
    }
    await mkdir(dirname(performanceConfigPath), { recursive: true });
    await writeFile(
      performanceConfigPath,
      enableDiagnosticIntervalReports(originalPerformanceConfig ?? ""),
      "utf8"
    );
  }
  const initialPerformanceReports = await snapshotDirectoryState(performanceReportDirectory);
  const initialProfileReports = await snapshotDirectoryState(profileReportDirectory);
  const startedAt = Date.now();
  let pid = 0;
  let previousSample;
  let caughtSignal = "";
  const signalHandler = (signal) => { caughtSignal = signal; };
  process.on("SIGINT", signalHandler);
  process.on("SIGTERM", signalHandler);

  try {
    if (options.launch) {
      const existingPid = await findSneakOutPid();
      if (existingPid > 0) {
        throw new Error(`Sneak Out is already running as PID ${existingPid}`);
      }
      await runHostDetached(`flatpak run --command=/app/bin/steam com.valvesoftware.Steam steam://rungameid/${steamAppId}`);
    }
    pid = await waitForSneakOutPid();
    if (options.focusWindow) {
      await activateGameWindowOnce();
    }
    const { stdout: clockTicksText } = await runAndCapture("getconf", ["CLK_TCK"]);
    const clockTicks = Number.parseInt(clockTicksText.trim(), 10);
    const deadline = startedAt + options.durationSeconds * 1000;
    while (Date.now() < deadline && !caughtSignal) {
      try {
        const processSample = await readProcessSample(pid, previousSample, clockTicks);
        processSample.elapsed_s = Number(((Date.now() - startedAt) / 1000).toFixed(3));
        previousSample = processSample;
        const [gpuSample, cgroupIoSample] = await Promise.all([
          readGpuSample(),
          readCgroupIoSample(pid)
        ]);
        await sampleHandle.write(`${JSON.stringify({ ...processSample, ...gpuSample, ...cgroupIoSample })}\n`);
      } catch (error) {
        if (error?.code === "ENOENT") {
          break;
        }
        throw error;
      }
      await sleep(sampleIntervalMs);
    }
  } finally {
    try {
      if (pid > 0 && !options.leaveRunning) {
        await closeGame(pid);
        await sleep(1000);
        await cleanupSneakOutLaunchers();
      }
    } finally {
      if (options.launch && options.diagnosticReports) {
        if (originalPerformanceConfig === null) {
          await rm(performanceConfigPath, { force: true });
        } else {
          await writeFile(performanceConfigPath, originalPerformanceConfig, "utf8");
        }
      }
    }
    await sampleHandle.close();
    await snapshotPath(join(gameDirectory, "BepInEx", "LogOutput.log"), sessionDirectory);
    await snapshotPath(join(gameDirectory, "BepInEx", "ErrorLog.log"), sessionDirectory);
    await snapshotChangedFiles(performanceReportDirectory, sessionDirectory, initialPerformanceReports);
    await snapshotChangedFiles(profileReportDirectory, sessionDirectory, initialProfileReports);
    const playerLog = resolve(
      gameDirectory,
      "..",
      "..",
      "compatdata",
      `${steamAppId}`,
      "pfx",
      "drive_c",
      "users",
      "steamuser",
      "AppData",
      "LocalLow",
      "Kinguin Studios",
      "Sneak Out",
      "Player.log"
    );
    await snapshotPath(playerLog, sessionDirectory);
    await writeFile(join(sessionDirectory, "session.json"), JSON.stringify({
      started_at: new Date(startedAt).toISOString(),
      duration_seconds: options.durationSeconds,
      pid,
      game_directory: gameDirectory,
      signal: caughtSignal,
      player_log: playerLog
    }, null, 2));
  }
  console.log(sessionDirectory);
}

await main();
