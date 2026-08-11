#![cfg_attr(windows, windows_subsystem = "windows")]

use anyhow::{Result, bail};
use base64::prelude::{BASE64_STANDARD, Engine as _};
use clap::Parser;
use eframe::egui::{self, Color32, RichText};
use sneakout_installer::payload::embedded_manifest;
use sneakout_installer::{
    FORCED_HIDDEN_RUNTIME_MOD_ID, InstallRequest, ProgressEvent, RuntimeMod, compatibility_issues,
    detect_game_directories, install, installed_runtime_mod_ids, is_steam_client_running,
    load_payload_metadata, proton_launch_configuration_required, read_runtime_mod_version,
    resolve_bepinex, resolve_embedded_payload, resolve_game_directory, resolve_latest_payload,
    resolve_payload, uninstall, update_installed_runtime_mods, validate_installed,
};
use std::collections::HashSet;
use std::path::PathBuf;
use std::sync::mpsc::TryRecvError;
use std::sync::mpsc::{self, Receiver, Sender};
use std::thread;
use std::time::Duration;

const APP_BACKGROUND: Color32 = Color32::from_rgb(6, 20, 29);
const SHELL_BACKGROUND: Color32 = Color32::from_rgb(29, 41, 61);
const CARD_BACKGROUND: Color32 = Color32::from_rgb(10, 22, 34);
const CONTROL_BACKGROUND: Color32 = Color32::from_rgb(18, 43, 57);
const ACCENT: Color32 = Color32::from_rgb(13, 145, 181);
const ACCENT_HOVERED: Color32 = Color32::from_rgb(18, 166, 204);
const ACTIVE_ACCENT: Color32 = Color32::from_rgb(229, 103, 31);
const ACTIVE_ACCENT_HOVERED: Color32 = Color32::from_rgb(244, 121, 43);
const PRIMARY_TEXT: Color32 = Color32::from_rgb(246, 248, 250);
const SECONDARY_TEXT: Color32 = Color32::from_rgb(183, 199, 211);
const SUCCESS: Color32 = Color32::from_rgb(119, 218, 55);
const ERROR: Color32 = Color32::from_rgb(215, 48, 39);
const LEGACY: Color32 = Color32::from_rgb(240, 172, 67);
const BODY_TEXT_SIZE: f32 = 17.0;
const UI_TEXT_SIZE: f32 = 20.0;
const CARD_TEXT_GAP: f32 = 7.0;
const BUTTON_HEIGHT: f32 = 44.0;
const SECTION_FONT_FAMILY: &str = "Open Sans Bold";
const SECTION_FONT_DATA: &str = "open-sans-bold";
const LOBBY_TEST_BOT_ID: &str = "lobby-test-bot";

#[derive(Debug, Parser)]
#[command(
    name = "SneakOutPatches",
    version,
    about = "Sneak Out patches graphical installer"
)]
struct GuiArgs {
    /// Use the embedded payload without checking GitHub for updates.
    #[arg(long, alias = "offline")]
    no_update: bool,
}

enum WorkerMessage {
    Progress(ProgressEvent),
    StartupFinished(Result<StartupSnapshot, String>),
    Finished(WorkerOperation, Result<String, String>),
}

#[derive(Clone, Copy)]
enum WorkerOperation {
    Install,
    Uninstall,
}

struct StartupSnapshot {
    manifest: Vec<RuntimeMod>,
    legacy_ids: HashSet<String>,
    installed_ids: HashSet<String>,
    selected_ids: HashSet<String>,
    message: String,
}

#[derive(Clone)]
struct InstallJob {
    game_directory: PathBuf,
    selected_ids: Vec<String>,
    catalog: Vec<RuntimeMod>,
    allow_unsupported_build: bool,
    offline: bool,
}

struct InstallerApp {
    manifest: Vec<RuntimeMod>,
    legacy_ids: HashSet<String>,
    installed_ids: HashSet<String>,
    selected: HashSet<String>,
    game_directory: String,
    allow_unsupported_build: bool,
    offline: bool,
    updates_disabled: bool,
    busy: bool,
    status: String,
    log: Vec<String>,
    download: Option<(String, f32)>,
    worker: Option<Receiver<WorkerMessage>>,
}

