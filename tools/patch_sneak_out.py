#!/usr/bin/env python3

from __future__ import annotations

import hashlib
import os
import platform
import re
import struct
import subprocess
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
ABSENT_MARKER_SUFFIX = ".codex-sneak-out.absent"
REPO_ROOT = Path(__file__).resolve().parent.parent
RUNTIME_MOD_DOTNET = REPO_ROOT / ".tmp/runtime-mod/dotnet/dotnet"


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
class ExecutableRegionLintSpec:
    offset: int
    code_bytes: bytes
    description: str
    entry_rsp_mod16: int | None = None
    allowed_ret_deltas: frozenset[int] = frozenset()
    allowed_tail_jumps: tuple[tuple[int, int], ...] = ()


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


@dataclass(frozen=True)
class RuntimeModOption:
    option_id: str
    label: str
    details: str
    default_enabled: bool
    project_relative_path: str
    assembly_name: str

    @property
    def project_path(self) -> Path:
        return REPO_ROOT / self.project_relative_path

    @property
    def built_dll_path(self) -> Path:
        return self.project_path.parent / "bin/Release/net6.0" / f"{self.assembly_name}.dll"


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
    PatchOption(
        option_id="fix-battlepass-refresh-crash",
        label="Disable crashy battlepass refresh handler",
        details=(
            "Turns BattlepassView.OnOnWebplayerRefreshEvent into a no-op to avoid the lobby "
            "NullReferenceException currently crashing the client before portal selector testing."
        ),
        default_enabled=True,
        file_patch_groups=(
            FilePatchGroup(
                relative_path="GameAssembly.dll",
                patches=(
                    BinaryPatch(
                        0x6D43D0,
                        "40574883ec20",
                        "c39090909090",
                        "Return immediately from BattlepassView.OnOnWebplayerRefreshEvent to avoid the lobby refresh NullReference crash.",
                    ),
                ),
            ),
        ),
    ),
)


PATCH_OPTION_BY_ID = {option.option_id: option for option in PATCH_OPTIONS}

RUNTIME_MOD_OPTIONS: tuple[RuntimeModOption, ...] = (
    RuntimeModOption(
        option_id="portal-mode-selector",
        label="Install Portal Mode Selector runtime mod",
        details="Builds and installs the BepInEx runtime mod that replaces fragile raw portal UI scene edits.",
        default_enabled=False,
        project_relative_path="mods/portal_mode_selector/PortalModeSelector.csproj",
        assembly_name="SneakOut.PortalModeSelector",
    ),
    RuntimeModOption(
        option_id="mummy-unlock",
        label="Install Mummy Unlock runtime mod",
        details="Builds and installs the BepInEx runtime mod used for restoring Mummy-related runtime hooks.",
        default_enabled=False,
        project_relative_path="mods/mummy_unlock/MummyUnlock.csproj",
        assembly_name="SneakOut.MummyUnlock",
    ),
)

RUNTIME_MOD_OPTION_BY_ID = {option.option_id: option for option in RUNTIME_MOD_OPTIONS}

LEVEL0_PORTAL_SELECTOR_ROOT_TRANSFORM = 13300
LEVEL0_BACKGROUND_TRANSFORM = 14710
LEVEL0_PRIVATE_GAME_TRANSFORM = 14866
LEVEL0_MODE_ROW_Y = 393.0
LEVEL0_ROLE_ROW_Y = 295.0
LEVEL0_PRIVATE_ROW_Y = 197.0
LEVEL0_GAME_MODE_LABEL_TEXT = b"Game mode"
LEVEL0_CLASSIC_LABEL_TEXT = b"Classic"
LEVEL0_CROWN_LABEL_TEXT = b"Crown"
LEVEL0_SAFE_MODE_LABEL_TRANSFORM = 14171
LEVEL0_SAFE_MODE_LABEL_TEXT_COMPONENT = 19543
LEVEL0_SAFE_MODE_LEFT_TEXT_TRANSFORM = 13519
LEVEL0_SAFE_MODE_LEFT_TEXT_COMPONENT = 19255
LEVEL0_SAFE_MODE_RIGHT_TEXT_TRANSFORM = 13870
LEVEL0_SAFE_MODE_RIGHT_TEXT_COMPONENT = 19309
LEVEL0_ORIGINAL_TOP_LABEL_GO = 2288
LEVEL0_ORIGINAL_LEFT_TEXT_GO = 1360
LEVEL0_ORIGINAL_RIGHT_TEXT_GO = 2464

GAMEASSEMBLY_MODE_SELECTOR_WRAPPER_OFFSET = 0x5E4EA0
GAMEASSEMBLY_MODE_SELECTOR_LOADER_OFFSET = 0x5E4FC0
GAMEASSEMBLY_ROLE_BUTTON_ENTRY_OFFSET = 0x7E10C0
GAMEASSEMBLY_MODE_CALL_SITE_ONE_OFFSET = 0x7E15AD
GAMEASSEMBLY_MODE_CALL_SITE_TWO_OFFSET = 0x7E15DC

GAMEASSEMBLY_IMAGE_BASE = 0x180000000
GAMEASSEMBLY_IL2CPP_RAW_START = 0x569C00
GAMEASSEMBLY_IL2CPP_RVA_START = 0x56B000

