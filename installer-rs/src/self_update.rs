use anyhow::{Context, Result, bail};
use semver::Version;
use sha2::{Digest, Sha256};
use std::env;
use std::ffi::{OsStr, OsString};
use std::fs::{self, OpenOptions};
use std::path::{Path, PathBuf};
use std::process::Command;
use std::thread;
use std::time::Duration;

use crate::ProgressReporter;
use crate::io::{copy_file_atomic, sha256_file, write_file_atomic};
use crate::model::ProgressEvent;
use crate::payload::{cache_root, fetch_bytes, fetch_release};

const HELPER_ARGUMENT: &str = "--sneakout-self-update-helper";
const SKIP_SELF_UPDATE_ENVIRONMENT: &str = "SNEAKOUT_PATCHES_SKIP_SELF_UPDATE_ONCE";
const PAYLOAD_OVERRIDE_ENVIRONMENT: &str = "SNEAKOUT_PATCHES_PAYLOAD_DIR";
const REPLACEMENT_RETRY_COUNT: usize = 150;
const REPLACEMENT_RETRY_DELAY: Duration = Duration::from_millis(100);

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum BinaryKind {
    Cli,
    Gui,
}

#[derive(Clone, Debug)]
pub struct PreparedSelfUpdate {
    version: Version,
    helper: PathBuf,
    replacement: PathBuf,
    target: PathBuf,
}

impl PreparedSelfUpdate {
    pub fn version(&self) -> &Version {
        &self.version
    }
}

#[derive(Clone, Copy)]
struct NativeBinarySpec {
    binary_name: &'static str,
    checksum_name: &'static str,
}

#[cfg(any(test, all(target_os = "linux", target_arch = "x86_64")))]
const LINUX_BINARY_SPEC: NativeBinarySpec = NativeBinarySpec {
    binary_name: "SneakOutPatches-linux-x86_64",
    checksum_name: "SneakOutPatches-linux-x86_64.sha256",
};
#[cfg(any(test, all(target_os = "windows", target_arch = "x86_64")))]
const WINDOWS_BINARY_SPEC: NativeBinarySpec = NativeBinarySpec {
    binary_name: "SneakOutPatches-windows-x86_64.exe",
    checksum_name: "SneakOutPatches-windows-x86_64.exe.sha256",
};

#[cfg(all(target_os = "linux", target_arch = "x86_64"))]
fn native_binary_spec() -> Option<NativeBinarySpec> {
    Some(LINUX_BINARY_SPEC)
}

#[cfg(all(target_os = "windows", target_arch = "x86_64"))]
fn native_binary_spec() -> Option<NativeBinarySpec> {
    Some(WINDOWS_BINARY_SPEC)
}

#[cfg(not(any(
    all(target_os = "linux", target_arch = "x86_64"),
    all(target_os = "windows", target_arch = "x86_64")
)))]
fn native_binary_spec() -> Option<NativeBinarySpec> {
    None
}

fn self_update_is_disabled() -> bool {
    cfg!(debug_assertions)
        || env::var_os(SKIP_SELF_UPDATE_ENVIRONMENT).is_some()
        || env::var_os(PAYLOAD_OVERRIDE_ENVIRONMENT).is_some()
}

fn release_version(tag: &str) -> Result<Version> {
    let value = tag
        .strip_prefix('v')
        .or_else(|| tag.strip_prefix('V'))
        .unwrap_or(tag);
    Version::parse(value).with_context(|| format!("release tag {tag:?} is not semantic versioning"))
}

fn expected_checksum(bytes: Vec<u8>) -> Result<String> {
    let text = String::from_utf8(bytes).context("native installer checksum is not UTF-8")?;
    let checksum = text
        .split_whitespace()
        .next()
        .context("native installer checksum is empty")?
        .to_ascii_lowercase();
    if checksum.len() != 64 || !checksum.bytes().all(|byte| byte.is_ascii_hexdigit()) {
        bail!("native installer checksum is not SHA-256");
    }
    Ok(checksum)
}

fn stage_current_binary_as_helper(
    current_executable: &Path,
    binary_root: &Path,
) -> Result<PathBuf> {
    let extension = if cfg!(windows) { ".exe" } else { "" };
    let helper = binary_root.join(format!(
        ".self-update-helper-{}{}",
        std::process::id(),
        extension
    ));
    copy_file_atomic(current_executable, &helper)?;
    Ok(helper)
}

