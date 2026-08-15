use anyhow::{Context, Result, bail};
use directories::BaseDirs;
use regex::Regex;
use sha2::{Digest, Sha256};
use std::collections::HashSet;
use std::env;
use std::fs::{self, OpenOptions};
use std::io::Read;
use std::path::{Path, PathBuf};
use std::process::{Child, Command};
use std::sync::OnceLock;
use walkdir::WalkDir;

use crate::ProgressReporter;
use crate::io::{copy_file_atomic, sha256_file, write_file_atomic};
use crate::model::ProgressEvent;
use crate::payload::{cache_root, fetch_bytes, fetch_release};
use crate::steam::is_game_directory;

const WINDOWS_BINARY_ASSET_NAME: &str = "SneakOutPatches-windows-x86_64.exe";
const WINDOWS_CHECKSUM_NAME: &str = "SneakOutPatches-windows-x86_64.exe.sha256";
const WINDOWS_INSTALLER_NAME: &str = "SneakOutPatches.exe";
const WRAPPER_LAUNCHER: &str = "Contents/MacOS/WineskinLauncher";
const INSTALLER_MODE: &str = "WSS-installer";

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct SikarugirWrapper {
    path: PathBuf,
    launcher: PathBuf,
    game_directory: Option<PathBuf>,
    steam_directory: Option<PathBuf>,
}

impl SikarugirWrapper {
    pub fn path(&self) -> &Path {
        &self.path
    }

    pub fn display_name(&self) -> String {
        self.path
            .file_stem()
            .and_then(|name| name.to_str())
            .unwrap_or("Sikarugir wrapper")
            .to_owned()
    }

    pub fn game_directory(&self) -> Option<&Path> {
        self.game_directory.as_deref()
    }

    pub fn contains_steam(&self) -> bool {
        self.steam_directory.is_some()
    }

    fn is_automatic_candidate(&self) -> bool {
        self.game_directory.is_some() || self.steam_directory.is_some()
    }
}

fn unique_existing(paths: impl IntoIterator<Item = PathBuf>) -> Vec<PathBuf> {
    let mut seen = HashSet::new();
    paths
        .into_iter()
        .filter(|path| path.exists())
        .map(|path| fs::canonicalize(&path).unwrap_or(path))
        .filter(|path| seen.insert(path.clone()))
        .collect()
}

fn wrapper_prefixes(wrapper: &Path) -> Vec<PathBuf> {
    unique_existing([
        wrapper.join("Contents/SharedSupport/prefix"),
        wrapper.join("Contents/SharedSupport/CrossOverGames/support/default"),
    ])
}

