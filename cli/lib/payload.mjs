import { createHash } from "node:crypto";
import { mkdir, readFile, rm, writeFile } from "node:fs/promises";
import { homedir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { unzipSync } from "fflate";
import { exists } from "./io.mjs";

const moduleDirectory = dirname(fileURLToPath(import.meta.url));
const packageRoot = resolve(moduleDirectory, "..", "..");
const repository = "chelokot/sneak-out-patches";
const payloadAssetName = "sneakout-patches-payload.zip";
const payloadChecksumAssetName = `${payloadAssetName}.sha256`;
const bepinexUrl = "https://builds.bepinex.dev/projects/bepinex_be/755/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.755%2B3fab71a.zip";
const bepinexSha256 = "3616d6a67f5f595973ec4aa7bd7edaf7f799d5bb9926f7146a6dcc7b4abf478f";

function cacheRoot() {
  if (process.env.SNEAKOUT_PATCHES_CACHE_DIR) {
    return resolve(process.env.SNEAKOUT_PATCHES_CACHE_DIR);
  }
  if (process.platform === "win32") {
    return join(process.env.LOCALAPPDATA ?? homedir(), "sneakout-patches", "cache");
  }
  return join(process.env.XDG_CACHE_HOME ?? join(homedir(), ".cache"), "sneakout-patches");
}

async function fetchBytes(url, accept = "application/octet-stream") {
  const response = await fetch(url, {
    headers: {
      Accept: accept,
      "User-Agent": "@chelokot/sneak-out-patches"
    },
    redirect: "follow"
  });
  if (!response.ok) {
    throw new Error(`Download failed (${response.status} ${response.statusText}): ${url}`);
  }
  return Buffer.from(await response.arrayBuffer());
}

async function fetchJson(url) {
  return JSON.parse((await fetchBytes(url, "application/vnd.github+json")).toString("utf8"));
}

function validateArchiveEntries(entries) {
  for (const name of Object.keys(entries)) {
    const normalized = name.replace(/\\/g, "/");
    if (normalized.startsWith("/") || normalized.split("/").includes("..")) {
      throw new Error(`Unsafe archive entry: ${name}`);
    }
  }
}

async function extractVerifiedZip(zipBytes, expectedSha256, destination) {
  const actual = createHash("sha256").update(zipBytes).digest("hex");
  if (actual !== expectedSha256.toLowerCase()) {
    throw new Error(`Archive checksum mismatch: expected ${expectedSha256}, received ${actual}`);
  }
  const entries = unzipSync(new Uint8Array(zipBytes));
  validateArchiveEntries(entries);
  await mkdir(destination, { recursive: true });
  for (const [name, bytes] of Object.entries(entries)) {
    if (name.endsWith("/")) {
      await mkdir(join(destination, name), { recursive: true });
      continue;
    }
    const path = join(destination, name);
    await mkdir(dirname(path), { recursive: true });
    await writeFile(path, bytes);
  }
}

async function isPayloadRoot(root) {
  return (
    await exists(join(root, "runtime_mods_manifest.json")) &&
    await exists(join(root, "supported_game_build.json")) &&
    await exists(join(root, "artifacts", "runtime_mods"))
  );
}

export async function resolvePayload({ offline = false } = {}) {
  if (process.env.SNEAKOUT_PATCHES_PAYLOAD_DIR) {
    const root = resolve(process.env.SNEAKOUT_PATCHES_PAYLOAD_DIR);
    if (!(await isPayloadRoot(root))) {
      throw new Error(`Invalid SNEAKOUT_PATCHES_PAYLOAD_DIR: ${root}`);
    }
    return { root, source: "local override" };
  }

  if (!offline) {
    try {
      const release = await fetchJson(`https://api.github.com/repos/${repository}/releases/latest`);
      const payloadAsset = release.assets?.find((asset) => asset.name === payloadAssetName);
      const checksumAsset = release.assets?.find((asset) => asset.name === payloadChecksumAssetName);
      if (payloadAsset && checksumAsset) {
        const releaseCache = join(cacheRoot(), "releases", String(release.id));
        if (!(await isPayloadRoot(releaseCache))) {
          await rm(releaseCache, { recursive: true, force: true });
          const [payloadBytes, checksumBytes] = await Promise.all([
            fetchBytes(payloadAsset.browser_download_url),
            fetchBytes(checksumAsset.browser_download_url)
          ]);
          const expectedSha256 = checksumBytes.toString("utf8").trim().split(/\s+/)[0];
          await extractVerifiedZip(payloadBytes, expectedSha256, releaseCache);
        }
        return { root: releaseCache, source: `GitHub release ${release.tag_name}` };
      }
    } catch (error) {
      process.stderr.write(`warning: could not use the latest GitHub release: ${error.message}\n`);
    }
  }

  if (await isPayloadRoot(packageRoot)) {
    return { root: packageRoot, source: "npm package fallback" };
  }
  throw new Error("No installer payload is available.");
}

function validBepInExRoot(root) {
  return Promise.all([
    exists(join(root, "BepInEx", "core", "BepInEx.Unity.IL2CPP.dll")),
    exists(join(root, "winhttp.dll"))
  ]).then((checks) => checks.every(Boolean));
}

export async function resolveBepInEx() {
  if (process.env.SNEAKOUT_BEPINEX_DIR) {
    const root = resolve(process.env.SNEAKOUT_BEPINEX_DIR);
    if (!(await validBepInExRoot(root))) {
      throw new Error(`Invalid SNEAKOUT_BEPINEX_DIR: ${root}`);
    }
    return root;
  }

  const root = join(cacheRoot(), "bepinex", bepinexSha256);
  if (await validBepInExRoot(root)) {
    return root;
  }
  await rm(root, { recursive: true, force: true });
  process.stdout.write("Downloading BepInEx IL2CPP...\n");
  await extractVerifiedZip(await fetchBytes(bepinexUrl), bepinexSha256, root);
  if (!(await validBepInExRoot(root))) {
    throw new Error("Downloaded BepInEx archive is incomplete.");
  }
  return root;
}

export async function loadPayloadMetadata(root) {
  const [manifest, supportedBuild] = await Promise.all([
    readFile(join(root, "runtime_mods_manifest.json"), "utf8").then(JSON.parse),
    readFile(join(root, "supported_game_build.json"), "utf8").then(JSON.parse)
  ]);
  return { manifest, supportedBuild };
}