fn cached_binary_is_valid(binary: &Path, marker: &Path, expected: &str) -> bool {
    let marker_matches =
        fs::read_to_string(marker).is_ok_and(|value| value.trim().eq_ignore_ascii_case(expected));
    marker_matches
        && binary.is_file()
        && sha256_file(binary).is_ok_and(|actual| actual.eq_ignore_ascii_case(expected))
}

fn materialize_native_binary(
    release_id: u64,
    binary_url: &str,
    checksum_url: &str,
    spec: NativeBinarySpec,
    reporter: ProgressReporter<'_>,
) -> Result<(PathBuf, PathBuf)> {
    let update_root = cache_root()?
        .join("self-update")
        .join(release_id.to_string());
    fs::create_dir_all(&update_root)?;
    let lock_path = update_root.join("download.lock");
    let lock = OpenOptions::new()
        .create(true)
        .read(true)
        .write(true)
        .truncate(false)
        .open(&lock_path)
        .with_context(|| format!("failed to open update lock {}", lock_path.display()))?;
    lock.lock()
        .with_context(|| format!("failed to acquire update lock {}", lock_path.display()))?;

    let checksum_bytes = fetch_bytes(checksum_url, "Installer checksum", reporter)?;
    let expected = expected_checksum(checksum_bytes)?;
    let binary = update_root.join(spec.binary_name);
    let marker = update_root.join("verified.sha256");
    if cached_binary_is_valid(&binary, &marker, &expected) {
        return Ok((update_root, binary));
    }

    reporter(ProgressEvent::Message(format!(
        "Downloading installer update {}...",
        spec.binary_name
    )));
    let bytes = fetch_bytes(binary_url, "Installer update", reporter)?;
    let actual = hex::encode(Sha256::digest(&bytes));
    if actual != expected {
        let _ = fs::remove_file(&binary);
        bail!("installer binary checksum mismatch: expected {expected}, received {actual}");
    }
    write_file_atomic(&binary, bytes)?;
    set_executable_permissions(&binary)?;
    write_file_atomic(&marker, format!("{expected}\n"))?;
    Ok((update_root, binary))
}

#[cfg(unix)]
fn set_executable_permissions(path: &Path) -> Result<()> {
    use std::os::unix::fs::PermissionsExt;
    fs::set_permissions(path, fs::Permissions::from_mode(0o755))?;
    Ok(())
}

#[cfg(not(unix))]
fn set_executable_permissions(_path: &Path) -> Result<()> {
    Ok(())
}

pub fn prepare_self_update(
    binary_kind: BinaryKind,
    reporter: ProgressReporter<'_>,
) -> Result<Option<PreparedSelfUpdate>> {
    if binary_kind != BinaryKind::Gui || self_update_is_disabled() {
        return Ok(None);
    }
    let Some(spec) = native_binary_spec() else {
        return Ok(None);
    };
    let release = fetch_release(reporter)?;
    let latest = release_version(&release.tag_name)?;
    let current = Version::parse(env!("CARGO_PKG_VERSION"))
        .context("the running installer version is invalid")?;
    if latest <= current {
        return Ok(None);
    }

    let binary_url = release
        .assets
        .iter()
        .find(|asset| asset.name == spec.binary_name)
        .with_context(|| {
            format!(
                "release {} is missing {}",
                release.tag_name, spec.binary_name
            )
        })?
        .browser_download_url
        .clone();
    let checksum_url = release
        .assets
        .iter()
        .find(|asset| asset.name == spec.checksum_name)
        .with_context(|| {
            format!(
                "release {} is missing {}",
                release.tag_name, spec.checksum_name
            )
        })?
        .browser_download_url
        .clone();
    let (binary_root, replacement) =
        materialize_native_binary(release.id, &binary_url, &checksum_url, spec, reporter)?;
    let target = env::current_exe().context("could not locate the running installer")?;
    let helper = stage_current_binary_as_helper(&target, &binary_root)?;
    Ok(Some(PreparedSelfUpdate {
        version: latest,
        helper,
        replacement,
        target,
    }))
}

