use anyhow::{Context, Result, bail};
use base64::Engine;
use base64::engine::general_purpose::STANDARD as BASE64;
use regex::Regex;
use std::collections::{HashMap, HashSet};
use std::ffi::OsString;
use std::fs;
use std::path::{Path, PathBuf};
use std::sync::OnceLock;

use crate::ProgressReporter;
use crate::io::{
    copy_file_atomic, exists, from_portable_relative, list_files, portable_relative,
    remove_empty_parents, remove_path, sha256_file, write_file_atomic,
};
use crate::model::{
    ExternalFileRecord, FORCED_HIDDEN_RUNTIME_MOD_ID, FileRecord, InstallRequest, InstallState,
    ProgressEvent, RuntimeMod, SupportedBuild,
};
use crate::runtime_mod_version::read_runtime_mod_version;
use crate::steam::{
    STEAM_APP_ID, is_proton_install, read_installed_build_id, steam_active_local_config_paths,
    steam_local_config_paths,
};

const STATE_FILE_NAME: &str = ".sneakout-patches-install.json";
const BACKUP_DIRECTORY_NAME: &str = ".sneakout-patches-backup";
const LEGACY_BACKUP_SUFFIX: &str = ".codex-sneak-out.bak";
const LEGACY_ABSENT_SUFFIX: &str = ".codex-sneak-out.absent";
const PROTON_INPUT_ENVIRONMENT: &str = "XMODIFIERS=@im=none";
const PROTON_LOADER_ENVIRONMENT: &str = r#"WINEDLLOVERRIDES="winhttp=n,b""#;
const LOADER_ROOT_NAMES: &[&str] = &[
    "BepInEx",
    "dotnet",
    ".doorstop_version",
    "doorstop_config.ini",
    "winhttp.dll",
    "changelog.txt",
];

#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct RuntimeModUpdateSummary {
    pub installed_ids: Vec<String>,
    pub updated_ids: Vec<String>,
    pub local_newer_ids: Vec<String>,
    pub legacy_ids: Vec<String>,
    pub unreadable_ids: Vec<String>,
}

fn state_path(game_directory: &Path) -> PathBuf {
    game_directory.join(STATE_FILE_NAME)
}

fn backup_root(game_directory: &Path) -> PathBuf {
    game_directory.join(BACKUP_DIRECTORY_NAME)
}

fn with_suffix(path: &Path, suffix: &str) -> PathBuf {
    let mut value: OsString = path.as_os_str().to_owned();
    value.push(suffix);
    PathBuf::from(value)
}

fn load_state(game_directory: &Path) -> Result<InstallState> {
    let path = state_path(game_directory);
    let bytes = match fs::read(&path) {
        Ok(bytes) => bytes,
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => {
            return Ok(InstallState::default());
        }
        Err(error) => return Err(error.into()),
    };
    let state: InstallState = serde_json::from_slice(&bytes)
        .with_context(|| format!("invalid installer state at {}", path.display()))?;
    if state.schema != 1 {
        bail!(
            "invalid installer state at {}: unsupported schema",
            path.display()
        );
    }
    Ok(state)
}

fn save_state(game_directory: &Path, state: &InstallState) -> Result<()> {
    let mut encoded = serde_json::to_vec_pretty(state)?;
    encoded.push(b'\n');
    write_file_atomic(state_path(game_directory), encoded)
}

enum Original {
    Absent,
    File(PathBuf),
}

fn legacy_original(game_directory: &Path, destination: &Path) -> Option<Original> {
    if exists(with_suffix(destination, LEGACY_ABSENT_SUFFIX)) {
        return Some(Original::Absent);
    }
    for root_name in LOADER_ROOT_NAMES {
        let root = game_directory.join(root_name);
        if (destination == root || destination.starts_with(&root))
            && exists(with_suffix(&root, LEGACY_ABSENT_SUFFIX))
        {
            return Some(Original::Absent);
        }
    }
    let backup = with_suffix(destination, LEGACY_BACKUP_SUFFIX);
    if exists(&backup) {
        return Some(Original::File(backup));
    }
    None
}