impl InstallerApp {
    fn new(context: &eframe::CreationContext<'_>, no_update: bool) -> Self {
        install_fonts(&context.egui_ctx);

        let mut visuals = egui::Visuals::dark();
        visuals.panel_fill = APP_BACKGROUND;
        visuals.window_fill = SHELL_BACKGROUND;
        visuals.window_stroke = egui::Stroke::NONE;
        visuals.window_corner_radius = egui::CornerRadius::same(18);
        visuals.faint_bg_color = CONTROL_BACKGROUND;
        visuals.extreme_bg_color = APP_BACKGROUND;
        visuals.text_edit_bg_color = Some(CONTROL_BACKGROUND);
        visuals.selection.bg_fill = ACCENT;
        visuals.selection.stroke = egui::Stroke::new(1.0, Color32::WHITE);
        visuals.override_text_color = Some(PRIMARY_TEXT);
        visuals.weak_text_color = Some(SECONDARY_TEXT);
        visuals.button_frame = true;
        visuals.collapsing_header_frame = false;
        visuals.indent_has_left_vline = false;
        visuals.interact_cursor = Some(egui::CursorIcon::PointingHand);

        visuals.widgets.noninteractive.bg_fill = Color32::TRANSPARENT;
        visuals.widgets.noninteractive.weak_bg_fill = Color32::TRANSPARENT;
        visuals.widgets.noninteractive.bg_stroke = egui::Stroke::NONE;
        visuals.widgets.noninteractive.corner_radius = egui::CornerRadius::same(10);
        visuals.widgets.noninteractive.fg_stroke = egui::Stroke::new(1.0, PRIMARY_TEXT);

        visuals.widgets.inactive.bg_fill = ACCENT;
        visuals.widgets.inactive.weak_bg_fill = ACCENT;
        visuals.widgets.inactive.bg_stroke = egui::Stroke::new(1.5, Color32::WHITE);
        visuals.widgets.inactive.corner_radius = egui::CornerRadius::same(11);
        visuals.widgets.inactive.fg_stroke = egui::Stroke::new(1.0, PRIMARY_TEXT);

        visuals.widgets.hovered.bg_fill = ACCENT_HOVERED;
        visuals.widgets.hovered.weak_bg_fill = ACCENT_HOVERED;
        visuals.widgets.hovered.bg_stroke = egui::Stroke::new(2.0, Color32::WHITE);
        visuals.widgets.hovered.corner_radius = egui::CornerRadius::same(11);
        visuals.widgets.hovered.fg_stroke = egui::Stroke::new(1.0, Color32::WHITE);
        visuals.widgets.hovered.expansion = 0.0;

        visuals.widgets.active.bg_fill = ACTIVE_ACCENT_HOVERED;
        visuals.widgets.active.weak_bg_fill = ACTIVE_ACCENT_HOVERED;
        visuals.widgets.active.bg_stroke = egui::Stroke::new(2.0, Color32::WHITE);
        visuals.widgets.active.corner_radius = egui::CornerRadius::same(11);
        visuals.widgets.active.fg_stroke = egui::Stroke::new(1.0, Color32::WHITE);
        visuals.widgets.active.expansion = 0.0;

        visuals.widgets.open.bg_fill = ACCENT_HOVERED;
        visuals.widgets.open.weak_bg_fill = ACCENT_HOVERED;
        visuals.widgets.open.bg_stroke = egui::Stroke::new(2.0, Color32::WHITE);
        visuals.widgets.open.corner_radius = egui::CornerRadius::same(11);

        context.egui_ctx.all_styles_mut(|style| {
            use egui::{FontFamily, FontId, TextStyle};

            style.visuals = visuals.clone();
            style.text_styles.insert(
                TextStyle::Heading,
                FontId::new(UI_TEXT_SIZE, FontFamily::Proportional),
            );
            style.text_styles.insert(
                TextStyle::Body,
                FontId::new(BODY_TEXT_SIZE, FontFamily::Proportional),
            );
            style.text_styles.insert(
                TextStyle::Button,
                FontId::new(UI_TEXT_SIZE, FontFamily::Proportional),
            );
            style.text_styles.insert(
                TextStyle::Small,
                FontId::new(BODY_TEXT_SIZE, FontFamily::Proportional),
            );
            style.text_styles.insert(
                TextStyle::Monospace,
                FontId::new(BODY_TEXT_SIZE, FontFamily::Monospace),
            );
            style.spacing.item_spacing = egui::vec2(10.0, 10.0);
            style.spacing.button_padding = egui::vec2(12.0, 7.0);
            style.spacing.interact_size.y = 34.0;
            style.spacing.icon_width = 20.0;
            style.spacing.icon_width_inner = 12.0;
            style.spacing.scroll = egui::style::ScrollStyle::solid();
        });

        let manifest = embedded_manifest().unwrap_or_default();
        let defaults = default_selection(&manifest, &HashSet::new());
        let detected = detect_game_directories().unwrap_or_default();
        let game_directory = detected
            .first()
            .map(|path| path.display().to_string())
            .unwrap_or_default();
        let installed = if game_directory.is_empty() {
            Vec::new()
        } else {
            installed_runtime_mod_ids(PathBuf::from(&game_directory).as_path(), &manifest)
        };
        let installed_ids: HashSet<_> = installed.into_iter().collect();
        let mut selected = if installed_ids.is_empty() {
            defaults
        } else {
            installed_ids.clone()
        };
        ensure_forced_selection(&mut selected);
        let (busy, status, worker) = if no_update {
            (
                false,
                "Update checks disabled; using the embedded catalog.".to_owned(),
                None,
            )
        } else {
            let (sender, receiver) = mpsc::channel();
            let startup_game_directory =
                (!game_directory.is_empty()).then(|| PathBuf::from(game_directory.as_str()));
            thread::spawn(move || {
                let result = perform_startup_check(startup_game_directory, &sender)
                    .map_err(|error| format!("{error:#}"));
                let _ = sender.send(WorkerMessage::StartupFinished(result));
            });
            (
                true,
                "Checking the latest GitHub release...".to_owned(),
                Some(receiver),
            )
        };
        Self {
            manifest,
            legacy_ids: HashSet::new(),
            installed_ids,
            selected,
            game_directory,
            allow_unsupported_build: false,
            offline: no_update,
            updates_disabled: no_update,
            busy,
            status,
            log: Vec::new(),
            download: None,
            worker,
        }
    }

    fn append_log(&mut self, message: String) {
        self.status = message.clone();
        self.log.push(message);
        if self.log.len() > 250 {
            self.log.drain(..self.log.len() - 250);
        }
    }

    fn poll_worker(&mut self) {
        let mut messages = Vec::new();
        let mut disconnected = false;
        if let Some(receiver) = &self.worker {
            loop {
                match receiver.try_recv() {
                    Ok(message) => messages.push(message),
                    Err(TryRecvError::Empty) => break,
                    Err(TryRecvError::Disconnected) => {
                        disconnected = true;
                        break;
                    }
                }
            }
        }
        let mut finished = false;
        for message in messages {
            match message {
                WorkerMessage::Progress(ProgressEvent::Message(message)) => {
                    self.download = None;
                    self.append_log(message);
                }
                WorkerMessage::Progress(ProgressEvent::Download {
                    label,
                    downloaded,
                    total,
                }) => {
                    let fraction = total
                        .filter(|total| *total > 0)
                        .map(|total| downloaded as f32 / total as f32)
                        .unwrap_or(0.0)
                        .clamp(0.0, 1.0);
                    self.status = format!(
                        "Downloading {label}: {:.1} MB{}",
                        downloaded as f64 / 1_048_576.0,
                        total
                            .map(|total| format!(" / {:.1} MB", total as f64 / 1_048_576.0))
                            .unwrap_or_default()
                    );
                    self.download = Some((label, fraction));
                }
                WorkerMessage::StartupFinished(result) => {
                    finished = true;
                    self.busy = false;
                    self.download = None;
                    match result {
                        Ok(snapshot) => {
                            self.manifest = snapshot.manifest;
                            self.legacy_ids = snapshot.legacy_ids;
                            self.installed_ids = snapshot.installed_ids;
                            self.selected = snapshot.selected_ids;
                            self.append_log(snapshot.message);
                        }
                        Err(error) => self.append_log(format!(
                            "Error: update check failed; using the embedded catalog: {error}"
                        )),
                    }
                    self.worker = None;
                }
                WorkerMessage::Finished(operation, result) => {
                    finished = true;
                    self.busy = false;
                    self.download = None;
                    match result {
                        Ok(message) => {
                            match operation {
                                WorkerOperation::Install => {
                                    self.installed_ids = self.selected.clone();
                                }
                                WorkerOperation::Uninstall => {
                                    self.installed_ids.clear();
                                    self.select_defaults();
                                }
                            }
                            self.append_log(message);
                        }
                        Err(error) => self.append_log(format!("Error: {error}")),
                    }
                    self.worker = None;
                }
            }
        }
        if disconnected && !finished && self.busy {
            self.busy = false;
            self.download = None;
            self.worker = None;
            self.append_log("Error: the installer worker stopped unexpectedly.".to_owned());
        }
    }

