import { mkdir, readFile, readdir, writeFile } from "node:fs/promises";
import { basename, join, resolve } from "node:path";
import { repositoryRoot } from "./lib/workspace-tools.mjs";

async function latestSessionDirectory() {
  const root = resolve(repositoryRoot, ".tmp", "performance-sessions");
  const entries = (await readdir(root, { withFileTypes: true }))
    .filter((entry) => entry.isDirectory())
    .map((entry) => entry.name)
    .sort();
  if (entries.length === 0) {
    throw new Error("No performance sessions were found");
  }
  return join(root, entries.at(-1));
}

function percentile(values, fraction) {
  if (values.length === 0) {
    return 0;
  }
  const sorted = [...values].sort((left, right) => left - right);
  return sorted[Math.min(sorted.length - 1, Math.floor((sorted.length - 1) * fraction))];
}

function numericSummary(values) {
  const valid = values.filter(Number.isFinite);
  return {
    average: valid.length > 0 ? valid.reduce((sum, value) => sum + value, 0) / valid.length : 0,
    p95: percentile(valid, 0.95),
    maximum: valid.length > 0 ? Math.max(...valid) : 0
  };
}

function parseCsv(content) {
  const lines = content.replace(/^\uFEFF/, "").trim().split(/\r?\n/);
  if (lines.length < 2) {
    return [];
  }
  const headers = lines[0].split(",");
  return lines.slice(1).map((line) => Object.fromEntries(
    line.split(",").map((value, index) => [headers[index], value])
  ));
}

async function readChangedReport(directory, extension) {
  try {
    const names = (await readdir(directory)).filter((name) => name.endsWith(extension)).sort();
    return names.length > 0 ? readFile(join(directory, names.at(-1)), "utf8") : "";
  } catch (error) {
    if (error?.code === "ENOENT") {
      return "";
    }
    throw error;
  }
}

function summarizeTelemetry(rows) {
  const byScene = new Map();
  for (const row of rows) {
    if (!row.scene || row.scene === "Initialization") {
      continue;
    }
    if (!byScene.has(row.scene)) {
      byScene.set(row.scene, []);
    }
    byScene.get(row.scene).push(row);
  }

  return Object.fromEntries(Array.from(byScene, ([scene, sceneRows]) => {
    const settledRows = sceneRows.slice(Math.min(2, Math.max(0, sceneRows.length - 1)));
    const totalFrames = settledRows.reduce((sum, row) => sum + Number(row.frames || 0), 0);
    const totalSeconds = settledRows.reduce(
      (sum, row) => sum + Number(row.frames || 0) / Math.max(0.001, Number(row.avg_fps || 0)),
      0
    );
    const last = sceneRows.at(-1);
    return [scene, {
      samples: settledRows.length,
      frames: totalFrames,
      average_fps: totalSeconds > 0 ? totalFrames / totalSeconds : 0,
      p50_frame_ms_average: numericSummary(settledRows.map((row) => Number(row.p50_ms))).average,
      p95_frame_ms_average: numericSummary(settledRows.map((row) => Number(row.p95_ms))).average,
      severe_stutters: settledRows.reduce((sum, row) => sum + Number(row.frames_over_100ms || 0), 0),
      gc_collections: settledRows.reduce(
        (sum, row) => sum + Number(row.gc0 || 0) + Number(row.gc1 || 0) + Number(row.gc2 || 0),
        0
      ),
      quality_level: Number(last.quality_level),
      render_scale: Number(last.render_scale),
      additional_light_shadows: last.additional_shadows === "1",
      v_sync_count: Number(last.vsync_count)
    }];
  }));
}

async function main() {
  const sessionDirectory = process.argv[2]
    ? resolve(process.argv[2])
    : await latestSessionDirectory();
  const samples = (await readFile(join(sessionDirectory, "process-samples.jsonl"), "utf8"))
    .trim()
    .split(/\r?\n/)
    .filter(Boolean)
    .map((line) => JSON.parse(line));
  const telemetryText = await readChangedReport(join(sessionDirectory, "performance-reports"), ".csv");
  const telemetryRows = telemetryText ? parseCsv(telemetryText) : [];
  const playerLog = await readFile(join(sessionDirectory, "Player.log"), "utf8").catch(() => "");
  const bepLog = await readFile(join(sessionDirectory, "LogOutput.log"), "utf8").catch(() => "");
  const firstIo = samples.find((sample) => Number.isFinite(sample.cgroup_read_mb));
  const lastIo = [...samples].reverse().find((sample) => Number.isFinite(sample.cgroup_read_mb));
  const summary = {
    session: basename(sessionDirectory),
    duration_seconds: samples.at(-1)?.elapsed_s ?? 0,
    process: {
      cpu_one_core_100: numericSummary(samples.map((sample) => sample.cpu_pct_one_core_100)),
      rss_mb: numericSummary(samples.map((sample) => sample.rss_mb)),
      threads: numericSummary(samples.map((sample) => sample.threads)),
      gpu_percent: numericSummary(samples.map((sample) => sample.gpu_pct)),
      gpu_memory_mb: numericSummary(samples.map((sample) => sample.gpu_memory_mb)),
      cgroup_read_mb_delta: firstIo && lastIo ? lastIo.cgroup_read_mb - firstIo.cgroup_read_mb : null,
      cgroup_write_mb_delta: firstIo && lastIo ? lastIo.cgroup_write_mb - firstIo.cgroup_write_mb : null
    },
    scenes: summarizeTelemetry(telemetryRows),
    diagnostics: {
      null_reference_exceptions: (playerLog.match(/NullReferenceException/g) ?? []).length,
      bep_exceptions: (bepLog.match(/\[(?:Error|Fatal)/g) ?? []).length,
      negative_box_collider_warnings: (playerLog.match(/BoxCollider does not support negative scale or size/g) ?? []).length,
      missing_script_warnings: (playerLog.match(/The referenced script on this Behaviour/g) ?? []).length
    }
  };
  await mkdir(sessionDirectory, { recursive: true });
  await writeFile(join(sessionDirectory, "summary.json"), JSON.stringify(summary, null, 2));
  console.log(JSON.stringify(summary, null, 2));
}

await main();
