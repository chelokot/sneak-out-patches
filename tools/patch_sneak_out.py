#!/usr/bin/env python3

from __future__ import annotations

import hashlib
import os
import platform
import re
import struct
import sys
import termios
import textwrap
import tty
from tempfile import NamedTemporaryFile
from argparse import ArgumentParser
from dataclasses import dataclass
from pathlib import Path
from shutil import get_terminal_size


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
    details: str
    default_enabled: bool
    file_patch_groups: tuple[FilePatchGroup, ...]
    custom_steps: tuple[str, ...] = ()


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
    ManagedFile(
        relative_path="Sneak Out_Data/level0",
        clean_sha256="059822a124c1178fe97674d3f8cfff298ff743191d7c352f5d937e6b6a211d7e",
    ),
)


PATCH_OPTIONS: tuple[PatchOption, ...] = (
    PatchOption(
        option_id="mode-selector",
        label="Add Classic / Crown mode selector",
        details=(
            "Adds a real Classic/Crown selector to the live portal popup, keeps Preferred role as a "
            "separate control, and wires the selected mode into PortalPlayView play flow."
        ),
        default_enabled=True,
        file_patch_groups=(
            FilePatchGroup(
                relative_path="Sneak Out_Data/resources.assets",
                patches=(
                    BinaryPatch(0x4990E2C, "0000", "831e", "Pre-wire SpookedNetworkPlayer.EntityBerekComponent to the prefab EntityBerekComponent."),
                ),
            ),
        ),
        custom_steps=("mode-selector",),
    ),
    PatchOption(
        option_id="fix-private-party-first-invite",
        label="Fix private party join on first invite",
        details=(
            "Makes the invitation join flow use the explicit lobby id from JoinLobbyEvent instead "
            "of the stale cached lobby id, so the first accepted invite joins the host lobby."
        ),
        default_enabled=True,
        file_patch_groups=(
            FilePatchGroup(
                relative_path="GameAssembly.dll",
                patches=(
                    BinaryPatch(
                        0x81593E,
                        "488b4e104885c9741633d2488b5c2430488b7424384883c4205fe973fadeffe81ee8ccffcccccccccccccccccccccccccccc",
                        "488b4e104885c974214885ff741c488b57184c8bc3e808fadeff488b5c2430488b7424384883c4205fc3e813e8ccff909090",
                        "Pass JoinLobbyEvent.LobbyId and Region into SpookedLobbyUtils.JoinLobby instead of using the stale no-arg path.",
                    ),
                ),
            ),
        ),
    ),
    PatchOption(
        option_id="uniform-hunter-random",
        label="Make hunter random selection uniform",
        details=(
            "Redirects the first seeker-threshold load inside ShouldStartState.GetRandomSeeker() "
            "from 0.1 to an existing 1.0 constant without touching the shared global 0.1 value."
        ),
        default_enabled=True,
        file_patch_groups=(
            FilePatchGroup(
                relative_path="GameAssembly.dll",
                patches=(
                    BinaryPatch(
                        0x6A1D8F,
                        "f3440f101528cced02",
                        "f3440f101559cced02",
                        "Load 1.0 for the first seeker bucket inside ShouldStartState.GetRandomSeeker instead of the shared 0.1 constant.",
                    ),
                ),
            ),
        ),
    ),
)


PATCH_OPTION_BY_ID = {option.option_id: option for option in PATCH_OPTIONS}

LEVEL0_PORTAL_SELECTOR_ROOT_TRANSFORM = 13300
LEVEL0_BACKGROUND_TRANSFORM = 14710
LEVEL0_PRIVATE_GAME_TRANSFORM = 14866
LEVEL0_PORTAL_VIEW_COMPONENT = 24702
LEVEL0_MODE_LABEL_COMPONENT = 19499
LEVEL0_MODE_TITLE_COMPONENT = 19506
LEVEL0_MODE_LEFT_TEXT_COMPONENT = 19339
LEVEL0_MODE_RIGHT_TEXT_COMPONENT = 19520
LEVEL0_ROLE_LABEL_CLONE_COMPONENT = 28087
LEVEL0_MODE_ROW_Y = 393.0
LEVEL0_ROLE_ROW_Y = 295.0
LEVEL0_PRIVATE_ROW_Y = 197.0
LEVEL0_GAME_MODE_LABEL_TEXT = b"Game mode"
LEVEL0_ROLE_LABEL_TEXT = b"Preferred role"
LEVEL0_BEREK_LABEL_TEXT = b"Berek!"
LEVEL0_CLASSIC_LABEL_TEXT = b"Normal"