    fn validate_job(&self) -> Result<InstallJob> {
        if self.game_directory.trim().is_empty() {
            bail!("select the Sneak Out installation directory");
        }
        let mut selected_ids: Vec<_> = self.selected.iter().cloned().collect();
        ensure_forced_selection_vec(&mut selected_ids);
        Ok(InstallJob {
            game_directory: PathBuf::from(self.game_directory.trim()),
            selected_ids,
            catalog: self.manifest.clone(),
            allow_unsupported_build: self.allow_unsupported_build,
            offline: self.offline,
        })
    }

    fn start_install(&mut self) {
        let job = match self.validate_job() {
            Ok(job) => job,
            Err(error) => {
                self.append_log(format!("Error: {error}"));
                return;
            }
        };
        let (sender, receiver) = mpsc::channel();
        self.worker = Some(receiver);
        self.busy = true;
        self.log.clear();
        self.append_log("Preparing installation...".to_owned());
        thread::spawn(move || {
            let result = perform_install(job, &sender).map_err(|error| format!("{error:#}"));
            let _ = sender.send(WorkerMessage::Finished(WorkerOperation::Install, result));
        });
    }

    fn start_uninstall(&mut self) {
        if self.game_directory.trim().is_empty() {
            self.append_log("Error: select the Sneak Out installation directory".to_owned());
            return;
        }
        let game_directory = PathBuf::from(self.game_directory.trim());
        let manifest = self.manifest.clone();
        let (sender, receiver) = mpsc::channel();
        self.worker = Some(receiver);
        self.busy = true;
        self.log.clear();
        self.append_log("Preparing removal...".to_owned());
        thread::spawn(move || {
            let reporter = |event| {
                let _ = sender.send(WorkerMessage::Progress(event));
            };
            let result = (|| -> Result<String> {
                let game_directory = resolve_game_directory(game_directory)?;
                uninstall(&game_directory, &manifest, &reporter)?;
                Ok("Sneak Out patches were removed and replaced files were restored.".to_owned())
            })()
            .map_err(|error| format!("{error:#}"));
            let _ = sender.send(WorkerMessage::Finished(WorkerOperation::Uninstall, result));
        });
    }

    fn select_defaults(&mut self) {
        self.selected = default_selection(&self.manifest, &self.legacy_ids);
    }

    fn select_debug(&mut self) {
        self.selected = debug_selection(&self.manifest, &self.legacy_ids);
    }

    fn show_mods_pane(&mut self, ui: &mut egui::Ui, grouped_mods: &[(String, Vec<RuntimeMod>)]) {
        ui.set_width(ui.available_width());
        card(CARD_BACKGROUND, 16, 16).show(ui, |ui| {
            ui.set_width(ui.available_width());
            ui.horizontal(|ui| {
                ui.spacing_mut().item_spacing.x = 0.0;
                ui.label(section_text("Mods"));
                ui.label(large_text(format!(
                    " · {}",
                    visible_selection_count(&self.selected)
                )));
            });
            let default_active =
                self.selected == default_selection(&self.manifest, &self.legacy_ids);
            let debug_active = self.selected == debug_selection(&self.manifest, &self.legacy_ids);
            let all_active = visible_selection_count(&self.selected)
                == self
                    .manifest
                    .iter()
                    .filter(|runtime_mod| runtime_mod.option_id != FORCED_HIDDEN_RUNTIME_MOD_ID)
                    .count();
            let clear_active = visible_selection_count(&self.selected) == 0;
            ui.horizontal(|ui| {
                if ui
                    .add_enabled(!self.busy, preset_button("Defaults", default_active))
                    .clicked()
                {
                    self.select_defaults();
                }
                if ui
                    .add_enabled(!self.busy, preset_button("Debug", debug_active))
                    .clicked()
                {
                    self.select_debug();
                }
                if ui
                    .add_enabled(!self.busy, preset_button("All", all_active))
                    .clicked()
                {
                    let mut selected: HashSet<_> = self
                        .manifest
                        .iter()
                        .map(|runtime_mod| runtime_mod.option_id.clone())
                        .collect();
                    ensure_forced_selection(&mut selected);
                    self.selected = selected;
                }
                if ui
                    .add_enabled(!self.busy, preset_button("Clear", clear_active))
                    .clicked()
                {
                    self.selected = HashSet::from([FORCED_HIDDEN_RUNTIME_MOD_ID.to_owned()]);
                }
            });
        });

        ui.add_space(10.0);
        let list_height = ui.available_height();
        egui::ScrollArea::vertical()
            .id_salt("runtime-mods")
            .max_height(list_height)
            .auto_shrink([false, false])
            .show(ui, |ui| {
                for (category, runtime_mods) in grouped_mods {
                    card(CARD_BACKGROUND, 16, 14).show(ui, |ui| {
                        ui.set_width(ui.available_width());
                        let category_selected = runtime_mods
                            .iter()
                            .filter(|runtime_mod| self.selected.contains(&runtime_mod.option_id))
                            .count();
                        ui.horizontal(|ui| {
                            ui.label(section_text(category_title(category)));
                            ui.with_layout(
                                egui::Layout::right_to_left(egui::Align::Center),
                                |ui| {
                                    ui.label(description_text(format!(
                                        "{category_selected}/{} enabled",
                                        runtime_mods.len()
                                    )));
                                },
                            );
                        });

                        let available_width = ui.available_width();
                        let columns = if available_width >= 980.0 {
                            3
                        } else if available_width >= 620.0 {
                            2
                        } else {
                            1
                        };
                        let gap = 10.0;
                        let card_width =
                            (available_width - gap * (columns - 1) as f32) / columns as f32;
                        let card_height = self
                            .manifest
                            .iter()
                            .map(|runtime_mod| {
                                selectable_card_height(
                                    ui,
                                    &runtime_mod.label,
                                    &runtime_mod.details,
                                    card_width,
                                )
                            })
                            .fold(0.0, f32::max);
                        for row in runtime_mods.chunks(columns) {
                            ui.horizontal_top(|ui| {
                                ui.spacing_mut().item_spacing.x = gap;
                                for runtime_mod in row {
                                    let enabled = self.selected.contains(&runtime_mod.option_id);
                                    let legacy = self.legacy_ids.contains(&runtime_mod.option_id);
                                    let interactive = !self.busy;
                                    let response = selectable_card(
                                        ui,
                                        &runtime_mod.label,
                                        &runtime_mod.details,
                                        legacy,
                                        enabled,
                                        interactive,
                                        card_width,
                                        card_height,
                                    );
                                    if response.clicked() {
                                        if enabled {
                                            self.selected.remove(&runtime_mod.option_id);
                                        } else {
                                            self.selected.insert(runtime_mod.option_id.clone());
                                        }
                                    }
                                }
                            });
                        }
                    });
                    ui.add_space(10.0);
                }
            });
    }

