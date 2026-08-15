use anyhow::Result;
#[cfg(windows)]
use std::io;

use crate::ProgressReporter;
use crate::model::{ProgressEvent, RegistryValueRecord};

const WINHTTP_OVERRIDE_KEY: &str = r"Software\Wine\AppDefaults\Sneak Out.exe\DllOverrides";
const WINHTTP_OVERRIDE_NAME: &str = "winhttp";
const WINHTTP_OVERRIDE_VALUE: &str = "native,builtin";

#[derive(Debug, PartialEq, Eq)]
enum RestoreAction<'a> {
    Preserve,
    Delete,
    Write(&'a str),
}

fn restore_action<'a>(current: Option<&str>, record: &'a RegistryValueRecord) -> RestoreAction<'a> {
    if current != Some(record.installed.as_str()) {
        RestoreAction::Preserve
    } else {
        match record.original.as_deref() {
            Some(original) => RestoreAction::Write(original),
            None => RestoreAction::Delete,
        }
    }
}

#[cfg(windows)]
pub fn is_wine_install() -> bool {
    use windows_sys::Win32::System::LibraryLoader::{GetModuleHandleA, GetProcAddress};

    let module = unsafe { GetModuleHandleA(c"ntdll.dll".as_ptr().cast()) };
    !module.is_null()
        && unsafe { GetProcAddress(module, c"wine_get_version".as_ptr().cast()) }.is_some()
}

#[cfg(not(windows))]
pub fn is_wine_install() -> bool {
    false
}

#[cfg(windows)]
fn read_registry_value(key_path: &str, name: &str) -> Result<Option<String>> {
    use winreg::RegKey;
    use winreg::enums::HKEY_CURRENT_USER;

    let root = RegKey::predef(HKEY_CURRENT_USER);
    let key = match root.open_subkey(key_path) {
        Ok(key) => key,
        Err(error) if error.kind() == io::ErrorKind::NotFound => return Ok(None),
        Err(error) => return Err(error.into()),
    };
    match key.get_value(name) {
        Ok(value) => Ok(Some(value)),
        Err(error) if error.kind() == io::ErrorKind::NotFound => Ok(None),
        Err(error) => Err(error.into()),
    }
}

#[cfg(not(windows))]
fn read_registry_value(_key_path: &str, _name: &str) -> Result<Option<String>> {
    Ok(None)
}

#[cfg(windows)]
fn write_registry_value(key_path: &str, name: &str, value: &str) -> Result<()> {
    use winreg::RegKey;
    use winreg::enums::HKEY_CURRENT_USER;

    let root = RegKey::predef(HKEY_CURRENT_USER);
    let (key, _) = root.create_subkey(key_path)?;
    key.set_value(name, &value)?;
    Ok(())
}

#[cfg(not(windows))]
fn write_registry_value(_key_path: &str, _name: &str, _value: &str) -> Result<()> {
    Ok(())
}

#[cfg(windows)]
fn delete_registry_value(key_path: &str, name: &str) -> Result<()> {
    use winreg::RegKey;
    use winreg::enums::{HKEY_CURRENT_USER, KEY_SET_VALUE};

    let root = RegKey::predef(HKEY_CURRENT_USER);
    let key = match root.open_subkey_with_flags(key_path, KEY_SET_VALUE) {
        Ok(key) => key,
        Err(error) if error.kind() == io::ErrorKind::NotFound => return Ok(()),
        Err(error) => return Err(error.into()),
    };
    match key.delete_value(name) {
        Ok(()) => Ok(()),
        Err(error) if error.kind() == io::ErrorKind::NotFound => Ok(()),
        Err(error) => Err(error.into()),
    }
}

#[cfg(not(windows))]
fn delete_registry_value(_key_path: &str, _name: &str) -> Result<()> {
    Ok(())
}

pub fn winhttp_override_record() -> Result<RegistryValueRecord> {
    Ok(RegistryValueRecord {
        key: WINHTTP_OVERRIDE_KEY.to_owned(),
        name: WINHTTP_OVERRIDE_NAME.to_owned(),
        original: read_registry_value(WINHTTP_OVERRIDE_KEY, WINHTTP_OVERRIDE_NAME)?,
        installed: WINHTTP_OVERRIDE_VALUE.to_owned(),
    })
}

pub fn enable_winhttp_override() -> Result<()> {
    write_registry_value(
        WINHTTP_OVERRIDE_KEY,
        WINHTTP_OVERRIDE_NAME,
        WINHTTP_OVERRIDE_VALUE,
    )
}

pub fn winhttp_override_is_active() -> bool {
    read_registry_value(WINHTTP_OVERRIDE_KEY, WINHTTP_OVERRIDE_NAME)
        .is_ok_and(|value| value.as_deref() == Some(WINHTTP_OVERRIDE_VALUE))
}

pub fn restore_registry_values(
    records: &[RegistryValueRecord],
    reporter: ProgressReporter<'_>,
) -> Result<()> {
    for record in records {
        let current = read_registry_value(&record.key, &record.name)?;
        match restore_action(current.as_deref(), record) {
            RestoreAction::Preserve => {
                reporter(ProgressEvent::Message(format!(
                    "Preserved a Wine registry value changed after installation: {}\\{}",
                    record.key, record.name
                )));
                continue;
            }
            RestoreAction::Delete => delete_registry_value(&record.key, &record.name)?,
            RestoreAction::Write(original) => {
                write_registry_value(&record.key, &record.name, original)?
            }
        }
        reporter(ProgressEvent::Message(format!(
            "Restored Wine configuration {}\\{}",
            record.key, record.name
        )));
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn override_record_is_game_scoped() {
        let record = winhttp_override_record().unwrap();
        assert_eq!(record.name, "winhttp");
        assert_eq!(record.installed, "native,builtin");
        assert!(
            record
                .key
                .contains(r"AppDefaults\Sneak Out.exe\DllOverrides")
        );
    }

    #[test]
    fn uninstall_restores_only_the_value_owned_by_the_installer() {
        let absent = RegistryValueRecord {
            key: "key".to_owned(),
            name: "name".to_owned(),
            original: None,
            installed: "native,builtin".to_owned(),
        };
        let previous = RegistryValueRecord {
            original: Some("builtin".to_owned()),
            ..absent.clone()
        };

        assert_eq!(
            restore_action(Some("native,builtin"), &absent),
            RestoreAction::Delete
        );
        assert_eq!(
            restore_action(Some("native,builtin"), &previous),
            RestoreAction::Write("builtin")
        );
        assert_eq!(
            restore_action(Some("native"), &previous),
            RestoreAction::Preserve
        );
    }
}