GAMEASSEMBLY_MODE_SELECTOR_ENTRY_BYTES = bytes.fromhex("40574883ec20")
GAMEASSEMBLY_MODE_CALL_SITE_ONE_BYTES = bytes.fromhex("4533c0488bc8488bd8418d5001")
GAMEASSEMBLY_MODE_CALL_SITE_TWO_BYTES = bytes.fromhex("4533c0418d5001")


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


def choose_options_interactively(
    options: tuple[PatchOption | RuntimeModOption, ...],
    title: str,
) -> tuple[str, ...]:
    if not sys.stdin.isatty() or not sys.stdout.isatty():
        raise SystemExit("Interactive selection requires a TTY. Use command-line selection flags instead.")
    selected = [option.default_enabled for option in options]
    cursor = 0
    while True:
        selected_option = options[cursor]
        terminal_width = max(get_terminal_size(fallback=(100, 24)).columns, 60)
        wrapped_details = textwrap.wrap(selected_option.details, width=max(terminal_width - 2, 20))
        lines = [
            CLEAR_SCREEN,
            f"{GREEN}Use ↑/↓ to move, Space to toggle, Enter to apply.{RESET}",
            "",
            title,
        ]
        for index, option in enumerate(options):
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
            cursor = (cursor - 1) % len(options)
        elif key == "down":
            cursor = (cursor + 1) % len(options)
        elif key == "space":
            selected[cursor] = not selected[cursor]
        elif key == "enter":
            sys.stdout.write(CLEAR_SCREEN)
            sys.stdout.flush()
            return tuple(option.option_id for option, is_selected in zip(options, selected) if is_selected)
        elif key == "quit":
            raise SystemExit(1)


def choose_patch_options_interactively() -> tuple[str, ...]:
    return choose_options_interactively(PATCH_OPTIONS, "Patch options (1/2):")


def choose_runtime_mod_options_interactively() -> tuple[str, ...]:
    return choose_options_interactively(RUNTIME_MOD_OPTIONS, "Runtime mod options (2/2):")


def choose_installation_plan_interactively() -> tuple[tuple[str, ...], tuple[str, ...]]:
    return (
        choose_patch_options_interactively(),
        choose_runtime_mod_options_interactively(),
    )


def parse_selection(
    raw_selection: str,
    option_by_id: dict[str, PatchOption | RuntimeModOption],
    label: str,
) -> tuple[str, ...]:
    if not raw_selection.strip():
        return ()
    option_ids = tuple(part.strip() for part in raw_selection.split(",") if part.strip())
    unknown_option_ids = [option_id for option_id in option_ids if option_id not in option_by_id]
    if unknown_option_ids:
        available_option_ids = ", ".join(sorted(option_by_id))
        raise SystemExit(
            f"Unknown {label} option(s): {', '.join(unknown_option_ids)}\n"
            f"Available {label} ids: {available_option_ids}"
        )
    return option_ids


def parse_patch_selection(raw_selection: str) -> tuple[str, ...]:
    return parse_selection(raw_selection, PATCH_OPTION_BY_ID, "patch")


def parse_runtime_mod_selection(raw_selection: str) -> tuple[str, ...]:
    return parse_selection(raw_selection, RUNTIME_MOD_OPTION_BY_ID, "runtime mod")


def default_patch_selection() -> tuple[str, ...]:
    return tuple(option.option_id for option in PATCH_OPTIONS if option.default_enabled)


def default_runtime_mod_selection() -> tuple[str, ...]:
    return tuple(option.option_id for option in RUNTIME_MOD_OPTIONS if option.default_enabled)


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


def first_diff_offset(expected: bytes, actual: bytes) -> int | None:
    limit = min(len(expected), len(actual))
    for index in range(limit):
        if expected[index] != actual[index]:
            return index
    if len(expected) != len(actual):
        return limit
    return None


def format_diff_window(expected: bytes, actual: bytes, offset: int, window: int = 16) -> str:
    start = max(offset - window, 0)
    end = min(offset + window, max(len(expected), len(actual)))
    expected_slice = expected[start:end]
    actual_slice = actual[start:end]
    return (
        f"first diff at 0x{offset:x}\n"
        f"expected[{start:#x}:{end:#x}]: {expected_slice.hex()}\n"
        f"actual[{start:#x}:{end:#x}]:   {actual_slice.hex()}"
    )


def _replace_qword(raw_bytes: bytearray, offset: int, value: int) -> None:
    raw_bytes[offset : offset + 8] = struct.pack("<q", value)


def _replace_rel32(raw_bytes: bytearray, offset: int, opcode_length: int, source_va: int, target_va: int) -> None:
    displacement = target_va - (source_va + opcode_length)
    raw_bytes[offset : offset + 4] = struct.pack("<i", displacement)


def _patch_text_component(raw_bytes: bytearray, updated_text: bytes) -> None:
    raw_bytes[88:92] = struct.pack("<I", len(updated_text))
    raw_bytes[92 : 92 + len(updated_text)] = updated_text
    padded_text_end = 92 + ((len(updated_text) + 3) & ~3)
    raw_bytes[92 + len(updated_text) : padded_text_end] = b"\x00" * (
        padded_text_end - (92 + len(updated_text))
    )


def _disable_monobehaviour(raw_bytes: bytearray) -> None:
    raw_bytes[12:16] = struct.pack("<I", 0)


