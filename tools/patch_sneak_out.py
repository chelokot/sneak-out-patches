#!/usr/bin/env python3

from __future__ import annotations

import hashlib
import os
import platform
import re
import sys
import termios
import tty
from argparse import ArgumentParser
from dataclasses import dataclass
from pathlib import Path


GREEN = "\033[32m"
RESET = "\033[0m"
CLEAR_SCREEN = "\033[2J\033[H"
STEAM_APP_ID = "2410490"
GAME_DIRECTORY_NAME = "Sneak Out"
BACKUP_SUFFIX = ".codex-sneak-out.bak"


@dataclass(frozen=True)
class BinaryPatch:
    offset: int
    before_hex: str
    after_hex: str
    description: str

    @property
    def before(self) -> bytes:
        return bytes.fromhex(self.before_hex)

    @property
    def after(self) -> bytes:
        return bytes.fromhex(self.after_hex)


@dataclass(frozen=True)
class ManagedFile:
    relative_path: str
    clean_sha256: str


@dataclass(frozen=True)
class FilePatchGroup:
    relative_path: str
    patches: tuple[BinaryPatch, ...]


@dataclass(frozen=True)
class PatchOption:
    option_id: str
    label: str
    default_enabled: bool
    file_patch_groups: tuple[FilePatchGroup, ...]


@dataclass
class PreparedFile:
    spec: ManagedFile
    path: Path
    backup_path: Path
    current_bytes: bytes
    working_bytes: bytearray
    backup_created: bool
    restored_from_backup: bool


MANAGED_FILES: tuple[ManagedFile, ...] = (
    ManagedFile(
        relative_path="GameAssembly.dll",
        clean_sha256="4c6c11f0d477cbb1b370bdf2b85ab4b267b5f5883bf96f005989e013ab15719e",
    ),
    ManagedFile(
        relative_path="Sneak Out_Data/resources.assets",
        clean_sha256="50335a6501314dafcff9ae0711b5a1949a81c04d2bd3f61201a64c1fe3ac8adc",
    ),
)


PATCH_OPTIONS: tuple[PatchOption, ...] = (
    PatchOption(
        option_id="get-the-crown",
        label="Switch mode to Get the Crown",
        default_enabled=True,
        file_patch_groups=(
            FilePatchGroup(
                relative_path="GameAssembly.dll",
                patches=(
                    BinaryPatch(0x67FA02, "747f", "eb19", "Force PrepareVictims into the Berek start coroutine instead of Default."),
                    BinaryPatch(0x6971D7, "75", "eb", "Force BeforeSelectionState into the Berek branch."),
                    BinaryPatch(0x6972C3, "e000", "0001", "Switch SelectionState pointer from default selection to BerekSelectionState."),
                    BinaryPatch(0x7E15B9, "01", "02", "Replace the default game mode with Berek in portal play flow."),
                    BinaryPatch(0x7E15E2, "01", "02", "Replace the default game mode with Berek in portal play flow."),
                    BinaryPatch(0x803726, "8b4318", "6a0258", "Write Berek into the host session property creation path."),
                    BinaryPatch(0x80373B, "8b5318", "6a025a", "Write Berek into the host session property creation path."),
                    BinaryPatch(0x803FBD, "8b004883c428c3", "b8020000009090", "Force HostChosenGameMode getter to return Berek."),
                    BinaryPatch(0x823201, "e86ab54a", "b8020000", "Force the host-chosen mode event path to use Berek."),
                    BinaryPatch(0x823310, "e8db0de9ff", "b802000000", "Force the host map selection path to treat the room as Berek."),
                    BinaryPatch(0x8233EE, "e8fd0ce9ff", "b802000000", "Force the host map selection path to treat the room as Berek."),
                ),
            ),
            FilePatchGroup(
                relative_path="Sneak Out_Data/resources.assets",
                patches=(
                    BinaryPatch(0x4990E2C, "0000", "831e", "Pre-wire SpookedNetworkPlayer.EntityBerekComponent to the prefab EntityBerekComponent."),
                ),
            ),
        ),
    ),
    PatchOption(
        option_id="uniform-hunter-random",
        label="Make hunter random selection uniform",
        default_enabled=True,
        file_patch_groups=(
            FilePatchGroup(
                relative_path="GameAssembly.dll",
                patches=(
                    BinaryPatch(
                        0x357E7C0,
                        "cdcccc3d",
                        "0000803f",
                        "Expand the first default-mode seeker fairness threshold from 0.1 to 1.0.",
                    ),
                ),
            ),
        ),
    ),
)


