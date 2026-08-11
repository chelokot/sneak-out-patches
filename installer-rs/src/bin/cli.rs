use anyhow::{Context, Result, bail};
use clap::{Args, Parser, Subcommand};
use sneakout_installer::model::ProgressEvent;
use sneakout_installer::payload::embedded_manifest;
use sneakout_installer::{
    BinaryKind, FORCED_HIDDEN_RUNTIME_MOD_ID, InstallRequest, RuntimeMod, compatibility_issues,
    detect_game_directories, install, is_steam_client_running, launch_self_update_without_relaunch,
    load_payload_metadata, prepare_self_update, proton_launch_configuration_required,
    read_runtime_mod_version, reconcile_runtime_mod_selection, resolve_bepinex,
    resolve_embedded_payload, resolve_game_directory, resolve_payload,
    run_self_update_helper_if_requested, uninstall, validate_installed,
};
use std::collections::HashSet;
use std::io::{self, IsTerminal, Write};
use std::path::PathBuf;

#[cfg(feature = "gui")]
#[path = "gui.rs"]
mod gui;

#[derive(Debug, Parser)]
#[command(
    name = "sneakout-patches",
    version,
    about = "Sneak Out patches installer"
)]
struct Cli {
    #[command(flatten)]
    common: CommonArgs,

    /// Install an authoritative comma-separated selection. Values may include `default` or `all`.
    #[arg(long, value_delimiter = ',', conflicts_with = "remove_mods")]
    install_mods: Option<Vec<String>>,

    /// Remove all managed mods and restore replaced files.
    #[arg(long, conflicts_with = "install_mods")]
    remove_mods: bool,

    /// Install despite a game build or fingerprint mismatch.
    #[arg(long, global = true)]
    allow_unsupported_build: bool,

    #[command(subcommand)]
    command: Option<Command>,
}

#[derive(Debug, Subcommand)]
enum Command {
    /// Compatibility form for `--install-mods`.
    Install(LegacyInstallArgs),
    /// Compatibility form for `--remove-mods`.
    Uninstall,
}

#[derive(Clone, Debug, Args)]
struct CommonArgs {
    /// Confirm the detected path and prompt for choices.
    #[arg(long, global = true)]
    interactive: bool,

    /// Use an explicit Sneak Out installation.
    #[arg(long, value_name = "PATH", global = true)]
    game_dir: Option<PathBuf>,

    /// Use the embedded payload without checking GitHub for installer or mod updates.
    #[arg(long, alias = "offline", global = true)]
    no_update: bool,

    /// Accepted for compatibility; noninteractive mode is already the default.
    #[arg(long, short = 'y', hide = true, global = true)]
    yes: bool,
}

#[derive(Debug, Args)]
struct LegacyInstallArgs {
    /// Include experimental and debug mods.
    #[arg(long)]
    all: bool,

    /// Install exactly the comma-separated mod ids.
    #[arg(long, value_delimiter = ',')]
    mods: Option<Vec<String>>,
}

fn report(event: ProgressEvent) {
    if let ProgressEvent::Message(message) = event {
        println!("{message}");
    }
}

fn prompt(question: &str) -> Result<String> {
    print!("{question}");
    io::stdout().flush()?;
    let mut answer = String::new();
    io::stdin().read_line(&mut answer)?;
    Ok(answer.trim().to_owned())
}

fn ask_yes_no(question: &str, default: bool) -> Result<bool> {
    let suffix = if default { "[Y/n]" } else { "[y/N]" };
    loop {
        let answer = prompt(&format!("{question} {suffix} "))?.to_ascii_lowercase();
        match answer.as_str() {
            "" => return Ok(default),
            "y" | "yes" => return Ok(true),
            "n" | "no" => return Ok(false),
            _ => {}
        }
    }
}

fn choose_game_directory(common: &CommonArgs) -> Result<PathBuf> {
    if let Some(path) = &common.game_dir {
        return resolve_game_directory(path);
    }
    let detected = detect_game_directories()?;
    if !common.interactive {
        let path = detected
            .first()
            .context("Sneak Out was not found. Pass --game-dir PATH or use --interactive.")?;
        println!("Detected Sneak Out: {}", path.display());
        return Ok(path.clone());
    }
    let default = detected.first();
    let question = match default {
        Some(path) => format!("Sneak Out directory [{}]: ", path.display()),
        None => "Sneak Out directory: ".to_owned(),
    };
    let answer = prompt(&question)?;
    let selected = if answer.is_empty() {
        default.cloned().context("no game directory was selected")?
    } else {
        PathBuf::from(answer)
    };
    resolve_game_directory(selected)
}

