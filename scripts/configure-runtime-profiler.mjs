import { mkdir, readFile, writeFile } from "node:fs/promises";
import { dirname, join, resolve } from "node:path";
import { resolveGameDirectory } from "./lib/game-install.mjs";

const explicitConfigPath = process.env.SNEAKOUT_RUNTIME_PROFILER_CONFIG;

const presets = new Map([
  [
    "off",
    {
      enableMod: false,
      enableLogging: false,
      includeNamespacePrefixes: [],
      targetMethodPatterns: [],
      maxPatchedMethods: 0,
      includeConstructors: false
    }
  ],
  [
    "skills-host",
    {
      enableMod: true,
      enableLogging: true,
      includeNamespacePrefixes: [
        "Collections.Skills.",
        "Gameplay.Skills.",
        "Gameplay.Spawn."
      ],
      targetMethodPatterns: [
        "Collections.Skills.PlayersActiveSkills.GetPlayerSkillModifier",
        "Collections.Skills.PlayersActiveSkills.HaveSkillEquipped",
        "Gameplay.Skills.PumpkinBombs.OnConfirmSeekerCharacterEvent",
        "Gameplay.Skills.PlayerBombs..ctor",
        "Gameplay.Spawn.SceneSpawner.OnPlayerLoaded"
      ],
      maxPatchedMethods: 12,
      includeConstructors: false
    }
  ],
  [
    "skins-sync",
    {
      enableMod: true,
      enableLogging: true,
      includeNamespacePrefixes: [
        "Gameplay.Player.Components.",
        "Gameplay.Spawn.",
        "UI.Views."
      ],
      targetMethodPatterns: [
        "Gameplay.Player.Components.SpookedNetworkPlayer.ChangeCharacterData",
        "Gameplay.Player.Components.SpookedNetworkPlayer.RPC_ClientRequestCharacterDataChange",
        "Gameplay.Spawn.SceneSpawner.OnPlayerLoaded",
        "UI.Views.PlayerCustomizationView.OnTryOnCharacterOutfitLocally"
      ],
      maxPatchedMethods: 12,
      includeConstructors: false
    }
  ],
  [
    "lobby-hotspots",
    {
      enableMod: true,
      enableLogging: true,
      includeNamespacePrefixes: [
        "UI.Views.",
        "UI.Buttons.",
        "Networking.Party.",
        "Collections.",
        "Base.",
        "Gameplay.Player."
      ],
      targetMethodPatterns: [],
      maxPatchedMethods: 120,
      includeConstructors: false
    }
  ],
  [
    "lobby-ui-safe",
    {
      enableMod: true,
      enableLogging: true,
      includeNamespacePrefixes: [
        "UI.Views."
      ],
      targetMethodPatterns: [],
      maxPatchedMethods: 40,
      includeConstructors: false
    }
  ]
]);

function replaceOrAppendSetting(content, key, value) {
  const line = `${key} = ${value}`;
  const pattern = new RegExp(`^${key}\\s*=.*$`, "m");
  if (pattern.test(content)) {
    return content.replace(pattern, line);
  }

  return `${content.trimEnd()}\n${line}\n`;
}

async function ensureConfig(path) {
  try {
    await readFile(path, "utf8");
  } catch {
    await mkdir(dirname(path), { recursive: true });
    await writeFile(path, "");
  }
}

async function main() {
  const presetName = process.argv[2];
  if (!presetName || !presets.has(presetName)) {
    throw new Error(`Usage: node scripts/configure-runtime-profiler.mjs <${Array.from(presets.keys()).join("|")}>`);
  }

  const configPath = explicitConfigPath
    ? resolve(explicitConfigPath)
    : join(await resolveGameDirectory(), "BepInEx", "config", "chelokot.sneakout.runtime-profiler.cfg");
  const preset = presets.get(presetName);
  await ensureConfig(configPath);
  let content = await readFile(configPath, "utf8");

  content = replaceOrAppendSetting(content, "EnableMod", preset.enableMod ? "true" : "false");
  content = replaceOrAppendSetting(content, "EnableLogging", preset.enableLogging ? "true" : "false");
  content = replaceOrAppendSetting(content, "IncludeNamespacePrefixes", preset.includeNamespacePrefixes.join(";"));
  content = replaceOrAppendSetting(content, "TargetMethodPatterns", preset.targetMethodPatterns.join(";"));
  content = replaceOrAppendSetting(content, "ExcludeNamespacePrefixes", "");
  content = replaceOrAppendSetting(content, "IncludePropertyAccessors", "false");
  content = replaceOrAppendSetting(content, "IncludeConstructors", preset.includeConstructors ? "true" : "false");
  content = replaceOrAppendSetting(content, "IncludeCompilerGenerated", "false");
  content = replaceOrAppendSetting(content, "MaxPatchedMethods", `${preset.maxPatchedMethods}`);

  await writeFile(configPath, content);
  console.log(configPath);
}

await main();