def _gameassembly_raw_to_va(raw_offset: int) -> int:
    return GAMEASSEMBLY_IMAGE_BASE + GAMEASSEMBLY_IL2CPP_RVA_START + (raw_offset - GAMEASSEMBLY_IL2CPP_RAW_START)


def _disassemble_exact(code_bytes: bytes, virtual_address: int, label: str) -> None:
    from capstone import CS_ARCH_X86, CS_MODE_64, Cs

    disassembler = Cs(CS_ARCH_X86, CS_MODE_64)
    instructions = list(disassembler.disasm(code_bytes, virtual_address))
    consumed = sum(instruction.size for instruction in instructions)
    if consumed != len(code_bytes):
        raise SystemExit(
            f"Static validation failed for {label}\n"
            f"disassembled: {consumed} bytes\n"
            f"expected:     {len(code_bytes)} bytes"
        )


def _read_rel32_target(raw_bytes: bytes, offset: int, mnemonic: str) -> int:
    from capstone import CS_ARCH_X86, CS_MODE_64, CS_OP_IMM, Cs

    disassembler = Cs(CS_ARCH_X86, CS_MODE_64)
    disassembler.detail = True
    virtual_address = _gameassembly_raw_to_va(offset)
    instructions = list(disassembler.disasm(raw_bytes[offset : offset + 16], virtual_address))
    if not instructions:
        raise SystemExit(f"Static validation failed at 0x{offset:x}: no instruction decoded")
    instruction = instructions[0]
    if instruction.mnemonic != mnemonic:
        raise SystemExit(
            f"Static validation failed at 0x{offset:x}: expected {mnemonic}, got {instruction.mnemonic}"
        )
    if len(instruction.operands) != 1 or instruction.operands[0].type != CS_OP_IMM:
        raise SystemExit(f"Static validation failed at 0x{offset:x}: expected rel32 immediate operand")
    return int(instruction.operands[0].imm)


def _section_name_for_offset(pe, raw_offset: int) -> str:
    for section in pe.sections:
        start = int(section.PointerToRawData)
        end = start + int(section.SizeOfRawData)
        if start <= raw_offset < end:
            return section.Name.rstrip(b"\x00").decode("ascii", errors="ignore")
    return "<none>"


def _section_is_executable(pe, raw_offset: int) -> bool:
    for section in pe.sections:
        start = int(section.PointerToRawData)
        end = start + int(section.SizeOfRawData)
        if start <= raw_offset < end:
            return bool(section.Characteristics & 0x20000000)
    return False


def _build_executable_region_specs(selected_option_ids: tuple[str, ...]) -> list[ExecutableRegionLintSpec]:
    region_specs: list[ExecutableRegionLintSpec] = []
    for option_id in selected_option_ids:
        option = PATCH_OPTION_BY_ID[option_id]
        for file_patch_group in option.file_patch_groups:
            if file_patch_group.relative_path != "GameAssembly.dll":
                continue
            for patch in file_patch_group.patches:
                region_specs.append(
                    ExecutableRegionLintSpec(
                        offset=patch.offset,
                        code_bytes=patch.after,
                        description=patch.description,
                    )
                )

    if "mode-selector" in selected_option_ids:
        mode_regions = _build_gameassembly_mode_selector_regions()
        region_specs.extend(
            (
                ExecutableRegionLintSpec(
                    offset=GAMEASSEMBLY_MODE_SELECTOR_WRAPPER_OFFSET,
                    code_bytes=mode_regions[GAMEASSEMBLY_MODE_SELECTOR_WRAPPER_OFFSET],
                    description="mode-selector role-button wrapper",
                    entry_rsp_mod16=8,
                    allowed_ret_deltas=frozenset({0}),
                    allowed_tail_jumps=((0x1807E24C6, -40),),
                ),
                ExecutableRegionLintSpec(
                    offset=GAMEASSEMBLY_MODE_SELECTOR_LOADER_OFFSET,
                    code_bytes=mode_regions[GAMEASSEMBLY_MODE_SELECTOR_LOADER_OFFSET],
                    description="mode-selector game-mode loader",
                    entry_rsp_mod16=8,
                    allowed_ret_deltas=frozenset({0}),
                ),
                ExecutableRegionLintSpec(
                    offset=GAMEASSEMBLY_ROLE_BUTTON_ENTRY_OFFSET,
                    code_bytes=mode_regions[GAMEASSEMBLY_ROLE_BUTTON_ENTRY_OFFSET],
                    description="mode-selector role-button entry hook",
                ),
                ExecutableRegionLintSpec(
                    offset=GAMEASSEMBLY_MODE_CALL_SITE_ONE_OFFSET,
                    code_bytes=mode_regions[GAMEASSEMBLY_MODE_CALL_SITE_ONE_OFFSET],
                    description="mode-selector first OnPlay loader hook",
                ),
                ExecutableRegionLintSpec(
                    offset=GAMEASSEMBLY_MODE_CALL_SITE_TWO_OFFSET,
                    code_bytes=mode_regions[GAMEASSEMBLY_MODE_CALL_SITE_TWO_OFFSET],
                    description="mode-selector second OnPlay loader hook",
                ),
            )
        )
    return region_specs


