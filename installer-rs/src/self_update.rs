use anyhow::{Context, Result, bail};
use semver::Version;
use sha2::{Digest, Sha256};
use std::env;
use std::ffi::{OsStr, OsString};
use std::fs::{self, File, OpenOptions};
use std::io::{Cursor, Write};
use std::path::{Path, PathBuf};
use std::process::Command;
use std::thread;
use std::time::Duration;

use crate::ProgressReporter;
use crate::io::{copy_file_atomic, write_file_atomic};
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
struct NativeArchiveSpec {
    archive_name: &'static str,
    checksum_name: &'static str,
    gui_binary_name: &'static str,
    cli_binary_name: &'static str,
}

#[cfg(all(target_os = "linux", target_arch = "x86_64"))]
fn native_archive_spec() -> Option<NativeArchiveSpec> {
    Some(NativeArchiveSpec {
        archive_name: "SneakOutPatches-linux-x86_64.tar.gz",
        checksum_name: "SneakOutPatches-linux-x86_64.tar.gz.sha256",
        gui_binary_name: "SneakOutPatches",
        cli_binary_name: "sneakout-patches",
    })
}

#[cfg(all(target_os = "windows", target_arch = "x86_64"))]
fn native_archive_spec() -> Option<NativeArchiveSpec> {
    Some(NativeArchiveSpec {
        archive_name: "SneakOutPatches-windows-x86_64.zip",
        checksum_name: "SneakOutPatches-windows-x86_64.zip.sha256",
        gui_binary_name: "SneakOutPatches.exe",
        cli_binary_name: "sneakout-patches.exe",
    })
}

#[cfg(not(any(
    all(target_os = "linux", target_arch = "x86_64"),
    all(target_os = "windows", target_arch = "x86_64")
)))]
fn native_archive_spec() -> Option<NativeArchiveSpec> {
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

fn extracted_binary_paths(root: &Path, spec: NativeArchiveSpec) -> (PathBuf, PathBuf) {
    (
        root.join(spec.gui_binary_name),
        root.join(spec.cli_binary_name),
    )
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

fn cached_binaries_are_valid(
    root: &Path,
    marker: &Path,
    expected: &str,
    spec: NativeArchiveSpec,
) -> bool {
    let marker_matches =
        fs::read_to_string(marker).is_ok_and(|value| value.trim().eq_ignore_ascii_case(expected));
    let (gui, cli) = extracted_binary_paths(root, spec);
    marker_matches && gui.is_file() && cli.is_file()
}

fn materialize_native_binaries(
    release_id: u64,
    archive_url: &str,
    checksum_url: &str,
    spec: NativeArchiveSpec,
    reporter: ProgressReporter<'_>,
) -> Result<PathBuf> {
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
    let binary_root = update_root.join("binaries");
    let marker = update_root.join("verified.sha256");
    if cached_binaries_are_valid(&binary_root, &marker, &expected, spec) {
        return Ok(binary_root);
    }

    if binary_root.exists() {
        fs::remove_dir_all(&binary_root)?;
    }
    fs::create_dir_all(&binary_root)?;
    reporter(ProgressEvent::Message(format!(
        "Downloading installer update {}...",
        spec.archive_name
    )));
    let archive = fetch_bytes(archive_url, "Installer update", reporter)?;
    let actual = hex::encode(Sha256::digest(&archive));
    if actual != expected {
        let _ = fs::remove_dir_all(&binary_root);
        bail!("installer archive checksum mismatch: expected {expected}, received {actual}");
    }
    if let Err(error) = extract_native_binaries(&archive, &binary_root, spec) {
        let _ = fs::remove_dir_all(&binary_root);
        return Err(error);
    }
    let (gui, cli) = extracted_binary_paths(&binary_root, spec);
    if !gui.is_file() || !cli.is_file() {
        let _ = fs::remove_dir_all(&binary_root);
        bail!("native installer archive is missing one or more executables");
    }
    write_file_atomic(&marker, format!("{expected}\n"))?;
    Ok(binary_root)
}

#[cfg(all(target_os = "linux", target_arch = "x86_64"))]
fn extract_native_binaries(
    bytes: &[u8],
    destination: &Path,
    spec: NativeArchiveSpec,
) -> Result<()> {
    use flate2::read::GzDecoder;
    use std::os::unix::fs::PermissionsExt;
    use tar::Archive;

    let decoder = GzDecoder::new(Cursor::new(bytes));
    let mut archive = Archive::new(decoder);
    for entry in archive
        .entries()
        .context("native installer TAR is invalid")?
    {
        let mut entry = entry?;
        if !entry.header().entry_type().is_file() {
            continue;
        }
        let path = entry.path()?;
        let Some(name) = path.file_name().and_then(OsStr::to_str) else {
            continue;
        };
        if path.components().count() != 1
            || (name != spec.gui_binary_name && name != spec.cli_binary_name)
        {
            continue;
        }
        let output = destination.join(name);
        let mut file = File::create(&output)?;
        std::io::copy(&mut entry, &mut file)?;
        file.flush()?;
        fs::set_permissions(&output, fs::Permissions::from_mode(0o755))?;
    }
    Ok(())
}

#[cfg(all(target_os = "windows", target_arch = "x86_64"))]
fn extract_native_binaries(
    bytes: &[u8],
    destination: &Path,
    spec: NativeArchiveSpec,
) -> Result<()> {
    use zip::ZipArchive;

    let mut archive =
        ZipArchive::new(Cursor::new(bytes)).context("native installer ZIP is invalid")?;
    for index in 0..archive.len() {
        let mut entry = archive.by_index(index)?;
        if entry.is_dir() {
            continue;
        }
        let Some(path) = entry.enclosed_name() else {
            continue;
        };
        let Some(name) = path.file_name().and_then(OsStr::to_str) else {
            continue;
        };
        if path.components().count() != 1
            || (name != spec.gui_binary_name && name != spec.cli_binary_name)
        {
            continue;
        }
        let output = destination.join(name);
        let mut file = File::create(output)?;
        std::io::copy(&mut entry, &mut file)?;
        file.flush()?;
    }
    Ok(())
}

#[cfg(not(any(
    all(target_os = "linux", target_arch = "x86_64"),
    all(target_os = "windows", target_arch = "x86_64")
)))]
fn extract_native_binaries(
    _bytes: &[u8],
    _destination: &Path,
    _spec: NativeArchiveSpec,
) -> Result<()> {
    bail!("self-update is not supported on this platform")
}

