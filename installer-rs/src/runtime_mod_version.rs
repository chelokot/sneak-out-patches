use anyhow::{Context, Result, bail};
use semver::Version;
use std::collections::HashSet;
use std::fs;
use std::path::Path;

pub fn read_runtime_mod_version(path: impl AsRef<Path>) -> Result<Version> {
    let path = path.as_ref();
    let bytes = fs::read(path).with_context(|| format!("failed to read {}", path.display()))?;
    let mut versions = HashSet::new();

    for alignment in 0..=1 {
        let mut text = String::new();
        let mut offset = alignment;
        while offset + 1 < bytes.len() {
            let character = bytes[offset];
            let is_ascii_text = bytes[offset + 1] == 0 && matches!(character, 0x20..=0x7e);
            if is_ascii_text {
                text.push(character as char);
            } else {
                collect_plain_semver(&text, &mut versions);
                text.clear();
            }
            offset += 2;
        }
        collect_plain_semver(&text, &mut versions);
    }

    match versions.len() {
        1 => Ok(versions.into_iter().next().unwrap()),
        0 => bail!("{} has no readable runtime-mod version", path.display()),
        _ => {
            let mut versions: Vec<_> = versions.into_iter().collect();
            versions.sort();
            bail!(
                "{} has ambiguous runtime-mod versions: {}",
                path.display(),
                versions
                    .iter()
                    .map(ToString::to_string)
                    .collect::<Vec<_>>()
                    .join(", ")
            )
        }
    }
}

fn collect_plain_semver(text: &str, versions: &mut HashSet<Version>) {
    let text = text.trim();
    if text.is_empty() {
        return;
    }
    if let Ok(version) = Version::parse(text)
        && version.pre.is_empty()
        && version.build.is_empty()
    {
        versions.insert(version);
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn utf16_bytes(text: &str) -> Vec<u8> {
        text.encode_utf16()
            .flat_map(u16::to_le_bytes)
            .collect::<Vec<_>>()
    }

    #[test]
    fn reads_a_plain_semantic_version_from_managed_strings() {
        let temporary = tempfile::tempdir().unwrap();
        let path = temporary.path().join("plugin.dll");
        let mut bytes = utf16_bytes("not a version");
        bytes.extend([0xff, 0xff]);
        bytes.extend(utf16_bytes("0.16.13"));
        bytes.extend([0xff, 0xff]);
        bytes.extend(utf16_bytes("1.0.0+build-metadata"));
        fs::write(&path, bytes).unwrap();

        assert_eq!(
            read_runtime_mod_version(path).unwrap(),
            Version::new(0, 16, 13)
        );
    }

    #[test]
    fn rejects_ambiguous_plain_versions() {
        let temporary = tempfile::tempdir().unwrap();
        let path = temporary.path().join("plugin.dll");
        let mut bytes = utf16_bytes("1.0.0");
        bytes.extend([0xff, 0xff]);
        bytes.extend(utf16_bytes("2.0.0"));
        fs::write(&path, bytes).unwrap();

        assert!(read_runtime_mod_version(path).is_err());
    }

    #[test]
    fn reads_versions_from_the_packaged_runtime_mods() {
        let root = Path::new(env!("CARGO_MANIFEST_DIR"))
            .parent()
            .unwrap()
            .join("artifacts/runtime_mods");
        for entry in fs::read_dir(root).unwrap() {
            let path = entry.unwrap().path();
            if path.extension().and_then(|extension| extension.to_str()) == Some("dll") {
                read_runtime_mod_version(&path)
                    .unwrap_or_else(|error| panic!("{}: {error}", path.display()));
            }
        }
    }
}
