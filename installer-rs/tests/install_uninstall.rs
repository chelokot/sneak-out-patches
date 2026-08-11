use serde_json::Value;
use sneakout_installer::{
    RuntimeMod, no_progress, read_runtime_mod_version, update_installed_runtime_mods,
};
use std::fs;
use std::path::{Path, PathBuf};
use std::process::{Command, Output};
use tempfile::TempDir;

struct Fixture {
    _temporary: TempDir,
    steam_root: PathBuf,
    game_directory: PathBuf,
    local_config: PathBuf,
    bepinex_root: PathBuf,
}

impl Fixture {
    fn new() -> Self {
        let temporary = tempfile::tempdir().unwrap();
        let steam_root = temporary.path().join("Steam");
        let game_directory = steam_root.join("steamapps/common/Sneak Out");
        let metadata = game_directory.join("Sneak Out_Data/il2cpp_data/Metadata");
        let local_config = steam_root.join("userdata/123/config/localconfig.vdf");
        let anonymous_config = steam_root.join("userdata/anonymous/config/localconfig.vdf");
        let bepinex_root = temporary.path().join("BepInExSource");
        fs::create_dir_all(&metadata).unwrap();
        fs::create_dir_all(local_config.parent().unwrap()).unwrap();
        fs::create_dir_all(anonymous_config.parent().unwrap()).unwrap();
        fs::create_dir_all(bepinex_root.join("BepInEx/core")).unwrap();
        fs::write(
            game_directory.join("GameAssembly.dll"),
            "unsupported-game-assembly",
        )
        .unwrap();
        fs::write(game_directory.join("Sneak Out.exe"), "game").unwrap();
        fs::write(
            game_directory.join("Sneak Out_Data/resources.assets"),
            "resources",
        )
        .unwrap();
        fs::write(metadata.join("global-metadata.dat"), "unsupported-metadata").unwrap();
        fs::write(
            steam_root.join("steamapps/appmanifest_2410490.acf"),
            "\"AppState\"\n{\n\t\"appid\" \"2410490\"\n\t\"buildid\" \"1\"\n}\n",
        )
        .unwrap();
        fs::write(
            &local_config,
            concat!(
                "\"UserLocalConfigStore\"\n{\n\t\"Software\"\n\t{\n",
                "\t\t\"Valve\"\n\t\t{\n\t\t\t\"Steam\"\n\t\t\t{\n",
                "\t\t\t\t\"apps\"\n\t\t\t\t{\n\t\t\t\t}\n",
                "\t\t\t}\n\t\t}\n\t}\n}\n"
            ),
        )
        .unwrap();
        fs::write(anonymous_config, "\"UserLocalConfigStore\"\n{\n}\n").unwrap();
        fs::write(
            bepinex_root.join("BepInEx/core/BepInEx.Unity.IL2CPP.dll"),
            "core",
        )
        .unwrap();
        fs::write(bepinex_root.join("winhttp.dll"), "loader").unwrap();
        fs::write(bepinex_root.join("doorstop_config.ini"), "doorstop").unwrap();
        Self {
            _temporary: temporary,
            steam_root,
            game_directory,
            local_config,
            bepinex_root,
        }
    }

    fn command(&self) -> Command {
        let repository_root = Path::new(env!("CARGO_MANIFEST_DIR")).parent().unwrap();
        let mut command = Command::new(env!("CARGO_BIN_EXE_sneakout-patches"));
        command
            .env("SNEAKOUT_PATCHES_PAYLOAD_DIR", repository_root)
            .env("SNEAKOUT_BEPINEX_DIR", &self.bepinex_root)
            .env("SNEAKOUT_STEAM_ROOTS", &self.steam_root)
            .env("SNEAKOUT_STEAM_RUNNING", "0");
        command
    }

    fn run(&self, arguments: &[&str]) -> Output {
        self.command().args(arguments).output().unwrap()
    }
}

fn output_text(output: &Output) -> String {
    format!(
        "stdout:\n{}\nstderr:\n{}",
        String::from_utf8_lossy(&output.stdout),
        String::from_utf8_lossy(&output.stderr)
    )
}

