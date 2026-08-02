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
    "lobby-ui-frame",
    {
      enableMod: true,
      enableLogging: true,
      includeNamespacePrefixes: ["UI.Views.Lobby."],
      targetMethodPatterns: [".Update(", ".LateUpdate(", ".FixedUpdate(", ".Tick("],
      maxPatchedMethods: 24,
      includeConstructors: false
    }
  ],
  [
    "cloth-frame",
    {
      enableMod: true,
      enableLogging: true,
      targetAssemblies: ["MagicaClothV2"],
      includeNamespacePrefixes: [
        "MagicaCloth2.ClothManager",
        "MagicaCloth2.SimulationManager",
        "MagicaCloth2.VirtualMeshManager",
        "MagicaCloth2.ClothProcess"
      ],
      targetMethodPatterns: [
        ".ClothUpdate(",
        ".OnBeforeLateUpdate(",
        ".OnAfterLateUpdate(",
        ".OnEarlyClothUpdate(",
        ".PreSimulationUpdate(",
        ".SimulationStepUpdate(",
        ".WorkBufferUpdate(",
        ".PreProxyMeshUpdate(",
        ".PostProxyMeshUpdate(",
        ".PostMappingMeshUpdate("
      ],
      maxPatchedMethods: 24,
      includeConstructors: false
    }
  ],
  [
    "fusion-frame",
    {
      enableMod: true,
      enableLogging: true,
      targetAssemblies: ["Fusion.Unity", "Fusion.Runtime", "Fusion.Realtime"],
      includeNamespacePrefixes: [
        "Fusion.NetworkRunnerUpdaterDefault",
        "Fusion.SimulationBehaviourUpdater",
        "Fusion.CloudServices",
        "Fusion.Photon.Realtime.FusionRelayClient"
      ],
      targetMethodPatterns: [
        ".InvokeUpdate(",
        ".InvokeRender(",
        ".InvokeFixedUpdateNetwork(",
        ".Update(",
        ".Service("
      ],
      maxPatchedMethods: 24,
      includeConstructors: false,
      warmupSeconds: 45
    }
  ],
  [
    "network-frame",
    {
      enableMod: true,
      enableLogging: true,
      includeNamespacePrefixes: ["Networking."],
      targetMethodPatterns: [".Update(", ".LateUpdate(", ".FixedUpdate(", ".Tick("],
      maxPatchedMethods: 32,
      includeConstructors: false
    }
  ],
  [
    "player-frame",
    {
      enableMod: true,
      enableLogging: true,
      includeNamespacePrefixes: ["Gameplay.Player."],
      targetMethodPatterns: [".Update(", ".LateUpdate(", ".FixedUpdate(", ".Tick("],
      maxPatchedMethods: 32,
      includeConstructors: false
    }
  ],
  [
    "base-frame",
    {
      enableMod: true,
      enableLogging: true,
      includeNamespacePrefixes: ["Base."],
      targetMethodPatterns: [".Update(", ".LateUpdate(", ".FixedUpdate(", ".Tick("],
      maxPatchedMethods: 32,
      includeConstructors: false
    }
  ],
  [
    "frame-loops",
    {
      enableMod: true,
      enableLogging: true,
      includeNamespacePrefixes: [
        "UI.Views.",
        "UI.Buttons.",
        "Networking.",
        "Base.",
        "Gameplay.Player."
      ],
      targetMethodPatterns: [
        ".Update(",
        ".LateUpdate(",
        ".FixedUpdate(",
        ".Tick("
      ],
      maxPatchedMethods: 48,
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

function replaceOrAppendSetting(content, section, key, value) {
  const line = `${key} = ${value}`;
  const sectionPattern = new RegExp(`^\\[${section.replace(/[.*+?^${}()|[\\]\\]/g, "\\$&")}\\]\\s*$`, "m");
  const sectionMatch = sectionPattern.exec(content);
  if (!sectionMatch) {
    return `${content.trimEnd()}\n\n[${section}]\n\n${line}\n`;
  }

  const sectionStart = sectionMatch.index + sectionMatch[0].length;
  const nextSectionMatch = /^\[[^\]]+\]\s*$/m.exec(content.slice(sectionStart));
  const sectionEnd = nextSectionMatch ? sectionStart + nextSectionMatch.index : content.length;
  const sectionContent = content.slice(sectionStart, sectionEnd);
  const settingPattern = new RegExp(`^${key}\\s*=.*$`, "m");
  const updatedSection = settingPattern.test(sectionContent)
    ? sectionContent.replace(settingPattern, line)
    : `${sectionContent.trimEnd()}\n${line}\n\n`;
  return content.slice(0, sectionStart) + updatedSection + content.slice(sectionEnd);
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
  content = content.replace(/\r?\n\[\]\r?\n[\s\S]*?(?=\r?\n\[general\]\r?\n)/, "\n");

  content = replaceOrAppendSetting(content, "general", "EnableMod", preset.enableMod ? "true" : "false");
  content = replaceOrAppendSetting(content, "general", "EnableLogging", preset.enableLogging ? "true" : "false");
  content = replaceOrAppendSetting(
    content,
    "targeting",
    "TargetAssemblies",
    (preset.targetAssemblies ?? ["Assembly-CSharp", "Kinguinverse"]).join(";")
  );
  content = replaceOrAppendSetting(content, "targeting", "IncludeNamespacePrefixes", preset.includeNamespacePrefixes.join(";"));
  content = replaceOrAppendSetting(content, "targeting", "TargetMethodPatterns", preset.targetMethodPatterns.join(";"));
  content = replaceOrAppendSetting(content, "targeting", "ExcludeNamespacePrefixes", "");
  content = replaceOrAppendSetting(content, "targeting", "IncludePropertyAccessors", "false");
  content = replaceOrAppendSetting(content, "targeting", "IncludeConstructors", preset.includeConstructors ? "true" : "false");
  content = replaceOrAppendSetting(content, "targeting", "IncludeCompilerGenerated", "false");
  content = replaceOrAppendSetting(content, "targeting", "MaxPatchedMethods", `${preset.maxPatchedMethods}`);
  content = replaceOrAppendSetting(content, "report", "WarmupSeconds", `${preset.warmupSeconds ?? 0}`);

  await writeFile(configPath, content);
  console.log(configPath);
}

await main();