PATCH_OPTION_BY_ID = {option.option_id: option for option in PATCH_OPTIONS}


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def normalize_vdf_path(raw_path: str) -> Path:
    return Path(raw_path.replace("\\\\", "\\")).expanduser()


def parse_libraryfolders(vdf_path: Path) -> list[Path]:
    if not vdf_path.is_file():
        return []
    content = vdf_path.read_text(encoding="utf-8", errors="ignore")
    paths = [normalize_vdf_path(match) for match in re.findall(r'"path"\s+"([^"]+)"', content)]
    return [path for path in paths if path.exists()]


def candidate_steam_roots() -> list[Path]:
    system_name = platform.system().lower()
    home = Path.home()
    candidates: list[Path] = []
    if system_name == "linux":
        candidates.extend(
            [
                home / ".local/share/Steam",
                home / ".steam/steam",
                home / ".var/app/com.valvesoftware.Steam/.local/share/Steam",
            ]
        )
    elif system_name == "darwin":
        candidates.append(home / "Library/Application Support/Steam")
    elif system_name == "windows":
        for variable_name in ("PROGRAMFILES(X86)", "PROGRAMFILES", "LOCALAPPDATA"):
            value = os.environ.get(variable_name)
            if value:
                candidates.append(Path(value) / "Steam")
    return [path for path in candidates if path.exists()]


def candidate_library_roots() -> list[Path]:
    roots: list[Path] = []
    for steam_root in candidate_steam_roots():
        roots.append(steam_root)
        roots.extend(parse_libraryfolders(steam_root / "steamapps/libraryfolders.vdf"))
    for base_path in (Path("/run/media"), Path("/media"), Path("/mnt")):
        if not base_path.exists():
            continue
        roots.extend(sorted(base_path.glob("*/SteamLibrary")))
        roots.extend(sorted(base_path.glob("*/*/SteamLibrary")))
    unique_roots: list[Path] = []
    seen: set[Path] = set()
    for root in roots:
        resolved_root = root.expanduser()
        if resolved_root not in seen:
            seen.add(resolved_root)
            unique_roots.append(resolved_root)
    return unique_roots


def is_valid_game_directory(path: Path) -> bool:
    required_files = (
        path / "GameAssembly.dll",
        path / "Sneak Out.exe",
        path / "Sneak Out_Data/resources.assets",
    )
    return path.is_dir() and all(required_file.is_file() for required_file in required_files)


def detect_game_directory() -> Path | None:
    candidates: list[Path] = []
    for library_root in candidate_library_roots():
        game_path = library_root / "steamapps/common" / GAME_DIRECTORY_NAME
        if is_valid_game_directory(game_path):
            candidates.append(game_path)
    unique_candidates: list[Path] = []
    seen: set[Path] = set()
    for candidate in candidates:
        resolved_candidate = candidate.resolve()
        if resolved_candidate not in seen:
            seen.add(resolved_candidate)
            unique_candidates.append(resolved_candidate)
    return unique_candidates[0] if unique_candidates else None


def resolve_game_directory(raw_path: str) -> Path:
    path = Path(raw_path).expanduser().resolve()
    if not is_valid_game_directory(path):
        raise SystemExit(
            f"Invalid Sneak Out directory: {path}\n"
            "Expected to find GameAssembly.dll, Sneak Out.exe, and Sneak Out_Data/resources.assets."
        )
    return path


def prompt_confirm(message: str, default_yes: bool = True) -> bool:
    prompt = "[Y/n]" if default_yes else "[y/N]"
    while True:
        answer = input(f"{message} {prompt} ").strip().lower()
        if not answer:
            return default_yes
        if answer in {"y", "yes"}:
            return True
        if answer in {"n", "no"}:
            return False


def prompt_for_game_directory(detected_path: Path | None) -> Path:
    if detected_path is not None:
        print(f"Detected Steam app {STEAM_APP_ID} at:\n{detected_path}")
        if prompt_confirm("Use this directory?", default_yes=True):
            return detected_path
    while True:
        raw_path = input("Enter the full path to the Sneak Out directory: ").strip()
        if not raw_path:
            continue
        try:
            return resolve_game_directory(raw_path)
        except SystemExit as error:
            print(error)