fn expand_mod_selection(
    manifest: &[RuntimeMod],
    catalog: &[RuntimeMod],
    values: &[String],
) -> Result<Vec<String>> {
    let known: HashSet<&str> = catalog
        .iter()
        .map(|entry| entry.option_id.as_str())
        .collect();
    let unknown: Vec<_> = values
        .iter()
        .map(|value| value.trim())
        .filter(|value| !value.is_empty() && *value != "default" && *value != "all")
        .filter(|value| !known.contains(*value))
        .map(str::to_owned)
        .collect();
    if !unknown.is_empty() {
        bail!("unknown mod ids: {}", unknown.join(", "));
    }

    let mut expanded = Vec::new();
    for value in values.iter().map(|value| value.trim()) {
        match value {
            "" => {}
            "default" => expanded.extend(
                manifest
                    .iter()
                    .filter(|entry| entry.default_enabled)
                    .map(|entry| entry.option_id.clone()),
            ),
            "all" => expanded.extend(manifest.iter().map(|entry| entry.option_id.clone())),
            id => expanded.push(id.to_owned()),
        }
    }
    let mut unique = HashSet::new();
    Ok(expanded
        .into_iter()
        .filter(|id| unique.insert(id.clone()))
        .collect())
}

fn choose_mods(
    common: &CommonArgs,
    requested: Option<&[String]>,
    all: bool,
    manifest: &[RuntimeMod],
    catalog: &[RuntimeMod],
) -> Result<Vec<String>> {
    if let Some(values) = requested {
        return expand_mod_selection(manifest, catalog, values);
    }
    if all {
        return expand_mod_selection(manifest, catalog, &["all".to_owned()]);
    }
    if !common.interactive {
        return expand_mod_selection(manifest, catalog, &["default".to_owned()]);
    }
    println!("\nChoose runtime mods (press Enter to accept each default):");
    let mut selected = Vec::new();
    for runtime_mod in manifest {
        if ask_yes_no(
            &format!("  {} [{}]", runtime_mod.label, runtime_mod.category),
            runtime_mod.default_enabled,
        )? {
            selected.push(runtime_mod.option_id.clone());
        }
    }
    Ok(selected)
}

fn ensure_forced_selection(selected: &mut Vec<String>) {
    if !selected.iter().any(|id| id == FORCED_HIDDEN_RUNTIME_MOD_ID) {
        selected.push(FORCED_HIDDEN_RUNTIME_MOD_ID.to_owned());
    }
}

fn merged_catalog(
    latest: &[RuntimeMod],
    embedded: &[RuntimeMod],
) -> (Vec<RuntimeMod>, HashSet<String>) {
    let latest_ids: HashSet<&str> = latest
        .iter()
        .map(|runtime_mod| runtime_mod.option_id.as_str())
        .collect();
    let mut catalog = latest.to_vec();
    let mut fallback_ids = HashSet::new();
    for runtime_mod in embedded
        .iter()
        .filter(|runtime_mod| !latest_ids.contains(runtime_mod.option_id.as_str()))
    {
        fallback_ids.insert(runtime_mod.option_id.clone());
        catalog.push(runtime_mod.clone());
    }
    (catalog, fallback_ids)
}

fn installed_mods_not_older_than_release(
    game_directory: &std::path::Path,
    payload_root: &std::path::Path,
    fallback_payload_root: &std::path::Path,
    catalog: &[RuntimeMod],
    fallback_ids: &HashSet<String>,
    selected_ids: &[String],
) -> Result<Vec<String>> {
    let selected: HashSet<&str> = selected_ids.iter().map(String::as_str).collect();
    let mut preserve_ids = Vec::new();
    for runtime_mod in catalog
        .iter()
        .filter(|runtime_mod| selected.contains(runtime_mod.option_id.as_str()))
    {
        let installed = game_directory
            .join("BepInEx/plugins")
            .join(format!("{}.dll", runtime_mod.assembly_name));
        let source_root = if fallback_ids.contains(&runtime_mod.option_id) {
            fallback_payload_root
        } else {
            payload_root
        };
        let release = source_root
            .join("artifacts/runtime_mods")
            .join(format!("{}.dll", runtime_mod.assembly_name));
        let local_is_not_older = if installed.exists() {
            match read_runtime_mod_version(&installed) {
                Ok(installed_version) => installed_version >= read_runtime_mod_version(&release)?,
                Err(_) => false,
            }
        } else {
            false
        };
        if local_is_not_older {
            preserve_ids.push(runtime_mod.option_id.clone());
        }
    }
    Ok(preserve_ids)
}

