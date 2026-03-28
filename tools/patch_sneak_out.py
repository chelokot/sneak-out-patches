#!/usr/bin/env python3

from __future__ import annotations

import hashlib
import os
import platform
import re
import subprocess
import sys
import termios
import textwrap
import tty
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
RUNTIME_MOD_ARTIFACTS_DIR = REPO_ROOT / "artifacts/runtime_mods"


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


@dataclass(frozen=True)
class RuntimeModOption:
    option_id: str
    label: str
    details: str
    default_enabled: bool
    project_relative_path: str
    assembly_name: str
    config_relative_path: str | None = None
    default_config_text: str | None = None

    @property
    def project_path(self) -> Path:
        return REPO_ROOT / self.project_relative_path

    @property
    def built_dll_path(self) -> Path:
        return self.project_path.parent / "bin/Release/net6.0" / f"{self.assembly_name}.dll"

    @property
    def artifact_dll_path(self) -> Path:
        return RUNTIME_MOD_ARTIFACTS_DIR / f"{self.assembly_name}.dll"


@dataclass
class PreparedFile:
    spec: ManagedFile
    path: Path
    backup_path: Path
    current_bytes: bytes
    working_bytes: bytearray
    backup_created: bool
    restored_from_backup: bool


MANAGED_FILES: tuple[ManagedFile, ...] = ()


PATCH_OPTIONS: tuple[PatchOption, ...] = ()


PATCH_OPTION_BY_ID = {option.option_id: option for option in PATCH_OPTIONS}

