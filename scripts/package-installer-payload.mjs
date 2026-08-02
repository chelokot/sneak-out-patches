import { createHash } from "node:crypto";
import { mkdir, readFile, readdir, rm, writeFile } from "node:fs/promises";
import { basename, dirname, join, relative, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { zipSync } from "fflate";

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const repositoryRoot = resolve(scriptDirectory, "..");
const outputDirectory = join(repositoryRoot, "dist");
const outputPath = join(outputDirectory, "sneakout-patches-payload.zip");

async function listFiles(root) {
  const results = [];
  async function visit(directory) {
    for (const entry of await readdir(directory, { withFileTypes: true })) {
      const path = join(directory, entry.name);
      if (entry.isDirectory()) {
        await visit(path);
      } else if (entry.isFile()) {
        results.push(path);
      }
    }
  }
  await visit(root);
  return results;
}

async function addTree(entries, root, archiveRoot) {
  const files = await listFiles(root);
  for (const path of files.sort()) {
    const archivePath = join(archiveRoot, relative(root, path)).replace(/\\/g, "/");
    entries[archivePath] = new Uint8Array(await readFile(path));
  }
}

async function main() {
  const manifest = JSON.parse(await readFile(join(repositoryRoot, "runtime_mods_manifest.json"), "utf8"));
  for (const mod of manifest) {
    const artifact = join(repositoryRoot, "artifacts", "runtime_mods", `${mod.assembly_name}.dll`);
    await readFile(artifact).catch(() => {
      throw new Error(`Missing runtime artifact: ${artifact}`);
    });
  }

  await rm(outputDirectory, { recursive: true, force: true });
  await mkdir(outputDirectory, { recursive: true });
  const entries = {
    "runtime_mods_manifest.json": new Uint8Array(await readFile(join(repositoryRoot, "runtime_mods_manifest.json"))),
    "supported_game_build.json": new Uint8Array(await readFile(join(repositoryRoot, "supported_game_build.json")))
  };
  await addTree(
    entries,
    join(repositoryRoot, "artifacts", "runtime_mods"),
    "artifacts/runtime_mods"
  );
  await addTree(
    entries,
    join(repositoryRoot, "config_templates", "runtime_mods"),
    "config_templates/runtime_mods"
  );
  await writeFile(outputPath, zipSync(entries, { level: 9 }));

  const bytes = await readFile(outputPath);
  const sha256 = createHash("sha256").update(bytes).digest("hex");
  await writeFile(`${outputPath}.sha256`, `${sha256}  ${basename(outputPath)}\n`);
  process.stdout.write(`${outputPath}\n${sha256}\n`);
}

main().catch((error) => {
  process.stderr.write(`${error instanceof Error ? error.message : String(error)}\n`);
  process.exitCode = 1;
});
