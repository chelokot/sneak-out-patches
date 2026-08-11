#!/usr/bin/env python3
"""Build the native installer's GitHub Release payload without external packages."""

from __future__ import annotations

import hashlib
import json
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile


REPOSITORY_ROOT = Path(__file__).resolve().parent.parent
OUTPUT_DIRECTORY = REPOSITORY_ROOT / "dist"
OUTPUT_PATH = OUTPUT_DIRECTORY / "sneakout-patches-payload.zip"


def add_tree(archive: ZipFile, root: Path, archive_root: str) -> None:
    for path in sorted(candidate for candidate in root.rglob("*") if candidate.is_file()):
        relative = path.relative_to(root).as_posix()
        archive.write(path, f"{archive_root}/{relative}")


def main() -> None:
    manifest_path = REPOSITORY_ROOT / "runtime_mods_manifest.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    artifact_root = REPOSITORY_ROOT / "artifacts" / "runtime_mods"
    for runtime_mod in manifest:
        artifact = artifact_root / f"{runtime_mod['assembly_name']}.dll"
        if not artifact.is_file():
            raise FileNotFoundError(f"Missing runtime artifact: {artifact}")

    OUTPUT_DIRECTORY.mkdir(parents=True, exist_ok=True)
    OUTPUT_PATH.unlink(missing_ok=True)
    OUTPUT_PATH.with_suffix(".zip.sha256").unlink(missing_ok=True)
    with ZipFile(OUTPUT_PATH, "w", ZIP_DEFLATED, compresslevel=9) as archive:
        archive.write(manifest_path, "runtime_mods_manifest.json")
        archive.write(
            REPOSITORY_ROOT / "supported_game_build.json",
            "supported_game_build.json",
        )
        add_tree(archive, artifact_root, "artifacts/runtime_mods")
        add_tree(
            archive,
            REPOSITORY_ROOT / "config_templates" / "runtime_mods",
            "config_templates/runtime_mods",
        )

    digest = hashlib.sha256(OUTPUT_PATH.read_bytes()).hexdigest()
    OUTPUT_PATH.with_suffix(".zip.sha256").write_text(
        f"{digest}  {OUTPUT_PATH.name}\n", encoding="utf-8"
    )
    print(OUTPUT_PATH)
    print(digest)


if __name__ == "__main__":
    main()