pub fn launch_self_update(
    update: &PreparedSelfUpdate,
    relaunch_arguments: impl IntoIterator<Item = OsString>,
) -> Result<()> {
    let mut command = Command::new(&update.helper);
    command
        .arg(HELPER_ARGUMENT)
        .arg(&update.replacement)
        .arg(&update.target)
        .arg("--relaunch")
        .args(relaunch_arguments);
    command.spawn().with_context(|| {
        format!(
            "failed to launch installer update helper {}",
            update.helper.display()
        )
    })?;
    Ok(())
}

pub fn launch_self_update_without_relaunch(update: &PreparedSelfUpdate) -> Result<()> {
    Command::new(&update.helper)
        .arg(HELPER_ARGUMENT)
        .arg(&update.replacement)
        .arg(&update.target)
        .arg("--no-relaunch")
        .spawn()
        .with_context(|| {
            format!(
                "failed to launch installer update helper {}",
                update.helper.display()
            )
        })?;
    Ok(())
}

fn sibling_with_suffix(path: &Path, suffix: &str) -> Result<PathBuf> {
    let name = path
        .file_name()
        .context("installer executable has no file name")?;
    let mut updated = name.to_os_string();
    updated.push(suffix);
    Ok(path.with_file_name(updated))
}

fn move_target_to_backup(target: &Path, backup: &Path) -> Result<bool> {
    if !target.exists() {
        return Ok(false);
    }
    for attempt in 0..REPLACEMENT_RETRY_COUNT {
        match fs::rename(target, backup) {
            Ok(()) => return Ok(true),
            Err(error) if cfg!(windows) && attempt + 1 < REPLACEMENT_RETRY_COUNT => {
                thread::sleep(REPLACEMENT_RETRY_DELAY);
                let _ = error;
            }
            Err(error) => {
                return Err(error)
                    .with_context(|| format!("failed to move {} aside", target.display()));
            }
        }
    }
    unreachable!("replacement retry loop always returns")
}

fn replace_executable(replacement: &Path, target: &Path) -> Result<()> {
    let suffix = format!(".self-update-{}", std::process::id());
    let temporary = sibling_with_suffix(target, &format!("{suffix}.tmp"))?;
    let backup = sibling_with_suffix(target, &format!("{suffix}.old"))?;
    let _ = fs::remove_file(&temporary);
    let _ = fs::remove_file(&backup);

    fs::copy(replacement, &temporary).with_context(|| {
        format!(
            "failed to stage installer update from {} to {}",
            replacement.display(),
            temporary.display()
        )
    })?;
    OpenOptions::new()
        .write(true)
        .open(&temporary)?
        .sync_all()?;

    let had_target = move_target_to_backup(target, &backup)?;
    if let Err(error) = fs::rename(&temporary, target) {
        if had_target {
            let _ = fs::rename(&backup, target);
        }
        let _ = fs::remove_file(&temporary);
        return Err(error).with_context(|| format!("failed to replace {}", target.display()));
    }
    if had_target {
        let _ = fs::remove_file(&backup);
    }
    Ok(())
}

fn relaunch(target: &Path, arguments: &[OsString]) -> Result<()> {
    Command::new(target)
        .args(arguments)
        .env(SKIP_SELF_UPDATE_ENVIRONMENT, "1")
        .spawn()
        .with_context(|| format!("failed to relaunch {}", target.display()))?;
    Ok(())
}

fn apply_self_update(
    replacement: &Path,
    target: &Path,
    relaunch_arguments: Option<&[OsString]>,
) -> Result<()> {
    match replace_executable(replacement, target) {
        Ok(()) => match relaunch_arguments {
            Some(arguments) => relaunch(target, arguments),
            None => Ok(()),
        },
        Err(error) => {
            if let Some(arguments) = relaunch_arguments
                && target.is_file()
            {
                let _ = relaunch(target, arguments);
            }
            Err(error)
        }
    }
}

