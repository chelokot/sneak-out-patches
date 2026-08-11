use anyhow::{Context, Result, bail};
use path_absolutize::Absolutize;
use sha2::{Digest, Sha256};
use std::ffi::OsString;
use std::fs::{self, File};
use std::io::{BufReader, Read, Write};
use std::path::{Component, Path, PathBuf};
use walkdir::WalkDir;

pub fn exists(path: impl AsRef<Path>) -> bool {
    path.as_ref().exists()
}

pub fn sha256_file(path: impl AsRef<Path>) -> Result<String> {
    let path = path.as_ref();
    let file = File::open(path).with_context(|| format!("failed to open {}", path.display()))?;
    let mut reader = BufReader::new(file);
    let mut digest = Sha256::new();
    let mut buffer = [0_u8; 64 * 1024];
    loop {
        let read = reader
            .read(&mut buffer)
            .with_context(|| format!("failed to read {}", path.display()))?;
        if read == 0 {
            break;
        }
        digest.update(&buffer[..read]);
    }
    Ok(hex::encode(digest.finalize()))
}

fn temporary_path(path: &Path) -> PathBuf {
    let mut name: OsString = path.as_os_str().to_owned();
    name.push(format!(".sneakout-patches.tmp-{}", std::process::id()));
    PathBuf::from(name)
}

fn replace_with_temporary(temporary: &Path, destination: &Path) -> Result<()> {
    match fs::rename(temporary, destination) {
        Ok(()) => Ok(()),
        Err(error)
            if matches!(
                error.kind(),
                std::io::ErrorKind::AlreadyExists
                    | std::io::ErrorKind::PermissionDenied
                    | std::io::ErrorKind::DirectoryNotEmpty
            ) =>
        {
            if destination.is_dir() {
                fs::remove_dir_all(destination)?;
            } else {
                let _ = fs::remove_file(destination);
            }
            fs::rename(temporary, destination).with_context(|| {
                format!(
                    "failed to replace {} with {}",
                    destination.display(),
                    temporary.display()
                )
            })
        }
        Err(error) => Err(error).with_context(|| {
            format!(
                "failed to rename {} to {}",
                temporary.display(),
                destination.display()
            )
        }),
    }
}

pub fn write_file_atomic(path: impl AsRef<Path>, bytes: impl AsRef<[u8]>) -> Result<()> {
    let path = path.as_ref();
    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent)?;
    }
    let temporary = temporary_path(path);
    let result = (|| {
        let mut file = File::create(&temporary)?;
        file.write_all(bytes.as_ref())?;
        file.sync_all()?;
        drop(file);
        replace_with_temporary(&temporary, path)
    })();
    if result.is_err() {
        let _ = fs::remove_file(&temporary);
    }
    result.with_context(|| format!("failed to write {}", path.display()))
}

pub fn copy_file_atomic(source: impl AsRef<Path>, destination: impl AsRef<Path>) -> Result<()> {
    let source = source.as_ref();
    let destination = destination.as_ref();
    if let Some(parent) = destination.parent() {
        fs::create_dir_all(parent)?;
    }
    let temporary = temporary_path(destination);
    let result = (|| {
        fs::copy(source, &temporary)?;
        replace_with_temporary(&temporary, destination)
    })();
    if result.is_err() {
        let _ = fs::remove_file(&temporary);
    }
    result.with_context(|| {
        format!(
            "failed to copy {} to {}",
            source.display(),
            destination.display()
        )
    })
}

pub fn list_files(root: impl AsRef<Path>) -> Result<Vec<PathBuf>> {
    let root = root.as_ref();
    if !root.exists() {
        return Ok(Vec::new());
    }
    let mut files = Vec::new();
    for entry in WalkDir::new(root).follow_links(false) {
        let entry = entry?;
        if entry.file_type().is_file() {
            files.push(entry.into_path());
        }
    }
    files.sort();
    Ok(files)
}

pub fn portable_relative(root: &Path, path: &Path) -> Result<String> {
    let root = root.absolutize()?;
    let path = path.absolutize()?;
    let relative = path.strip_prefix(root.as_ref()).with_context(|| {
        format!(
            "refusing to manage a path outside the game: {}",
            path.display()
        )
    })?;
    if relative.as_os_str().is_empty() {
        bail!("refusing to manage the game directory itself")
    }
    let mut parts = Vec::new();
    for component in relative.components() {
        match component {
            Component::Normal(value) => parts.push(
                value
                    .to_str()
                    .context("installer state paths must be valid UTF-8")?,
            ),
            _ => bail!("invalid managed path: {}", relative.display()),
        }
    }
    Ok(parts.join("/"))
}

pub fn from_portable_relative(root: &Path, portable: &str) -> Result<PathBuf> {
    if portable.is_empty() || portable.contains('\\') {
        bail!("invalid managed path in install state: {portable}")
    }
    let mut result = root.to_path_buf();
    for component in portable.split('/') {
        if component.is_empty() || component == "." || component == ".." {
            bail!("invalid managed path in install state: {portable}")
        }
        result.push(component);
    }
    let absolute_root = root.absolutize()?;
    let absolute_result = result.absolutize()?;
    if absolute_result
        .strip_prefix(absolute_root.as_ref())
        .is_err()
    {
        bail!("invalid managed path in install state: {portable}")
    }
    Ok(absolute_result.into_owned())
}

pub fn remove_path(path: &Path) -> Result<()> {
    if path.is_dir() {
        fs::remove_dir_all(path)?;
    } else if path.exists() {
        fs::remove_file(path)?;
    }
    Ok(())
}

pub fn remove_empty_parents(path: &Path, stop_at: &Path) {
    let Some(mut current) = path.parent().map(Path::to_path_buf) else {
        return;
    };
    while current != stop_at && current.starts_with(stop_at) {
        let empty = fs::read_dir(&current)
            .map(|mut entries| entries.next().is_none())
            .unwrap_or(false);
        if !empty || fs::remove_dir(&current).is_err() {
            return;
        }
        let Some(parent) = current.parent() else {
            return;
        };
        current = parent.to_path_buf();
    }
}