RUNTIME_MOD_OPTIONS: tuple[RuntimeModOption, ...] = (
    RuntimeModOption(
        option_id="runtime-profiler",
        label="Install Runtime Profiler runtime mod",
        details="Builds and installs the BepInEx runtime mod that profiles configured game methods and writes a human-readable report on exit.",
        default_enabled=False,
        project_relative_path="mods/runtime_profiler/RuntimeProfiler.csproj",
        assembly_name="SneakOut.RuntimeProfiler",
        config_relative_path="BepInEx/config/chelokot.sneakout.runtime-profiler.cfg",
        default_config_text=(
            "[general]\n"
            "## Settings file was created by version 0.1.0 of Runtime Profiler\n"
            "## Plugin GUID: chelokot.sneakout.runtime-profiler\n"
            "## Plugin Name: Runtime Profiler\n"
            "## Plugin Version: 0.1.0\n\n"
            "## Enable the runtime method profiler.\n"
            "# Setting type: Boolean\n"
            "# Default value: true\n"
            "EnableMod = true\n\n"
            "## Write profiler setup details to the BepInEx log.\n"
            "# Setting type: Boolean\n"
            "# Default value: false\n"
            "EnableLogging = false\n\n"
            "[targeting]\n"
            "## Semicolon-separated assembly names to scan for methods.\n"
            "# Setting type: String\n"
            "# Default value: Assembly-CSharp;Kinguinverse\n"
            "TargetAssemblies = Assembly-CSharp;Kinguinverse\n\n"
            "## Semicolon-separated full type-name prefixes to include.\n"
            "# Setting type: String\n"
            "# Default value: Gameplay.Match.;Networking.PGOS.;UI.Views.\n"
            "IncludeNamespacePrefixes = Gameplay.Match.;Networking.PGOS.;UI.Views.\n\n"
            "## Semicolon-separated full type-name prefixes to exclude.\n"
            "# Setting type: String\n"
            "# Default value: Gameplay.Match.MatchState.;UI.Views.BattlepassView;UI.Views.DailyQuestsView\n"
            "ExcludeNamespacePrefixes = Gameplay.Match.MatchState.;UI.Views.BattlepassView;UI.Views.DailyQuestsView\n\n"
            "## Patch property/event accessor methods.\n"
            "# Setting type: Boolean\n"
            "# Default value: false\n"
            "IncludePropertyAccessors = false\n\n"
            "## Patch constructors.\n"
            "# Setting type: Boolean\n"
            "# Default value: false\n"
            "IncludeConstructors = false\n\n"
            "## Patch compiler-generated methods and closure/state-machine types.\n"
            "# Setting type: Boolean\n"
            "# Default value: false\n"
            "IncludeCompilerGenerated = false\n\n"
            "## Maximum number of methods to patch after filtering.\n"
            "# Setting type: Int32\n"
            "# Default value: 300\n"
            "MaxPatchedMethods = 300\n\n"
            "[report]\n"
            "## Maximum number of methods to write into the report.\n"
            "# Setting type: Int32\n"
            "# Default value: 200\n"
            "TopMethodCount = 200\n\n"
            "## Maximum number of caller->callee edges to write into the report.\n"
            "# Setting type: Int32\n"
            "# Default value: 100\n"
            "TopEdgeCount = 100\n"
        ),
    ),
    RuntimeModOption(
        option_id="core-fixes",
        label="Install Core Fixes runtime mod",
        details="Builds and installs the BepInEx runtime mod that replaces the former GameAssembly byte patches with Harmony fixes.",
        default_enabled=True,
        project_relative_path="mods/core_fixes/CoreFixes.csproj",
        assembly_name="SneakOut.CoreFixes",
        config_relative_path="BepInEx/config/chelokot.sneakout.core-fixes.cfg",
        default_config_text=(
            "[general]\n"
            "## Settings file was created by version 0.1.0 of Core Fixes\n"
            "## Plugin GUID: chelokot.sneakout.core-fixes\n"
            "## Plugin Name: Core Fixes\n"
            "## Plugin Version: 0.1.0\n\n"
            "## Enable runtime replacements for the former GameAssembly byte patches.\n"
            "# Setting type: Boolean\n"
            "# Default value: true\n"
            "EnableMod = true\n\n"
            "## Use JoinLobbyEvent lobby id and region directly when joining from the first accepted invite.\n"
            "# Setting type: Boolean\n"
            "# Default value: true\n"
            "FixPrivatePartyJoinOnFirstInvite = true\n\n"
            "## Use uniform hunter random selection in default mode.\n"
            "# Setting type: Boolean\n"
            "# Default value: true\n"
            "MakeHunterRandomSelectionUniform = true\n\n"
            "## Turn BattlepassView.OnOnWebplayerRefreshEvent into a no-op.\n"
            "# Setting type: Boolean\n"
            "# Default value: true\n"
            "DisableCrashyBattlepassRefreshHandler = true\n\n"
            "## Log runtime replacements for the former GameAssembly byte patches.\n"
            "# Setting type: Boolean\n"
            "# Default value: false\n"
            "EnableLogging = false\n"
        ),
    ),
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
    RuntimeModOption(
        option_id="backend-stabilizer",
        label="Install Backend Stabilizer runtime mod",
        details="Builds and installs the BepInEx runtime mod that applies a local max-profile overlay without touching stock Steam or matchmaking flows.",
        default_enabled=False,
        project_relative_path="mods/backend_stabilizer/BackendStabilizer.csproj",
        assembly_name="SneakOut.BackendStabilizer",
        config_relative_path="BepInEx/config/chelokot.sneakout.backend-stabilizer.cfg",
        default_config_text=(
            "[general]\n"
            "## Settings file was created by version 0.1.0 of Backend Stabilizer\n"
            "## Plugin GUID: chelokot.sneakout.backend-stabilizer\n"
            "## Plugin Name: Backend Stabilizer\n"
            "## Plugin Version: 0.1.0\n\n"
            "## Enable backend stabilizer research logs.\n"
            "# Setting type: Boolean\n"
            "# Default value: false\n"
            "EnableResearchLogging = false\n\n"
            "## Apply a local max-profile overlay after the stock backend bootstrap has completed.\n"
            "# Setting type: Boolean\n"
            "# Default value: true\n"
            "EnableLocalStub = true\n"
        ),
    ),
    RuntimeModOption(
        option_id="start-delay-reducer",
        label="Install Start Delay Reducer runtime mod",
        details="Builds and installs the BepInEx runtime mod that reduces host-side BeforeStart and CountingToStart delays.",
        default_enabled=False,
        project_relative_path="mods/start_delay_reducer/StartDelayReducer.csproj",
        assembly_name="SneakOut.StartDelayReducer",
        config_relative_path="BepInEx/config/chelokot.sneakout.start-delay-reducer.cfg",
        default_config_text=(
            "[general]\n"
            "## Settings file was created by version 0.1.0 of Start Delay Reducer\n"
            "## Plugin GUID: chelokot.sneakout.start-delay-reducer\n"
            "## Plugin Name: Start Delay Reducer\n"
            "## Plugin Version: 0.1.0\n\n"
            "## Reduce host-side start delays during match startup.\n"
            "# Setting type: Boolean\n"
            "# Default value: true\n"
            "EnableMod = true\n\n"
            "## Log tick adjustments for matchmaking startup phases.\n"
            "# Setting type: Boolean\n"
            "# Default value: false\n"
            "EnableLogging = false\n\n"
            "[timings]\n"
            "## Target duration in seconds for the BeforeStart phase.\n"
            "# Setting type: Single\n"
            "# Default value: 10\n"
            "BeforeStartSeconds = 10\n\n"
            "## Target duration in seconds for the CountingToStart phase.\n"
            "# Setting type: Single\n"
            "# Default value: 3\n"
            "CountingToStartSeconds = 3\n"
        ),
    ),
    RuntimeModOption(
        option_id="friend-invite-unlock",
        label="Install Friend Invite Unlock runtime mod",
        details="Builds and installs the BepInEx runtime mod that keeps offline friends inviteable from the lobby list.",
        default_enabled=False,
        project_relative_path="mods/friend_invite_unlock/FriendInviteUnlock.csproj",
        assembly_name="SneakOut.FriendInviteUnlock",
        config_relative_path="BepInEx/config/chelokot.sneakout.friend-invite-unlock.cfg",
        default_config_text=(
            "[general]\n"
            "## Settings file was created by version 0.1.0 of Friend Invite Unlock\n"
            "## Plugin GUID: chelokot.sneakout.friend-invite-unlock\n"
            "## Plugin Name: Friend Invite Unlock\n"
            "## Plugin Version: 0.1.0\n\n"
            "## Allow party invites to stay active for offline friends.\n"
            "# Setting type: Boolean\n"
            "# Default value: true\n"
            "EnableMod = true\n\n"
            "## Only force invite buttons when the local player is the current team leader.\n"
            "# Setting type: Boolean\n"
            "# Default value: true\n"
            "RequireTeamLeader = true\n\n"
            "## Log forced friend invite state transitions.\n"
            "# Setting type: Boolean\n"
            "# Default value: false\n"
            "EnableLogging = false\n"
        ),
    ),
    RuntimeModOption(
        option_id="lobby-penguin-skills",
        label="Install Lobby Penguin Skills runtime mod",
        details="Builds and installs the BepInEx runtime mod that enables the local penguin skill panel and lobby-only use of slide and prop change.",
        default_enabled=False,
        project_relative_path="mods/lobby_penguin_skills/LobbyPenguinSkills.csproj",
        assembly_name="SneakOut.LobbyPenguinSkills",
        config_relative_path="BepInEx/config/chelokot.sneakout.lobby-penguin-skills.cfg",
        default_config_text=(
            "[general]\n"
            "## Settings file was created by version 0.1.0 of Lobby Penguin Skills\n"
            "## Plugin GUID: chelokot.sneakout.lobby-penguin-skills\n"
            "## Plugin Name: Lobby Penguin Skills\n"
            "## Plugin Version: 0.1.0\n\n"
            "## Enable lobby-only penguin skill UI and use hooks.\n"
            "# Setting type: Boolean\n"
            "# Default value: true\n"
            "EnableMod = true\n\n"
            "## Show the in-game penguin skill panel while in the lobby.\n"
            "# Setting type: Boolean\n"
            "# Default value: true\n"
            "EnableLobbySkillUi = true\n\n"
            "## Allow the local penguin to use slide and prop-change while in the lobby.\n"
            "# Setting type: Boolean\n"
            "# Default value: true\n"
            "EnableLobbySkillUse = true\n\n"
            "## Log lobby penguin skill decisions.\n"
            "# Setting type: Boolean\n"
            "# Default value: false\n"
            "EnableLogging = false\n"
        ),
    ),
    RuntimeModOption(
        option_id="free-fly",
        label="Install Free Fly runtime mod",
        details="Builds and installs the BepInEx runtime mod that moves the local player vertically with UpArrow and DownArrow.",
        default_enabled=False,
        project_relative_path="mods/free_fly/FreeFly.csproj",
        assembly_name="SneakOut.FreeFly",
        config_relative_path="BepInEx/config/chelokot.sneakout.free-fly.cfg",
        default_config_text=(
            "[general]\n"
            "## Settings file was created by version 0.1.0 of Free Fly\n"
            "## Plugin GUID: chelokot.sneakout.free-fly\n"
            "## Plugin Name: Free Fly\n"
            "## Plugin Version: 0.1.0\n\n"
            "## Enable local free-fly controls on PageUp and PageDown.\n"
            "# Setting type: Boolean\n"
            "# Default value: true\n"
            "EnableMod = true\n\n"
            "## Log local free-fly movement.\n"
            "# Setting type: Boolean\n"
            "# Default value: false\n"
            "EnableLogging = false\n\n"
            "[movement]\n"
            "## Vertical movement speed in units per second.\n"
            "# Setting type: Single\n"
            "# Default value: 8\n"
            "MovementSpeed = 8\n\n"
            "## Axis to move on. Y is the normal Unity vertical axis.\n"
            "# Setting type: FreeFlyAxis\n"
            "# Default value: Y\n"
            "Axis = Y\n"
        ),
    ),
)