GAMEASSEMBLY_MODE_SELECTOR_WRAPPER_OFFSET = 0x5E4EA0
GAMEASSEMBLY_MODE_SELECTOR_LOADER_OFFSET = 0x5E4FC0
GAMEASSEMBLY_MODE_SELECTOR_ONAWAKE_HELPER_OFFSET = 0x5E6490
GAMEASSEMBLY_ROLE_BUTTON_ENTRY_OFFSET = 0x7E10C0
GAMEASSEMBLY_MODE_CALL_SITE_ONE_OFFSET = 0x7E15AD
GAMEASSEMBLY_MODE_CALL_SITE_TWO_OFFSET = 0x7E15DC
GAMEASSEMBLY_ONAWAKE_TAIL_OFFSET = 0x7E10A4

GAMEASSEMBLY_IMAGE_BASE = 0x180000000
GAMEASSEMBLY_IL2CPP_RAW_START = 0x569C00
GAMEASSEMBLY_IL2CPP_RVA_START = 0x56B000

GAMEASSEMBLY_MODE_SELECTOR_ENTRY_BYTES = bytes.fromhex("40574883ec20")
GAMEASSEMBLY_MODE_CALL_SITE_ONE_BYTES = bytes.fromhex("4533c0488bc8488bd8418d5001")
GAMEASSEMBLY_MODE_CALL_SITE_TWO_BYTES = bytes.fromhex("4533c0418d5001")
GAMEASSEMBLY_ONAWAKE_TAIL_BYTES = bytes.fromhex("488b7c2438488b7424308883500100004883c4205bc3")


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
        selected_option = PATCH_OPTIONS[cursor]
        terminal_width = max(get_terminal_size(fallback=(100, 24)).columns, 60)
        wrapped_details = textwrap.wrap(selected_option.details, width=max(terminal_width - 2, 20))
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
        lines.append(f"{GREEN}{selected_option.option_id}{RESET}")
        lines.extend(wrapped_details)
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


def _replace_qword(raw_bytes: bytearray, offset: int, value: int) -> None:
    raw_bytes[offset : offset + 8] = struct.pack("<q", value)


def _patch_text_component(raw_bytes: bytearray, updated_text: bytes) -> None:
    raw_bytes[88:92] = struct.pack("<I", len(updated_text))
    raw_bytes[92 : 92 + len(updated_text)] = updated_text
    padded_text_end = 92 + ((len(updated_text) + 3) & ~3)
    raw_bytes[92 + len(updated_text) : padded_text_end] = b"\x00" * (
        padded_text_end - (92 + len(updated_text))
    )


def _gameassembly_raw_to_va(raw_offset: int) -> int:
    return GAMEASSEMBLY_IMAGE_BASE + GAMEASSEMBLY_IL2CPP_RVA_START + (raw_offset - GAMEASSEMBLY_IL2CPP_RAW_START)


def _collect_level0_subtree(asset, root_transform_path_id: int) -> list[int]:
    subtree_path_ids: list[int] = []

    def walk(transform_path_id: int) -> None:
        transform_tree = asset.objects[transform_path_id].read_typetree()
        game_object_path_id = transform_tree["m_GameObject"]["m_PathID"]
        game_object_tree = asset.objects[game_object_path_id].read_typetree()
        subtree_path_ids.append(game_object_path_id)
        subtree_path_ids.append(transform_path_id)
        for component in game_object_tree["m_Component"]:
            component_path_id = component["component"]["m_PathID"]
            if component_path_id != transform_path_id:
                subtree_path_ids.append(component_path_id)
        for child in transform_tree["m_Children"]:
            walk(child["m_PathID"])

    walk(root_transform_path_id)
    return subtree_path_ids


def _remap_typetree_path_ids(node, cloned_path_ids: dict[int, int]):
    if isinstance(node, dict):
        remapped: dict[object, object] = {}
        for key, value in node.items():
            if key == "m_PathID" and isinstance(value, int):
                remapped[key] = cloned_path_ids.get(value, value)
            else:
                remapped[key] = _remap_typetree_path_ids(value, cloned_path_ids)
        return remapped
    if isinstance(node, list):
        return [_remap_typetree_path_ids(item, cloned_path_ids) for item in node]
    return node