def read_key() -> str:
    file_descriptor = sys.stdin.fileno()
    old_settings = termios.tcgetattr(file_descriptor)
    try:
        tty.setraw(file_descriptor)
        first = sys.stdin.read(1)
        if first == "\x1b":
            second = sys.stdin.read(1)
            third = sys.stdin.read(1)
            sequence = first + second + third
            if sequence == "\x1b[A":
                return "up"
            if sequence == "\x1b[B":
                return "down"
            return "escape"
        if first == " ":
            return "space"
        if first in {"\r", "\n"}:
            return "enter"
        if first.lower() == "q":
            return "quit"
        return "other"
    finally:
        termios.tcsetattr(file_descriptor, termios.TCSADRAIN, old_settings)


def choose_patch_options_interactively() -> tuple[str, ...]:
    if not sys.stdin.isatty() or not sys.stdout.isatty():
        raise SystemExit("Interactive patch selection requires a TTY. Use --patches to select options non-interactively.")
    selected = [option.default_enabled for option in PATCH_OPTIONS]
    cursor = 0
    while True:
        lines = [
            CLEAR_SCREEN,
            f"{GREEN}Use ↑/↓ to move, Space to toggle, Enter to apply.{RESET}",
            "",
            "Patch options:",
        ]
        for index, option in enumerate(PATCH_OPTIONS):
            marker = ">" if index == cursor else " "
            checkbox = "x" if selected[index] else " "
            lines.append(f"{marker} [{checkbox}] {option.label}")
        lines.append("")
        lines.append("Press q to cancel.")
        sys.stdout.write("\n".join(lines))
        sys.stdout.flush()
        key = read_key()
        if key == "up":
            cursor = (cursor - 1) % len(PATCH_OPTIONS)
        elif key == "down":
            cursor = (cursor + 1) % len(PATCH_OPTIONS)
        elif key == "space":
            selected[cursor] = not selected[cursor]
        elif key == "enter":
            sys.stdout.write(CLEAR_SCREEN)
            sys.stdout.flush()
            return tuple(option.option_id for option, is_selected in zip(PATCH_OPTIONS, selected) if is_selected)
        elif key == "quit":
            raise SystemExit(1)


def parse_patch_selection(raw_selection: str) -> tuple[str, ...]:
    if not raw_selection.strip():
        return ()
    option_ids = tuple(part.strip() for part in raw_selection.split(",") if part.strip())
    unknown_option_ids = [option_id for option_id in option_ids if option_id not in PATCH_OPTION_BY_ID]
    if unknown_option_ids:
        available_option_ids = ", ".join(sorted(PATCH_OPTION_BY_ID))
        raise SystemExit(
            f"Unknown patch option(s): {', '.join(unknown_option_ids)}\n"
            f"Available patch ids: {available_option_ids}"
        )
    return option_ids


def prepare_file(game_dir: Path, managed_file: ManagedFile) -> PreparedFile:
    path = game_dir / managed_file.relative_path
    if not path.is_file():
        raise SystemExit(f"Missing file: {path}")
    backup_path = path.with_name(path.name + BACKUP_SUFFIX)
    current_bytes = path.read_bytes()
    backup_created = False
    restored_from_backup = False
    if backup_path.is_file():
        baseline_bytes = backup_path.read_bytes()
        restored_from_backup = sha256_bytes(current_bytes) != managed_file.clean_sha256
    else:
        if sha256_bytes(current_bytes) != managed_file.clean_sha256:
            raise SystemExit(
                f"Cannot create a trusted backup for {path}\n"
                f"expected clean hash: {managed_file.clean_sha256}\n"
                f"actual hash:         {sha256_bytes(current_bytes)}"
            )
        baseline_bytes = current_bytes
        backup_path.write_bytes(current_bytes)
        backup_created = True
    if sha256_bytes(baseline_bytes) != managed_file.clean_sha256:
        raise SystemExit(
            f"Unexpected backup hash for {backup_path}\n"
            f"expected clean hash: {managed_file.clean_sha256}\n"
            f"actual hash:         {sha256_bytes(baseline_bytes)}"
        )
    return PreparedFile(
        spec=managed_file,
        path=path,
        backup_path=backup_path,
        current_bytes=current_bytes,
        working_bytes=bytearray(baseline_bytes),
        backup_created=backup_created,
        restored_from_backup=restored_from_backup,
    )