RUNTIME_MOD_OPTION_BY_ID = {option.option_id: option for option in RUNTIME_MOD_OPTIONS}


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
    if not options:
        return ()
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
    if not PATCH_OPTIONS:
        return ()
    return choose_options_interactively(PATCH_OPTIONS, "Patch options (1/2):")


def choose_runtime_mod_options_interactively() -> tuple[str, ...]:
    return choose_options_interactively(RUNTIME_MOD_OPTIONS, "Runtime mod options (2/2):")


def choose_installation_plan_interactively() -> tuple[tuple[str, ...], tuple[str, ...]]:
    return choose_patch_options_interactively(), choose_runtime_mod_options_interactively()


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


def validate_prepared_files(prepared_files: dict[str, PreparedFile], selected_option_ids: tuple[str, ...]) -> None:
    pass


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


def resolve_runtime_mod_install_path(game_dir: Path, runtime_mod: RuntimeModOption) -> Path:
    bepinex_dir = game_dir / "BepInEx"
    if not bepinex_dir.is_dir():
        raise SystemExit(f"Missing BepInEx directory: {bepinex_dir}")
    plugins_dir = bepinex_dir / "plugins"
    plugins_dir.mkdir(parents=True, exist_ok=True)
    return plugins_dir / f"{runtime_mod.assembly_name}.dll"