    fn show_controls_pane(&mut self, ui: &mut egui::Ui) {
        ui.set_width(ui.available_width());
        card(CARD_BACKGROUND, 16, 18).show(ui, |ui| {
            ui.set_width(ui.available_width());
            ui.label(section_text("Game installation"));
            ui.add_space(4.0);

            card(CONTROL_BACKGROUND, 12, 14).show(ui, |ui| {
                ui.set_width(ui.available_width());
                let path = if self.game_directory.trim().is_empty() {
                    "No directory selected"
                } else {
                    self.game_directory.trim()
                };
                let path_text = if self.game_directory.trim().is_empty() {
                    description_text(path)
                } else {
                    normal_text(path)
                }
                .monospace();
                ui.add(egui::Label::new(path_text).wrap());
            });

            ui.horizontal(|ui| {
                let choose_folder = ui.add_enabled(
                    !self.busy,
                    control_button("Choose folder…").min_size(egui::vec2(150.0, BUTTON_HEIGHT)),
                );
                if choose_folder.clicked()
                    && let Some(path) = rfd::FileDialog::new()
                        .set_title("Select the Sneak Out directory")
                        .pick_folder()
                {
                    self.game_directory = path.display().to_string();
                }

                if ui
                    .add_enabled(
                        !self.busy,
                        control_button("Auto-detect").min_size(egui::vec2(130.0, BUTTON_HEIGHT)),
                    )
                    .clicked()
                {
                    match detect_game_directories() {
                        Ok(paths) if !paths.is_empty() => {
                            self.game_directory = paths[0].display().to_string();
                            self.append_log("Detected the Steam installation.".to_owned());
                        }
                        Ok(_) => self.append_log(
                            "Sneak Out was not detected. Select its directory manually.".to_owned(),
                        ),
                        Err(error) => self.append_log(format!("Detection failed: {error:#}")),
                    }
                }
            });
        });

        ui.add_space(12.0);
        card(CARD_BACKGROUND, 16, 16).show(ui, |ui| {
            ui.set_width(ui.available_width());
            ui.label(section_text("Advanced options"));
            let option_width = (ui.available_width() - 10.0) / 2.0;
            let option_height = selectable_card_height(
                ui,
                "Allow unsupported build",
                "Skip the build and native-file compatibility checks.",
                option_width,
            )
            .max(selectable_card_height(
                ui,
                "Use embedded payload",
                if self.updates_disabled {
                    "Forced by --no-update; no GitHub request will be made."
                } else {
                    "Use bundled mods for manual installation after the startup release check."
                },
                option_width,
            ));
            ui.horizontal_top(|ui| {
                let unsupported = selectable_card(
                    ui,
                    "Allow unsupported build",
                    "Skip the build and native-file compatibility checks.",
                    false,
                    self.allow_unsupported_build,
                    !self.busy,
                    option_width,
                    option_height,
                );
                if unsupported.clicked() {
                    self.allow_unsupported_build = !self.allow_unsupported_build;
                }

                let embedded = selectable_card(
                    ui,
                    "Use embedded payload",
                    if self.updates_disabled {
                        "Forced by --no-update; no GitHub request will be made."
                    } else {
                        "Use bundled mods for manual installation after the startup release check."
                    },
                    false,
                    self.offline,
                    !self.busy && !self.updates_disabled,
                    option_width,
                    option_height,
                );
                if embedded.clicked() {
                    self.offline = !self.offline;
                }
            });
        });

        ui.add_space(12.0);
        card(CARD_BACKGROUND, 16, 16).show(ui, |ui| {
            ui.set_width(ui.available_width());
            let button_width = (ui.available_width() - 10.0) / 2.0;
            ui.horizontal(|ui| {
                if ui
                    .add_enabled(
                        !self.busy,
                        primary_button("Install / Update")
                            .min_size(egui::vec2(button_width, BUTTON_HEIGHT)),
                    )
                    .clicked()
                {
                    self.start_install();
                }
                if ui
                    .add_enabled(
                        !self.busy,
                        danger_button("Remove patches")
                            .min_size(egui::vec2(button_width, BUTTON_HEIGHT)),
                    )
                    .clicked()
                {
                    self.start_uninstall();
                }
            });
        });

        ui.add_space(12.0);
        card(CARD_BACKGROUND, 14, 14).show(ui, |ui| {
            ui.set_width(ui.available_width());
            ui.horizontal_top(|ui| {
                if self.busy {
                    ui.spinner();
                } else {
                    let status_color = if self.status.starts_with("Error:") {
                        ERROR
                    } else {
                        SUCCESS
                    };
                    ui.label(normal_text("●").color(status_color));
                }
                ui.add(egui::Label::new(normal_text(self.status.as_str())).wrap());
            });
            if let Some((label, fraction)) = &self.download {
                ui.add_space(4.0);
                ui.add(
                    egui::ProgressBar::new(*fraction)
                        .show_percentage()
                        .text(normal_text(label)),
                );
            }
        });

        if !self.log.is_empty() {
            ui.collapsing(section_text("Details"), |ui| {
                card(CARD_BACKGROUND, 12, 12).show(ui, |ui| {
                    ui.set_width(ui.available_width());
                    egui::ScrollArea::vertical()
                        .id_salt("installer-log")
                        .max_height(140.0)
                        .auto_shrink([false, false])
                        .stick_to_bottom(true)
                        .show(ui, |ui| {
                            ui.set_width(ui.available_width());
                            for line in &self.log {
                                ui.add(egui::Label::new(description_text(line).monospace()).wrap());
                            }
                        });
                });
            });
        }
    }
}

