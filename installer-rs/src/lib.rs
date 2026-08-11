pub mod installer;
pub mod io;
pub mod model;
pub mod payload;
pub mod runtime_mod_version;
pub mod steam;

pub use installer::{
    RuntimeModUpdateSummary, compatibility_issues, install, installed_runtime_mod_ids,
    proton_launch_configuration_required, reconcile_runtime_mod_selection, uninstall,
    update_installed_runtime_mods, validate_installed,
};
pub use model::{
    FORCED_HIDDEN_RUNTIME_MOD_ID, InstallRequest, InstallState, ProgressEvent, RuntimeMod,
    SupportedBuild,
};
pub use payload::{
    Payload, load_payload_metadata, resolve_bepinex, resolve_embedded_payload,
    resolve_latest_payload, resolve_payload,
};
pub use runtime_mod_version::read_runtime_mod_version;
pub use steam::{detect_game_directories, is_steam_client_running, resolve_game_directory};

pub type ProgressReporter<'a> = &'a dyn Fn(ProgressEvent);

pub fn no_progress(_: ProgressEvent) {}