def resolve_runtime_mod_config_path(game_dir: Path, runtime_mod: RuntimeModOption) -> Path | None:
    if runtime_mod.config_relative_path is None:
        return None
    return game_dir / runtime_mod.config_relative_path


def update_runtime_mod_artifact(runtime_mod: RuntimeModOption, built_dll_path: Path) -> None:
    RUNTIME_MOD_ARTIFACTS_DIR.mkdir(parents=True, exist_ok=True)
    artifact_bytes = built_dll_path.read_bytes()
    if runtime_mod.artifact_dll_path.is_file() and runtime_mod.artifact_dll_path.read_bytes() == artifact_bytes:
        return
    runtime_mod.artifact_dll_path.write_bytes(artifact_bytes)


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
    update_runtime_mod_artifact(runtime_mod, runtime_mod.built_dll_path)
    return runtime_mod.built_dll_path


def resolve_runtime_mod_source_dll(runtime_mod: RuntimeModOption, *, build_runtime_mods: bool) -> Path:
    if build_runtime_mods:
        return build_runtime_mod(runtime_mod)
    if not runtime_mod.artifact_dll_path.is_file():
        raise SystemExit(
            f"Missing runtime mod artifact: {runtime_mod.artifact_dll_path}\n"
            "Build the runtime mod once without --nobuild and commit the generated DLL."
        )
    return runtime_mod.artifact_dll_path


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

    config_path = resolve_runtime_mod_config_path(game_dir, runtime_mod)
    if config_path is None or runtime_mod.default_config_text is None:
        return

    config_absent_marker_path = config_path.with_name(config_path.name + ABSENT_MARKER_SUFFIX)
    if config_path.exists():
        return

    config_path.parent.mkdir(parents=True, exist_ok=True)
    config_path.write_text(runtime_mod.default_config_text, encoding="utf-8")
    if not config_absent_marker_path.exists():
        config_absent_marker_path.write_text("absent\n", encoding="utf-8")
        print(f"created:   {config_absent_marker_path}")
    print(f"created:   {config_path}")