fn grouped_runtime_mods(manifest: &[RuntimeMod]) -> Vec<(String, Vec<RuntimeMod>)> {
    let mut groups: Vec<(String, Vec<RuntimeMod>)> = Vec::new();
    for runtime_mod in manifest
        .iter()
        .filter(|runtime_mod| runtime_mod.option_id != FORCED_HIDDEN_RUNTIME_MOD_ID)
    {
        if let Some((_, runtime_mods)) = groups
            .iter_mut()
            .find(|(category, _)| category == &runtime_mod.category)
        {
            runtime_mods.push(runtime_mod.clone());
        } else {
            groups.push((runtime_mod.category.clone(), vec![runtime_mod.clone()]));
        }
    }
    groups
}

fn ensure_forced_selection(selected: &mut HashSet<String>) {
    selected.insert(FORCED_HIDDEN_RUNTIME_MOD_ID.to_owned());
}

fn ensure_forced_selection_vec(selected: &mut Vec<String>) {
    if !selected.iter().any(|id| id == FORCED_HIDDEN_RUNTIME_MOD_ID) {
        selected.push(FORCED_HIDDEN_RUNTIME_MOD_ID.to_owned());
    }
}

fn visible_selection_count(selected: &HashSet<String>) -> usize {
    selected
        .iter()
        .filter(|id| id.as_str() != FORCED_HIDDEN_RUNTIME_MOD_ID)
        .count()
}

fn default_selection(manifest: &[RuntimeMod], legacy_ids: &HashSet<String>) -> HashSet<String> {
    let mut selected: HashSet<_> = manifest
        .iter()
        .filter(|runtime_mod| {
            runtime_mod.default_enabled && !legacy_ids.contains(&runtime_mod.option_id)
        })
        .map(|runtime_mod| runtime_mod.option_id.clone())
        .collect();
    ensure_forced_selection(&mut selected);
    selected
}

fn debug_selection(manifest: &[RuntimeMod], legacy_ids: &HashSet<String>) -> HashSet<String> {
    let mut selected = manifest
        .iter()
        .filter(|runtime_mod| {
            runtime_mod.default_enabled
                || runtime_mod.option_id == LOBBY_TEST_BOT_ID
                || legacy_ids.contains(&runtime_mod.option_id)
        })
        .map(|runtime_mod| runtime_mod.option_id.clone())
        .collect();
    ensure_forced_selection(&mut selected);
    selected
}

fn category_title(category: &str) -> String {
    category
        .split(['-', '_'])
        .map(|word| {
            let mut characters = word.chars();
            match characters.next() {
                Some(first) => first.to_uppercase().chain(characters).collect(),
                None => String::new(),
            }
        })
        .collect::<Vec<_>>()
        .join(" ")
}

fn normal_text(text: impl Into<String>) -> RichText {
    RichText::new(text.into()).text_style(egui::TextStyle::Body)
}

fn description_text(text: impl Into<String>) -> RichText {
    normal_text(text).color(SECONDARY_TEXT)
}

fn large_text(text: impl Into<String>) -> RichText {
    RichText::new(text.into()).text_style(egui::TextStyle::Button)
}

fn button_text(text: impl Into<String>) -> RichText {
    RichText::new(text.into())
        .font(egui::FontId::new(
            BODY_TEXT_SIZE,
            egui::FontFamily::Name(SECTION_FONT_FAMILY.into()),
        ))
        .color(Color32::WHITE)
}

fn section_text(text: impl Into<String>) -> RichText {
    RichText::new(text.into()).font(egui::FontId::new(
        UI_TEXT_SIZE,
        egui::FontFamily::Name(SECTION_FONT_FAMILY.into()),
    ))
}

fn install_fonts(context: &egui::Context) {
    let mut fonts = egui::FontDefinitions::default();
    let encoded_bold_font: String = include_str!("../../assets/fonts/OpenSans-Bold.ttf.base64")
        .split_whitespace()
        .collect();
    let bold_font = BASE64_STANDARD
        .decode(encoded_bold_font)
        .expect("embedded Open Sans Bold font should be valid base64");
    fonts.font_data.insert(
        SECTION_FONT_DATA.to_owned(),
        std::sync::Arc::new(egui::FontData::from_owned(bold_font)),
    );

    let mut section_fonts = vec![SECTION_FONT_DATA.to_owned()];
    if let Some(fallbacks) = fonts.families.get(&egui::FontFamily::Proportional) {
        section_fonts.extend(fallbacks.iter().cloned());
    }
    fonts.families.insert(
        egui::FontFamily::Name(SECTION_FONT_FAMILY.into()),
        section_fonts,
    );
    context.set_fonts(fonts);
}

fn control_button(label: &str) -> egui::Button<'_> {
    egui::Button::new(button_text(label))
        .fill(ACCENT)
        .stroke(egui::Stroke::new(1.5, Color32::WHITE))
        .corner_radius(11)
        .min_size(egui::vec2(0.0, BUTTON_HEIGHT))
}

fn primary_button(label: &str) -> egui::Button<'_> {
    control_button(label).fill(ACTIVE_ACCENT)
}

fn danger_button(label: &str) -> egui::Button<'_> {
    control_button(label).fill(ERROR)
}

fn preset_button(label: &str, active: bool) -> egui::Button<'_> {
    control_button(label)
        .fill(if active { ACTIVE_ACCENT } else { ACCENT })
        .min_size(egui::vec2(88.0, BUTTON_HEIGHT))
}

fn card(fill: Color32, radius: u8, margin: i8) -> egui::Frame {
    egui::Frame::new()
        .fill(fill)
        .stroke(egui::Stroke::NONE)
        .corner_radius(radius)
        .inner_margin(margin)
}