def _instruction_stack_delta(instruction) -> int:
    from capstone import CS_OP_IMM, CS_OP_REG

    if instruction.mnemonic == "push":
        return -8
    if instruction.mnemonic == "pop":
        return 8
    if instruction.mnemonic in {"sub", "add"} and len(instruction.operands) == 2:
        left_operand, right_operand = instruction.operands
        if (
            left_operand.type == CS_OP_REG
            and instruction.reg_name(left_operand.reg) == "rsp"
            and right_operand.type == CS_OP_IMM
        ):
            immediate = int(right_operand.imm)
            return -immediate if instruction.mnemonic == "sub" else immediate
    return 0


def _lint_executable_region(pe, region_spec: ExecutableRegionLintSpec) -> None:
    from capstone import CS_AC_WRITE, CS_ARCH_X86, CS_MODE_64, CS_OP_IMM, CS_OP_MEM, Cs
    from capstone.x86_const import X86_REG_RIP

    _disassemble_exact(
        region_spec.code_bytes,
        _gameassembly_raw_to_va(region_spec.offset),
        f"{region_spec.description} in section {_section_name_for_offset(pe, region_spec.offset)}",
    )

    if region_spec.entry_rsp_mod16 is None:
        return

    disassembler = Cs(CS_ARCH_X86, CS_MODE_64)
    disassembler.detail = True
    instructions = list(
        disassembler.disasm(region_spec.code_bytes, _gameassembly_raw_to_va(region_spec.offset))
    )
    instruction_by_address = {instruction.address: instruction for instruction in instructions}
    region_start = _gameassembly_raw_to_va(region_spec.offset)
    region_end = region_start + len(region_spec.code_bytes)
    allowed_tail_jumps = dict(region_spec.allowed_tail_jumps)
    pending_states: list[tuple[int, int]] = [(region_start, 0)]
    seen_states: dict[int, int] = {}

    while pending_states:
        address, stack_delta = pending_states.pop()
        recorded_delta = seen_states.get(address)
        if recorded_delta is not None:
            if recorded_delta != stack_delta:
                raise SystemExit(
                    f"ABI lint failed for {region_spec.description}\n"
                    f"conflicting stack states at 0x{address:x}: {recorded_delta} vs {stack_delta}"
                )
            continue
        seen_states[address] = stack_delta

        instruction = instruction_by_address.get(address)
        if instruction is None:
            raise SystemExit(
                f"ABI lint failed for {region_spec.description}\n"
                f"control flow reached unknown address 0x{address:x}"
            )

        current_rsp_mod16 = (region_spec.entry_rsp_mod16 + stack_delta) % 16
        if instruction.mnemonic == "call" and current_rsp_mod16 != 0:
            raise SystemExit(
                f"ABI lint failed for {region_spec.description}\n"
                f"misaligned stack before call at 0x{instruction.address:x}: rsp mod 16 = {current_rsp_mod16}"
            )

        for operand in instruction.operands:
            if operand.type != CS_OP_MEM:
                continue
            if operand.access & CS_AC_WRITE == 0:
                continue
            if operand.mem.base != X86_REG_RIP:
                continue
            memory_target = instruction.address + instruction.size + operand.mem.disp
            raw_target = GAMEASSEMBLY_IL2CPP_RAW_START + (
                memory_target - GAMEASSEMBLY_IMAGE_BASE - GAMEASSEMBLY_IL2CPP_RVA_START
            )
            if _section_is_executable(pe, raw_target):
                raise SystemExit(
                    f"ABI lint failed for {region_spec.description}\n"
                    f"self-modifying write into executable section at 0x{instruction.address:x}"
                )

        next_stack_delta = stack_delta + _instruction_stack_delta(instruction)
        next_address = instruction.address + instruction.size

        if instruction.mnemonic == "ret":
            if next_stack_delta not in region_spec.allowed_ret_deltas:
                raise SystemExit(
                    f"ABI lint failed for {region_spec.description}\n"
                    f"unexpected stack delta before ret at 0x{instruction.address:x}: {next_stack_delta}"
                )
            continue

        if instruction.mnemonic == "jmp":
            if len(instruction.operands) != 1 or instruction.operands[0].type != CS_OP_IMM:
                raise SystemExit(
                    f"ABI lint failed for {region_spec.description}\n"
                    f"unsupported indirect jmp at 0x{instruction.address:x}"
                )
            target = int(instruction.operands[0].imm)
            if region_start <= target < region_end:
                pending_states.append((target, next_stack_delta))
                continue
            allowed_delta = allowed_tail_jumps.get(target)
            if allowed_delta is None or allowed_delta != next_stack_delta:
                raise SystemExit(
                    f"ABI lint failed for {region_spec.description}\n"
                    f"unexpected tail jmp at 0x{instruction.address:x} -> 0x{target:x} with stack delta {next_stack_delta}"
                )
            continue

        if instruction.mnemonic.startswith("j") and instruction.mnemonic != "jmp":
            if len(instruction.operands) != 1 or instruction.operands[0].type != CS_OP_IMM:
                raise SystemExit(
                    f"ABI lint failed for {region_spec.description}\n"
                    f"unsupported conditional branch at 0x{instruction.address:x}"
                )
            target = int(instruction.operands[0].imm)
            if not (region_start <= target < region_end):
                raise SystemExit(
                    f"ABI lint failed for {region_spec.description}\n"
                    f"conditional branch escapes region at 0x{instruction.address:x} -> 0x{target:x}"
                )
            pending_states.append((target, next_stack_delta))
            pending_states.append((next_address, next_stack_delta))
            continue

        if next_address < region_end:
            pending_states.append((next_address, next_stack_delta))


