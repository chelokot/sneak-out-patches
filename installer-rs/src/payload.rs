use anyhow::{Context, Result, bail};
use directories::ProjectDirs;
use include_dir::{Dir, include_dir};
use reqwest::blocking::{Client, Response};
use serde::Deserialize;
use sha2::{Digest, Sha256};
use std::env;
use std::fs::{self, File, OpenOptions};
use std::io::{Cursor, Read, Write};
use std::path::{Path, PathBuf};
use std::time::Duration;
use zip::ZipArchive;

use crate::ProgressReporter;
use crate::io::{exists, write_file_atomic};
use crate::model::{ProgressEvent, RuntimeMod, SupportedBuild};

const REPOSITORY: &str = "chelokot/sneak-out-patches";
const PAYLOAD_ASSET_NAME: &str = "sneakout-patches-payload.zip";
const PAYLOAD_CHECKSUM_ASSET_NAME: &str = "sneakout-patches-payload.zip.sha256";
const BEPINEX_URL: &str = "https://builds.bepinex.dev/projects/bepinex_be/755/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.755%2B3fab71a.zip";
const BEPINEX_SHA256: &str = "3616d6a67f5f595973ec4aa7bd7edaf7f799d5bb9926f7146a6dcc7b4abf478f";

static EMBEDDED_MODS: Dir<'_> = include_dir!("$CARGO_MANIFEST_DIR/../artifacts/runtime_mods");
static EMBEDDED_CONFIGS: Dir<'_> =
    include_dir!("$CARGO_MANIFEST_DIR/../config_templates/runtime_mods");
const EMBEDDED_MANIFEST: &str = include_str!("../../runtime_mods_manifest.json");
const EMBEDDED_SUPPORTED_BUILD: &str = include_str!("../../supported_game_build.json");

#[derive(Clone, Debug)]
pub struct Payload {
    pub root: PathBuf,
    pub source: String,
}

#[derive(Debug, Deserialize)]
struct GithubRelease {
    id: u64,
    tag_name: String,
    assets: Vec<GithubAsset>,
}

#[derive(Debug, Deserialize)]
struct GithubAsset {
    name: String,
    browser_download_url: String,
}

fn cache_root() -> Result<PathBuf> {
    if let Some(path) = env::var_os("SNEAKOUT_PATCHES_CACHE_DIR") {
        return Ok(PathBuf::from(path));
    }
    ProjectDirs::from("", "chelokot", "sneakout-patches")
        .map(|directories| directories.cache_dir().to_path_buf())
        .context("could not determine the installer cache directory")
}

fn http_client() -> Result<Client> {
    Client::builder()
        .user_agent("sneakout-patches-native")
        .timeout(Duration::from_secs(120))
        .build()
        .context("failed to initialize the HTTP client")
}

fn read_response(
    mut response: Response,
    label: &str,
    reporter: ProgressReporter<'_>,
) -> Result<Vec<u8>> {
    let total = response.content_length();
    let mut bytes = Vec::with_capacity(total.unwrap_or(0).min(usize::MAX as u64) as usize);
    let mut buffer = [0_u8; 64 * 1024];
    let mut downloaded = 0_u64;
    loop {
        let read = response.read(&mut buffer)?;
        if read == 0 {
            break;
        }
        bytes.extend_from_slice(&buffer[..read]);
        downloaded += read as u64;
        reporter(ProgressEvent::Download {
            label: label.to_owned(),
            downloaded,
            total,
        });
    }
    Ok(bytes)
}

fn fetch_bytes(url: &str, label: &str, reporter: ProgressReporter<'_>) -> Result<Vec<u8>> {
    let response = http_client()?
        .get(url)
        .send()
        .with_context(|| format!("download failed: {url}"))?
        .error_for_status()
        .with_context(|| format!("download failed: {url}"))?;
    read_response(response, label, reporter)
}

fn fetch_release(reporter: ProgressReporter<'_>) -> Result<GithubRelease> {
    reporter(ProgressEvent::Message(
        "Checking the latest GitHub release...".to_owned(),
    ));
    http_client()?
        .get(format!(
            "https://api.github.com/repos/{REPOSITORY}/releases/latest"
        ))
        .send()?
        .error_for_status()?
        .json()
        .context("the latest GitHub release response was invalid")
}

fn extract_verified_zip(bytes: &[u8], expected_sha256: &str, destination: &Path) -> Result<()> {
    let actual = hex::encode(Sha256::digest(bytes));
    if actual != expected_sha256.to_ascii_lowercase() {
        bail!(
            "archive checksum mismatch: expected {}, received {actual}",
            expected_sha256
        );
    }
    fs::create_dir_all(destination)?;
    let mut archive = ZipArchive::new(Cursor::new(bytes)).context("downloaded ZIP is invalid")?;
    for index in 0..archive.len() {
        let mut entry = archive.by_index(index)?;
        let relative = entry
            .enclosed_name()
            .context("ZIP contains an unsafe path")?
            .to_path_buf();
        let output = destination.join(relative);
        if entry.is_dir() {
            fs::create_dir_all(&output)?;
            continue;
        }
        if let Some(parent) = output.parent() {
            fs::create_dir_all(parent)?;
        }
        let mut file = File::create(&output)?;
        std::io::copy(&mut entry, &mut file)?;
        file.flush()?;
    }
    Ok(())
}

