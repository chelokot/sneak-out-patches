import { createHash } from "node:crypto";
import { createReadStream } from "node:fs";
import { readFile } from "node:fs/promises";
import { join, resolve } from "node:path";
import {
  fileExists,
  localBepInExDirectory,
  localDotnetExecutablePath,
  repositoryRoot,
  resolvePythonCommand,
  runAndCapture
} from "./lib/workspace-tools.mjs";
import {
  readSteamBuildId,
  resolveBuildInteropDirectory,
  resolveGameDirectory,
  steamAppId
} from "./lib/game-install.mjs";

const supportedBuildPath = resolve(repositoryRoot, "supported_game_build.json");

function argumentValue(argv, index, optionName) {
  const value = argv[index + 1];
  if (!value || value.startsWith("--")) {
    throw new Error(`${optionName} requires a path.`);
  }
  return value;
}

function parseArguments(argv) {
  const options = {
    gameDirectory: undefined,
    interopDirectory: undefined
  };

  for (let index = 0; index < argv.length; index += 1) {
    switch (argv[index]) {
      case "--game-dir":
        options.gameDirectory = argumentValue(argv, index, "--game-dir");
        index += 1;
        break;
      case "--interop-dir":
        options.interopDirectory = argumentValue(argv, index, "--interop-dir");
        index += 1;
        break;
      case "--help":
        console.log([
          "Usage: node scripts/doctor.mjs [options]",
          "",
          "Options:",
          "  --game-dir <path>     Override Steam game-directory detection.",
          "  --interop-dir <path>  Override the installed BepInEx interop directory."
        ].join("\n"));
        process.exit(0);
        break;
      default:
        throw new Error(`Unknown argument: ${argv[index]}`);
    }
  }

  return options;
}

async function executableVersion(executablePath, versionArguments) {
  const { stdout, stderr } = await runAndCapture(executablePath, versionArguments);
  return stdout.trim() || stderr.trim();
}

function sha256File(path) {
  return new Promise((resolvePromise, rejectPromise) => {
    const hash = createHash("sha256");
    const stream = createReadStream(path);
    stream.on("error", rejectPromise);
    stream.on("data", (chunk) => hash.update(chunk));
    stream.on("end", () => resolvePromise(hash.digest("hex")));
  });
}

function printFingerprint(label, actualValue, expectedValue) {
  const status = actualValue === expectedValue ? "current" : `mismatch, expected ${expectedValue}`;
  console.log(`${label}: ${actualValue} (${status})`);
  return actualValue === expectedValue;
}

async function main() {
  const options = parseArguments(process.argv.slice(2));
  const python = await resolvePythonCommand();
  console.log(`python: ${await executableVersion(python.command, [...python.prefix, "--version"])}`);
  console.log(`dotnet: ${await executableVersion(localDotnetExecutablePath(), ["--version"])}`);

  const bepinexCorePath = join(localBepInExDirectory, "BepInEx", "core", "BepInEx.Unity.IL2CPP.dll");
  if (!(await fileExists(bepinexCorePath))) {
    throw new Error(`BepInEx build references are missing: ${bepinexCorePath}. Run npm install.`);
  }
  console.log(`BepInEx build references: ${bepinexCorePath}`);

  const supportedBuild = JSON.parse(await readFile(supportedBuildPath, "utf8"));
  if (supportedBuild.steam_app_id !== steamAppId) {
    throw new Error(
      `${supportedBuildPath} targets Steam app ${supportedBuild.steam_app_id}, expected ${steamAppId}.`
    );
  }

  const gameDirectory = await resolveGameDirectory(options.gameDirectory);
  const { interopDirectory, source: interopSource } = await resolveBuildInteropDirectory({
    gameDirectory,
    interopDirectory: options.interopDirectory
  });
  const gameAssemblyPath = join(gameDirectory, "GameAssembly.dll");
  const globalMetadataPath = join(
    gameDirectory,
    "Sneak Out_Data",
    "il2cpp_data",
    "Metadata",
    "global-metadata.dat"
  );
  const interopAssemblyPath = join(interopDirectory, supportedBuild.interop_assembly);

  console.log(`game: ${gameDirectory}`);
  console.log(`interop: ${interopDirectory} (${interopSource})`);

  const [
    installedBuildId,
    gameAssemblySha256,
    globalMetadataSha256,
    interopSha256
  ] = await Promise.all([
    readSteamBuildId(gameDirectory),
    sha256File(gameAssemblyPath),
    sha256File(globalMetadataPath),
    sha256File(interopAssemblyPath)
  ]);

  const checks = [
    printFingerprint("Steam build", installedBuildId, supportedBuild.steam_build_id),
    printFingerprint("GameAssembly.dll sha256", gameAssemblySha256, supportedBuild.game_assembly_sha256),
    printFingerprint("global-metadata.dat sha256", globalMetadataSha256, supportedBuild.global_metadata_sha256),
    printFingerprint(
      `interop fingerprint (${supportedBuild.interop_assembly} sha256)`,
      interopSha256,
      supportedBuild.interop_sha256
    )
  ];

  if (checks.every(Boolean)) {
    console.log("Sneak Out build inputs are current.");
    return;
  }

  throw new Error("Sneak Out build inputs do not match the supported Steam build.");
}

main().catch((error) => {
  console.error(error instanceof Error ? error.message : String(error));
  process.exitCode = 1;
});