fn ensure_tracked_original(
    game_directory: &Path,
    state: &mut InstallState,
    destination: &Path,
) -> Result<usize> {
    let path = portable_relative(game_directory, destination)?;
    if let Some(index) = state.files.iter().position(|record| record.path == path) {
        return Ok(index);
    }

    let legacy = legacy_original(game_directory, destination);
    let record = match legacy {
        Some(Original::Absent) => FileRecord {
            path,
            original: "absent".to_owned(),
            backup: None,
            installed_sha256: None,
        },
        legacy => {
            if legacy.is_none() && !destination.exists() {
                FileRecord {
                    path,
                    original: "absent".to_owned(),
                    backup: None,
                    installed_sha256: None,
                }
            } else {
                let source = match legacy {
                    Some(Original::File(path)) => path,
                    _ => destination.to_path_buf(),
                };
                let backup = format!("files/{path}");
                copy_file_atomic(&source, backup_root(game_directory).join(&backup))?;
                FileRecord {
                    path,
                    original: "backup".to_owned(),
                    backup: Some(backup),
                    installed_sha256: None,
                }
            }
        }
    };
    state.files.push(record);
    save_state(game_directory, state)?;
    Ok(state.files.len() - 1)
}

fn install_file(
    game_directory: &Path,
    state: &mut InstallState,
    source: &Path,
    destination: &Path,
) -> Result<()> {
    let index = ensure_tracked_original(game_directory, state, destination)?;
    copy_file_atomic(source, destination)?;
    state.files[index].installed_sha256 = Some(sha256_file(destination)?);
    Ok(())
}

fn install_bytes(
    game_directory: &Path,
    state: &mut InstallState,
    bytes: &[u8],
    destination: &Path,
) -> Result<()> {
    use sha2::{Digest, Sha256};

    let index = ensure_tracked_original(game_directory, state, destination)?;
    write_file_atomic(destination, bytes)?;
    state.files[index].installed_sha256 = Some(hex::encode(Sha256::digest(bytes)));
    Ok(())
}

fn restore_record(
    game_directory: &Path,
    record: &FileRecord,
    reporter: ProgressReporter<'_>,
) -> Result<()> {
    let destination = from_portable_relative(game_directory, &record.path)?;
    if record.original == "backup" {
        let backup = record
            .backup
            .as_deref()
            .context("backup record has no backup path")?;
        let backup = from_portable_relative(&backup_root(game_directory), backup)?;
        copy_file_atomic(backup, &destination)?;
        reporter(ProgressEvent::Message(format!(
            "Restored {}",
            destination.display()
        )));
    } else {
        remove_path(&destination)?;
        remove_empty_parents(&destination, game_directory);
        reporter(ProgressEvent::Message(format!(
            "Removed {}",
            destination.display()
        )));
    }
    Ok(())
}

pub fn compatibility_issues(
    game_directory: &Path,
    supported_build: &SupportedBuild,
) -> Result<Vec<String>> {
    let mut issues = Vec::new();
    if let Some(installed_build_id) = read_installed_build_id(game_directory)
        && installed_build_id != supported_build.steam_build_id
    {
        issues.push(format!(
            "Steam build {installed_build_id}, supported build {}",
            supported_build.steam_build_id
        ));
    }
    let fingerprints = [
        (
            game_directory.join("GameAssembly.dll"),
            &supported_build.game_assembly_sha256,
        ),
        (
            game_directory.join("Sneak Out_Data/il2cpp_data/Metadata/global-metadata.dat"),
            &supported_build.global_metadata_sha256,
        ),
    ];
    for (path, expected) in fingerprints {
        if !path.exists() {
            issues.push(format!("missing {}", path.display()));
            continue;
        }
        let actual = sha256_file(&path)?;
        if actual != *expected {
            issues.push(format!(
                "{} has unsupported SHA-256 {actual}",
                path.display()
            ));
        }
    }
    Ok(issues)
}

fn game_mode_available(game_directory: &Path) -> bool {
    let flatpak_fragment = Path::new(".var/app/com.valvesoftware.Steam");
    game_directory
        .ancestors()
        .any(|ancestor| ancestor.ends_with(flatpak_fragment))
        || exists("/usr/bin/gamemoderun")
}

fn default_proton_launch_options(use_game_mode: bool) -> String {
    format!(
        "{PROTON_INPUT_ENVIRONMENT} {PROTON_LOADER_ENVIRONMENT} {}%command%",
        if use_game_mode { "gamemoderun " } else { "" }
    )
}

