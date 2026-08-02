#!/usr/bin/env node

import { createInterface } from "node:readline/promises";
import { stdin, stdout } from "node:process";
import {
  compatibilityIssues,
  install,
  protonLaunchConfigurationRequired,
  uninstall,
  validateInstalled
} from "./lib/installer.mjs";
import { loadPayloadMetadata, resolveBepInEx, resolvePayload } from "./lib/payload.mjs";
import { detectGameDirectories, isSteamClientRunning, resolveGameDirectory } from "./lib/steam.mjs";

function usage() {
  return `Sneak Out patches installer

Usage:
  sneakout-patches install [--interactive] [--game-dir PATH] [--all]
  sneakout-patches uninstall [--interactive] [--game-dir PATH]

Options:
  --interactive              Confirm/change the game path and choose mods.
  --game-dir PATH            Use an explicit Sneak Out installation.
  --all                      Include experimental and debug mods.
  --mods ID,ID               Install exactly the listed mod ids.
  --allow-unsupported-build  Install despite game build/hash mismatch.
  --offline                  Use the payload bundled with the npm package.
  -h, --help                 Show this help.
`;
}

function parseArguments(argumentsList) {
  if (argumentsList[0] === "--help" || argumentsList[0] === "-h") {
    return { command: "help" };
  }
  const result = {
    command: argumentsList[0],
    interactive: false,
    all: false,
    allowUnsupportedBuild: false,
    offline: false,
    gameDirectory: null,
    mods: null
  };
  for (let index = 1; index < argumentsList.length; index += 1) {
    const argument = argumentsList[index];
    if (argument === "--interactive") {
      result.interactive = true;
    } else if (argument === "--all") {
      result.all = true;
    } else if (argument === "--allow-unsupported-build") {
      result.allowUnsupportedBuild = true;
    } else if (argument === "--offline") {
      result.offline = true;
    } else if (argument === "--yes" || argument === "-y") {
      // Noninteractive operation is already the default; accept the familiar alias.
    } else if (argument === "--game-dir") {
      result.gameDirectory = argumentsList[++index];
      if (!result.gameDirectory) {
        throw new Error("--game-dir requires a path");
      }
    } else if (argument === "--mods") {
      const value = argumentsList[++index];
      if (value === undefined) {
        throw new Error("--mods requires a comma-separated list");
      }
      result.mods = value ? value.split(",").map((item) => item.trim()).filter(Boolean) : [];
    } else if (argument === "--help" || argument === "-h") {
      result.command = "help";
    } else {
      throw new Error(`Unknown argument: ${argument}`);
    }
  }
  if (!result.command) {
    result.command = "help";
  }
  if (!["install", "uninstall", "help"].includes(result.command)) {
    throw new Error(`Unknown command: ${result.command}`);
  }
  return result;
}

async function askYesNo(readline, question, defaultValue) {
  const suffix = defaultValue ? "[Y/n]" : "[y/N]";
  while (true) {
    const answer = (await readline.question(`${question} ${suffix} `)).trim().toLowerCase();
    if (!answer) {
      return defaultValue;
    }
    if (["y", "yes"].includes(answer)) {
      return true;
    }
    if (["n", "no"].includes(answer)) {
      return false;
    }
  }
}

async function chooseGameDirectory(options, readline) {
  if (options.gameDirectory) {
    return resolveGameDirectory(options.gameDirectory);
  }
  const detected = await detectGameDirectories();
  if (!options.interactive) {
    if (detected.length === 0) {
      throw new Error("Sneak Out was not found. Pass --game-dir PATH or use --interactive.");
    }
    process.stdout.write(`Detected Sneak Out: ${detected[0]}\n`);
    return detected[0];
  }

  const defaultPath = detected[0] ?? "";
  const answer = await readline.question(
    `Sneak Out directory${defaultPath ? ` [${defaultPath}]` : ""}: `
  );
  const selected = answer.trim() || defaultPath;
  if (!selected) {
    throw new Error("No game directory was selected.");
  }
  return resolveGameDirectory(selected);
}