fn runtime_mod(option_id: &str, label: &str, assembly_name: &str) -> RuntimeMod {
    RuntimeMod {
        option_id: option_id.to_owned(),
        label: label.to_owned(),
        details: String::new(),
        category: "test".to_owned(),
        default_enabled: false,
        assembly_name: assembly_name.to_owned(),
        config_relative_path: None,
        default_config_template_path: None,
    }
}

fn fake_runtime_mod(version: &str, marker: &str) -> Vec<u8> {
    let mut bytes = marker.as_bytes().to_vec();
    bytes.extend([0xff, 0xff]);
    bytes.extend(version.encode_utf16().flat_map(u16::to_le_bytes));
    bytes.extend([0xff, 0xff]);
    bytes
}

#[test]
fn default_install_and_uninstall_restore_clean_state() {
    let fixture = Fixture::new();
    let original_local_config = fs::read(&fixture.local_config).unwrap();
    let install = fixture.run(&["install", "--allow-unsupported-build", "--offline"]);
    assert!(install.status.success(), "{}", output_text(&install));
    assert!(
        String::from_utf8_lossy(&install.stdout).contains("Installation complete"),
        "{}",
        output_text(&install)
    );

    let repository_root = Path::new(env!("CARGO_MANIFEST_DIR")).parent().unwrap();
    let manifest: Vec<Value> = serde_json::from_slice(
        &fs::read(repository_root.join("runtime_mods_manifest.json")).unwrap(),
    )
    .unwrap();
    for runtime_mod in manifest {
        let assembly = runtime_mod["assembly_name"].as_str().unwrap();
        let enabled = runtime_mod["default_enabled"].as_bool().unwrap();
        assert_eq!(
            fixture
                .game_directory
                .join(format!("BepInEx/plugins/{assembly}.dll"))
                .exists(),
            enabled,
            "{}",
            runtime_mod["option_id"]
        );
    }
    assert_eq!(
        fs::read_to_string(fixture.game_directory.join("winhttp.dll")).unwrap(),
        "loader"
    );
    let state: Value = serde_json::from_slice(
        &fs::read(
            fixture
                .game_directory
                .join(".sneakout-patches-install.json"),
        )
        .unwrap(),
    )
    .unwrap();
    assert!(state.get("selectedMods").is_some());
    assert!(state.get("externalFiles").is_some());
    assert!(state.get("selected_mods").is_none());
    #[cfg(not(windows))]
    {
        let local_config = fs::read_to_string(&fixture.local_config).unwrap();
        assert!(local_config.contains("XMODIFIERS=@im=none"));
        assert!(local_config.contains(r#"WINEDLLOVERRIDES=\"winhttp=n,b\""#));
    }

    let reinstall = fixture.run(&[
        "install",
        "--game-dir",
        fixture.game_directory.to_str().unwrap(),
        "--mods",
        "keyboard-layout-fix",
        "--allow-unsupported-build",
        "--offline",
    ]);
    assert!(reinstall.status.success(), "{}", output_text(&reinstall));
    assert!(
        fixture
            .game_directory
            .join("BepInEx/plugins/SneakOut.KeyboardLayoutFix.dll")
            .exists()
    );
    assert!(
        !fixture
            .game_directory
            .join("BepInEx/plugins/SneakOut.PerformanceOptimizer.dll")
            .exists()
    );

    let uninstall = fixture.run(&["uninstall", "--offline"]);
    assert!(uninstall.status.success(), "{}", output_text(&uninstall));
    assert_eq!(
        fs::read(&fixture.local_config).unwrap(),
        original_local_config
    );
    assert!(!fixture.game_directory.join("winhttp.dll").exists());
    assert!(
        !fixture
            .game_directory
            .join(".sneakout-patches-install.json")
            .exists()
    );
}

#[test]
fn flat_cli_expands_default_selection_and_removes_mods() {
    let fixture = Fixture::new();
    let install = fixture.run(&[
        "--install-mods=default,lobby-test-bot",
        "--game-dir",
        fixture.game_directory.to_str().unwrap(),
        "--allow-unsupported-build",
        "--no-update",
    ]);
    assert!(install.status.success(), "{}", output_text(&install));

    let repository_root = Path::new(env!("CARGO_MANIFEST_DIR")).parent().unwrap();
    let manifest: Vec<Value> = serde_json::from_slice(
        &fs::read(repository_root.join("runtime_mods_manifest.json")).unwrap(),
    )
    .unwrap();
    for runtime_mod in manifest {
        let id = runtime_mod["option_id"].as_str().unwrap();
        let assembly = runtime_mod["assembly_name"].as_str().unwrap();
        let expected = runtime_mod["default_enabled"].as_bool().unwrap() || id == "lobby-test-bot";
        assert_eq!(
            fixture
                .game_directory
                .join(format!("BepInEx/plugins/{assembly}.dll"))
                .exists(),
            expected,
            "{id}"
        );
    }

    let remove = fixture.run(&[
        "--remove-mods",
        "--game-dir",
        fixture.game_directory.to_str().unwrap(),
        "--no-update",
    ]);
    assert!(remove.status.success(), "{}", output_text(&remove));
    assert!(!fixture.game_directory.join("winhttp.dll").exists());
}

#[test]
fn flat_cli_preserves_a_selected_newer_local_build() {
    let fixture = Fixture::new();
    let plugins = fixture.game_directory.join("BepInEx/plugins");
    fs::create_dir_all(&plugins).unwrap();
    let private_build = fake_runtime_mod("99.0.0", "private-keyboard-build");
    fs::write(
        plugins.join("SneakOut.KeyboardLayoutFix.dll"),
        &private_build,
    )
    .unwrap();

    let install = fixture.run(&[
        "--install-mods=keyboard-layout-fix",
        "--game-dir",
        fixture.game_directory.to_str().unwrap(),
        "--allow-unsupported-build",
        "--no-update",
    ]);
    assert!(install.status.success(), "{}", output_text(&install));
    assert_eq!(
        fs::read(plugins.join("SneakOut.KeyboardLayoutFix.dll")).unwrap(),
        private_build
    );
    assert!(
        String::from_utf8_lossy(&install.stdout).contains("Keeping installed local build"),
        "{}",
        output_text(&install)
    );
}

#[test]
fn flat_cli_does_not_rewrite_an_equal_version_local_build() {
    let fixture = Fixture::new();
    let repository_root = Path::new(env!("CARGO_MANIFEST_DIR")).parent().unwrap();
    let release = repository_root.join("artifacts/runtime_mods/SneakOut.KeyboardLayoutFix.dll");
    let release_version = read_runtime_mod_version(&release).unwrap();
    let plugins = fixture.game_directory.join("BepInEx/plugins");
    fs::create_dir_all(&plugins).unwrap();
    let equal_local_build = fake_runtime_mod(&release_version.to_string(), "equal-private-build");
    fs::write(
        plugins.join("SneakOut.KeyboardLayoutFix.dll"),
        &equal_local_build,
    )
    .unwrap();

    let install = fixture.run(&[
        "--install-mods=keyboard-layout-fix",
        "--game-dir",
        fixture.game_directory.to_str().unwrap(),
        "--allow-unsupported-build",
        "--no-update",
    ]);
    assert!(install.status.success(), "{}", output_text(&install));
    assert_eq!(
        fs::read(plugins.join("SneakOut.KeyboardLayoutFix.dll")).unwrap(),
        equal_local_build
    );
}

#[test]
fn flat_cli_installs_an_embedded_mod_missing_from_the_latest_catalog() {
    let fixture = Fixture::new();
    let repository_root = Path::new(env!("CARGO_MANIFEST_DIR")).parent().unwrap();
    let latest_payload = fixture._temporary.path().join("latest-payload");
    fs::create_dir_all(latest_payload.join("artifacts/runtime_mods")).unwrap();
    fs::create_dir_all(latest_payload.join("config_templates/runtime_mods")).unwrap();
    let mut manifest: Vec<Value> = serde_json::from_slice(
        &fs::read(repository_root.join("runtime_mods_manifest.json")).unwrap(),
    )
    .unwrap();
    manifest.retain(|runtime_mod| runtime_mod["option_id"] != "lobby-test-bot");
    fs::write(
        latest_payload.join("runtime_mods_manifest.json"),
        serde_json::to_vec(&manifest).unwrap(),
    )
    .unwrap();
    fs::copy(
        repository_root.join("supported_game_build.json"),
        latest_payload.join("supported_game_build.json"),
    )
    .unwrap();
    fs::copy(
        repository_root.join("artifacts/runtime_mods/SneakOut.GlobeLaunch.dll"),
        latest_payload.join("artifacts/runtime_mods/SneakOut.GlobeLaunch.dll"),
    )
    .unwrap();
    fs::copy(
        repository_root.join("config_templates/runtime_mods/globe-launch.cfg"),
        latest_payload.join("config_templates/runtime_mods/globe-launch.cfg"),
    )
    .unwrap();

    let output = fixture
        .command()
        .env("SNEAKOUT_PATCHES_PAYLOAD_DIR", &latest_payload)
        .args([
            "--install-mods=lobby-test-bot",
            "--game-dir",
            fixture.game_directory.to_str().unwrap(),
            "--allow-unsupported-build",
        ])
        .output()
        .unwrap();
    assert!(output.status.success(), "{}", output_text(&output));
    assert_eq!(
        fs::read(
            fixture
                .game_directory
                .join("BepInEx/plugins/SneakOut.LobbyTestBot.dll"),
        )
        .unwrap(),
        fs::read(repository_root.join("artifacts/runtime_mods/SneakOut.LobbyTestBot.dll")).unwrap()
    );
}

#[test]
fn install_removes_untracked_unselected_runtime_mods() {
    let fixture = Fixture::new();
    let plugins = fixture.game_directory.join("BepInEx/plugins");
    fs::create_dir_all(&plugins).unwrap();
    fs::write(plugins.join("SneakOut.LobbyTestBot.dll"), "legacy-bot").unwrap();
    fs::write(plugins.join("ThirdParty.Plugin.dll"), "third-party").unwrap();

    let install = fixture.run(&[
        "install",
        "--game-dir",
        fixture.game_directory.to_str().unwrap(),
        "--mods",
        "keyboard-layout-fix",
        "--allow-unsupported-build",
        "--offline",
    ]);
    assert!(install.status.success(), "{}", output_text(&install));
    assert!(
        !plugins.join("SneakOut.LobbyTestBot.dll").exists(),
        "an untracked, unselected manifest DLL must be removed"
    );
    assert!(
        plugins.join("SneakOut.KeyboardLayoutFix.dll").exists(),
        "the selected manifest DLL must be installed"
    );
    assert!(
        plugins.join("SneakOut.GlobeLaunch.dll").exists(),
        "the hidden runtime mod must always be installed"
    );
    assert!(
        !String::from_utf8_lossy(&install.stdout).contains("Globe Launch"),
        "the hidden runtime mod must not be named in installer output"
    );
    assert!(
        plugins.join("ThirdParty.Plugin.dll").exists(),
        "plugins outside this manifest must be preserved"
    );
}

#[test]
fn uninstall_does_not_restore_a_preexisting_runtime_mod() {
    let fixture = Fixture::new();
    let plugins = fixture.game_directory.join("BepInEx/plugins");
    fs::create_dir_all(&plugins).unwrap();
    fs::write(plugins.join("SneakOut.LobbyTestBot.dll"), "legacy-bot").unwrap();

    let install = fixture.run(&[
        "install",
        "--game-dir",
        fixture.game_directory.to_str().unwrap(),
        "--mods",
        "lobby-test-bot",
        "--allow-unsupported-build",
        "--offline",
    ]);
    assert!(install.status.success(), "{}", output_text(&install));
    assert!(plugins.join("SneakOut.LobbyTestBot.dll").exists());

    let uninstall = fixture.run(&[
        "uninstall",
        "--game-dir",
        fixture.game_directory.to_str().unwrap(),
        "--offline",
    ]);
    assert!(uninstall.status.success(), "{}", output_text(&uninstall));
    assert!(
        !plugins.join("SneakOut.LobbyTestBot.dll").exists(),
        "uninstall must not resurrect an older build of a manifest runtime mod"
    );
}

#[test]
fn startup_updates_only_release_newer_runtime_mods() {
    let fixture = Fixture::new();
    let current = runtime_mod("current", "Current", "SneakOut.Current");
    let private = runtime_mod("private", "Private", "SneakOut.Private");
    let legacy = runtime_mod("legacy", "Legacy", "SneakOut.Legacy");
    let catalog = vec![current.clone(), private.clone(), legacy.clone()];
    let latest = vec![current.clone(), private.clone()];
    let payload = fixture._temporary.path().join("payload");
    let artifacts = payload.join("artifacts/runtime_mods");
    let plugins = fixture.game_directory.join("BepInEx/plugins");
    fs::create_dir_all(&artifacts).unwrap();
    fs::create_dir_all(&plugins).unwrap();

    let current_release = fake_runtime_mod("1.1.0", "release-current");
    let private_release = fake_runtime_mod("1.5.0", "release-private");
    fs::write(artifacts.join("SneakOut.Current.dll"), &current_release).unwrap();
    fs::write(artifacts.join("SneakOut.Private.dll"), &private_release).unwrap();
    fs::write(
        plugins.join("SneakOut.Current.dll"),
        fake_runtime_mod("1.0.0", "local-current"),
    )
    .unwrap();
    let private_local = fake_runtime_mod("2.0.0", "local-private");
    fs::write(plugins.join("SneakOut.Private.dll"), &private_local).unwrap();
    fs::write(
        plugins.join("SneakOut.Legacy.dll"),
        fake_runtime_mod("0.9.0", "local-legacy"),
    )
    .unwrap();

    let summary = update_installed_runtime_mods(
        &fixture.game_directory,
        &payload,
        &catalog,
        &latest,
        &no_progress,
    )
    .unwrap();

    assert_eq!(
        fs::read(plugins.join("SneakOut.Current.dll")).unwrap(),
        current_release
    );
    assert_eq!(
        fs::read(plugins.join("SneakOut.Private.dll")).unwrap(),
        private_local
    );
    assert_eq!(summary.updated_ids, vec!["current"]);
    assert_eq!(summary.local_newer_ids, vec!["private"]);
    assert_eq!(summary.legacy_ids, vec!["legacy"]);
    assert!(summary.unreadable_ids.is_empty());
}

#[test]
#[cfg(not(windows))]
fn running_steam_refuses_before_mutating_files() {
    let fixture = Fixture::new();
    let original_local_config = fs::read(&fixture.local_config).unwrap();
    let output = fixture
        .command()
        .env("SNEAKOUT_STEAM_RUNNING", "1")
        .args([
            "install",
            "--game-dir",
            fixture.game_directory.to_str().unwrap(),
            "--mods",
            "keyboard-layout-fix",
            "--allow-unsupported-build",
            "--offline",
        ])
        .output()
        .unwrap();
    assert!(!output.status.success(), "{}", output_text(&output));
    assert!(
        String::from_utf8_lossy(&output.stderr).contains("Quit Steam completely"),
        "{}",
        output_text(&output)
    );
    assert_eq!(
        fs::read(&fixture.local_config).unwrap(),
        original_local_config
    );
    assert!(!fixture.game_directory.join("winhttp.dll").exists());
}

#[test]
fn uninstall_restores_a_preexisting_loader() {
    let fixture = Fixture::new();
    fs::write(
        fixture.game_directory.join("winhttp.dll"),
        "original-loader",
    )
    .unwrap();
    let install = fixture.run(&[
        "install",
        "--game-dir",
        fixture.game_directory.to_str().unwrap(),
        "--mods",
        "keyboard-layout-fix",
        "--allow-unsupported-build",
        "--offline",
    ]);
    assert!(install.status.success(), "{}", output_text(&install));
    assert_eq!(
        fs::read_to_string(fixture.game_directory.join("winhttp.dll")).unwrap(),
        "loader"
    );
    let uninstall = fixture.run(&[
        "uninstall",
        "--game-dir",
        fixture.game_directory.to_str().unwrap(),
        "--offline",
    ]);
    assert!(uninstall.status.success(), "{}", output_text(&uninstall));
    assert_eq!(
        fs::read_to_string(fixture.game_directory.join("winhttp.dll")).unwrap(),
        "original-loader"
    );
}

#[test]
fn no_update_install_uses_the_payload_embedded_in_the_binary() {
    let fixture = Fixture::new();
    let output = fixture
        .command()
        .env_remove("SNEAKOUT_PATCHES_PAYLOAD_DIR")
        .env(
            "SNEAKOUT_PATCHES_CACHE_DIR",
            fixture._temporary.path().join("native-cache"),
        )
        .args([
            "--install-mods=keyboard-layout-fix",
            "--game-dir",
            fixture.game_directory.to_str().unwrap(),
            "--allow-unsupported-build",
            "--no-update",
        ])
        .output()
        .unwrap();
    assert!(output.status.success(), "{}", output_text(&output));
    assert!(
        fixture
            .game_directory
            .join("BepInEx/plugins/SneakOut.KeyboardLayoutFix.dll")
            .exists()
    );
}

#[test]
#[cfg(not(windows))]
fn running_steam_uses_only_the_active_account_configuration() {
    let fixture = Fixture::new();
    let inactive_config = fixture
        .steam_root
        .join("userdata/456/config/localconfig.vdf");
    let login_users = fixture.steam_root.join("config/loginusers.vdf");
    fs::create_dir_all(inactive_config.parent().unwrap()).unwrap();
    fs::create_dir_all(login_users.parent().unwrap()).unwrap();
    fs::write(
        &fixture.local_config,
        concat!(
            "\"UserLocalConfigStore\"\n{\n\t\"Software\"\n\t{\n",
            "\t\t\"Valve\"\n\t\t{\n\t\t\t\"Steam\"\n\t\t\t{\n",
            "\t\t\t\t\"apps\"\n\t\t\t\t{\n\t\t\t\t\t\"2410490\"\n",
            "\t\t\t\t\t{\n\t\t\t\t\t\t\"LaunchOptions\" ",
            "\"XMODIFIERS=@im=none WINEDLLOVERRIDES=\\\"winhttp=n,b\\\" %command%\"\n",
            "\t\t\t\t\t}\n\t\t\t\t}\n\t\t\t}\n\t\t}\n\t}\n}\n"
        ),
    )
    .unwrap();
    fs::write(&inactive_config, "\"UserLocalConfigStore\"\n{\n}\n").unwrap();
    fs::write(
        login_users,
        concat!(
            "\"users\"\n{\n",
            "\t\"76561197960265851\"\n\t{\n\t\t\"AutoLogin\" \"1\"\n\t}\n",
            "\t\"76561197960266184\"\n\t{\n\t\t\"AutoLogin\" \"0\"\n\t}\n",
            "}\n"
        ),
    )
    .unwrap();

    let output = fixture
        .command()
        .env("SNEAKOUT_STEAM_RUNNING", "1")
        .args([
            "install",
            "--game-dir",
            fixture.game_directory.to_str().unwrap(),
            "--mods",
            "keyboard-layout-fix",
            "--allow-unsupported-build",
            "--offline",
        ])
        .output()
        .unwrap();
    assert!(output.status.success(), "{}", output_text(&output));
    assert_eq!(
        fs::read_to_string(inactive_config).unwrap(),
        "\"UserLocalConfigStore\"\n{\n}\n"
    );
}