fn merge_proton_launch_options(current: &str, use_game_mode: bool) -> String {
    static XMODIFIERS_PATTERN: OnceLock<Regex> = OnceLock::new();
    static GAMEMODE_PATTERN: OnceLock<Regex> = OnceLock::new();

    let mut updated = current.trim().to_owned();
    if !(updated.contains("WINEDLLOVERRIDES") && updated.contains("winhttp")) {
        updated = if updated.contains("%command%") {
            format!("{PROTON_LOADER_ENVIRONMENT} {updated}")
        } else {
            format!("{PROTON_LOADER_ENVIRONMENT} %command% {updated}")
                .trim()
                .to_owned()
        };
    }
    if !XMODIFIERS_PATTERN
        .get_or_init(|| Regex::new(r"(?:^|\s)XMODIFIERS=").unwrap())
        .is_match(&updated)
    {
        updated = format!("{PROTON_INPUT_ENVIRONMENT} {updated}");
    }
    if use_game_mode
        && !GAMEMODE_PATTERN
            .get_or_init(|| Regex::new(r"(?:^|\s)gamemoderun(?:\s|$)").unwrap())
            .is_match(&updated)
    {
        updated = if updated.contains("%command%") {
            updated.replacen("%command%", "gamemoderun %command%", 1)
        } else {
            format!("{updated} gamemoderun %command%").trim().to_owned()
        };
    }
    updated
}