fn windows_path_to_host(prefix: &Path, value: &str) -> Option<PathBuf> {
    let value = value.replace(r"\\", r"\");
    let bytes = value.as_bytes();
    if bytes.len() < 3 || bytes[1] != b':' || !bytes[0].is_ascii_alphabetic() {
        return value.starts_with('/').then(|| PathBuf::from(value));
    }

    let drive = (bytes[0] as char).to_ascii_lowercase();
    let mapping = prefix.join("dosdevices").join(format!("{drive}:"));
    let target = fs::read_link(&mapping).ok()?;
    let mut host = if target.is_absolute() {
        target
    } else {
        mapping.parent()?.join(target)
    };
    for component in value[2..].split(['\\', '/']) {
        match component {
            "" | "." => {}
            ".." => return None,
            component => host.push(component),
        }
    }
    Some(host)
}

fn library_paths(steam_directory: &Path, prefix: &Path) -> Vec<PathBuf> {
    static PATH_PATTERN: OnceLock<Regex> = OnceLock::new();
    let mut paths = vec![steam_directory.to_path_buf()];
    let Ok(content) = fs::read_to_string(steam_directory.join("steamapps/libraryfolders.vdf"))
    else {
        return paths;
    };
    let pattern = PATH_PATTERN.get_or_init(|| Regex::new(r#""path"\s+"((?:\\.|[^"])*)""#).unwrap());
    paths.extend(
        pattern
            .captures_iter(&content)
            .filter_map(|capture| capture.get(1))
            .filter_map(|value| windows_path_to_host(prefix, value.as_str())),
    );
    unique_existing(paths)
}

fn steam_directories(prefix: &Path) -> Vec<PathBuf> {
    let drive_c = prefix.join("drive_c");
    unique_existing([
        drive_c.join("Program Files (x86)/Steam"),
        drive_c.join("Program Files/Steam"),
    ])
    .into_iter()
    .filter(|path| path.join("steam.exe").is_file())
    .collect()
}

fn game_from_steam(steam_directory: &Path, prefix: &Path) -> Option<PathBuf> {
    library_paths(steam_directory, prefix)
        .into_iter()
        .map(|library| library.join("steamapps/common/Sneak Out"))
        .find(|path| is_game_directory(path))
        .map(|path| fs::canonicalize(&path).unwrap_or(path))
}

fn fallback_game_search(prefix: &Path) -> Option<PathBuf> {
    let drive_c = prefix.join("drive_c");
    if !drive_c.is_dir() {
        return None;
    }
    WalkDir::new(drive_c)
        .max_depth(10)
        .follow_links(false)
        .into_iter()
        .filter_map(std::result::Result::ok)
        .filter(|entry| entry.file_type().is_file() && entry.file_name() == "Sneak Out.exe")
        .filter_map(|entry| entry.path().parent().map(Path::to_path_buf))
        .find(|path| is_game_directory(path))
        .map(|path| fs::canonicalize(&path).unwrap_or(path))
}

pub fn inspect_sikarugir_wrapper(path: impl AsRef<Path>) -> Result<SikarugirWrapper> {
    let path = path.as_ref();
    let launcher = path.join(WRAPPER_LAUNCHER);
    if !launcher.is_file() {
        bail!(
            "{} is not a Sikarugir wrapper: missing {}",
            path.display(),
            launcher.display()
        );
    }
    let prefixes = wrapper_prefixes(path);
    if prefixes.is_empty() {
        bail!(
            "{} is not initialized: its Wine prefix is missing",
            path.display()
        );
    }

    let mut steam_directory = None;
    let mut game_directory = None;
    for prefix in &prefixes {
        for steam in steam_directories(prefix) {
            if game_directory.is_none() {
                game_directory = game_from_steam(&steam, prefix);
            }
            steam_directory.get_or_insert(steam);
        }
        if game_directory.is_none() {
            game_directory = fallback_game_search(prefix);
        }
    }

    let path = fs::canonicalize(path).unwrap_or_else(|_| path.to_path_buf());
    Ok(SikarugirWrapper {
        launcher: path.join(WRAPPER_LAUNCHER),
        path,
        game_directory,
        steam_directory,
    })
}

fn app_paths_in(root: &Path) -> Vec<PathBuf> {
    if root.extension().and_then(|extension| extension.to_str()) == Some("app") {
        return vec![root.to_path_buf()];
    }
    let Ok(entries) = fs::read_dir(root) else {
        return Vec::new();
    };
    entries
        .filter_map(std::result::Result::ok)
        .map(|entry| entry.path())
        .filter(|path| path.extension().and_then(|extension| extension.to_str()) == Some("app"))
        .collect()
}

pub fn default_sikarugir_roots() -> Vec<PathBuf> {
    if let Some(value) = env::var_os("SNEAKOUT_SIKARUGIR_ROOTS") {
        return env::split_paths(&value).collect();
    }
    let Some(base) = BaseDirs::new() else {
        return vec![PathBuf::from("/Applications")];
    };
    vec![
        base.home_dir().join("Applications/Sikarugir"),
        base.home_dir().join("Applications"),
        PathBuf::from("/Applications/Sikarugir"),
        PathBuf::from("/Applications"),
    ]
}

pub fn discover_sikarugir_wrappers_in(
    roots: impl IntoIterator<Item = PathBuf>,
) -> Vec<SikarugirWrapper> {
    let mut seen = HashSet::new();
    let mut wrappers: Vec<_> = roots
        .into_iter()
        .flat_map(|root| app_paths_in(&root))
        .filter_map(|path| inspect_sikarugir_wrapper(path).ok())
        .filter(SikarugirWrapper::is_automatic_candidate)
        .filter(|wrapper| seen.insert(wrapper.path.clone()))
        .collect();
    wrappers.sort_by(|left, right| {
        right
            .game_directory
            .is_some()
            .cmp(&left.game_directory.is_some())
            .then_with(|| left.display_name().cmp(&right.display_name()))
    });
    wrappers
}

pub fn discover_sikarugir_wrappers() -> Vec<SikarugirWrapper> {
    discover_sikarugir_wrappers_in(default_sikarugir_roots())
}

fn valid_windows_installer(path: &Path) -> bool {
    let Ok(mut file) = fs::File::open(path) else {
        return false;
    };
    let mut magic = [0_u8; 2];
    file.read_exact(&mut magic).is_ok() && magic == *b"MZ"
}

fn bundled_windows_installer() -> Result<Option<PathBuf>> {
    if let Some(path) = env::var_os("SNEAKOUT_PATCHES_WINDOWS_INSTALLER") {
        let path = PathBuf::from(path);
        if !valid_windows_installer(&path) {
            bail!("invalid Windows installer override: {}", path.display());
        }
        return Ok(Some(path));
    }
    let current = env::current_exe().context("could not locate the macOS launcher")?;
    let Some(contents) = current.parent().and_then(Path::parent) else {
        return Ok(None);
    };
    let path = contents.join("Resources").join(WINDOWS_INSTALLER_NAME);
    Ok(valid_windows_installer(&path).then_some(path))
}

fn stage_bundled_windows_installer(source: &Path) -> Result<PathBuf> {
    let checksum = sha256_file(source)?;
    let root = cache_root()?
        .join("sikarugir")
        .join("bundled")
        .join(&checksum[..16]);
    fs::create_dir_all(&root)?;
    let destination = root.join(WINDOWS_INSTALLER_NAME);
    let healthy = valid_windows_installer(&destination)
        && sha256_file(&destination).is_ok_and(|actual| actual == checksum);
    if !healthy {
        copy_file_atomic(source, &destination)?;
    }
    Ok(destination)
}

fn expected_checksum(bytes: &[u8]) -> Result<String> {
    let text = std::str::from_utf8(bytes).context("installer checksum is not UTF-8")?;
    let checksum = text
        .split_whitespace()
        .next()
        .context("installer checksum is empty")?
        .to_ascii_lowercase();
    if checksum.len() != 64 || !checksum.bytes().all(|byte| byte.is_ascii_hexdigit()) {
        bail!("installer checksum is not SHA-256");
    }
    Ok(checksum)
}

fn download_windows_installer(reporter: ProgressReporter<'_>) -> Result<PathBuf> {
    let release = fetch_release(reporter)?;
    let binary = release
        .assets
        .iter()
        .find(|asset| asset.name == WINDOWS_BINARY_ASSET_NAME)
        .with_context(|| {
            format!(
                "release {} is missing {WINDOWS_BINARY_ASSET_NAME}",
                release.tag_name
            )
        })?;
    let checksum = release
        .assets
        .iter()
        .find(|asset| asset.name == WINDOWS_CHECKSUM_NAME)
        .with_context(|| {
            format!(
                "release {} is missing {WINDOWS_CHECKSUM_NAME}",
                release.tag_name
            )
        })?;
    let root = cache_root()?
        .join("sikarugir")
        .join("downloaded")
        .join(release.id.to_string());
    fs::create_dir_all(&root)?;
    let lock_path = root.join("download.lock");
    let lock = OpenOptions::new()
        .create(true)
        .read(true)
        .write(true)
        .truncate(false)
        .open(&lock_path)?;
    lock.lock()?;

    let destination = root.join(WINDOWS_INSTALLER_NAME);
    let marker = root.join("installer.sha256");
    let cached_checksum = fs::read_to_string(&marker)
        .ok()
        .map(|value| value.trim().to_owned());
    let cached_is_valid = valid_windows_installer(&destination)
        && cached_checksum.is_some_and(|expected| {
            sha256_file(&destination).is_ok_and(|actual| actual == expected)
        });
    if cached_is_valid {
        return Ok(destination);
    }

    reporter(ProgressEvent::Message(
        "The bundled Windows installer is missing; downloading a verified copy...".to_owned(),
    ));
    let checksum_bytes = fetch_bytes(
        &checksum.browser_download_url,
        "Windows installer checksum",
        reporter,
    )?;
    let expected = expected_checksum(&checksum_bytes)?;
    let binary_bytes = fetch_bytes(&binary.browser_download_url, "Windows installer", reporter)?;
    let actual = hex::encode(Sha256::digest(&binary_bytes));
    if actual != expected {
        bail!("Windows installer checksum mismatch: expected {expected}, received {actual}");
    }
    write_file_atomic(&destination, binary_bytes)?;
    if !valid_windows_installer(&destination) {
        bail!("downloaded Windows installer is not a valid executable");
    }
    write_file_atomic(marker, format!("{expected}\n"))?;
    Ok(destination)
}

pub fn resolve_sikarugir_installer(reporter: ProgressReporter<'_>) -> Result<PathBuf> {
    match bundled_windows_installer()? {
        Some(path) => stage_bundled_windows_installer(&path),
        None => download_windows_installer(reporter),
    }
}

fn launch_command(wrapper: &SikarugirWrapper, installer: &Path) -> Command {
    let mut command = Command::new(&wrapper.launcher);
    command.arg(INSTALLER_MODE).arg(installer);
    command
}

pub fn launch_sikarugir_installer(wrapper: &SikarugirWrapper, installer: &Path) -> Result<Child> {
    if !wrapper.launcher.is_file() {
        bail!(
            "Sikarugir launcher is missing: {}",
            wrapper.launcher.display()
        );
    }
    if !valid_windows_installer(installer) {
        bail!(
            "Windows installer is missing or invalid: {}",
            installer.display()
        );
    }
    launch_command(wrapper, installer).spawn().with_context(|| {
        format!(
            "failed to launch the installer through {}",
            wrapper.launcher.display()
        )
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    fn create_game(path: &Path) {
        fs::create_dir_all(path.join("Sneak Out_Data")).unwrap();
        fs::write(path.join("Sneak Out.exe"), b"game").unwrap();
        fs::write(path.join("GameAssembly.dll"), b"assembly").unwrap();
        fs::write(path.join("Sneak Out_Data/resources.assets"), b"assets").unwrap();
    }

    fn create_wrapper(root: &Path, name: &str) -> PathBuf {
        let wrapper = root.join(format!("{name}.app"));
        fs::create_dir_all(wrapper.join("Contents/MacOS")).unwrap();
        fs::create_dir_all(wrapper.join("Contents/SharedSupport/prefix/drive_c")).unwrap();
        fs::create_dir_all(wrapper.join("Contents/SharedSupport/prefix/dosdevices")).unwrap();
        fs::write(wrapper.join(WRAPPER_LAUNCHER), b"launcher").unwrap();
        wrapper
    }

    #[test]
    fn discovers_the_wrapper_that_contains_windows_steam_and_the_game() {
        let temporary = tempfile::tempdir().unwrap();
        let wrapper = create_wrapper(temporary.path(), "Steam");
        let steam = wrapper.join("Contents/SharedSupport/prefix/drive_c/Program Files (x86)/Steam");
        fs::create_dir_all(steam.join("steamapps/common")).unwrap();
        fs::write(steam.join("steam.exe"), b"steam").unwrap();
        create_game(&steam.join("steamapps/common/Sneak Out"));

        let wrappers = discover_sikarugir_wrappers_in([temporary.path().to_path_buf()]);

        assert_eq!(wrappers.len(), 1);
        assert_eq!(wrappers[0].display_name(), "Steam");
        assert!(wrappers[0].contains_steam());
        assert!(wrappers[0].game_directory().is_some());
    }

    #[cfg(unix)]
    #[test]
    fn resolves_a_steam_library_on_an_extra_wine_drive() {
        use std::os::unix::fs::symlink;

        let temporary = tempfile::tempdir().unwrap();
        let wrapper = create_wrapper(temporary.path(), "ExternalSteam");
        let prefix = wrapper.join("Contents/SharedSupport/prefix");
        let steam = prefix.join("drive_c/Program Files (x86)/Steam");
        let library = temporary.path().join("ExternalLibrary");
        fs::create_dir_all(steam.join("steamapps")).unwrap();
        fs::create_dir_all(library.join("steamapps/common")).unwrap();
        fs::write(steam.join("steam.exe"), b"steam").unwrap();
        symlink(&library, prefix.join("dosdevices/d:")).unwrap();
        fs::write(
            steam.join("steamapps/libraryfolders.vdf"),
            r#""libraryfolders"
{
    "1"
    {
        "path" "D:\\"
    }
}
"#,
        )
        .unwrap();
        create_game(&library.join("steamapps/common/Sneak Out"));

        let inspected = inspect_sikarugir_wrapper(&wrapper).unwrap();

        assert_eq!(
            inspected.game_directory().unwrap(),
            fs::canonicalize(library.join("steamapps/common/Sneak Out")).unwrap()
        );
    }

    #[test]
    fn ignores_apps_without_a_sikarugir_launcher() {
        let temporary = tempfile::tempdir().unwrap();
        fs::create_dir_all(temporary.path().join("Ordinary.app/Contents/MacOS")).unwrap();

        assert!(discover_sikarugir_wrappers_in([temporary.path().to_path_buf()]).is_empty());
    }

    #[test]
    fn installer_command_uses_sikarugirs_installer_mode() {
        let wrapper = SikarugirWrapper {
            path: PathBuf::from("/Applications/Steam.app"),
            launcher: PathBuf::from("/Applications/Steam.app/Contents/MacOS/WineskinLauncher"),
            game_directory: None,
            steam_directory: None,
        };
        let command = launch_command(&wrapper, Path::new("/tmp/SneakOutPatches.exe"));

        assert_eq!(command.get_program(), wrapper.launcher.as_os_str());
        assert_eq!(
            command.get_args().collect::<Vec<_>>(),
            [
                std::ffi::OsStr::new(INSTALLER_MODE),
                std::ffi::OsStr::new("/tmp/SneakOutPatches.exe"),
            ]
        );
    }

    #[cfg(unix)]
    #[test]
    fn launches_the_windows_installer_through_the_wrapper() {
        use std::os::unix::fs::PermissionsExt;

        let temporary = tempfile::tempdir().unwrap();
        let wrapper_path = create_wrapper(temporary.path(), "LaunchTest");
        let launcher = wrapper_path.join(WRAPPER_LAUNCHER);
        fs::write(
            &launcher,
            b"#!/bin/sh\nprintf '%s\\n%s\\n' \"$1\" \"$2\" > \"$(dirname \"$0\")/launched\"\n",
        )
        .unwrap();
        fs::set_permissions(&launcher, fs::Permissions::from_mode(0o755)).unwrap();
        let installer = temporary.path().join(WINDOWS_INSTALLER_NAME);
        fs::write(&installer, b"MZinstaller").unwrap();
        let wrapper = inspect_sikarugir_wrapper(&wrapper_path).unwrap();

        let mut child = launch_sikarugir_installer(&wrapper, &installer).unwrap();
        assert!(child.wait().unwrap().success());

        let arguments = fs::read_to_string(wrapper_path.join("Contents/MacOS/launched")).unwrap();
        assert_eq!(
            arguments,
            format!("{INSTALLER_MODE}\n{}\n", installer.display())
        );
    }

    #[test]
    fn rejects_parent_components_in_windows_library_paths() {
        let prefix = Path::new("/prefix");
        assert!(windows_path_to_host(prefix, r"C:\\..\\escape").is_none());
    }

    #[test]
    fn app_paths_do_not_walk_unrelated_nested_directories() {
        let temporary = tempfile::tempdir().unwrap();
        let nested = temporary.path().join("unrelated/nested/Steam.app");
        fs::create_dir_all(&nested).unwrap();

        assert!(app_paths_in(temporary.path()).is_empty());
    }
}