fn run_install(
    common: &CommonArgs,
    requested: Option<&[String]>,
    all: bool,
    allow_unsupported_build: bool,
) -> Result<()> {
    let game_directory = choose_game_directory(common)?;
    let payload = resolve_payload(common.no_update, &report)?;
    let (manifest, supported_build) = load_payload_metadata(&payload.root)?;
    let embedded_payload = resolve_embedded_payload()?;
    let embedded = embedded_manifest()?;
    let (catalog, fallback_ids) = merged_catalog(&manifest, &embedded);
    let mut selected_ids = choose_mods(common, requested, all, &manifest, &catalog)?;
    ensure_forced_selection(&mut selected_ids);
    if proton_launch_configuration_required() && is_steam_client_running() {
        bail!(
            "Steam is running and the required Proton loader/input environment is not active. \
             Quit Steam completely and run the same install command again. No game files were changed."
        );
    }
    let issues = compatibility_issues(&game_directory, &supported_build)?;
    if !issues.is_empty() {
        eprintln!("Unsupported game installation:\n- {}", issues.join("\n- "));
        let accepted = allow_unsupported_build
            || (common.interactive && ask_yes_no("Install anyway?", false)?);
        if !accepted {
            bail!(
                "installation stopped before modifying the game; use \
                 --allow-unsupported-build to override"
            );
        }
    }

    println!("Payload: {}", payload.source);
    println!(
        "Installing {} mods into {}",
        selected_ids
            .iter()
            .filter(|id| id.as_str() != FORCED_HIDDEN_RUNTIME_MOD_ID)
            .count(),
        game_directory.display()
    );
    let bepinex_root = resolve_bepinex(&report)?;
    let preserve_ids = installed_mods_not_older_than_release(
        &game_directory,
        &payload.root,
        &embedded_payload.root,
        &catalog,
        &fallback_ids,
        &selected_ids,
    )?;
    for id in &preserve_ids {
        if id != FORCED_HIDDEN_RUNTIME_MOD_ID {
            println!("Keeping installed local build: {id}");
        }
    }
    let request = InstallRequest {
        game_directory: game_directory.clone(),
        payload_root: payload.root.clone(),
        fallback_payload_root: Some(embedded_payload.root.clone()),
        bepinex_root,
        manifest: catalog.clone(),
        selected_ids: selected_ids.clone(),
        preserve_ids,
        fallback_ids: fallback_ids.iter().cloned().collect(),
    };
    install(&request, &report)?;
    reconcile_runtime_mod_selection(&game_directory, &catalog, &selected_ids, &report)?;
    let mut problems =
        validate_installed(&game_directory, &manifest, &selected_ids, &payload.root)?;
    let fallback_manifest: Vec<_> = catalog
        .iter()
        .filter(|runtime_mod| fallback_ids.contains(&runtime_mod.option_id))
        .cloned()
        .collect();
    problems.extend(validate_installed(
        &game_directory,
        &fallback_manifest,
        &selected_ids,
        &embedded_payload.root,
    )?);
    if !problems.is_empty() {
        bail!(
            "installation validation failed:\n- {}",
            problems.join("\n- ")
        );
    }
    println!("Installed mods:");
    for runtime_mod in catalog.iter().filter(|entry| {
        entry.option_id != FORCED_HIDDEN_RUNTIME_MOD_ID && selected_ids.contains(&entry.option_id)
    }) {
        println!("- {}", runtime_mod.label);
    }
    println!(
        "Installation complete. The first modded launch may take longer while BepInEx generates interop."
    );
    Ok(())
}

fn run_uninstall(common: &CommonArgs) -> Result<()> {
    let game_directory = choose_game_directory(common)?;
    if common.interactive
        && !ask_yes_no(
            &format!("Remove patches from {}?", game_directory.display()),
            true,
        )?
    {
        println!("Cancelled.");
        return Ok(());
    }
    let payload = resolve_payload(common.no_update, &report)?;
    let (manifest, _) = load_payload_metadata(&payload.root)?;
    let (catalog, _) = merged_catalog(&manifest, &embedded_manifest()?);
    uninstall(&game_directory, &catalog, &report)?;
    println!(
        "Sneak Out patches removed from {}",
        game_directory.display()
    );
    Ok(())
}

fn should_launch_gui(cli: &Cli) -> bool {
    cli.install_mods.is_none()
        && !cli.remove_mods
        && cli.command.is_none()
        && !cli.common.interactive
        && cli.common.game_dir.is_none()
        && !cli.common.yes
        && !cli.allow_unsupported_build
}