def prepare_files(game_dir: Path) -> dict[str, PreparedFile]:
    return {
        managed_file.relative_path: prepare_file(game_dir, managed_file)
        for managed_file in MANAGED_FILES
    }


def apply_selected_patches(prepared_files: dict[str, PreparedFile], selected_option_ids: tuple[str, ...]) -> None:
    for option_id in selected_option_ids:
        option = PATCH_OPTION_BY_ID[option_id]
        for file_patch_group in option.file_patch_groups:
            prepared_file = prepared_files[file_patch_group.relative_path]
            for patch in file_patch_group.patches:
                current_slice = bytes(
                    prepared_file.working_bytes[patch.offset : patch.offset + len(patch.before)]
                )
                if current_slice != patch.before:
                    raise SystemExit(
                        f"Unexpected bytes in {prepared_file.path} at 0x{patch.offset:x}\n"
                        f"patch:    {patch.description}\n"
                        f"expected: {patch.before.hex()}\n"
                        f"actual:   {current_slice.hex()}"
                    )
                patch_end = patch.offset + len(patch.after)
                prepared_file.working_bytes[patch.offset:patch_end] = patch.after


def write_prepared_files(prepared_files: dict[str, PreparedFile]) -> None:
    for prepared_file in prepared_files.values():
        final_bytes = bytes(prepared_file.working_bytes)
        if final_bytes == prepared_file.current_bytes:
            print(f"unchanged: {prepared_file.path}")
        else:
            prepared_file.path.write_bytes(final_bytes)
            print(f"updated:   {prepared_file.path}")
        if prepared_file.backup_created:
            print(f"backup:    {prepared_file.backup_path}")
        elif prepared_file.restored_from_backup:
            print(f"restored:  {prepared_file.backup_path}")


def rollback(game_dir: Path) -> None:
    for managed_file in MANAGED_FILES:
        path = game_dir / managed_file.relative_path
        if not path.is_file():
            raise SystemExit(f"Missing file: {path}")
        backup_path = path.with_name(path.name + BACKUP_SUFFIX)
        if not backup_path.is_file():
            raise SystemExit(f"Missing backup: {backup_path}")
        backup_bytes = backup_path.read_bytes()
        if sha256_bytes(backup_bytes) != managed_file.clean_sha256:
            raise SystemExit(
                f"Unexpected backup hash for {backup_path}\n"
                f"expected clean hash: {managed_file.clean_sha256}\n"
                f"actual hash:         {sha256_bytes(backup_bytes)}"
            )
        current_bytes = path.read_bytes()
        if current_bytes == backup_bytes:
            print(f"already clean: {path}")
            continue
        path.write_bytes(backup_bytes)
        print(f"restored: {path}")
        print(f"from:     {backup_path}")


def build_parser() -> ArgumentParser:
    parser = ArgumentParser(description="Interactive Sneak Out patcher.")
    parser.add_argument("game_dir", nargs="?", help="Explicit Sneak Out directory.")
    parser.add_argument("--game-dir", dest="game_dir_option", help="Explicit Sneak Out directory.")
    parser.add_argument("--patches", help="Comma-separated patch ids. Skips the interactive checkbox menu.")
    parser.add_argument("--rollback", action="store_true", help="Restore script-managed backups and exit.")
    parser.add_argument("--list-patches", action="store_true", help="Print patch ids and exit.")
    return parser


def print_patch_list() -> None:
    for option in PATCH_OPTIONS:
        print(f"{option.option_id}: {option.label}")


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()

    if args.list_patches:
        print_patch_list()
        return 0

    explicit_game_dir = args.game_dir_option or args.game_dir
    if explicit_game_dir:
        game_dir = resolve_game_directory(explicit_game_dir)
    else:
        game_dir = prompt_for_game_directory(detect_game_directory())

    if args.rollback:
        rollback(game_dir)
        print("done")
        return 0

    if args.patches is not None:
        selected_option_ids = parse_patch_selection(args.patches)
    else:
        selected_option_ids = choose_patch_options_interactively()

    prepared_files = prepare_files(game_dir)
    apply_selected_patches(prepared_files, selected_option_ids)
    write_prepared_files(prepared_files)

    if selected_option_ids:
        print("enabled patches:")
        for option_id in selected_option_ids:
            print(f"- {PATCH_OPTION_BY_ID[option_id].label}")
    else:
        print("enabled patches: none")
    print("done")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