def _validate_gameassembly_static(clean_bytes: bytes, patched_bytes: bytes, selected_option_ids: tuple[str, ...]) -> None:
    import pefile

    pe = pefile.PE(data=patched_bytes, fast_load=True)
    for region_spec in _build_executable_region_specs(selected_option_ids):
        if not _section_is_executable(pe, region_spec.offset):
            continue
        _lint_executable_region(pe, region_spec)

    if "mode-selector" in selected_option_ids:
        if patched_bytes[GAMEASSEMBLY_ROLE_BUTTON_ENTRY_OFFSET + 5] != 0x90:
            raise SystemExit("Static validation failed: role-button hook is missing trailing NOP")
        role_target = _read_rel32_target(patched_bytes, GAMEASSEMBLY_ROLE_BUTTON_ENTRY_OFFSET, "jmp")
        if role_target != _gameassembly_raw_to_va(GAMEASSEMBLY_MODE_SELECTOR_WRAPPER_OFFSET):
            raise SystemExit(
                "Static validation failed: role-button hook target does not point at mode-selector wrapper"
            )

        call_target_one = _read_rel32_target(patched_bytes, GAMEASSEMBLY_MODE_CALL_SITE_ONE_OFFSET + 6, "call")
        if call_target_one != _gameassembly_raw_to_va(GAMEASSEMBLY_MODE_SELECTOR_LOADER_OFFSET):
            raise SystemExit(
                "Static validation failed: first PortalPlayView.OnPlay mode loader call does not point at the selector loader"
            )

        call_target_two = _read_rel32_target(patched_bytes, GAMEASSEMBLY_MODE_CALL_SITE_TWO_OFFSET, "call")
        if call_target_two != _gameassembly_raw_to_va(GAMEASSEMBLY_MODE_SELECTOR_LOADER_OFFSET):
            raise SystemExit(
                "Static validation failed: second PortalPlayView.OnPlay mode loader call does not point at the selector loader"
            )


def validate_prepared_files(prepared_files: dict[str, PreparedFile], selected_option_ids: tuple[str, ...]) -> None:
    for prepared_file in prepared_files.values():
        expected_bytes = bytes(prepared_file.working_bytes)
        clean_bytes = prepared_file.backup_path.read_bytes()
        if prepared_file.spec.relative_path == "GameAssembly.dll":
            _validate_gameassembly_static(clean_bytes, expected_bytes, selected_option_ids)