def _patch_level0_mode_selector(prepared_file: PreparedFile) -> None:
    import UnityPy
    from UnityPy.files.ObjectReader import ObjectReader

    with NamedTemporaryFile(suffix=".level0") as temp_file:
        temp_file.write(bytes(prepared_file.working_bytes))
        temp_file.flush()
        environment = UnityPy.load(temp_file.name)
        asset = next(iter(environment.files.values()))
        source_bytes = bytes(prepared_file.working_bytes)

        subtree_path_ids = _collect_level0_subtree(asset, LEVEL0_PORTAL_SELECTOR_ROOT_TRANSFORM)
        next_path_id = max(asset.objects)
        cloned_path_ids: dict[int, int] = {}
        for source_path_id in subtree_path_ids:
            next_path_id += 1
            cloned_path_ids[source_path_id] = next_path_id

        pointer_replacements = tuple(
            (
                b"\x00\x00\x00\x00" + struct.pack("<q", source_path_id),
                b"\x00\x00\x00\x00" + struct.pack("<q", cloned_path_id),
            )
            for source_path_id, cloned_path_id in cloned_path_ids.items()
        )

        for source_path_id in subtree_path_ids:
            source_object = asset.objects[source_path_id]
            cloned_raw_bytes = bytearray(
                source_bytes[source_object.byte_start : source_object.byte_start + source_object.byte_size]
            )
            for old_pointer, new_pointer in pointer_replacements:
                cloned_raw_bytes = cloned_raw_bytes.replace(old_pointer, new_pointer)
            cloned_object = ObjectReader(
                asset,
                source_object.reader,
                cloned_path_ids[source_path_id],
                source_object.type_id,
                source_object.serialized_type,
                source_object.class_id,
                source_object.type,
                source_object.byte_start,
                len(cloned_raw_bytes),
                source_object.is_destroyed,
                source_object.is_stripped,
                data=bytes(cloned_raw_bytes),
                read_until=source_object._read_until,
            )
            asset.objects[cloned_object.path_id] = cloned_object

        for source_path_id in subtree_path_ids:
            source_object = asset.objects[source_path_id]
            cloned_object = asset.objects[cloned_path_ids[source_path_id]]
            try:
                source_tree = source_object.read_typetree()
            except Exception:
                continue
            cloned_object.save_typetree(_remap_typetree_path_ids(source_tree, cloned_path_ids))

        background_tree = asset.objects[LEVEL0_BACKGROUND_TRANSFORM].read_typetree()
        background_tree["m_Children"].insert(
            3,
            {"m_FileID": 0, "m_PathID": cloned_path_ids[LEVEL0_PORTAL_SELECTOR_ROOT_TRANSFORM]},
        )
        asset.objects[LEVEL0_BACKGROUND_TRANSFORM].save_typetree(background_tree)

        clone_root_tree = asset.objects[LEVEL0_PORTAL_SELECTOR_ROOT_TRANSFORM].read_typetree()
        clone_root_tree["m_GameObject"]["m_PathID"] = cloned_path_ids[602]
        clone_root_tree["m_Children"] = [
            {"m_FileID": 0, "m_PathID": cloned_path_ids[child["m_PathID"]]}
            for child in clone_root_tree["m_Children"]
        ]
        clone_root_tree["m_Father"] = {"m_FileID": 0, "m_PathID": LEVEL0_BACKGROUND_TRANSFORM}
        clone_root_tree["m_AnchoredPosition"]["y"] = LEVEL0_ROLE_ROW_Y
        asset.objects[cloned_path_ids[LEVEL0_PORTAL_SELECTOR_ROOT_TRANSFORM]].save_typetree(clone_root_tree)

        private_game_tree = asset.objects[LEVEL0_PRIVATE_GAME_TRANSFORM].read_typetree()
        private_game_tree["m_AnchoredPosition"]["y"] = LEVEL0_PRIVATE_ROW_Y
        asset.objects[LEVEL0_PRIVATE_GAME_TRANSFORM].save_typetree(private_game_tree)

        original_root_game_object = asset.objects[602].read_typetree()
        original_root_game_object["m_Name"] = "GameModeBackground"
        asset.objects[602].save_typetree(original_root_game_object)

        original_button_game_object = asset.objects[1514].read_typetree()
        original_button_game_object["m_Name"] = "GameMode"
        asset.objects[1514].save_typetree(original_button_game_object)

        cloned_root_game_object = _remap_typetree_path_ids(
            asset.objects[602].read_typetree(), cloned_path_ids
        )
        cloned_root_game_object["m_Name"] = "PreferredRoleBackgroundClone"
        asset.objects[cloned_path_ids[602]].save_typetree(cloned_root_game_object)

        cloned_button_game_object = _remap_typetree_path_ids(
            asset.objects[1514].read_typetree(), cloned_path_ids
        )
        cloned_button_game_object["m_Name"] = "PreferredRoleClone"
        asset.objects[cloned_path_ids[1514]].save_typetree(cloned_button_game_object)

        text_updates = {
            LEVEL0_MODE_LABEL_COMPONENT: LEVEL0_GAME_MODE_LABEL_TEXT,
            LEVEL0_MODE_TITLE_COMPONENT: LEVEL0_GAME_MODE_LABEL_TEXT,
            LEVEL0_MODE_LEFT_TEXT_COMPONENT: LEVEL0_BEREK_LABEL_TEXT,
            LEVEL0_MODE_RIGHT_TEXT_COMPONENT: LEVEL0_CLASSIC_LABEL_TEXT,
            cloned_path_ids[19499]: LEVEL0_ROLE_LABEL_TEXT,
        }
        for text_component_path_id, updated_text in text_updates.items():
            text_component = asset.objects[text_component_path_id]
            raw_bytes = bytearray(
                text_component.data
                if text_component.data is not None
                else source_bytes[text_component.byte_start : text_component.byte_start + text_component.byte_size]
            )
            _patch_text_component(raw_bytes, updated_text)
            text_component.data = bytes(raw_bytes)

        portal_view_component = asset.objects[LEVEL0_PORTAL_VIEW_COMPONENT]
        portal_raw_bytes = bytearray(
            source_bytes[
                portal_view_component.byte_start : portal_view_component.byte_start + portal_view_component.byte_size
            ]
        )
        repointed_component_paths = {
            60: cloned_path_ids[867],
            220: cloned_path_ids[20545],
            232: cloned_path_ids[2548],
            244: cloned_path_ids[3466],
            288: cloned_path_ids[14656],
            300: cloned_path_ids[16006],
        }
        for offset, path_id in repointed_component_paths.items():
            _replace_qword(portal_raw_bytes, offset, path_id)
        portal_view_component.data = bytes(portal_raw_bytes)

        prepared_file.working_bytes = bytearray(asset.save())


