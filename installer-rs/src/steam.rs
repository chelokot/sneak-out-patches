use anyhow::{Context, Result, bail};
use directories::BaseDirs;
use path_absolutize::Absolutize;
use regex::Regex;
use std::collections::HashSet;
use std::env;
use std::fs;
use std::path::{Path, PathBuf};
use std::sync::OnceLock;

use crate::io::exists;

pub const STEAM_APP_ID: &str = "2410490";
pub const GAME_DIRECTORY_NAME: &str = "Sneak Out";

fn unique(paths: Vec<PathBuf>) -> Vec<PathBuf> {
    let mut seen = HashSet::new();
    paths
        .into_iter()
        .map(|path| {
            path.absolutize()
                .map(|path| path.into_owned())
                .unwrap_or(path)
        })
        .filter(|path| seen.insert(path.clone()))
        .collect()
}

#[cfg(windows)]
fn windows_steam_path() -> Option<PathBuf> {
    use winreg::RegKey;
    use winreg::enums::HKEY_CURRENT_USER;

    let key = RegKey::predef(HKEY_CURRENT_USER)
        .open_subkey("Software\\Valve\\Steam")
        .ok()?;
    let value: String = key.get_value("SteamPath").ok()?;
    Some(PathBuf::from(value))
}

pub fn candidate_steam_roots() -> Vec<PathBuf> {
    if let Some(value) = env::var_os("SNEAKOUT_STEAM_ROOTS") {
        return unique(env::split_paths(&value).collect());
    }
    let home = BaseDirs::new().map(|dirs| dirs.home_dir().to_path_buf());
    #[cfg(windows)]
    {
        let mut paths = Vec::new();
        if let Some(path) = windows_steam_path() {
            paths.push(path);
        }
        if let Some(program_files_x86) = env::var_os("ProgramFiles(x86)") {
            paths.push(PathBuf::from(program_files_x86).join("Steam"));
        }
        if let Some(program_files) = env::var_os("ProgramFiles") {
            paths.push(PathBuf::from(program_files).join("Steam"));
        }
        paths.push(PathBuf::from(r"C:\Program Files (x86)\Steam"));
        paths.push(PathBuf::from(r"C:\Program Files\Steam"));
        unique(paths)
    }
    #[cfg(not(windows))]
    {
        let Some(home) = home else {
            return Vec::new();
        };
        unique(vec![
            home.join(".steam/steam"),
            home.join(".local/share/Steam"),
            home.join(".var/app/com.valvesoftware.Steam/data/Steam"),
        ])
    }
}