fn is_payload_root(root: &Path) -> bool {
    exists(root.join("runtime_mods_manifest.json"))
        && exists(root.join("supported_game_build.json"))
        && exists(root.join("artifacts/runtime_mods"))
}

fn materialize_directory(directory: &Dir<'_>, destination: &Path) -> Result<()> {
    for file in directory.files() {
        let path = destination.join(file.path());
        write_file_atomic(path, file.contents())?;
    }
    for child in directory.dirs() {
        materialize_directory(child, destination)?;
    }
    Ok(())
}

fn embedded_directory_is_materialized(directory: &Dir<'_>, destination: &Path) -> bool {
    directory
        .files()
        .all(|file| exists(destination.join(file.path())))
        && directory
            .dirs()
            .all(|child| embedded_directory_is_materialized(child, destination))
}

fn is_embedded_payload_root(root: &Path) -> bool {
    is_payload_root(root)
        && embedded_directory_is_materialized(&EMBEDDED_MODS, &root.join("artifacts/runtime_mods"))
        && embedded_directory_is_materialized(
            &EMBEDDED_CONFIGS,
            &root.join("config_templates/runtime_mods"),
        )
}

fn digest_embedded_directory(digest: &mut Sha256, directory: &Dir<'_>) {
    for file in directory.files() {
        digest.update(file.path().to_string_lossy().as_bytes());
        digest.update(file.contents());
    }
    for child in directory.dirs() {
        digest_embedded_directory(digest, child);
    }
}

fn embedded_payload_key() -> String {
    let mut digest = Sha256::new();
    digest.update(EMBEDDED_MANIFEST.as_bytes());
    digest.update(EMBEDDED_SUPPORTED_BUILD.as_bytes());
    digest_embedded_directory(&mut digest, &EMBEDDED_MODS);
    digest_embedded_directory(&mut digest, &EMBEDDED_CONFIGS);
    let fingerprint = hex::encode(digest.finalize());
    format!("{}-{}", env!("CARGO_PKG_VERSION"), &fingerprint[..16])
}

fn embedded_payload_lock_path(root: &Path) -> PathBuf {
    let mut path = root.as_os_str().to_owned();
    path.push(".lock");
    PathBuf::from(path)
}

fn materialize_embedded_payload(root: &Path) -> Result<()> {
    if is_embedded_payload_root(root) {
        return Ok(());
    }
    let lock_path = embedded_payload_lock_path(root);
    if let Some(parent) = lock_path.parent() {
        fs::create_dir_all(parent)?;
    }
    let lock = OpenOptions::new()
        .create(true)
        .read(true)
        .write(true)
        .open(&lock_path)
        .with_context(|| format!("failed to open cache lock {}", lock_path.display()))?;
    lock.lock()
        .with_context(|| format!("failed to acquire cache lock {}", lock_path.display()))?;
    if is_embedded_payload_root(root) {
        return Ok(());
    }

    if root.exists() {
        fs::remove_dir_all(root)?;
    }
    write_file_atomic(root.join("runtime_mods_manifest.json"), EMBEDDED_MANIFEST)?;
    write_file_atomic(
        root.join("supported_game_build.json"),
        EMBEDDED_SUPPORTED_BUILD,
    )?;
    materialize_directory(&EMBEDDED_MODS, &root.join("artifacts/runtime_mods"))?;
    materialize_directory(
        &EMBEDDED_CONFIGS,
        &root.join("config_templates/runtime_mods"),
    )?;
    Ok(())
}

fn embedded_payload_root() -> Result<PathBuf> {
    let root = cache_root()?.join("embedded").join(embedded_payload_key());
    materialize_embedded_payload(&root)?;
    Ok(root)
}

pub fn embedded_manifest() -> Result<Vec<RuntimeMod>> {
    serde_json::from_str(EMBEDDED_MANIFEST).context("embedded runtime mod manifest is invalid")
}

pub fn resolve_embedded_payload() -> Result<Payload> {
    Ok(Payload {
        root: embedded_payload_root()?,
        source: "embedded payload".to_owned(),
    })
}

fn payload_override() -> Result<Option<Payload>> {
    if let Some(path) = env::var_os("SNEAKOUT_PATCHES_PAYLOAD_DIR") {
        let root = PathBuf::from(path);
        if !is_payload_root(&root) {
            bail!("invalid SNEAKOUT_PATCHES_PAYLOAD_DIR: {}", root.display());
        }
        return Ok(Some(Payload {
            root,
            source: "local override".to_owned(),
        }));
    }
    Ok(None)
}