fn start_self_update_if_available(no_update: bool) {
    if no_update {
        return;
    }
    match prepare_self_update(BinaryKind::Cli, &report) {
        Ok(Some(update)) => {
            let version = update.version().clone();
            match launch_self_update_without_relaunch(&update) {
                Ok(()) => println!("Installer {version} downloaded; applying the update..."),
                Err(error) => {
                    eprintln!(
                        "Could not launch installer update: {error:#}. The completed mod operation is unaffected."
                    );
                }
            }
        }
        Ok(None) => {}
        Err(error) => {
            eprintln!(
                "Could not check for an installer update: {error:#}. The completed mod operation is unaffected."
            );
        }
    }
}

#[cfg(feature = "gui")]
fn run_gui(no_update: bool) -> Result<()> {
    gui::run(no_update, BinaryKind::Cli)
        .map_err(|error| anyhow::anyhow!("failed to launch graphical installer: {error}"))
}

#[cfg(not(feature = "gui"))]
fn run_gui(_no_update: bool) -> Result<()> {
    bail!("this build does not include the graphical installer")
}

fn main() {
    if let Some(result) = run_self_update_helper_if_requested() {
        if let Err(error) = result {
            eprintln!("Could not apply installer update: {error:#}");
            std::process::exit(1);
        }
        return;
    }
    let cli = Cli::parse();
    if cli.common.interactive && !(io::stdin().is_terminal() && io::stdout().is_terminal()) {
        eprintln!("--interactive requires a terminal.");
        std::process::exit(1);
    }
    let launches_gui = should_launch_gui(&cli);
    let result = if launches_gui {
        run_gui(cli.common.no_update)
    } else {
        match (&cli.install_mods, cli.remove_mods, &cli.command) {
            (Some(_), _, Some(_)) | (_, true, Some(_)) => Err(anyhow::anyhow!(
                "do not combine --install-mods or --remove-mods with a subcommand"
            )),
            (Some(mods), false, None) => {
                run_install(&cli.common, Some(mods), false, cli.allow_unsupported_build)
            }
            (None, true, None) => run_uninstall(&cli.common),
            (None, false, Some(Command::Install(args))) => run_install(
                &cli.common,
                args.mods.as_deref(),
                args.all,
                cli.allow_unsupported_build,
            ),
            (None, false, Some(Command::Uninstall)) => run_uninstall(&cli.common),
            (None, false, None) => Err(anyhow::anyhow!(
                "choose --install-mods=default, --install-mods=all, --install-mods=<ids>, or --remove-mods; pass no options to open the graphical installer"
            )),
            (Some(_), true, None) => unreachable!("clap rejects conflicting actions"),
        }
    };
    if let Err(error) = result {
        eprintln!("{error:#}");
        std::process::exit(1);
    }
    if !launches_gui {
        start_self_update_if_available(cli.common.no_update);
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn runtime_mod(id: &str, default_enabled: bool) -> RuntimeMod {
        RuntimeMod {
            option_id: id.to_owned(),
            label: id.to_owned(),
            details: String::new(),
            category: "test".to_owned(),
            default_enabled,
            stable: true,
            assembly_name: format!("SneakOut.{id}"),
            config_relative_path: None,
            default_config_template_path: None,
        }
    }

    #[test]
    fn default_and_explicit_ids_are_unioned_without_duplicates() {
        let manifest = vec![runtime_mod("stable", true), runtime_mod("optional", false)];
        let selected = expand_mod_selection(
            &manifest,
            &manifest,
            &[
                "default".to_owned(),
                "optional".to_owned(),
                "stable".to_owned(),
            ],
        )
        .unwrap();

        assert_eq!(selected, vec!["stable", "optional"]);
    }

    #[test]
    fn all_expands_to_the_complete_manifest() {
        let manifest = vec![runtime_mod("stable", true), runtime_mod("optional", false)];
        let selected = expand_mod_selection(&manifest, &manifest, &["all".to_owned()]).unwrap();

        assert_eq!(selected, vec!["stable", "optional"]);
    }

    #[test]
    fn no_arguments_launch_the_gui() {
        let cli = Cli::try_parse_from(["sneakout-patches"]).unwrap();

        assert!(should_launch_gui(&cli));
    }

    #[test]
    fn no_update_without_an_action_launches_the_gui_offline() {
        let cli = Cli::try_parse_from(["sneakout-patches", "--no-update"]).unwrap();

        assert!(should_launch_gui(&cli));
        assert!(cli.common.no_update);
    }

    #[test]
    fn command_line_actions_do_not_launch_the_gui() {
        let cli = Cli::try_parse_from(["sneakout-patches", "--install-mods=default"]).unwrap();

        assert!(!should_launch_gui(&cli));
    }
}