fn parse_library_folders(steam_root: &Path) -> Vec<PathBuf> {
    static PATH_PATTERN: OnceLock<Regex> = OnceLock::new();
    let Ok(content) = fs::read_to_string(steam_root.join("steamapps/libraryfolders.vdf")) else {
        return Vec::new();
    };
    let pattern = PATH_PATTERN.get_or_init(|| Regex::new(r#""path"\s+"((?:\\.|[^"])*)""#).unwrap());
    pattern
        .captures_iter(&content)
        .filter_map(|capture| capture.get(1))
        .map(|value| PathBuf::from(value.as_str().replace(r"\\", r"\")))
        .collect()
}

#[cfg(not(windows))]
fn mounted_steam_libraries() -> Vec<PathBuf> {
    let mut results = Vec::new();
    for base in ["/run/media", "/media", "/mnt", "/var/mnt"] {
        let Ok(first_level) = fs::read_dir(base) else {
            continue;
        };
        for first in first_level.flatten().filter(|entry| entry.path().is_dir()) {
            let direct = first.path().join("SteamLibrary");
            if direct.exists() {
                results.push(direct);
            }
            let Ok(second_level) = fs::read_dir(first.path()) else {
                continue;
            };
            for second in second_level.flatten().filter(|entry| entry.path().is_dir()) {
                let nested = second.path().join("SteamLibrary");
                if nested.exists() {
                    results.push(nested);
                }
            }
        }
    }
    results
}

#[cfg(windows)]
fn mounted_steam_libraries() -> Vec<PathBuf> {
    Vec::new()
}

pub fn is_game_directory(path: &Path) -> bool {
    exists(path.join("GameAssembly.dll"))
        && exists(path.join("Sneak Out.exe"))
        && exists(path.join("Sneak Out_Data/resources.assets"))
}

pub fn detect_game_directories() -> Result<Vec<PathBuf>> {
    let steam_roots = candidate_steam_roots();
    let mut libraries = steam_roots.clone();
    libraries.extend(mounted_steam_libraries());
    for root in &steam_roots {
        libraries.extend(parse_library_folders(root));
    }
    let mut valid = Vec::new();
    for library in unique(libraries) {
        let candidate = library.join("steamapps/common").join(GAME_DIRECTORY_NAME);
        if is_game_directory(&candidate) {
            valid.push(fs::canonicalize(&candidate).unwrap_or(candidate));
        }
    }
    Ok(unique(valid))
}

pub fn resolve_game_directory(path: impl AsRef<Path>) -> Result<PathBuf> {
    let path = path.as_ref();
    let resolved = if path.is_absolute() {
        path.to_path_buf()
    } else {
        env::current_dir()?.join(path)
    };
    if !is_game_directory(&resolved) {
        bail!(
            "Invalid Sneak Out directory: {}\nExpected GameAssembly.dll, Sneak Out.exe, and Sneak Out_Data/resources.assets.",
            resolved.display()
        );
    }
    Ok(fs::canonicalize(&resolved).unwrap_or(resolved))
}

pub fn app_manifest_path(game_directory: &Path) -> PathBuf {
    game_directory
        .parent()
        .and_then(Path::parent)
        .unwrap_or(game_directory)
        .join(format!("appmanifest_{STEAM_APP_ID}.acf"))
}

pub fn read_installed_build_id(game_directory: &Path) -> Option<String> {
    static BUILD_PATTERN: OnceLock<Regex> = OnceLock::new();
    let content = fs::read_to_string(app_manifest_path(game_directory)).ok()?;
    BUILD_PATTERN
        .get_or_init(|| Regex::new(r#""buildid"\s+"(\d+)""#).unwrap())
        .captures(&content)
        .and_then(|capture| capture.get(1))
        .map(|value| value.as_str().to_owned())
}

pub fn steam_local_config_paths() -> Vec<PathBuf> {
    let mut results = Vec::new();
    for steam_root in candidate_steam_roots() {
        let Ok(users) = fs::read_dir(steam_root.join("userdata")) else {
            continue;
        };
        for user in users.flatten() {
            let name = user.file_name();
            let Some(name) = name.to_str() else {
                continue;
            };
            if !user.path().is_dir() || !name.chars().all(|character| character.is_ascii_digit()) {
                continue;
            }
            let path = user.path().join("config/localconfig.vdf");
            if path.exists() {
                results.push(path);
            }
        }
    }
    unique(results)
}

fn login_user_flag(body: &str, key: &str) -> String {
    Regex::new(&format!(r#""{}"\s+"([^"]*)""#, regex::escape(key)))
        .ok()
        .and_then(|pattern| pattern.captures(body))
        .and_then(|capture| capture.get(1))
        .map(|value| value.as_str().to_owned())
        .unwrap_or_default()
}

fn steam_account_id(steam_id_64: &str) -> Option<String> {
    steam_id_64
        .parse::<u64>()
        .ok()
        .map(|value| (value & 0xffff_ffff).to_string())
}

pub fn steam_active_local_config_paths() -> Vec<PathBuf> {
    static USER_PATTERN: OnceLock<Regex> = OnceLock::new();
    let user_pattern =
        USER_PATTERN.get_or_init(|| Regex::new(r#""(\d{17})"\s*\{([^{}]*)\}"#).unwrap());
    let mut results = Vec::new();
    for steam_root in candidate_steam_roots() {
        let Ok(content) = fs::read_to_string(steam_root.join("config/loginusers.vdf")) else {
            continue;
        };
        let users: Vec<_> = user_pattern
            .captures_iter(&content)
            .filter_map(|capture| Some((capture.get(1)?.as_str(), capture.get(2)?.as_str())))
            .collect();
        let mut active: Vec<_> = users
            .iter()
            .copied()
            .filter(|(_, body)| login_user_flag(body, "MostRecent") == "1")
            .collect();
        if active.is_empty() {
            active = users
                .iter()
                .copied()
                .filter(|(_, body)| login_user_flag(body, "AutoLogin") == "1")
                .collect();
        }
        for (steam_id, _) in active {
            let Some(account_id) = steam_account_id(steam_id) else {
                continue;
            };
            let path = steam_root
                .join("userdata")
                .join(account_id)
                .join("config/localconfig.vdf");
            if path.exists() {
                results.push(path);
            }
        }
    }
    unique(results)
}

pub fn is_steam_client_running() -> bool {
    if let Some(value) = env::var_os("SNEAKOUT_STEAM_RUNNING") {
        return value == "1";
    }
    #[cfg(windows)]
    {
        false
    }
    #[cfg(not(windows))]
    {
        let Ok(processes) = fs::read_dir("/proc") else {
            return false;
        };
        for process in processes.flatten() {
            let name = process.file_name();
            let Some(name) = name.to_str() else {
                continue;
            };
            if !name.chars().all(|character| character.is_ascii_digit()) {
                continue;
            }
            let Ok(command) = fs::read_to_string(process.path().join("comm")) else {
                continue;
            };
            if matches!(command.trim(), "steam" | "steamwebhelper") {
                return true;
            }
        }
        false
    }
}

pub fn is_proton_install() -> bool {
    !cfg!(windows)
}

pub fn require_parent(path: &Path) -> Result<&Path> {
    path.parent()
        .with_context(|| format!("{} has no parent directory", path.display()))
}