fn selectable_card_height(ui: &egui::Ui, title: &str, description: &str, width: f32) -> f32 {
    let content_width = (width - 74.0).max(40.0);
    let title_font = egui::TextStyle::Button.resolve(ui.style());
    let description_font = egui::TextStyle::Body.resolve(ui.style());
    let text_height = ui.fonts_mut(|fonts| {
        let title_height = fonts
            .layout(title.to_owned(), title_font, PRIMARY_TEXT, content_width)
            .size()
            .y;
        let description_height = fonts
            .layout(
                description.to_owned(),
                description_font,
                SECONDARY_TEXT,
                content_width,
            )
            .size()
            .y;
        title_height + CARD_TEXT_GAP + description_height
    });
    (28.0 + text_height).max(96.0)
}

fn selectable_card(
    ui: &mut egui::Ui,
    title: &str,
    description: &str,
    legacy: bool,
    selected: bool,
    interactive: bool,
    width: f32,
    height: f32,
) -> egui::Response {
    let fill = if selected { ACTIVE_ACCENT } else { ACCENT };
    let title_color = if interactive {
        Color32::WHITE
    } else {
        Color32::WHITE.gamma_multiply(0.55)
    };
    let detail_color = Color32::WHITE.gamma_multiply(if interactive { 0.82 } else { 0.45 });
    let mut frame = egui::Frame::new()
        .fill(if interactive {
            fill
        } else {
            fill.gamma_multiply(0.55)
        })
        .stroke(egui::Stroke::new(
            2.0,
            if selected { Color32::WHITE } else { ACCENT },
        ))
        .corner_radius(16)
        .inner_margin(egui::Margin::symmetric(14, 12))
        .begin(ui);
    let frame_content_width =
        (width - frame.frame.inner_margin.sum().x - frame.frame.stroke.width * 2.0).max(40.0);
    let text_width = (frame_content_width - 42.0).max(40.0);
    let content_height =
        (height - frame.frame.inner_margin.sum().y - frame.frame.stroke.width * 2.0).max(0.0);
    frame
        .content_ui
        .with_layout(egui::Layout::top_down(egui::Align::LEFT), |ui| {
            ui.set_min_size(egui::vec2(frame_content_width, content_height));
            ui.allocate_ui_with_layout(
                egui::vec2(text_width, content_height),
                egui::Layout::top_down(egui::Align::LEFT),
                |ui| {
                    ui.set_width(text_width);
                    ui.spacing_mut().item_spacing.y = CARD_TEXT_GAP;
                    if legacy {
                        ui.horizontal(|ui| {
                            ui.spacing_mut().item_spacing.x = 7.0;
                            ui.add(egui::Label::new(large_text(title).color(title_color)).wrap());
                            egui::Frame::new()
                                .fill(LEGACY.gamma_multiply(0.18))
                                .stroke(egui::Stroke::new(1.0, LEGACY))
                                .corner_radius(5)
                                .inner_margin(egui::Margin::symmetric(6, 2))
                                .show(ui, |ui| {
                                    ui.label(
                                        RichText::new("LEGACY").size(11.0).strong().color(LEGACY),
                                    );
                                });
                        });
                    } else {
                        ui.add(egui::Label::new(large_text(title).color(title_color)).wrap());
                    }
                    ui.add(
                        egui::Label::new(description_text(description).color(detail_color)).wrap(),
                    );
                },
            );
        });

    let sense = if interactive {
        egui::Sense::click()
    } else {
        egui::Sense::hover()
    };
    let response = frame.allocate_space(ui).interact(sense);
    if response.hovered() && interactive && !selected {
        frame.frame.fill = ACCENT_HOVERED;
        frame.frame.stroke = egui::Stroke::new(2.0, Color32::WHITE);
    }
    frame.paint(ui);

    let indicator_rect = egui::Rect::from_center_size(
        egui::pos2(response.rect.right() - 30.0, response.rect.center().y),
        egui::vec2(28.0, 28.0),
    );
    let indicator_color = if selected { SUCCESS } else { ERROR };
    ui.painter().rect(
        indicator_rect,
        7,
        indicator_color.gamma_multiply(if interactive { 1.0 } else { 0.55 }),
        egui::Stroke::NONE,
        egui::StrokeKind::Inside,
    );
    if selected {
        ui.painter().line_segment(
            [
                indicator_rect.left_center() + egui::vec2(5.0, 0.0),
                indicator_rect.center() + egui::vec2(-1.0, 6.0),
            ],
            egui::Stroke::new(3.0, Color32::WHITE),
        );
        ui.painter().line_segment(
            [
                indicator_rect.center() + egui::vec2(-1.0, 6.0),
                indicator_rect.right_top() + egui::vec2(-4.0, 6.0),
            ],
            egui::Stroke::new(3.0, Color32::WHITE),
        );
    }

    if interactive {
        response.on_hover_cursor(egui::CursorIcon::PointingHand)
    } else {
        response
    }
}

fn merge_runtime_mod_catalog(
    latest: &[RuntimeMod],
    fallback: &[RuntimeMod],
) -> (Vec<RuntimeMod>, HashSet<String>) {
    let latest_ids: HashSet<&str> = latest
        .iter()
        .map(|runtime_mod| runtime_mod.option_id.as_str())
        .collect();
    let mut catalog = latest.to_vec();
    let mut legacy_ids = HashSet::new();
    for runtime_mod in fallback {
        if latest_ids.contains(runtime_mod.option_id.as_str()) {
            continue;
        }
        legacy_ids.insert(runtime_mod.option_id.clone());
        catalog.push(runtime_mod.clone());
    }
    (catalog, legacy_ids)
}

fn perform_startup_check(
    game_directory: Option<PathBuf>,
    sender: &Sender<WorkerMessage>,
) -> Result<StartupSnapshot> {
    let reporter = |event| {
        let _ = sender.send(WorkerMessage::Progress(event));
    };
    let payload = resolve_latest_payload(&reporter)?;
    let (latest_manifest, _) = load_payload_metadata(&payload.root)?;
    let embedded = embedded_manifest()?;
    let (manifest, legacy_ids) = merge_runtime_mod_catalog(&latest_manifest, &embedded);

    let summary = if let Some(game_directory) = game_directory {
        let game_directory = resolve_game_directory(game_directory)?;
        update_installed_runtime_mods(
            &game_directory,
            &payload.root,
            &manifest,
            &latest_manifest,
            &reporter,
        )?
    } else {
        Default::default()
    };
    let installed_ids: HashSet<_> = summary.installed_ids.iter().cloned().collect();
    let mut selected_ids = if installed_ids.is_empty() {
        default_selection(&manifest, &legacy_ids)
    } else {
        installed_ids.clone()
    };
    ensure_forced_selection(&mut selected_ids);

    let mut details = vec![format!("Catalog synchronized with {}", payload.source)];
    if !summary.updated_ids.is_empty() {
        details.push(format!("{} plugin(s) updated", summary.updated_ids.len()));
    }
    if !summary.local_newer_ids.is_empty() {
        details.push(format!(
            "{} newer local build(s) preserved",
            summary.local_newer_ids.len()
        ));
    }
    if !summary.legacy_ids.is_empty() {
        details.push(format!(
            "{} installed legacy plugin(s) preserved",
            summary.legacy_ids.len()
        ));
    }
    if !summary.unreadable_ids.is_empty() {
        details.push(format!(
            "{} plugin version(s) could not be read",
            summary.unreadable_ids.len()
        ));
    }

    Ok(StartupSnapshot {
        manifest,
        legacy_ids,
        installed_ids,
        selected_ids,
        message: format!("{}.", details.join("; ")),
    })
}