def _patch_gameassembly_mode_selector(prepared_file: PreparedFile) -> None:
    from keystone import KS_ARCH_X86, KS_MODE_64, Ks

    gameassembly_bytes = prepared_file.working_bytes

    if bytes(
        gameassembly_bytes[
            GAMEASSEMBLY_ROLE_BUTTON_ENTRY_OFFSET : GAMEASSEMBLY_ROLE_BUTTON_ENTRY_OFFSET
            + len(GAMEASSEMBLY_MODE_SELECTOR_ENTRY_BYTES)
        ]
    ) != GAMEASSEMBLY_MODE_SELECTOR_ENTRY_BYTES:
        raise SystemExit("Unexpected PortalPlayView.OnChangeRoleButton prologue in clean GameAssembly.dll")

    if bytes(
        gameassembly_bytes[
            GAMEASSEMBLY_MODE_CALL_SITE_ONE_OFFSET : GAMEASSEMBLY_MODE_CALL_SITE_ONE_OFFSET
            + len(GAMEASSEMBLY_MODE_CALL_SITE_ONE_BYTES)
        ]
    ) != GAMEASSEMBLY_MODE_CALL_SITE_ONE_BYTES:
        raise SystemExit("Unexpected first PortalPlayView.OnPlay mode literal in clean GameAssembly.dll")

    if bytes(
        gameassembly_bytes[
            GAMEASSEMBLY_MODE_CALL_SITE_TWO_OFFSET : GAMEASSEMBLY_MODE_CALL_SITE_TWO_OFFSET
            + len(GAMEASSEMBLY_MODE_CALL_SITE_TWO_BYTES)
        ]
    ) != GAMEASSEMBLY_MODE_CALL_SITE_TWO_BYTES:
        raise SystemExit("Unexpected second PortalPlayView.OnPlay mode literal in clean GameAssembly.dll")

    if bytes(
        gameassembly_bytes[
            GAMEASSEMBLY_ONAWAKE_TAIL_OFFSET : GAMEASSEMBLY_ONAWAKE_TAIL_OFFSET
            + len(GAMEASSEMBLY_ONAWAKE_TAIL_BYTES)
        ]
    ) != GAMEASSEMBLY_ONAWAKE_TAIL_BYTES:
        raise SystemExit("Unexpected PortalPlayView.OnAwake tail in clean GameAssembly.dll")

    assembler = Ks(KS_ARCH_X86, KS_MODE_64)
    wrapper_address = _gameassembly_raw_to_va(GAMEASSEMBLY_MODE_SELECTOR_WRAPPER_OFFSET)
    loader_address = _gameassembly_raw_to_va(GAMEASSEMBLY_MODE_SELECTOR_LOADER_OFFSET)
    onawake_helper_address = _gameassembly_raw_to_va(GAMEASSEMBLY_MODE_SELECTOR_ONAWAKE_HELPER_OFFSET)
    wrapper_assembly = """
        push rdi
        sub rsp, 0x20
        mov rdi, rcx
        call 0x1834AF8B0
        test rax, rax
        je role_path
        mov rcx, rax
        call 0x18056EC70
        mov [rsp+0x18], rax
        test rax, rax
        je role_path
        mov rcx, rax
        call 0x1832146A0
        mov [rsp+0x10], rax
        test rax, rax
        je role_path
        mov rcx, rax
        call 0x1832410F0
        cmp eax, 4
        je have_button
        mov rcx, [rsp+0x10]
        call 0x18323D6B0
        test rax, rax
        je have_button
        mov rcx, rax
        call 0x18320FB30
        mov [rsp+0x18], rax
    have_button:
        mov rcx, [rdi+0xF8]
        test rcx, rcx
        je role_path
        call 0x18320FB30
        mov rdx, [rsp+0x18]
        mov rcx, rax
        call 0x183219EB0
        test al, al
        jne role_path
        cmp byte ptr [rdi+0x152], 0
        sete al
        mov byte ptr [rdi+0x152], al
        mov rcx, [rsp+0x18]
        call 0x1832146A0
        test rax, rax
        je mode_done
        mov [rsp+0x10], rax
        mov rcx, rax
        mov edx, 1
        call 0x18323D4F0
        mov rcx, rax
        call 0x18320FB30
        movzx edx, byte ptr [rdi+0x152]
        xor edx, 1
        xor r8d, r8d
        call 0x183213DF0
        mov rcx, [rsp+0x10]
        mov edx, 2
        call 0x18323D4F0
        mov rcx, rax
        call 0x18320FB30
        movzx edx, byte ptr [rdi+0x152]
        xor r8d, r8d
        call 0x183213DF0
    mode_done:
        add rsp, 0x20
        pop rdi
        ret
    role_path:
        mov rcx, rdi
        jmp 0x1807E24C6
    """
    wrapper_bytes = bytes(assembler.asm(wrapper_assembly, addr=wrapper_address)[0])
    loader_bytes = bytes(
        assembler.asm(
            "xor r8d, r8d; movzx edx, byte ptr [rdi+0x152]; inc edx; ret",
            addr=loader_address,
        )[0]
    )
    onawake_helper_bytes = bytes(
        assembler.asm(
            """
            mov byte ptr [rbx+0x150], al
            mov rcx, [rbx+0xF8]
            test rcx, rcx
            je finish
            call 0x18320FB30
            test rax, rax
            je finish
            mov rcx, rax
            call 0x1832146A0
            test rax, rax
            je finish
            mov rcx, rax
            call 0x18323D6B0
            test rax, rax
            je finish
            mov rcx, rax
            call 0x18323D6B0
            test rax, rax
            je finish
            mov rcx, rax
            call 0x18323D6B0
            test rax, rax
            je finish
            mov rcx, rax
            mov edx, 2
            call 0x18323D4F0
            test rax, rax
            je finish
            mov rcx, rax
            mov edx, 1
            call 0x18323D4F0
            test rax, rax
            je finish
            mov rcx, rax
            mov edx, 2
            call 0x18323D4F0
            test rax, rax
            je finish
            mov rcx, rax
            call 0x18320FB30
            test rax, rax
            je finish
            mov [rsp+0x10], rax
            mov rcx, rax
            call 0x1832130B0
            cmp eax, 5
            jb finish
            mov rcx, [rsp+0x10]
            mov edx, 4
            call 0x183212F30
            test rax, rax
            je finish
            mov rdi, [rax+0x100]
            mov rcx, 0x1843B1E08
            mov rcx, [rcx]
            call 0x1804726E0
            mov r8, 0x1843A36D8
            mov r8, [r8]
            xor r9d, r9d
            mov rdx, rbx
            mov rcx, rax
            mov rsi, rax
            call 0x1808DA950
            test rdi, rdi
            je finish
            xor r8d, r8d
            mov rdx, rsi
            mov rcx, rdi
            call 0x183243D40
        finish:
            mov rdi, [rsp+0x38]
            mov rsi, [rsp+0x30]
            add rsp, 0x20
            pop rbx
            ret
            """,
            addr=onawake_helper_address,
        )[0]
    )

    gameassembly_bytes[
        GAMEASSEMBLY_MODE_SELECTOR_WRAPPER_OFFSET : GAMEASSEMBLY_MODE_SELECTOR_WRAPPER_OFFSET
        + len(wrapper_bytes)
    ] = wrapper_bytes
    gameassembly_bytes[
        GAMEASSEMBLY_MODE_SELECTOR_LOADER_OFFSET : GAMEASSEMBLY_MODE_SELECTOR_LOADER_OFFSET
        + len(loader_bytes)
    ] = loader_bytes
    gameassembly_bytes[
        GAMEASSEMBLY_MODE_SELECTOR_ONAWAKE_HELPER_OFFSET : GAMEASSEMBLY_MODE_SELECTOR_ONAWAKE_HELPER_OFFSET
        + len(onawake_helper_bytes)
    ] = onawake_helper_bytes

    wrapper_jump = (
        _gameassembly_raw_to_va(GAMEASSEMBLY_MODE_SELECTOR_WRAPPER_OFFSET)
    ) - (
        _gameassembly_raw_to_va(GAMEASSEMBLY_ROLE_BUTTON_ENTRY_OFFSET) + 5
    )
    gameassembly_bytes[
        GAMEASSEMBLY_ROLE_BUTTON_ENTRY_OFFSET : GAMEASSEMBLY_ROLE_BUTTON_ENTRY_OFFSET + 6
    ] = b"\xE9" + int(wrapper_jump).to_bytes(4, "little", signed=True) + b"\x90"

    loader_call_one = (
        _gameassembly_raw_to_va(GAMEASSEMBLY_MODE_SELECTOR_LOADER_OFFSET)
    ) - (
        _gameassembly_raw_to_va(GAMEASSEMBLY_MODE_CALL_SITE_ONE_OFFSET) + 11
    )
    gameassembly_bytes[
        GAMEASSEMBLY_MODE_CALL_SITE_ONE_OFFSET : GAMEASSEMBLY_MODE_CALL_SITE_ONE_OFFSET + 13
    ] = (
        b"\x48\x8b\xc8\x48\x8b\xd8\xe8"
        + int(loader_call_one).to_bytes(4, "little", signed=True)
        + b"\x90\x90"
    )

    loader_call_two = (
        _gameassembly_raw_to_va(GAMEASSEMBLY_MODE_SELECTOR_LOADER_OFFSET)
    ) - (
        _gameassembly_raw_to_va(GAMEASSEMBLY_MODE_CALL_SITE_TWO_OFFSET) + 5
    )
    gameassembly_bytes[
        GAMEASSEMBLY_MODE_CALL_SITE_TWO_OFFSET : GAMEASSEMBLY_MODE_CALL_SITE_TWO_OFFSET + 7
    ] = b"\xE8" + int(loader_call_two).to_bytes(4, "little", signed=True) + b"\x90\x90"

    onawake_tail_jump = (
        _gameassembly_raw_to_va(GAMEASSEMBLY_MODE_SELECTOR_ONAWAKE_HELPER_OFFSET)
    ) - (
        _gameassembly_raw_to_va(GAMEASSEMBLY_ONAWAKE_TAIL_OFFSET) + 5
    )
    gameassembly_bytes[
        GAMEASSEMBLY_ONAWAKE_TAIL_OFFSET : GAMEASSEMBLY_ONAWAKE_TAIL_OFFSET + len(GAMEASSEMBLY_ONAWAKE_TAIL_BYTES)
    ] = b"\xE9" + int(onawake_tail_jump).to_bytes(4, "little", signed=True) + b"\x90" * (
        len(GAMEASSEMBLY_ONAWAKE_TAIL_BYTES) - 5
    )


def apply_custom_patch_steps(
    prepared_files: dict[str, PreparedFile], selected_option_ids: tuple[str, ...]
) -> None:
    selected_option_ids_set = set(selected_option_ids)
    if "mode-selector" in selected_option_ids_set:
        _patch_gameassembly_mode_selector(prepared_files["GameAssembly.dll"])
        _patch_level0_mode_selector(prepared_files["Sneak Out_Data/level0"])


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
    apply_custom_patch_steps(prepared_files, selected_option_ids)


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
        print(f"  {option.details}")


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