pub fn prepare_self_update(
    binary_kind: BinaryKind,
    reporter: ProgressReporter<'_>,
) -> Result<Option<PreparedSelfUpdate>> {
    if self_update_is_disabled() {
        return Ok(None);
    }
    let Some(spec) = native_archive_spec() else {
        return Ok(None);
    };
    let release = fetch_release(reporter)?;
    let latest = release_version(&release.tag_name)?;
    let current = Version::parse(env!("CARGO_PKG_VERSION"))
        .context("the running installer version is invalid")?;
    if latest <= current {
        return Ok(None);
    }

    let archive_url = release
        .assets
        .iter()
        .find(|asset| asset.name == spec.archive_name)
        .with_context(|| {
            format!(
                "release {} is missing {}",
                release.tag_name, spec.archive_name
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
    let binary_root =
        materialize_native_binaries(release.id, &archive_url, &checksum_url, spec, reporter)?;
    let (gui, cli) = extracted_binary_paths(&binary_root, spec);
    let replacement = match binary_kind {
        BinaryKind::Cli => cli,
        BinaryKind::Gui => gui,
    };
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
    File::open(&temporary)?.sync_all()?;

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
            expected_checksum(format!("{expected}  installer.zip\n").into_bytes()).unwrap(),
            expected
        );
        assert!(expected_checksum(b"not-a-checksum".to_vec()).is_err());
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

    #[cfg(all(target_os = "linux", target_arch = "x86_64"))]
    #[test]
    fn extracts_only_the_expected_linux_binaries() {
        use flate2::Compression;
        use flate2::write::GzEncoder;
        use tar::{Builder, Header};

        let spec = native_archive_spec().unwrap();
        let mut compressed = Vec::new();
        {
            let encoder = GzEncoder::new(&mut compressed, Compression::default());
            let mut archive = Builder::new(encoder);
            for (name, contents) in [
                (spec.gui_binary_name, b"gui".as_slice()),
                (spec.cli_binary_name, b"cli".as_slice()),
                ("unexpected", b"ignored".as_slice()),
            ] {
                let mut header = Header::new_gnu();
                header.set_size(contents.len() as u64);
                header.set_mode(0o755);
                header.set_cksum();
                archive.append_data(&mut header, name, contents).unwrap();
            }
            archive.into_inner().unwrap().finish().unwrap();
        }
        let destination = tempfile::tempdir().unwrap();

        extract_native_binaries(&compressed, destination.path(), spec).unwrap();

        assert_eq!(
            fs::read(destination.path().join(spec.gui_binary_name)).unwrap(),
            b"gui"
        );
        assert_eq!(
            fs::read(destination.path().join(spec.cli_binary_name)).unwrap(),
            b"cli"
        );
        assert!(!destination.path().join("unexpected").exists());
    }

    #[cfg(all(target_os = "windows", target_arch = "x86_64"))]
    #[test]
    fn extracts_only_the_expected_windows_binaries() {
        let spec = native_archive_spec().unwrap();
        let mut bytes = Vec::new();
        {
            let mut archive = zip::ZipWriter::new(Cursor::new(&mut bytes));
            let options = zip::write::SimpleFileOptions::default();
            for (name, contents) in [
                (spec.gui_binary_name, b"gui".as_slice()),
                (spec.cli_binary_name, b"cli".as_slice()),
                ("unexpected", b"ignored".as_slice()),
            ] {
                archive.start_file(name, options).unwrap();
                archive.write_all(contents).unwrap();
            }
            archive.finish().unwrap();
        }
        let destination = tempfile::tempdir().unwrap();

        extract_native_binaries(&bytes, destination.path(), spec).unwrap();

        assert_eq!(
            fs::read(destination.path().join(spec.gui_binary_name)).unwrap(),
            b"gui"
        );
        assert_eq!(
            fs::read(destination.path().join(spec.cli_binary_name)).unwrap(),
            b"cli"
        );
        assert!(!destination.path().join("unexpected").exists());
    }
}