def validate_installed_files(game_dir: Path, selected_option_ids: tuple[str, ...]) -> None:
    prepared_files = prepare_files(game_dir)
    apply_selected_patches(prepared_files, selected_option_ids)
    validate_prepared_files(prepared_files, selected_option_ids)
    for prepared_file in prepared_files.values():
        expected_bytes = bytes(prepared_file.working_bytes)
        actual_bytes = prepared_file.path.read_bytes()
        if actual_bytes != expected_bytes:
            diff_offset = first_diff_offset(expected_bytes, actual_bytes)
            if diff_offset is None:
                raise SystemExit(f"Validation failed for {prepared_file.path}")
            raise SystemExit(
                f"Validation failed for {prepared_file.path}\n"
                + format_diff_window(expected_bytes, actual_bytes, diff_offset)
            )


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

        def move_transform(transform_path_id: int, new_parent_path_id: int, sibling_index: int | None = None) -> None:
            transform_tree = asset.objects[transform_path_id].read_typetree()
            old_parent_path_id = transform_tree["m_Father"]["m_PathID"]
            if old_parent_path_id != 0:
                old_parent_tree = asset.objects[old_parent_path_id].read_typetree()
                old_parent_tree["m_Children"] = [
                    child for child in old_parent_tree["m_Children"] if child["m_PathID"] != transform_path_id
                ]
                asset.objects[old_parent_path_id].save_typetree(old_parent_tree)
            new_parent_tree = asset.objects[new_parent_path_id].read_typetree()
            child_reference = {"m_FileID": 0, "m_PathID": transform_path_id}
            if sibling_index is None:
                new_parent_tree["m_Children"].append(child_reference)
            else:
                new_parent_tree["m_Children"].insert(sibling_index, child_reference)
            asset.objects[new_parent_path_id].save_typetree(new_parent_tree)
            transform_tree["m_Father"] = {"m_FileID": 0, "m_PathID": new_parent_path_id}
            asset.objects[transform_path_id].save_typetree(transform_tree)

        def set_game_object_active(game_object_path_id: int, active: bool) -> None:
            game_object_tree = asset.objects[game_object_path_id].read_typetree()
            game_object_tree["m_IsActive"] = 1 if active else 0
            asset.objects[game_object_path_id].save_typetree(game_object_tree)

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
        clone_root_tree["m_AnchoredPosition"]["y"] = LEVEL0_MODE_ROW_Y
        asset.objects[cloned_path_ids[LEVEL0_PORTAL_SELECTOR_ROOT_TRANSFORM]].save_typetree(clone_root_tree)

        original_root_tree = asset.objects[LEVEL0_PORTAL_SELECTOR_ROOT_TRANSFORM].read_typetree()
        original_root_tree["m_AnchoredPosition"]["y"] = LEVEL0_ROLE_ROW_Y
        asset.objects[LEVEL0_PORTAL_SELECTOR_ROOT_TRANSFORM].save_typetree(original_root_tree)

        private_game_tree = asset.objects[LEVEL0_PRIVATE_GAME_TRANSFORM].read_typetree()
        private_game_tree["m_AnchoredPosition"]["y"] = LEVEL0_PRIVATE_ROW_Y
        asset.objects[LEVEL0_PRIVATE_GAME_TRANSFORM].save_typetree(private_game_tree)

        cloned_root_game_object = _remap_typetree_path_ids(asset.objects[602].read_typetree(), cloned_path_ids)
        cloned_root_game_object["m_Name"] = "GameModeBackground"
        asset.objects[cloned_path_ids[602]].save_typetree(cloned_root_game_object)

        cloned_button_game_object = _remap_typetree_path_ids(asset.objects[1514].read_typetree(), cloned_path_ids)
        cloned_button_game_object["m_Name"] = "GameMode"
        asset.objects[cloned_path_ids[1514]].save_typetree(cloned_button_game_object)

        move_transform(LEVEL0_SAFE_MODE_LABEL_TRANSFORM, cloned_path_ids[LEVEL0_PORTAL_SELECTOR_ROOT_TRANSFORM])
        move_transform(LEVEL0_SAFE_MODE_LEFT_TEXT_TRANSFORM, cloned_path_ids[16006])
        move_transform(LEVEL0_SAFE_MODE_RIGHT_TEXT_TRANSFORM, cloned_path_ids[14656])

        mode_label_transform_tree = asset.objects[LEVEL0_SAFE_MODE_LABEL_TRANSFORM].read_typetree()
        mode_label_transform_tree["m_AnchoredPosition"]["x"] = 0.23002000153064728
        mode_label_transform_tree["m_AnchoredPosition"]["y"] = 28.200000762939453
        asset.objects[LEVEL0_SAFE_MODE_LABEL_TRANSFORM].save_typetree(mode_label_transform_tree)

        mode_left_transform_tree = asset.objects[LEVEL0_SAFE_MODE_LEFT_TEXT_TRANSFORM].read_typetree()
        mode_left_transform_tree["m_AnchoredPosition"]["x"] = 0.1750202178955078
        mode_left_transform_tree["m_AnchoredPosition"]["y"] = 9.5367431640625e-06
        asset.objects[LEVEL0_SAFE_MODE_LEFT_TEXT_TRANSFORM].save_typetree(mode_left_transform_tree)

        mode_right_transform_tree = asset.objects[LEVEL0_SAFE_MODE_RIGHT_TEXT_TRANSFORM].read_typetree()
        mode_right_transform_tree["m_AnchoredPosition"]["x"] = -3.814697265625e-05
        mode_right_transform_tree["m_AnchoredPosition"]["y"] = -0.5
        asset.objects[LEVEL0_SAFE_MODE_RIGHT_TEXT_TRANSFORM].save_typetree(mode_right_transform_tree)

        set_game_object_active(cloned_path_ids[LEVEL0_ORIGINAL_TOP_LABEL_GO], False)
        set_game_object_active(cloned_path_ids[LEVEL0_ORIGINAL_LEFT_TEXT_GO], False)
        set_game_object_active(cloned_path_ids[LEVEL0_ORIGINAL_RIGHT_TEXT_GO], False)

        text_updates = {
            LEVEL0_SAFE_MODE_LABEL_TEXT_COMPONENT: LEVEL0_GAME_MODE_LABEL_TEXT,
            LEVEL0_SAFE_MODE_LEFT_TEXT_COMPONENT: LEVEL0_CLASSIC_LABEL_TEXT,
            LEVEL0_SAFE_MODE_RIGHT_TEXT_COMPONENT: LEVEL0_CROWN_LABEL_TEXT,
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

        prepared_file.working_bytes = bytearray(asset.save())


def _build_gameassembly_mode_selector_regions() -> dict[int, bytes]:
    from keystone import KS_ARCH_X86, KS_MODE_64, Ks

    assembler = Ks(KS_ARCH_X86, KS_MODE_64)
    wrapper_address = _gameassembly_raw_to_va(GAMEASSEMBLY_MODE_SELECTOR_WRAPPER_OFFSET)
    loader_address = _gameassembly_raw_to_va(GAMEASSEMBLY_MODE_SELECTOR_LOADER_OFFSET)
    wrapper_bytes = bytes(
        assembler.asm(
            """
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
            """,
            addr=wrapper_address,
        )[0]
    )
    loader_bytes = bytes(
        assembler.asm(
            "xor r8d, r8d; movzx edx, byte ptr [rdi+0x152]; inc edx; ret",
            addr=loader_address,
        )[0]
    )
    wrapper_jump = (
        _gameassembly_raw_to_va(GAMEASSEMBLY_MODE_SELECTOR_WRAPPER_OFFSET)
    ) - (
        _gameassembly_raw_to_va(GAMEASSEMBLY_ROLE_BUTTON_ENTRY_OFFSET) + 5
    )
    role_hook_bytes = b"\xE9" + int(wrapper_jump).to_bytes(4, "little", signed=True) + b"\x90"

    loader_call_one = (
        _gameassembly_raw_to_va(GAMEASSEMBLY_MODE_SELECTOR_LOADER_OFFSET)
    ) - (
        _gameassembly_raw_to_va(GAMEASSEMBLY_MODE_CALL_SITE_ONE_OFFSET) + 11
    )
    mode_call_site_one_bytes = (
        b"\x48\x8b\xc8\x48\x8b\xd8\xe8"
        + int(loader_call_one).to_bytes(4, "little", signed=True)
        + b"\x90\x90"
    )

    loader_call_two = (
        _gameassembly_raw_to_va(GAMEASSEMBLY_MODE_SELECTOR_LOADER_OFFSET)
    ) - (
        _gameassembly_raw_to_va(GAMEASSEMBLY_MODE_CALL_SITE_TWO_OFFSET) + 5
    )
    mode_call_site_two_bytes = b"\xE8" + int(loader_call_two).to_bytes(4, "little", signed=True) + b"\x90\x90"

    return {
        GAMEASSEMBLY_MODE_SELECTOR_WRAPPER_OFFSET: wrapper_bytes,
        GAMEASSEMBLY_MODE_SELECTOR_LOADER_OFFSET: loader_bytes,
        GAMEASSEMBLY_ROLE_BUTTON_ENTRY_OFFSET: role_hook_bytes,
        GAMEASSEMBLY_MODE_CALL_SITE_ONE_OFFSET: mode_call_site_one_bytes,
        GAMEASSEMBLY_MODE_CALL_SITE_TWO_OFFSET: mode_call_site_two_bytes,
    }


def _patch_gameassembly_mode_selector(prepared_file: PreparedFile) -> None:
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

    for offset, patch_bytes in _build_gameassembly_mode_selector_regions().items():
        gameassembly_bytes[offset : offset + len(patch_bytes)] = patch_bytes



def apply_custom_patch_steps(
    prepared_files: dict[str, PreparedFile], selected_option_ids: tuple[str, ...]
) -> None:
    selected_option_ids_set = set(selected_option_ids)
    if "mode-selector" in selected_option_ids_set:
        _patch_level0_mode_selector(prepared_files["Sneak Out_Data/level0"])
        _patch_gameassembly_mode_selector(prepared_files["GameAssembly.dll"])


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


def resolve_runtime_mod_install_path(game_dir: Path, runtime_mod: RuntimeModOption) -> Path:
    bepinex_dir = game_dir / "BepInEx"
    if not bepinex_dir.is_dir():
        raise SystemExit(f"Missing BepInEx directory: {bepinex_dir}")
    plugins_dir = bepinex_dir / "plugins"
    plugins_dir.mkdir(parents=True, exist_ok=True)
    return plugins_dir / f"{runtime_mod.assembly_name}.dll"


def build_runtime_mod(runtime_mod: RuntimeModOption) -> Path:
    if not runtime_mod.project_path.is_file():
        raise SystemExit(f"Missing runtime mod project: {runtime_mod.project_path}")
    if not RUNTIME_MOD_DOTNET.is_file():
        raise SystemExit(f"Missing local runtime-mod dotnet SDK: {RUNTIME_MOD_DOTNET}")
    command = [
        str(RUNTIME_MOD_DOTNET),
        "build",
        str(runtime_mod.project_path.relative_to(REPO_ROOT)),
        "-c",
        "Release",
    ]
    completed = subprocess.run(
        command,
        cwd=REPO_ROOT,
        capture_output=True,
        text=True,
        check=False,
    )
    if completed.returncode != 0:
        raise SystemExit(
            f"Runtime mod build failed for {runtime_mod.label}\n"
            f"command: {' '.join(command)}\n"
            f"stdout:\n{completed.stdout}\n"
            f"stderr:\n{completed.stderr}"
        )
    if not runtime_mod.built_dll_path.is_file():
        raise SystemExit(f"Missing built runtime mod DLL: {runtime_mod.built_dll_path}")
    return runtime_mod.built_dll_path


def install_runtime_mod(game_dir: Path, runtime_mod: RuntimeModOption, built_dll_path: Path) -> None:
    install_path = resolve_runtime_mod_install_path(game_dir, runtime_mod)
    backup_path = install_path.with_name(install_path.name + BACKUP_SUFFIX)
    absent_marker_path = install_path.with_name(install_path.name + ABSENT_MARKER_SUFFIX)
    backup_created = False
    absent_marker_created = False

    if install_path.is_file():
        if not backup_path.is_file():
            backup_path.write_bytes(install_path.read_bytes())
            backup_created = True
        if absent_marker_path.exists():
            absent_marker_path.unlink()
    else:
        if not absent_marker_path.exists():
            absent_marker_path.write_text("absent\n", encoding="utf-8")
            absent_marker_created = True

    built_bytes = built_dll_path.read_bytes()
    current_bytes = install_path.read_bytes() if install_path.is_file() else None
    if current_bytes == built_bytes:
        print(f"unchanged: {install_path}")
    else:
        install_path.write_bytes(built_bytes)
        print(f"updated:   {install_path}")

    if backup_created:
        print(f"backup:    {backup_path}")
    if absent_marker_created:
        print(f"created:   {absent_marker_path}")

def install_selected_runtime_mods(game_dir: Path, selected_runtime_mod_option_ids: tuple[str, ...]) -> None:
    for option_id in selected_runtime_mod_option_ids:
        runtime_mod = RUNTIME_MOD_OPTION_BY_ID[option_id]
        built_dll_path = build_runtime_mod(runtime_mod)
        install_runtime_mod(game_dir, runtime_mod, built_dll_path)


def validate_installed_runtime_mods(game_dir: Path, selected_runtime_mod_option_ids: tuple[str, ...]) -> None:
    for option_id in selected_runtime_mod_option_ids:
        runtime_mod = RUNTIME_MOD_OPTION_BY_ID[option_id]
        built_dll_path = build_runtime_mod(runtime_mod)
        install_path = resolve_runtime_mod_install_path(game_dir, runtime_mod)
        if not install_path.is_file():
            raise SystemExit(f"Missing installed runtime mod: {install_path}")
        expected_bytes = built_dll_path.read_bytes()
        actual_bytes = install_path.read_bytes()
        if actual_bytes != expected_bytes:
            diff_offset = first_diff_offset(expected_bytes, actual_bytes)
            if diff_offset is None:
                raise SystemExit(f"Validation failed for {install_path}")
            raise SystemExit(
                f"Validation failed for {install_path}\n"
                + format_diff_window(expected_bytes, actual_bytes, diff_offset)
            )


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

    for runtime_mod in RUNTIME_MOD_OPTIONS:
        install_path = resolve_runtime_mod_install_path(game_dir, runtime_mod)
        backup_path = install_path.with_name(install_path.name + BACKUP_SUFFIX)
        absent_marker_path = install_path.with_name(install_path.name + ABSENT_MARKER_SUFFIX)
        if backup_path.is_file():
            backup_bytes = backup_path.read_bytes()
            current_bytes = install_path.read_bytes() if install_path.is_file() else None
            if current_bytes == backup_bytes:
                print(f"already clean: {install_path}")
            else:
                install_path.write_bytes(backup_bytes)
                print(f"restored: {install_path}")
                print(f"from:     {backup_path}")
            continue
        if absent_marker_path.is_file():
            if install_path.is_file():
                install_path.unlink()
                print(f"removed:  {install_path}")
            else:
                print(f"already absent: {install_path}")


def build_parser() -> ArgumentParser:
    parser = ArgumentParser(description="Interactive Sneak Out patcher and runtime mod installer.")
    parser.add_argument("game_dir", nargs="?", help="Explicit Sneak Out directory.")
    parser.add_argument("--game-dir", dest="game_dir_option", help="Explicit Sneak Out directory.")
    parser.add_argument("--patches", help="Comma-separated patch ids. Skips the interactive checkbox menu.")
    parser.add_argument("--mods", help="Comma-separated runtime mod ids. Skips the interactive mod checkbox menu.")
    parser.add_argument("--rollback", action="store_true", help="Restore script-managed backups and exit.")
    parser.add_argument("--validate", action="store_true", help="Validate the currently installed files against the selected patch set and exit.")
    parser.add_argument("--list-patches", action="store_true", help="Print patch ids and exit.")
    parser.add_argument("--list-mods", action="store_true", help="Print runtime mod ids and exit.")
    return parser


def print_patch_list() -> None:
    for option in PATCH_OPTIONS:
        print(f"{option.option_id}: {option.label}")
        print(f"  {option.details}")


def print_runtime_mod_list() -> None:
    for option in RUNTIME_MOD_OPTIONS:
        print(f"{option.option_id}: {option.label}")
        print(f"  {option.details}")


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()

    if args.list_patches:
        print_patch_list()
        return 0
    if args.list_mods:
        print_runtime_mod_list()
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

    if args.validate:
        selected_patch_option_ids = (
            parse_patch_selection(args.patches) if args.patches is not None else default_patch_selection()
        )
        selected_runtime_mod_option_ids = (
            parse_runtime_mod_selection(args.mods) if args.mods is not None else default_runtime_mod_selection()
        )
        if selected_patch_option_ids:
            validate_installed_files(game_dir, selected_patch_option_ids)
        if selected_runtime_mod_option_ids:
            validate_installed_runtime_mods(game_dir, selected_runtime_mod_option_ids)
        print("validated")
        return 0

    if args.patches is None and args.mods is None:
        selected_patch_option_ids, selected_runtime_mod_option_ids = choose_installation_plan_interactively()
    else:
        if args.patches is not None:
            selected_patch_option_ids = parse_patch_selection(args.patches)
        else:
            selected_patch_option_ids = choose_patch_options_interactively()

        if args.mods is not None:
            selected_runtime_mod_option_ids = parse_runtime_mod_selection(args.mods)
        else:
            selected_runtime_mod_option_ids = choose_runtime_mod_options_interactively()

    if selected_patch_option_ids:
        prepared_files = prepare_files(game_dir)
        apply_selected_patches(prepared_files, selected_patch_option_ids)
        validate_prepared_files(prepared_files, selected_patch_option_ids)
        write_prepared_files(prepared_files)
        validate_installed_files(game_dir, selected_patch_option_ids)

    if selected_runtime_mod_option_ids:
        install_selected_runtime_mods(game_dir, selected_runtime_mod_option_ids)
        validate_installed_runtime_mods(game_dir, selected_runtime_mod_option_ids)

    if selected_patch_option_ids:
        print("enabled patches:")
        for option_id in selected_patch_option_ids:
            print(f"- {PATCH_OPTION_BY_ID[option_id].label}")
    else:
        print("enabled patches: none")

    if selected_runtime_mod_option_ids:
        print("installed runtime mods:")
        for option_id in selected_runtime_mod_option_ids:
            print(f"- {RUNTIME_MOD_OPTION_BY_ID[option_id].label}")
    else:
        print("installed runtime mods: none")
    print("done")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
