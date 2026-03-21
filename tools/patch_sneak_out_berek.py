#!/usr/bin/env python3

from __future__ import annotations

import hashlib
import sys
from argparse import ArgumentParser
from dataclasses import dataclass
from pathlib import Path


@dataclass(frozen=True)
class BinaryPatch:
    offset: int
    before_hex: str
    after_hex: str

    @property
    def before(self) -> bytes:
        return bytes.fromhex(self.before_hex)

    @property
    def after(self) -> bytes:
        return bytes.fromhex(self.after_hex)


@dataclass(frozen=True)
class FilePatch:
    relative_path: str
    clean_sha256: str
    patched_sha256: str
    backup_suffix: str
    patches: tuple[BinaryPatch, ...]


PATCHES: tuple[FilePatch, ...] = (
    FilePatch(
        relative_path="GameAssembly.dll",
        clean_sha256="4c6c11f0d477cbb1b370bdf2b85ab4b267b5f5883bf96f005989e013ab15719e",
        patched_sha256="c41c7e78852eff74a4001ce7aa56a2235586493e0647ee3c6a4e2a6eec3fdb1a",
        backup_suffix=".codex-berek.bak",
        patches=(
            # Force PrepareVictims into the Berek start coroutine instead of Default.
            BinaryPatch(0x67FA02, "747f", "eb19"),
            # Force BeforeSelectionState into the Berek branch.
            BinaryPatch(0x6971D7, "75", "eb"),
            # Switch SelectionState pointer from default selection to BerekSelectionState.
            BinaryPatch(0x6972C3, "e000", "0001"),
            # Replace default game mode with Berek in portal play flow.
            BinaryPatch(0x7E15B9, "01", "02"),
            # Replace default game mode with Berek in portal play flow.
            BinaryPatch(0x7E15E2, "01", "02"),
            # Write Berek into host session property creation path.
            BinaryPatch(0x803726, "8b4318", "6a0258"),
            # Write Berek into host session property creation path.
            BinaryPatch(0x80373B, "8b5318", "6a025a"),
            # Force HostChosenGameMode getter to return Berek.
            BinaryPatch(0x803FBD, "8b004883c428c3", "b8020000009090"),
            # Force host-chosen mode event path to use Berek.
            BinaryPatch(0x823201, "e86ab54a", "b8020000"),
            # Force host map selection path to treat the room as Berek.
            BinaryPatch(0x823310, "e8db0de9ff", "b802000000"),
            # Force host map selection path to treat the room as Berek.
            BinaryPatch(0x8233EE, "e8fd0ce9ff", "b802000000"),
        ),
    ),
    FilePatch(
        relative_path="Sneak Out_Data/resources.assets",
        clean_sha256="50335a6501314dafcff9ae0711b5a1949a81c04d2bd3f61201a64c1fe3ac8adc",
        patched_sha256="7ce6e53dbf4fd2bd3d16c2852c49508cfae54bdf7fc481e8d97bb7e7e81791fd",
        backup_suffix=".codex-berek.bak",
        patches=(
            # Pre-wire SpookedNetworkPlayer.EntityBerekComponent to the prefab's EntityBerekComponent.
            BinaryPatch(0x4990E2C, "0000", "831e"),
        ),
    ),
)


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def apply_file_patch(game_dir: Path, file_patch: FilePatch) -> None:
    path = game_dir / file_patch.relative_path
    if not path.is_file():
        raise SystemExit(f"Missing file: {path}")

    data = bytearray(path.read_bytes())
    current_sha256 = sha256(data)

    if current_sha256 == file_patch.patched_sha256:
        print(f"already patched: {path}")
        return

    if current_sha256 != file_patch.clean_sha256:
        raise SystemExit(
            f"Unexpected hash for {path}\n"
            f"expected clean:   {file_patch.clean_sha256}\n"
            f"expected patched: {file_patch.patched_sha256}\n"
            f"actual:           {current_sha256}"
        )

    for patch in file_patch.patches:
        current = bytes(data[patch.offset : patch.offset + len(patch.before)])
        if current != patch.before:
            raise SystemExit(
                f"Unexpected bytes in {path} at 0x{patch.offset:x}\n"
                f"expected: {patch.before.hex()}\n"
                f"actual:   {current.hex()}"
            )

    backup_path = path.with_name(path.name + file_patch.backup_suffix)
    if not backup_path.exists():
        backup_path.write_bytes(bytes(data))

    for patch in file_patch.patches:
        end = patch.offset + len(patch.after)
        data[patch.offset:end] = patch.after

    patched_sha256 = sha256(data)
    if patched_sha256 != file_patch.patched_sha256:
        raise SystemExit(
            f"Patched hash mismatch for {path}\n"
            f"expected: {file_patch.patched_sha256}\n"
            f"actual:   {patched_sha256}"
        )

    path.write_bytes(data)
    print(f"patched: {path}")
    print(f"backup:  {backup_path}")


def rollback_file_patch(game_dir: Path, file_patch: FilePatch) -> None:
    path = game_dir / file_patch.relative_path
    if not path.is_file():
        raise SystemExit(f"Missing file: {path}")

    backup_path = path.with_name(path.name + file_patch.backup_suffix)
    if not backup_path.is_file():
        raise SystemExit(f"Missing backup: {backup_path}")

    backup_data = backup_path.read_bytes()
    backup_sha256 = sha256(backup_data)
    if backup_sha256 != file_patch.clean_sha256:
        raise SystemExit(
            f"Unexpected backup hash for {backup_path}\n"
            f"expected: {file_patch.clean_sha256}\n"
            f"actual:   {backup_sha256}"
        )

    current_data = path.read_bytes()
    current_sha256 = sha256(current_data)
    if current_sha256 == file_patch.clean_sha256:
        print(f"already clean: {path}")
        return

    if current_sha256 != file_patch.patched_sha256:
        raise SystemExit(
            f"Unexpected current hash for {path}\n"
            f"expected clean:   {file_patch.clean_sha256}\n"
            f"expected patched: {file_patch.patched_sha256}\n"
            f"actual:           {current_sha256}"
        )

    path.write_bytes(backup_data)
    print(f"restored: {path}")
    print(f"from:     {backup_path}")


def main() -> int:
    parser = ArgumentParser()
    parser.add_argument("game_dir")
    parser.add_argument("--rollback", action="store_true")
    if len(sys.argv) == 1:
        parser.print_help()
        return 1
    args = parser.parse_args()

    game_dir = Path(args.game_dir).expanduser().resolve()
    if not game_dir.is_dir():
        print(f"Not a directory: {game_dir}")
        return 1

    for file_patch in PATCHES:
        if args.rollback:
            rollback_file_patch(game_dir, file_patch)
        else:
            apply_file_patch(game_dir, file_patch)

    print("done")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
