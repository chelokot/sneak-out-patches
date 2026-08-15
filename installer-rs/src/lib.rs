pub mod installer;
pub mod io;
pub mod model;
pub mod payload;
pub mod runtime_mod_version;
pub mod self_update;
pub mod sikarugir;
pub mod steam;
pub mod wine;

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
    Payload, load_payload_metadata, resolve_bepinex, resolve_embedded_payload, resolve_payload,
};
pub use runtime_mod_version::read_runtime_mod_version;
pub use self_update::{
    BinaryKind, PreparedSelfUpdate, launch_self_update, launch_self_update_without_relaunch,
    prepare_self_update, run_self_update_helper_if_requested,
};
pub use sikarugir::{
    SikarugirWrapper, default_sikarugir_roots, discover_sikarugir_wrappers,
    discover_sikarugir_wrappers_in, inspect_sikarugir_wrapper, launch_sikarugir_installer,
    resolve_sikarugir_installer,
};
pub use steam::{detect_game_directories, is_steam_client_running, resolve_game_directory};

pub type ProgressReporter<'a> = &'a dyn Fn(ProgressEvent);

pub fn no_progress(_: ProgressEvent) {}