pub fn run_self_update_helper_if_requested() -> Option<Result<()>> {
    let mut arguments = env::args_os().skip(1);
    if arguments.next().as_deref() != Some(OsStr::new(HELPER_ARGUMENT)) {
        return None;
    }
    Some((|| {
        let replacement = PathBuf::from(
            arguments
                .next()
                .context("self-update helper is missing the replacement path")?,
        );
        let target = PathBuf::from(
            arguments
                .next()
                .context("self-update helper is missing the target path")?,
        );
        match arguments
            .next()
            .as_deref()
            .and_then(|argument| argument.to_str())
        {
            Some("--relaunch") => {
                let relaunch_arguments: Vec<_> = arguments.collect();
                apply_self_update(&replacement, &target, Some(&relaunch_arguments))
            }
            Some("--no-relaunch") if arguments.next().is_none() => {
                apply_self_update(&replacement, &target, None)
            }
            _ => bail!("self-update helper has an invalid relaunch mode"),
        }
    })())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn release_tags_are_compared_as_semantic_versions() {
        assert_eq!(release_version("v1.2.3").unwrap(), Version::new(1, 2, 3));
        assert_eq!(release_version("1.2.3").unwrap(), Version::new(1, 2, 3));
        assert!(release_version("latest").is_err());
    }

    #[test]
    fn checksums_must_be_sha256() {
        let expected = "a".repeat(64);
        assert_eq!(
            expected_checksum(format!("{expected}  installer\n").into_bytes()).unwrap(),
            expected
        );
        assert!(expected_checksum(b"not-a-checksum".to_vec()).is_err());
    }

    #[test]
    fn cached_binary_must_match_its_checksum() {
        let temporary = tempfile::tempdir().unwrap();
        let binary = temporary.path().join("installer");
        let marker = temporary.path().join("verified.sha256");
        let bytes = b"graphical installer";
        let expected = hex::encode(Sha256::digest(bytes));
        fs::write(&binary, bytes).unwrap();
        fs::write(&marker, format!("{expected}\n")).unwrap();

        assert!(cached_binary_is_valid(&binary, &marker, &expected));

        fs::write(&binary, b"corrupt").unwrap();
        assert!(!cached_binary_is_valid(&binary, &marker, &expected));
    }

    #[test]
    fn executable_replacement_preserves_a_recoverable_target() {
        let temporary = tempfile::tempdir().unwrap();
        let replacement = temporary.path().join("replacement");
        let target = temporary.path().join("target");
        fs::write(&replacement, b"new installer").unwrap();
        fs::write(&target, b"old installer").unwrap();

        replace_executable(&replacement, &target).unwrap();

        assert_eq!(fs::read(&target).unwrap(), b"new installer");
        assert_eq!(fs::read(&replacement).unwrap(), b"new installer");
        assert_eq!(
            fs::read_dir(temporary.path()).unwrap().count(),
            2,
            "temporary and backup files should be cleaned up"
        );
    }

    #[cfg(unix)]
    #[test]
    fn applied_update_relaunches_with_the_original_arguments() {
        use std::os::unix::fs::PermissionsExt;

        let temporary = tempfile::tempdir().unwrap();
        let replacement = temporary.path().join("replacement");
        let target = temporary.path().join("target");
        let marker = temporary.path().join("relaunch-marker");
        fs::write(&replacement, b"#!/bin/sh\nprintf '%s' \"$1\" > \"$2\"\n").unwrap();
        fs::set_permissions(&replacement, fs::Permissions::from_mode(0o755)).unwrap();
        fs::write(&target, b"#!/bin/sh\nexit 1\n").unwrap();
        fs::set_permissions(&target, fs::Permissions::from_mode(0o755)).unwrap();
        let arguments = vec![OsString::from("preserved"), marker.as_os_str().to_owned()];

        apply_self_update(&replacement, &target, Some(&arguments)).unwrap();

        for _ in 0..50 {
            if marker.exists() {
                break;
            }
            thread::sleep(Duration::from_millis(20));
        }
        assert_eq!(fs::read_to_string(marker).unwrap(), "preserved");
    }

    #[test]
    fn releases_use_direct_gui_binaries() {
        assert_eq!(
            LINUX_BINARY_SPEC.binary_name,
            "SneakOutPatches-linux-x86_64"
        );
        assert_eq!(
            LINUX_BINARY_SPEC.checksum_name,
            "SneakOutPatches-linux-x86_64.sha256"
        );
        assert_eq!(
            WINDOWS_BINARY_SPEC.binary_name,
            "SneakOutPatches-windows-x86_64.exe"
        );
        assert_eq!(
            WINDOWS_BINARY_SPEC.checksum_name,
            "SneakOutPatches-windows-x86_64.exe.sha256"
        );
    }
}