fn perform_install(job: InstallJob, sender: &Sender<WorkerMessage>) -> Result<String> {
    let reporter = |event| {
        let _ = sender.send(WorkerMessage::Progress(event));
    };
    let game_directory = resolve_game_directory(&job.game_directory)?;
    let payload = resolve_payload(job.offline, &reporter)?;
    let (manifest, supported_build) = load_payload_metadata(&payload.root)?;
    let (catalog, legacy_ids) = merge_runtime_mod_catalog(&manifest, &job.catalog);
    let embedded_payload = resolve_embedded_payload()?;
    let known: HashSet<&str> = catalog
        .iter()
        .map(|runtime_mod| runtime_mod.option_id.as_str())
        .collect();
    let unknown: Vec<_> = job
        .selected_ids
        .iter()
        .filter(|id| !known.contains(id.as_str()))
        .cloned()
        .collect();
    if !unknown.is_empty() {
        bail!(
            "the selected mods are unavailable in {}: {}",
            payload.source,
            unknown.join(", ")
        );
    }
    let fallback_ids: HashSet<String> = job
        .selected_ids
        .iter()
        .filter(|id| legacy_ids.contains(id.as_str()))
        .cloned()
        .collect();
    let mut preserve_ids = HashSet::new();
    for runtime_mod in &catalog {
        if !job.selected_ids.contains(&runtime_mod.option_id) {
            continue;
        }
        let source_root = if fallback_ids.contains(&runtime_mod.option_id) {
            &embedded_payload.root
        } else {
            &payload.root
        };
        let installed = game_directory
            .join("BepInEx/plugins")
            .join(format!("{}.dll", runtime_mod.assembly_name));
        let available = source_root
            .join("artifacts/runtime_mods")
            .join(format!("{}.dll", runtime_mod.assembly_name));
        if !available.exists() {
            bail!(
                "{} is in the local catalog but its embedded artifact is missing",
                runtime_mod.label
            );
        }
        let local_is_not_older = if installed.exists() {
            match read_runtime_mod_version(&installed) {
                Ok(installed_version) => installed_version >= read_runtime_mod_version(&available)?,
                Err(_) => false,
            }
        } else {
            false
        };
        if local_is_not_older {
            preserve_ids.insert(runtime_mod.option_id.clone());
        }
    }
    if proton_launch_configuration_required() && is_steam_client_running() {
        bail!(
            "Steam is running and the required Proton configuration is not active. Quit Steam completely and try again; no game files were changed."
        );
    }
    let issues = compatibility_issues(&game_directory, &supported_build)?;
    if !issues.is_empty() && !job.allow_unsupported_build {
        bail!(
            "unsupported game installation:\n- {}\n\nEnable “Allow unsupported game build” to override this check.",
            issues.join("\n- ")
        );
    }
    if !issues.is_empty() {
        reporter(ProgressEvent::Message(format!(
            "Continuing with unsupported installation: {}",
            issues.join("; ")
        )));
    }
    reporter(ProgressEvent::Message(format!(
        "Using payload: {}",
        payload.source
    )));
    let bepinex_root = resolve_bepinex(&reporter)?;
    let request = InstallRequest {
        game_directory: game_directory.clone(),
        payload_root: payload.root.clone(),
        fallback_payload_root: Some(embedded_payload.root.clone()),
        bepinex_root,
        manifest: catalog.clone(),
        selected_ids: job.selected_ids.clone(),
        preserve_ids: preserve_ids.into_iter().collect(),
        fallback_ids: fallback_ids.iter().cloned().collect(),
    };
    install(&request, &reporter)?;
    let mut problems =
        validate_installed(&game_directory, &manifest, &job.selected_ids, &payload.root)?;
    let legacy_manifest: Vec<_> = catalog
        .iter()
        .filter(|runtime_mod| legacy_ids.contains(&runtime_mod.option_id))
        .cloned()
        .collect();
    problems.extend(validate_installed(
        &game_directory,
        &legacy_manifest,
        &job.selected_ids,
        &embedded_payload.root,
    )?);
    if !problems.is_empty() {
        bail!(
            "installation validation failed:\n- {}",
            problems.join("\n- ")
        );
    }
    Ok(format!(
        "Installation complete: {} mods installed. The first launch may take longer while BepInEx generates interop.",
        job.selected_ids
            .iter()
            .filter(|id| id.as_str() != FORCED_HIDDEN_RUNTIME_MOD_ID)
            .count()
    ))
}

impl eframe::App for InstallerApp {
    fn ui(&mut self, root_ui: &mut egui::Ui, _frame: &mut eframe::Frame) {
        let context = root_ui.ctx().clone();
        let grouped_mods = grouped_runtime_mods(&self.manifest);
        self.poll_worker();
        if self.busy {
            context.request_repaint_after(Duration::from_millis(100));
            if context.input(|input| input.viewport().close_requested()) {
                context.send_viewport_cmd(egui::ViewportCommand::CancelClose);
                self.append_log("Finish the current operation before closing.".to_owned());
            }
        }

        let panel_frame = egui::Frame::new()
            .fill(SHELL_BACKGROUND)
            .stroke(egui::Stroke::NONE)
            .inner_margin(egui::Margin::same(16));
        egui::CentralPanel::default()
            .frame(panel_frame)
            .show(root_ui, |ui| {
                ui.set_width(ui.available_width());
                let available = ui.available_size();
                let pane_spacing = 16.0;
                let right_width = (available.x * 0.38).clamp(360.0, 480.0);
                let left_width = available.x - right_width - pane_spacing;

                ui.spacing_mut().item_spacing.x = pane_spacing;
                ui.horizontal(|ui| {
                    ui.allocate_ui_with_layout(
                        egui::vec2(left_width, available.y),
                        egui::Layout::top_down(egui::Align::LEFT),
                        |ui| self.show_mods_pane(ui, &grouped_mods),
                    );
                    ui.allocate_ui_with_layout(
                        egui::vec2(right_width, available.y),
                        egui::Layout::top_down(egui::Align::LEFT),
                        |ui| self.show_controls_pane(ui),
                    );
                });
            });
    }
}