def install_selected_runtime_mods(
    game_dir: Path,
    selected_runtime_mod_option_ids: tuple[str, ...],
    *,
    build_runtime_mods: bool,
) -> None:
    for option_id in selected_runtime_mod_option_ids:
        runtime_mod = RUNTIME_MOD_OPTION_BY_ID[option_id]
        source_dll_path = resolve_runtime_mod_source_dll(runtime_mod, build_runtime_mods=build_runtime_mods)
        install_runtime_mod(game_dir, runtime_mod, source_dll_path)


def validate_installed_runtime_mods(
    game_dir: Path,
    selected_runtime_mod_option_ids: tuple[str, ...],
    *,
    build_runtime_mods: bool,
) -> None:
    for option_id in selected_runtime_mod_option_ids:
        runtime_mod = RUNTIME_MOD_OPTION_BY_ID[option_id]
        built_dll_path = resolve_runtime_mod_source_dll(runtime_mod, build_runtime_mods=build_runtime_mods)
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
        config_path = resolve_runtime_mod_config_path(game_dir, runtime_mod)
        if config_path is None:
            continue
        config_absent_marker_path = config_path.with_name(config_path.name + ABSENT_MARKER_SUFFIX)
        if config_absent_marker_path.is_file():
            if config_path.is_file():
                config_path.unlink()
                print(f"removed:  {config_path}")
            else:
                print(f"already absent: {config_path}")


def build_parser() -> ArgumentParser:
    parser = ArgumentParser(description="Interactive Sneak Out patcher and runtime mod installer.")
    parser.add_argument("game_dir", nargs="?", help="Explicit Sneak Out directory.")
    parser.add_argument("--game-dir", dest="game_dir_option", help="Explicit Sneak Out directory.")
    parser.add_argument("--patches", help="Comma-separated patch ids. Skips the interactive checkbox menu.")
    parser.add_argument("--mods", help="Comma-separated runtime mod ids. Skips the interactive mod checkbox menu.")
    parser.add_argument("--rollback", action="store_true", help="Restore script-managed backups and exit.")
    parser.add_argument("--validate", action="store_true", help="Validate the currently installed files against the selected patch set and exit.")
    parser.add_argument(
        "--nobuild",
        action="store_true",
        help="Use committed runtime mod artifacts instead of building runtime mods locally.",
    )
    parser.add_argument("--list-patches", action="store_true", help="Print patch ids and exit.")
    parser.add_argument("--list-mods", action="store_true", help="Print runtime mod ids and exit.")
    return parser


def print_patch_list() -> None:
    if not PATCH_OPTIONS:
        print("No binary patch options remain. Use runtime mods instead.")
        return
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
            validate_installed_runtime_mods(
                game_dir,
                selected_runtime_mod_option_ids,
                build_runtime_mods=not args.nobuild,
            )
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
        install_selected_runtime_mods(
            game_dir,
            selected_runtime_mod_option_ids,
            build_runtime_mods=not args.nobuild,
        )
        validate_installed_runtime_mods(
            game_dir,
            selected_runtime_mod_option_ids,
            build_runtime_mods=not args.nobuild,
        )

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