function validateModIds(manifest, selectedIds) {
  const known = new Set(manifest.map((entry) => entry.option_id));
  const unknown = selectedIds.filter((id) => !known.has(id));
  if (unknown.length) {
    throw new Error(`Unknown mod ids: ${unknown.join(", ")}`);
  }
  return [...new Set(selectedIds)];
}

async function chooseMods(options, manifest, readline) {
  if (options.mods !== null) {
    return validateModIds(manifest, options.mods);
  }
  if (options.all) {
    return manifest.map((entry) => entry.option_id);
  }
  if (!options.interactive) {
    return manifest.filter((entry) => entry.default_enabled).map((entry) => entry.option_id);
  }
  process.stdout.write("\nChoose runtime mods (press Enter to accept each default):\n");
  const selected = [];
  for (const mod of manifest) {
    const enabled = await askYesNo(
      readline,
      `  ${mod.label} [${mod.category}]`,
      Boolean(mod.default_enabled)
    );
    if (enabled) {
      selected.push(mod.option_id);
    }
  }
  return selected;
}

async function main() {
  const options = parseArguments(process.argv.slice(2));
  if (options.command === "help") {
    process.stdout.write(usage());
    return;
  }
  if (options.interactive && (!stdin.isTTY || !stdout.isTTY)) {
    throw new Error("--interactive requires a terminal.");
  }

  const readline = options.interactive ? createInterface({ input: stdin, output: stdout }) : null;
  try {
    const gameDirectory = await chooseGameDirectory(options, readline);
    const payload = await resolvePayload({ offline: options.offline });
    const { manifest, supportedBuild } = await loadPayloadMetadata(payload.root);

    if (options.command === "uninstall") {
      if (options.interactive && !(await askYesNo(readline, `Remove patches from ${gameDirectory}?`, true))) {
        process.stdout.write("Cancelled.\n");
        return;
      }
      await uninstall({ gameDirectory, manifest });
      process.stdout.write(`Sneak Out patches removed from ${gameDirectory}\n`);
      return;
    }

    const selectedIds = await chooseMods(options, manifest, readline);
    if (selectedIds.length === 0) {
      throw new Error("No mods were selected.");
    }
    if (await protonLaunchConfigurationRequired() && await isSteamClientRunning()) {
      throw new Error(
        "Steam is running and the Proton winhttp override is not active. " +
        "Quit Steam completely and run the same install command again. No game files were changed."
      );
    }
    const issues = await compatibilityIssues(gameDirectory, supportedBuild);
    if (issues.length) {
      process.stderr.write(`Unsupported game installation:\n- ${issues.join("\n- ")}\n`);
      const accepted = options.allowUnsupportedBuild || (
        options.interactive && await askYesNo(readline, "Install anyway?", false)
      );
      if (!accepted) {
        throw new Error("Installation stopped before modifying the game. Use --allow-unsupported-build to override.");
      }
    }

    process.stdout.write(`Payload: ${payload.source}\n`);
    process.stdout.write(`Installing ${selectedIds.length} mods into ${gameDirectory}\n`);
    const bepinexRoot = await resolveBepInEx();
    await install({
      gameDirectory,
      payloadRoot: payload.root,
      bepinexRoot,
      manifest,
      selectedIds
    });
    const validationProblems = await validateInstalled(
      gameDirectory,
      manifest,
      selectedIds,
      payload.root
    );
    if (validationProblems.length) {
      throw new Error(`Installation validation failed:\n- ${validationProblems.join("\n- ")}`);
    }
    process.stdout.write("Installed mods:\n");
    for (const mod of manifest.filter((entry) => selectedIds.includes(entry.option_id))) {
      process.stdout.write(`- ${mod.label}\n`);
    }
    process.stdout.write("Installation complete. The first modded launch may take longer while BepInEx generates interop.\n");
  } finally {
    readline?.close();
  }
}

main().catch((error) => {
  process.stderr.write(`${error instanceof Error ? error.message : String(error)}\n`);
  process.exitCode = 1;
});