fn main() -> eframe::Result {
    let args = GuiArgs::parse();
    let options = eframe::NativeOptions {
        viewport: egui::ViewportBuilder::default()
            .with_inner_size([980.0, 900.0])
            .with_min_inner_size([900.0, 720.0])
            .with_maximized(true),
        renderer: eframe::Renderer::Glow,
        ..Default::default()
    };
    eframe::run_native(
        "Sneak Out Patches",
        options,
        Box::new(move |context| Ok(Box::new(InstallerApp::new(context, args.no_update)))),
    )
}

#[cfg(test)]
mod tests {
    use super::*;

    fn runtime_mod(option_id: &str, label: &str) -> RuntimeMod {
        RuntimeMod {
            option_id: option_id.to_owned(),
            label: label.to_owned(),
            details: String::new(),
            category: "test".to_owned(),
            default_enabled: false,
            assembly_name: format!("SneakOut.{label}"),
            config_relative_path: None,
            default_config_template_path: None,
        }
    }

    #[test]
    fn latest_catalog_wins_and_removed_mods_become_legacy() {
        let latest = vec![runtime_mod("current", "Current release")];
        let fallback = vec![
            runtime_mod("current", "Embedded copy"),
            runtime_mod("removed", "Removed mod"),
        ];

        let (catalog, legacy_ids) = merge_runtime_mod_catalog(&latest, &fallback);

        assert_eq!(catalog.len(), 2);
        assert_eq!(catalog[0].label, "Current release");
        assert_eq!(catalog[1].option_id, "removed");
        assert_eq!(legacy_ids, HashSet::from(["removed".to_owned()]));
    }

    #[test]
    fn debug_preset_selects_defaults_lobby_bot_and_every_legacy_mod() {
        let mut stable = runtime_mod("stable", "Stable");
        stable.default_enabled = true;
        let manifest = vec![
            stable,
            runtime_mod("optional", "Optional"),
            runtime_mod(LOBBY_TEST_BOT_ID, "Lobby Test Bot"),
            runtime_mod("local-development", "Local Development"),
        ];
        let legacy_ids = HashSet::from(["local-development".to_owned()]);

        assert_eq!(
            debug_selection(&manifest, &legacy_ids),
            HashSet::from([
                "stable".to_owned(),
                LOBBY_TEST_BOT_ID.to_owned(),
                "local-development".to_owned(),
                FORCED_HIDDEN_RUNTIME_MOD_ID.to_owned(),
            ])
        );
    }

    #[test]
    fn forced_mod_is_selected_but_excluded_from_visible_ui_state() {
        let manifest = vec![runtime_mod(FORCED_HIDDEN_RUNTIME_MOD_ID, "Hidden")];
        let selected = default_selection(&manifest, &HashSet::new());

        assert!(selected.contains(FORCED_HIDDEN_RUNTIME_MOD_ID));
        assert_eq!(visible_selection_count(&selected), 0);
        assert!(grouped_runtime_mods(&manifest).is_empty());
    }

    #[test]
    fn runtime_mod_categories_are_consolidated() {
        let manifest = embedded_manifest().expect("embedded manifest should load");
        let groups = grouped_runtime_mods(&manifest);
        let unique_categories: HashSet<_> = manifest
            .iter()
            .filter(|runtime_mod| runtime_mod.option_id != FORCED_HIDDEN_RUNTIME_MOD_ID)
            .map(|runtime_mod| runtime_mod.category.as_str())
            .collect();

        assert_eq!(groups.len(), unique_categories.len());
        assert_eq!(
            groups
                .iter()
                .map(|(_, runtime_mods)| runtime_mods.len())
                .sum::<usize>(),
            manifest
                .iter()
                .filter(|runtime_mod| runtime_mod.option_id != FORCED_HIDDEN_RUNTIME_MOD_ID)
                .count()
        );
        assert!(groups.iter().all(|(category, runtime_mods)| {
            runtime_mods
                .iter()
                .all(|runtime_mod| runtime_mod.category == *category)
        }));
        assert!(groups.iter().all(|(_, runtime_mods)| {
            runtime_mods
                .iter()
                .all(|runtime_mod| runtime_mod.option_id != FORCED_HIDDEN_RUNTIME_MOD_ID)
        }));
    }

    #[test]
    fn selectable_cards_keep_their_width_and_wrap_vertically() {
        let context = egui::Context::default();
        let mut card_rects = Vec::new();
        let _ = context.run_ui(Default::default(), |ui| {
            ui.set_width(800.0);
            let long_description = "Reduces startup overhead and provides measured frame pacing, memory, and loading improvements.";
            let card_height = selectable_card_height(
                ui,
                "Performance Optimizer",
                long_description,
                240.0,
            )
            .max(selectable_card_height(
                ui,
                "Short card",
                "Short description.",
                240.0,
            ));
            ui.horizontal_top(|ui| {
                card_rects.push(
                    selectable_card(
                        ui,
                        "Performance Optimizer",
                        long_description,
                        false,
                        true,
                        true,
                        240.0,
                        card_height,
                    )
                    .rect,
                );
                card_rects.push(
                    selectable_card(
                        ui,
                        "Short card",
                        "Short description.",
                        false,
                        false,
                        true,
                        240.0,
                        card_height,
                    )
                    .rect,
                );
            });
        });

        assert_eq!(card_rects.len(), 2);
        assert!(card_rects[0].width() >= 239.0, "card became too narrow");
        assert!(
            card_rects[0].height() < 240.0,
            "card text wrapped vertically"
        );
        assert!(
            (card_rects[0].height() - card_rects[1].height()).abs() < 0.1,
            "cards should stretch to the same height"
        );
    }
}