pub fn resolve_latest_payload(reporter: ProgressReporter<'_>) -> Result<Payload> {
    if let Some(payload) = payload_override()? {
        return Ok(payload);
    }

    let release = fetch_release(reporter)?;
    let payload = release
        .assets
        .iter()
        .find(|asset| asset.name == PAYLOAD_ASSET_NAME)
        .context("release payload asset is missing")?;
    let checksum = release
        .assets
        .iter()
        .find(|asset| asset.name == PAYLOAD_CHECKSUM_ASSET_NAME)
        .context("release payload checksum is missing")?;
    let root = cache_root()?.join("releases").join(release.id.to_string());
    if !is_payload_root(&root) {
        if root.exists() {
            fs::remove_dir_all(&root)?;
        }
        reporter(ProgressEvent::Message(format!(
            "Downloading payload for {}...",
            release.tag_name
        )));
        let payload_bytes =
            fetch_bytes(&payload.browser_download_url, "Sneak Out patches", reporter)?;
        let checksum_bytes =
            fetch_bytes(&checksum.browser_download_url, "Payload checksum", reporter)?;
        let checksum_text = String::from_utf8(checksum_bytes)?;
        let expected = checksum_text
            .split_whitespace()
            .next()
            .context("release checksum is empty")?;
        extract_verified_zip(&payload_bytes, expected, &root)?;
    }
    Ok(Payload {
        root,
        source: format!("GitHub release {}", release.tag_name),
    })
}

pub fn resolve_payload(offline: bool, reporter: ProgressReporter<'_>) -> Result<Payload> {
    if let Some(payload) = payload_override()? {
        return Ok(payload);
    }

    if !offline {
        match resolve_latest_payload(reporter) {
            Ok(payload) => return Ok(payload),
            Err(error) => reporter(ProgressEvent::Message(format!(
                "Could not use the latest release: {error}. Using the embedded payload."
            ))),
        }
    }

    resolve_embedded_payload()
}

fn valid_bepinex_root(root: &Path) -> bool {
    exists(root.join("BepInEx/core/BepInEx.Unity.IL2CPP.dll")) && exists(root.join("winhttp.dll"))
}

pub fn resolve_bepinex(reporter: ProgressReporter<'_>) -> Result<PathBuf> {
    if let Some(path) = env::var_os("SNEAKOUT_BEPINEX_DIR") {
        let root = PathBuf::from(path);
        if !valid_bepinex_root(&root) {
            bail!("invalid SNEAKOUT_BEPINEX_DIR: {}", root.display());
        }
        return Ok(root);
    }

    let root = cache_root()?.join("bepinex").join(BEPINEX_SHA256);
    if valid_bepinex_root(&root) {
        return Ok(root);
    }
    if root.exists() {
        fs::remove_dir_all(&root)?;
    }
    reporter(ProgressEvent::Message(
        "Downloading BepInEx IL2CPP...".to_owned(),
    ));
    let bytes = fetch_bytes(BEPINEX_URL, "BepInEx IL2CPP", reporter)?;
    extract_verified_zip(&bytes, BEPINEX_SHA256, &root)?;
    if !valid_bepinex_root(&root) {
        bail!("downloaded BepInEx archive is incomplete");
    }
    Ok(root)
}

pub fn load_payload_metadata(root: &Path) -> Result<(Vec<RuntimeMod>, SupportedBuild)> {
    let manifest = serde_json::from_slice(&fs::read(root.join("runtime_mods_manifest.json"))?)?;
    let supported_build =
        serde_json::from_slice(&fs::read(root.join("supported_game_build.json"))?)?;
    Ok((manifest, supported_build))
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::{Arc, Barrier};
    use std::thread;
    use std::time::Duration;

    #[test]
    fn embedded_payload_materialization_waits_for_the_cache_lock() {
        let temporary = tempfile::tempdir().unwrap();
        let root = temporary.path().join("embedded-payload");
        materialize_embedded_payload(&root).unwrap();
        let missing = root.join("config_templates/runtime_mods/lobby-test-bot.cfg");
        fs::remove_file(&missing).unwrap();
        assert!(is_payload_root(&root));
        assert!(!is_embedded_payload_root(&root));

        let lock_path = embedded_payload_lock_path(&root);
        let lock = OpenOptions::new()
            .create(true)
            .read(true)
            .write(true)
            .open(lock_path)
            .unwrap();
        lock.lock().unwrap();

        let barrier = Arc::new(Barrier::new(2));
        let worker_barrier = Arc::clone(&barrier);
        let worker_root = root.clone();
        let worker = thread::spawn(move || {
            worker_barrier.wait();
            materialize_embedded_payload(&worker_root)
        });
        barrier.wait();
        thread::sleep(Duration::from_millis(100));

        assert!(
            !worker.is_finished(),
            "an incomplete cache must wait for the process that owns its lock"
        );
        lock.unlock().unwrap();
        worker.join().unwrap().unwrap();

        assert!(is_embedded_payload_root(&root));
        assert!(missing.exists());
    }
}
