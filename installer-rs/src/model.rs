use serde::{Deserialize, Serialize};
use std::path::PathBuf;

pub const FORCED_HIDDEN_RUNTIME_MOD_ID: &str = "globe-launch";

#[derive(Clone, Debug, Deserialize)]
pub struct RuntimeMod {
    pub option_id: String,
    pub label: String,
    pub details: String,
    pub category: String,
    pub default_enabled: bool,
    pub stable: bool,
    pub assembly_name: String,
    #[serde(default)]
    pub config_relative_path: Option<String>,
    #[serde(default)]
    pub default_config_template_path: Option<String>,
}

#[derive(Clone, Debug, Deserialize)]
pub struct SupportedBuild {
    pub steam_build_id: String,
    pub game_assembly_sha256: String,
    pub global_metadata_sha256: String,
}

#[derive(Clone, Debug)]
pub struct InstallRequest {
    pub game_directory: PathBuf,
    pub payload_root: PathBuf,
    pub fallback_payload_root: Option<PathBuf>,
    pub bepinex_root: PathBuf,
    pub manifest: Vec<RuntimeMod>,
    pub selected_ids: Vec<String>,
    pub preserve_ids: Vec<String>,
    pub fallback_ids: Vec<String>,
}

#[derive(Clone, Debug)]
pub enum ProgressEvent {
    Message(String),
    Download {
        label: String,
        downloaded: u64,
        total: Option<u64>,
    },
}

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct InstallState {
    pub schema: u32,
    #[serde(default)]
    pub files: Vec<FileRecord>,
    #[serde(default)]
    pub external_files: Vec<ExternalFileRecord>,
    #[serde(default)]
    pub selected_mods: Vec<String>,
}

impl Default for InstallState {
    fn default() -> Self {
        Self {
            schema: 1,
            files: Vec::new(),
            external_files: Vec::new(),
            selected_mods: Vec::new(),
        }
    }
}

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct FileRecord {
    pub path: String,
    pub original: String,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub backup: Option<String>,
    #[serde(default)]
    pub installed_sha256: Option<String>,
}

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ExternalFileRecord {
    pub path: PathBuf,
    pub original_base64: String,
}
