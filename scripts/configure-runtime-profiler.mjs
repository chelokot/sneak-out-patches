import { mkdir, readFile, writeFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";

const defaultConfigPath = process.env.SNEAKOUT_RUNTIME_PROFILER_CONFIG
  ?? "/run/media/chelokot/second/SteamLibrary/steamapps/common/Sneak Out/BepInEx/config/chelokot.sneakout.runtime-profiler.cfg";

const presets = new Map([
  [
    "off",
    {
      enableMod: false,
      enableLogging: false,
      includeNamespacePrefixes: [],
      targetMethodPatterns: [],
      maxPatchedMethods: 0
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
      maxPatchedMethods: 12
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
      maxPatchedMethods: 12
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

  const configPath = resolve(defaultConfigPath);
  const preset = presets.get(presetName);
  await ensureConfig(configPath);
  let content = await readFile(configPath, "utf8");

  content = replaceOrAppendSetting(content, "EnableMod", preset.enableMod ? "true" : "false");
  content = replaceOrAppendSetting(content, "EnableLogging", preset.enableLogging ? "true" : "false");
  content = replaceOrAppendSetting(content, "IncludeNamespacePrefixes", preset.includeNamespacePrefixes.join(";"));
  content = replaceOrAppendSetting(content, "TargetMethodPatterns", preset.targetMethodPatterns.join(";"));
  content = replaceOrAppendSetting(content, "ExcludeNamespacePrefixes", "");
  content = replaceOrAppendSetting(content, "IncludePropertyAccessors", "false");
  content = replaceOrAppendSetting(content, "IncludeConstructors", "true");
  content = replaceOrAppendSetting(content, "IncludeCompilerGenerated", "false");
  content = replaceOrAppendSetting(content, "MaxPatchedMethods", `${preset.maxPatchedMethods}`);

  await writeFile(configPath, content);
  console.log(configPath);
}

await main();