fn escape_vdf_string(value: &str) -> String {
    value.replace('\\', r"\\").replace('"', r#"\""#)
}

fn unescape_vdf_string(value: &str) -> String {
    value.replace(r#"\""#, "\"").replace(r"\\", r"\")
}

fn vdf_patterns() -> (&'static Regex, &'static Regex) {
    static KEY_VALUE: OnceLock<Regex> = OnceLock::new();
    static KEY_ONLY: OnceLock<Regex> = OnceLock::new();
    (
        KEY_VALUE.get_or_init(|| Regex::new(r#"^"([^"]+)"\s+"((?:\\.|[^"])*)""#).unwrap()),
        KEY_ONLY.get_or_init(|| Regex::new(r#"^"([^"]+)"\s*$"#).unwrap()),
    )
}

fn app_launch_options(content: &str) -> Option<String> {
    let (key_value_pattern, key_only_pattern) = vdf_patterns();
    let mut stack: Vec<String> = Vec::new();
    let mut pending_key: Option<String> = None;
    for line in content.split('\n') {
        let trimmed = line.trim();
        if let Some(capture) = key_value_pattern.captures(trimmed) {
            if stack.join("/")
                == format!("UserLocalConfigStore/Software/Valve/Steam/apps/{STEAM_APP_ID}")
                && capture.get(1).map(|value| value.as_str()) == Some("LaunchOptions")
            {
                return capture
                    .get(2)
                    .map(|value| unescape_vdf_string(value.as_str()));
            }
            pending_key = None;
            continue;
        }
        if let Some(capture) = key_only_pattern.captures(trimmed) {
            pending_key = capture.get(1).map(|value| value.as_str().to_owned());
        } else if trimmed == "{" {
            if let Some(key) = pending_key.take() {
                stack.push(key);
            }
        } else if trimmed == "}" {
            stack.pop();
            pending_key = None;
        }
    }
    None
}

fn has_required_proton_launch_options(content: &str) -> bool {
    app_launch_options(content).is_some_and(|value| {
        value.contains("WINEDLLOVERRIDES")
            && value.contains("winhttp")
            && value.contains(PROTON_INPUT_ENVIRONMENT)
    })
}

fn relevant_local_config_paths() -> Vec<PathBuf> {
    let active = steam_active_local_config_paths();
    if active.is_empty() {
        steam_local_config_paths()
    } else {
        active
    }
}

pub fn proton_launch_configuration_required() -> bool {
    if !is_proton_install() {
        return false;
    }
    let paths = relevant_local_config_paths();
    paths.is_empty()
        || paths.iter().any(|path| {
            fs::read_to_string(path)
                .map(|content| !has_required_proton_launch_options(&content))
                .unwrap_or(true)
        })
}

fn update_launch_options(content: &str, use_game_mode: bool) -> String {
    static LAUNCH_OPTIONS_PATTERN: OnceLock<Regex> = OnceLock::new();
    static INDENT_PATTERN: OnceLock<Regex> = OnceLock::new();
    let (key_value_pattern, key_only_pattern) = vdf_patterns();
    let mut lines: Vec<String> = content
        .split('\n')
        .map(|line| line.strip_suffix('\r').unwrap_or(line).to_owned())
        .collect();
    let mut stack: Vec<String> = Vec::new();
    let mut pending_key: Option<String> = None;
    let mut app_closing_index = None;
    let mut apps_closing_index = None;
    let mut launch_options_index = None;

    for (index, line) in lines.iter().enumerate() {
        let trimmed = line.trim();
        if let Some(capture) = key_value_pattern.captures(trimmed) {
            if stack.join("/")
                == format!("UserLocalConfigStore/Software/Valve/Steam/apps/{STEAM_APP_ID}")
                && capture.get(1).map(|value| value.as_str()) == Some("LaunchOptions")
            {
                launch_options_index = Some(index);
            }
            pending_key = None;
            continue;
        }
        if let Some(capture) = key_only_pattern.captures(trimmed) {
            pending_key = capture.get(1).map(|value| value.as_str().to_owned());
            continue;
        }
        if trimmed == "{" {
            if let Some(key) = pending_key.take() {
                stack.push(key);
            }
            continue;
        }
        if trimmed == "}" {
            let current = stack.join("/");
            if current == format!("UserLocalConfigStore/Software/Valve/Steam/apps/{STEAM_APP_ID}") {
                app_closing_index = Some(index);
            } else if current == "UserLocalConfigStore/Software/Valve/Steam/apps" {
                apps_closing_index = Some(index);
            }
            stack.pop();
            pending_key = None;
        }
    }

    let launch_pattern = LAUNCH_OPTIONS_PATTERN
        .get_or_init(|| Regex::new(r#"^(\s*)"LaunchOptions"\s+"((?:\\.|[^"])*)""#).unwrap());
    let indent_pattern = INDENT_PATTERN.get_or_init(|| Regex::new(r"^\s*").unwrap());
    if let Some(index) = launch_options_index {
        let Some(capture) = launch_pattern.captures(&lines[index]) else {
            return content.to_owned();
        };
        let indent = capture.get(1).map(|value| value.as_str()).unwrap_or("");
        let current = capture.get(2).map(|value| value.as_str()).unwrap_or("");
        let updated = escape_vdf_string(&merge_proton_launch_options(
            &unescape_vdf_string(current),
            use_game_mode,
        ));
        lines[index] = format!("{indent}\"LaunchOptions\"\t\t\"{updated}\"");
    } else if let Some(index) = app_closing_index {
        let indent = indent_pattern
            .find(&lines[index])
            .map(|value| value.as_str())
            .unwrap_or("");
        lines.insert(
            index,
            format!(
                "{indent}\t\"LaunchOptions\"\t\t\"{}\"",
                escape_vdf_string(&default_proton_launch_options(use_game_mode))
            ),
        );
    } else if let Some(index) = apps_closing_index {
        let indent = indent_pattern
            .find(&lines[index])
            .map(|value| value.as_str())
            .unwrap_or("");
        let replacement = [
            format!("{indent}\"{STEAM_APP_ID}\""),
            format!("{indent}{{"),
            format!(
                "{indent}\t\"LaunchOptions\"\t\t\"{}\"",
                escape_vdf_string(&default_proton_launch_options(use_game_mode))
            ),
            format!("{indent}}}"),
        ];
        lines.splice(index..index, replacement);
    } else {
        return content.to_owned();
    }
    format!("{}\n", lines.join("\n").trim_end_matches('\n'))
}

fn configure_proton(
    game_directory: &Path,
    state: &mut InstallState,
    reporter: ProgressReporter<'_>,
) -> Result<()> {
    let use_game_mode = game_mode_available(game_directory);
    let paths = relevant_local_config_paths();
    if paths.is_empty() {
        bail!("Steam localconfig.vdf was not found; cannot activate the Proton BepInEx loader.");
    }
    for path in paths {
        let original = fs::read(&path)?;
        let original_text = String::from_utf8_lossy(&original);
        let updated = update_launch_options(&original_text, use_game_mode);
        if updated.as_bytes() == original.as_slice() {
            continue;
        }
        if !state.external_files.iter().any(|entry| entry.path == path) {
            state.external_files.push(ExternalFileRecord {
                path: path.clone(),
                original_base64: BASE64.encode(&original),
            });
            save_state(game_directory, state)?;
        }
        write_file_atomic(&path, updated)?;
        reporter(ProgressEvent::Message(format!(
            "Updated Proton launch options in {}",
            path.display()
        )));
    }
    Ok(())
}

fn update_ini_setting(content: &str, section: &str, key: &str, value: &str) -> String {
    static SECTION_PATTERN: OnceLock<Regex> = OnceLock::new();
    let mut lines: Vec<String> = content
        .split('\n')
        .map(|line| line.strip_suffix('\r').unwrap_or(line).to_owned())
        .collect();
    let section_header = format!("[{section}]");
    let Some(section_start) = lines.iter().position(|line| line.trim() == section_header) else {
        if lines.last().is_some_and(|line| !line.is_empty()) {
            lines.push(String::new());
        }
        lines.push(section_header);
        lines.push(String::new());
        lines.push(format!("{key} = {value}"));
        return format!("{}\n", lines.join("\n").trim_end_matches('\n'));
    };
    let any_section = SECTION_PATTERN.get_or_init(|| Regex::new(r"^\s*\[.+\]\s*$").unwrap());
    let section_end = lines
        .iter()
        .enumerate()
        .skip(section_start + 1)
        .find(|(_, line)| any_section.is_match(line))
        .map(|(index, _)| index)
        .unwrap_or(lines.len());
    let setting = Regex::new(&format!(r"^\s*{}\s*=", regex::escape(key))).unwrap();
    if let Some(setting_index) =
        (section_start + 1..section_end).find(|index| setting.is_match(&lines[*index]))
    {
        lines[setting_index] = format!("{key} = {value}");
    } else {
        lines.splice(
            section_start + 1..section_start + 1,
            [String::new(), format!("{key} = {value}")],
        );
    }
    format!("{}\n", lines.join("\n").trim_end_matches('\n'))
}

fn configure_bepinex_for_proton(game_directory: &Path, state: &mut InstallState) -> Result<()> {
    let path = game_directory.join("BepInEx/config/BepInEx.cfg");
    let original = fs::read_to_string(&path).unwrap_or_default();
    let updated = update_ini_setting(&original, "Logging.Console", "Enabled", "false");
    let updated = update_ini_setting(
        &updated,
        "IL2CPP",
        "PreloadIL2CPPInteropAssemblies",
        "false",
    );
    if updated != original {
        install_bytes(game_directory, state, updated.as_bytes(), &path)?;
    }
    Ok(())
}

fn restore_deselected_mods(
    game_directory: &Path,
    state: &mut InstallState,
    manifest: &[RuntimeMod],
    selected_ids: &[String],
    reporter: ProgressReporter<'_>,
) -> Result<()> {
    let selected: HashSet<&str> = selected_ids.iter().map(String::as_str).collect();
    let mut desired = HashSet::new();
    let mut managed = HashSet::new();
    for runtime_mod in manifest {
        let assembly = format!("BepInEx/plugins/{}.dll", runtime_mod.assembly_name);
        managed.insert(assembly.clone());
        if selected.contains(runtime_mod.option_id.as_str()) {
            desired.insert(assembly);
        }
        if let Some(config) = &runtime_mod.config_relative_path {
            let config = config.replace('\\', "/");
            managed.insert(config.clone());
            if selected.contains(runtime_mod.option_id.as_str()) {
                desired.insert(config);
            }
        }
    }
    let removed: HashSet<String> = state
        .files
        .iter()
        .filter(|record| managed.contains(&record.path) && !desired.contains(&record.path))
        .map(|record| record.path.clone())
        .collect();
    for record in state
        .files
        .iter()
        .filter(|record| removed.contains(&record.path))
    {
        restore_record(game_directory, record, reporter)?;
    }
    if !removed.is_empty() {
        state.files.retain(|record| !removed.contains(&record.path));
        save_state(game_directory, state)?;
    }
    Ok(())
}

fn runtime_mod_assembly_path(game_directory: &Path, runtime_mod: &RuntimeMod) -> PathBuf {
    game_directory
        .join("BepInEx/plugins")
        .join(format!("{}.dll", runtime_mod.assembly_name))
}

pub fn installed_runtime_mod_ids(game_directory: &Path, catalog: &[RuntimeMod]) -> Vec<String> {
    catalog
        .iter()
        .filter(|runtime_mod| runtime_mod_assembly_path(game_directory, runtime_mod).exists())
        .map(|runtime_mod| runtime_mod.option_id.clone())
        .collect()
}

pub fn update_installed_runtime_mods(
    game_directory: &Path,
    payload_root: &Path,
    catalog: &[RuntimeMod],
    latest_manifest: &[RuntimeMod],
    reporter: ProgressReporter<'_>,
) -> Result<RuntimeModUpdateSummary> {
    let latest_by_id: HashMap<&str, &RuntimeMod> = latest_manifest
        .iter()
        .map(|runtime_mod| (runtime_mod.option_id.as_str(), runtime_mod))
        .collect();
    let mut state = load_state(game_directory)?;
    let mut state_changed = false;
    let mut summary = RuntimeModUpdateSummary::default();

    for runtime_mod in catalog {
        let installed = runtime_mod_assembly_path(game_directory, runtime_mod);
        if !installed.exists() {
            continue;
        }
        summary.installed_ids.push(runtime_mod.option_id.clone());

        let Some(latest) = latest_by_id.get(runtime_mod.option_id.as_str()) else {
            summary.legacy_ids.push(runtime_mod.option_id.clone());
            continue;
        };
        let available = payload_root
            .join("artifacts/runtime_mods")
            .join(format!("{}.dll", latest.assembly_name));
        if !available.exists() {
            bail!("latest release is missing {}.dll", latest.assembly_name);
        }

        let installed_version = match read_runtime_mod_version(&installed) {
            Ok(version) => version,
            Err(error) => {
                if runtime_mod.option_id != FORCED_HIDDEN_RUNTIME_MOD_ID {
                    reporter(ProgressEvent::Message(format!(
                        "Could not compare {}: {error}",
                        runtime_mod.label
                    )));
                }
                summary.unreadable_ids.push(runtime_mod.option_id.clone());
                continue;
            }
        };
        let available_version = read_runtime_mod_version(&available)?;
        if installed_version > available_version {
            summary.local_newer_ids.push(runtime_mod.option_id.clone());
            if runtime_mod.option_id != FORCED_HIDDEN_RUNTIME_MOD_ID {
                reporter(ProgressEvent::Message(format!(
                    "Keeping local {} {} (latest release is {})",
                    runtime_mod.label, installed_version, available_version
                )));
            }
            continue;
        }
        if installed_version == available_version {
            continue;
        }

        if runtime_mod.option_id != FORCED_HIDDEN_RUNTIME_MOD_ID {
            reporter(ProgressEvent::Message(format!(
                "Updating {} from {} to {}...",
                runtime_mod.label, installed_version, available_version
            )));
        }
        install_file(game_directory, &mut state, &available, &installed)?;
        summary.updated_ids.push(runtime_mod.option_id.clone());
        state_changed = true;
    }

    if state_changed {
        save_state(game_directory, &state)?;
    }
    Ok(summary)
}

pub fn reconcile_runtime_mod_selection(
    game_directory: &Path,
    manifest: &[RuntimeMod],
    selected_ids: &[String],
    reporter: ProgressReporter<'_>,
) -> Result<()> {
    let selected: HashSet<&str> = selected_ids.iter().map(String::as_str).collect();
    for runtime_mod in manifest
        .iter()
        .filter(|runtime_mod| !selected.contains(runtime_mod.option_id.as_str()))
    {
        let assembly = runtime_mod_assembly_path(game_directory, runtime_mod);
        if !assembly.exists() {
            continue;
        }

        remove_path(&assembly)?;
        if runtime_mod.option_id != FORCED_HIDDEN_RUNTIME_MOD_ID {
            reporter(ProgressEvent::Message(format!(
                "Removed unselected {}",
                runtime_mod.label
            )));
        }
    }
    Ok(())
}

pub fn install(request: &InstallRequest, reporter: ProgressReporter<'_>) -> Result<InstallState> {
    if !request
        .manifest
        .iter()
        .any(|runtime_mod| runtime_mod.option_id == FORCED_HIDDEN_RUNTIME_MOD_ID)
    {
        bail!("required embedded runtime mod is missing from the catalog");
    }
    let mut selected_ids = request.selected_ids.clone();
    if !selected_ids
        .iter()
        .any(|id| id == FORCED_HIDDEN_RUNTIME_MOD_ID)
    {
        selected_ids.push(FORCED_HIDDEN_RUNTIME_MOD_ID.to_owned());
    }
    let mut state = load_state(&request.game_directory)?;
    restore_deselected_mods(
        &request.game_directory,
        &mut state,
        &request.manifest,
        &selected_ids,
        reporter,
    )?;

    for source in list_files(&request.bepinex_root)? {
        let relative = source.strip_prefix(&request.bepinex_root)?;
        let destination = request.game_directory.join(relative);
        install_file(&request.game_directory, &mut state, &source, &destination)?;
    }

    let selected: HashSet<&str> = selected_ids.iter().map(String::as_str).collect();
    let preserved: HashSet<&str> = request.preserve_ids.iter().map(String::as_str).collect();
    let fallback: HashSet<&str> = request.fallback_ids.iter().map(String::as_str).collect();
    for runtime_mod in request
        .manifest
        .iter()
        .filter(|runtime_mod| selected.contains(runtime_mod.option_id.as_str()))
    {
        let destination = runtime_mod_assembly_path(&request.game_directory, runtime_mod);
        if preserved.contains(runtime_mod.option_id.as_str()) {
            if !destination.exists() {
                bail!(
                    "cannot preserve missing local runtime mod {}",
                    runtime_mod.label
                );
            }
            if runtime_mod.option_id != FORCED_HIDDEN_RUNTIME_MOD_ID {
                reporter(ProgressEvent::Message(format!(
                    "Keeping installed local {}",
                    runtime_mod.label
                )));
            }
        } else {
            let source_root = if fallback.contains(runtime_mod.option_id.as_str()) {
                request
                    .fallback_payload_root
                    .as_ref()
                    .context("a fallback mod was selected without a fallback payload")?
            } else {
                &request.payload_root
            };
            let source = source_root
                .join("artifacts/runtime_mods")
                .join(format!("{}.dll", runtime_mod.assembly_name));
            if !source.exists() {
                bail!("release is missing {}.dll", runtime_mod.assembly_name);
            }
            if runtime_mod.option_id != FORCED_HIDDEN_RUNTIME_MOD_ID {
                reporter(ProgressEvent::Message(format!(
                    "Installing {}...",
                    runtime_mod.label
                )));
            }
            install_file(&request.game_directory, &mut state, &source, &destination)?;
        }
        if let (Some(config), Some(template)) = (
            &runtime_mod.config_relative_path,
            &runtime_mod.default_config_template_path,
        ) {
            let destination = config
                .split('/')
                .fold(request.game_directory.clone(), |path, component| {
                    path.join(component)
                });
            if !destination.exists() {
                let source_root = if fallback.contains(runtime_mod.option_id.as_str()) {
                    request
                        .fallback_payload_root
                        .as_ref()
                        .context("a fallback mod was selected without a fallback payload")?
                } else {
                    &request.payload_root
                };
                let source = template
                    .split('/')
                    .fold(source_root.clone(), |path, component| path.join(component));
                install_file(&request.game_directory, &mut state, &source, &destination)?;
            }
        }
    }

    // The manifest selection is authoritative. A runtime-mod DLL can predate the current
    // installer state (for example, after a legacy uninstall restored it), so state-based
    // restoration alone cannot guarantee that an unselected plugin is inactive.
    reconcile_runtime_mod_selection(
        &request.game_directory,
        &request.manifest,
        &selected_ids,
        reporter,
    )?;

    if is_proton_install() {
        configure_proton(&request.game_directory, &mut state, reporter)?;
        configure_bepinex_for_proton(&request.game_directory, &mut state)?;
    }
    state.selected_mods = selected_ids;
    save_state(&request.game_directory, &state)?;
    Ok(state)
}

fn rollback_legacy_install(game_directory: &Path, manifest: &[RuntimeMod]) -> Result<()> {
    for runtime_mod in manifest {
        let mut paths = vec![
            game_directory
                .join("BepInEx/plugins")
                .join(format!("{}.dll", runtime_mod.assembly_name)),
        ];
        if let Some(config) = &runtime_mod.config_relative_path {
            paths.push(
                config
                    .split('/')
                    .fold(game_directory.to_path_buf(), |path, component| {
                        path.join(component)
                    }),
            );
        }
        for path in paths {
            let backup = with_suffix(&path, LEGACY_BACKUP_SUFFIX);
            let absent = with_suffix(&path, LEGACY_ABSENT_SUFFIX);
            if backup.exists() {
                copy_file_atomic(&backup, &path)?;
            } else if absent.exists() {
                remove_path(&path)?;
            }
            remove_path(&backup)?;
            remove_path(&absent)?;
        }
    }
    for name in LOADER_ROOT_NAMES.iter().rev() {
        let path = game_directory.join(name);
        let backup = with_suffix(&path, LEGACY_BACKUP_SUFFIX);
        let absent = with_suffix(&path, LEGACY_ABSENT_SUFFIX);
        if backup.exists() {
            remove_path(&path)?;
            copy_file_atomic(&backup, &path)?;
        } else if absent.exists() {
            remove_path(&path)?;
        }
        remove_path(&backup)?;
        remove_path(&absent)?;
    }
    for path in steam_local_config_paths() {
        let backup = with_suffix(&path, LEGACY_BACKUP_SUFFIX);
        if backup.exists() {
            copy_file_atomic(&backup, &path)?;
            remove_path(&backup)?;
        }
    }
    Ok(())
}

pub fn uninstall(
    game_directory: &Path,
    manifest: &[RuntimeMod],
    reporter: ProgressReporter<'_>,
) -> Result<()> {
    let state = load_state(game_directory)?;
    let mut files = state.files.clone();
    files.sort_by(|left, right| right.path.len().cmp(&left.path.len()));
    for record in &files {
        restore_record(game_directory, record, reporter)?;
    }
    for external in &state.external_files {
        write_file_atomic(&external.path, BASE64.decode(&external.original_base64)?)?;
        reporter(ProgressEvent::Message(format!(
            "Restored {}",
            external.path.display()
        )));
    }
    rollback_legacy_install(game_directory, manifest)?;
    reconcile_runtime_mod_selection(game_directory, manifest, &[], reporter)?;
    remove_path(&state_path(game_directory))?;
    remove_path(&backup_root(game_directory))?;
    Ok(())
}

pub fn validate_installed(
    game_directory: &Path,
    manifest: &[RuntimeMod],
    selected_ids: &[String],
    payload_root: &Path,
) -> Result<Vec<String>> {
    let mut problems = Vec::new();
    for relative in [
        "winhttp.dll",
        "doorstop_config.ini",
        "BepInEx/core/BepInEx.Unity.IL2CPP.dll",
    ] {
        let installed = game_directory.join(relative);
        if !installed.exists() {
            problems.push(format!("missing loader file {}", installed.display()));
        }
    }
    let selected: HashSet<&str> = selected_ids.iter().map(String::as_str).collect();
    for runtime_mod in manifest {
        let expected = payload_root
            .join("artifacts/runtime_mods")
            .join(format!("{}.dll", runtime_mod.assembly_name));
        let installed = game_directory
            .join("BepInEx/plugins")
            .join(format!("{}.dll", runtime_mod.assembly_name));
        if !selected.contains(runtime_mod.option_id.as_str()) {
            if installed.exists() {
                problems.push(format!(
                    "unselected runtime mod is installed {}",
                    installed.display()
                ));
            }
        } else if !installed.exists() {
            problems.push(format!("missing {}", installed.display()));
        } else if sha256_file(&expected)? != sha256_file(&installed)? {
            let local_is_not_older = read_runtime_mod_version(&installed)
                .and_then(|installed_version| {
                    read_runtime_mod_version(&expected)
                        .map(|expected_version| installed_version >= expected_version)
                })
                .unwrap_or(false);
            if !local_is_not_older {
                problems.push(format!("hash mismatch {}", installed.display()));
            }
        }
    }
    if is_proton_install() {
        let paths = relevant_local_config_paths();
        if paths.is_empty() {
            problems.push(
                "Steam localconfig.vdf was not found; Proton loader override is inactive"
                    .to_owned(),
            );
        }
        for path in paths {
            let healthy = fs::read_to_string(&path)
                .map(|content| has_required_proton_launch_options(&content))
                .unwrap_or(false);
            if !healthy {
                problems.push(format!(
                    "Proton loader/input environment is inactive in {}",
                    path.display()
                ));
            }
        }
    }
    Ok(problems)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn merges_existing_launch_options_without_duplication() {
        let current = "mangohud %command% --flag";
        let updated = merge_proton_launch_options(current, true);
        assert_eq!(
            updated,
            r#"XMODIFIERS=@im=none WINEDLLOVERRIDES="winhttp=n,b" mangohud gamemoderun %command% --flag"#
        );
        assert_eq!(merge_proton_launch_options(&updated, true), updated);
    }

    #[test]
    fn updates_only_the_target_app_launch_options() {
        let input = concat!(
            "\"UserLocalConfigStore\"\n{\n",
            "\t\"Software\"\n\t{\n\t\t\"Valve\"\n\t\t{\n",
            "\t\t\t\"Steam\"\n\t\t\t{\n\t\t\t\t\"apps\"\n\t\t\t\t{\n",
            "\t\t\t\t\t\"2410490\"\n\t\t\t\t\t{\n",
            "\t\t\t\t\t\t\"LaunchOptions\" \"mangohud %command%\"\n",
            "\t\t\t\t\t}\n\t\t\t\t}\n\t\t\t}\n\t\t}\n\t}\n}\n"
        );
        let updated = update_launch_options(input, false);
        assert!(has_required_proton_launch_options(&updated));
        assert!(updated.contains("mangohud %command%"));
    }

    #[test]
    fn repairs_legacy_unescaped_launch_options() {
        let input = concat!(
            "\"UserLocalConfigStore\"\n{\n\t\"Software\"\n\t{\n",
            "\t\t\"Valve\"\n\t\t{\n\t\t\t\"Steam\"\n\t\t\t{\n",
            "\t\t\t\t\"apps\"\n\t\t\t\t{\n\t\t\t\t\t\"2410490\"\n",
            "\t\t\t\t\t{\n\t\t\t\t\t\t\"LaunchOptions\" \"WINEDLLOVERRIDES=\"winhttp=n,b\" %command%\"\n",
            "\t\t\t\t\t}\n\t\t\t\t}\n\t\t\t}\n\t\t}\n\t}\n}\n"
        );
        let updated = update_launch_options(input, false);
        assert!(updated.contains(r#"WINEDLLOVERRIDES=\"winhttp=n,b\""#));
        assert!(updated.contains(PROTON_INPUT_ENVIRONMENT));
    }
}
