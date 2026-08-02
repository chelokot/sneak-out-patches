import { createHash } from "node:crypto";
import { createReadStream } from "node:fs";
import { access, copyFile, mkdir, readdir, readFile, rm, stat, writeFile } from "node:fs/promises";
import { constants } from "node:fs";
import { dirname, join, relative, resolve, sep } from "node:path";

export async function exists(path) {
  try {
    await access(path, constants.F_OK);
    return true;
  } catch {
    return false;
  }
}

export async function sha256File(path) {
  const digest = createHash("sha256");
  for await (const chunk of createReadStream(path)) {
    digest.update(chunk);
  }
  return digest.digest("hex");
}

export async function writeFileAtomic(path, bytes) {
  await mkdir(dirname(path), { recursive: true });
  const temporary = `${path}.sneakout-patches.tmp-${process.pid}`;
  await writeFile(temporary, bytes);
  const { rename } = await import("node:fs/promises");
  try {
    await rename(temporary, path);
  } catch (error) {
    if (!(["EEXIST", "EPERM", "ENOTEMPTY"].includes(error.code))) {
      throw error;
    }
    await rm(path, { force: true });
    await rename(temporary, path);
  }
}

export async function copyFileAtomic(source, destination) {
  await mkdir(dirname(destination), { recursive: true });
  const temporary = `${destination}.sneakout-patches.tmp-${process.pid}`;
  await copyFile(source, temporary);
  const { rename } = await import("node:fs/promises");
  try {
    await rename(temporary, destination);
  } catch (error) {
    if (!(["EEXIST", "EPERM", "ENOTEMPTY"].includes(error.code))) {
      throw error;
    }
    await rm(destination, { force: true });
    await rename(temporary, destination);
  }
}

export async function listFiles(root) {
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
  if (await exists(root)) {
    await visit(root);
  }
  return results;
}

export function safeRelativePath(root, path) {
  const value = relative(resolve(root), resolve(path));
  if (!value || value === ".." || value.startsWith(`..${sep}`)) {
    throw new Error(`Path escapes root: ${path}`);
  }
  return value;
}

export async function removeEmptyParents(path, stopAt) {
  let current = dirname(path);
  const stop = resolve(stopAt);
  while (resolve(current) !== stop && resolve(current).startsWith(`${stop}${sep}`)) {
    try {
      if ((await readdir(current)).length !== 0) {
        return;
      }
      await rm(current, { recursive: false });
      current = dirname(current);
    } catch {
      return;
    }
  }
}

export async function fileSize(path) {
  return (await stat(path)).size;
}
