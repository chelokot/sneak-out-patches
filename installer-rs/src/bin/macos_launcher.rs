#[cfg(target_os = "macos")]
use eframe::egui::{self, Color32, RichText};
#[cfg(target_os = "macos")]
use sneakout_installer::{
    ProgressEvent, SikarugirWrapper, default_sikarugir_roots, discover_sikarugir_wrappers,
    inspect_sikarugir_wrapper, launch_sikarugir_installer, resolve_sikarugir_installer,
};
#[cfg(target_os = "macos")]
use std::sync::mpsc::{self, Receiver, TryRecvError};
#[cfg(target_os = "macos")]
use std::thread;
#[cfg(target_os = "macos")]
use std::time::Duration;

#[cfg(target_os = "macos")]
const BACKGROUND: Color32 = Color32::from_rgb(6, 20, 29);
#[cfg(target_os = "macos")]
const CARD: Color32 = Color32::from_rgb(15, 34, 48);
#[cfg(target_os = "macos")]
const ACCENT: Color32 = Color32::from_rgb(13, 145, 181);
#[cfg(target_os = "macos")]
const PRIMARY: Color32 = Color32::from_rgb(246, 248, 250);
#[cfg(target_os = "macos")]
const SECONDARY: Color32 = Color32::from_rgb(183, 199, 211);
#[cfg(target_os = "macos")]
const SUCCESS: Color32 = Color32::from_rgb(119, 218, 55);
#[cfg(target_os = "macos")]
const ERROR: Color32 = Color32::from_rgb(244, 104, 95);

#[cfg(target_os = "macos")]
enum WorkerMessage {
    Discovered(Vec<SikarugirWrapper>),
    Progress(ProgressEvent),
    Launched(Result<String, String>),
}

#[cfg(target_os = "macos")]
struct MacLauncherApp {
    wrappers: Vec<SikarugirWrapper>,
    selected: Option<usize>,
    busy: bool,
    launched: bool,
    status: String,
    error: bool,
    receiver: Receiver<WorkerMessage>,
    sender: mpsc::Sender<WorkerMessage>,
}

#[cfg(target_os = "macos")]
impl MacLauncherApp {
    fn new(context: &eframe::CreationContext<'_>) -> Self {
        let mut visuals = egui::Visuals::dark();
        visuals.panel_fill = BACKGROUND;
        visuals.window_fill = BACKGROUND;
        visuals.override_text_color = Some(PRIMARY);
        visuals.widgets.inactive.weak_bg_fill = ACCENT;
        visuals.widgets.hovered.weak_bg_fill = ACCENT;
        visuals.widgets.active.weak_bg_fill = ACCENT;
        context.egui_ctx.set_visuals(visuals);
        context.egui_ctx.style_mut(|style| {
            style.spacing.item_spacing = egui::vec2(10.0, 10.0);
            style.spacing.button_padding = egui::vec2(14.0, 8.0);
        });

        let (sender, receiver) = mpsc::channel();
        let discovery_sender = sender.clone();
        thread::spawn(move || {
            let wrappers = discover_sikarugir_wrappers();
            let _ = discovery_sender.send(WorkerMessage::Discovered(wrappers));
        });
        Self {
            wrappers: Vec::new(),
            selected: None,
            busy: true,
            launched: false,
            status: "Looking for a Sikarugir Steam wrapper...".to_owned(),
            error: false,
            receiver,
            sender,
        }
    }

    fn receive_messages(&mut self) {
        loop {
            match self.receiver.try_recv() {
                Ok(WorkerMessage::Discovered(wrappers)) => {
                    self.wrappers = wrappers;
                    self.selected = (!self.wrappers.is_empty()).then_some(0);
                    self.busy = false;
                    self.error = self.wrappers.is_empty();
                    self.status = if self.wrappers.is_empty() {
                        "No Sikarugir wrapper containing Windows Steam was found. Choose the wrapper manually or rescan after installing Sneak Out."
                            .to_owned()
                    } else if self.wrappers.len() == 1 {
                        "Sikarugir Steam wrapper found.".to_owned()
                    } else {
                        "More than one Steam wrapper was found. Choose the one containing Sneak Out."
                            .to_owned()
                    };
                }
                Ok(WorkerMessage::Progress(ProgressEvent::Message(message))) => {
                    self.status = message;
                    self.error = false;
                }
                Ok(WorkerMessage::Progress(ProgressEvent::Download {
                    label,
                    downloaded,
                    total,
                })) => {
                    self.status = match total {
                        Some(total) if total > 0 => {
                            format!("{label}: {:.0}%", downloaded as f64 / total as f64 * 100.0)
                        }
                        _ => format!("{label}: {} KiB", downloaded / 1024),
                    };
                }
                Ok(WorkerMessage::Launched(result)) => {
                    self.busy = false;
                    match result {
                        Ok(message) => {
                            self.launched = true;
                            self.error = false;
                            self.status = message;
                        }
                        Err(error) => {
                            self.error = true;
                            self.status = error;
                        }
                    }
                }
                Err(TryRecvError::Empty) => break,
                Err(TryRecvError::Disconnected) => {
                    if self.busy {
                        self.busy = false;
                        self.error = true;
                        self.status = "The launcher worker stopped unexpectedly.".to_owned();
                    }
                    break;
                }
            }
        }
    }

    fn rescan(&mut self) {
        self.busy = true;
        self.error = false;
        self.status = "Looking for a Sikarugir Steam wrapper...".to_owned();
        let sender = self.sender.clone();
        thread::spawn(move || {
            let wrappers = discover_sikarugir_wrappers();
            let _ = sender.send(WorkerMessage::Discovered(wrappers));
        });
    }

    fn choose_wrapper(&mut self) {
        let mut dialog = rfd::FileDialog::new()
            .set_title("Choose the Sikarugir Steam wrapper")
            .add_filter("Sikarugir wrapper", &["app"]);
        if let Some(root) = default_sikarugir_roots()
            .into_iter()
            .find(|path| path.exists())
        {
            dialog = dialog.set_directory(root);
        }
        let Some(path) = dialog.pick_file_or_folder() else {
            return;
        };
        match inspect_sikarugir_wrapper(&path) {
            Ok(wrapper) => {
                if let Some(index) = self
                    .wrappers
                    .iter()
                    .position(|existing| existing.path() == wrapper.path())
                {
                    self.selected = Some(index);
                } else {
                    self.wrappers.push(wrapper);
                    self.selected = Some(self.wrappers.len() - 1);
                }
                self.error = false;
                self.status = "Sikarugir wrapper selected.".to_owned();
            }
            Err(error) => {
                self.error = true;
                self.status = format!("{error:#}");
            }
        }
    }

    fn launch(&mut self) {
        let Some(wrapper) = self
            .selected
            .and_then(|index| self.wrappers.get(index))
            .cloned()
        else {
            self.error = true;
            self.status = "Choose a Sikarugir wrapper first.".to_owned();
            return;
        };
        self.busy = true;
        self.error = false;
        self.status = "Preparing the Windows installer...".to_owned();
        let sender = self.sender.clone();
        thread::spawn(move || {
            let reporter = |event| {
                let _ = sender.send(WorkerMessage::Progress(event));
            };
            let result = (|| {
                let installer = resolve_sikarugir_installer(&reporter)?;
                launch_sikarugir_installer(&wrapper, &installer)?;
                Ok(format!(
                    "The installer was opened inside {}. Complete the installation there, then launch Sneak Out normally through Steam.",
                    wrapper.display_name()
                ))
            })()
            .map_err(|error: anyhow::Error| format!("{error:#}"));
            let _ = sender.send(WorkerMessage::Launched(result));
        });
    }
}

#[cfg(target_os = "macos")]
impl eframe::App for MacLauncherApp {
    fn update(&mut self, context: &egui::Context, _frame: &mut eframe::Frame) {
        self.receive_messages();
        if self.busy {
            context.request_repaint_after(Duration::from_millis(100));
        }

        egui::CentralPanel::default().show(context, |ui| {
            ui.add_space(14.0);
            ui.heading(RichText::new("Sneak Out Patches for Sikarugir").size(25.0));
            ui.label(
                RichText::new(
                    "This helper opens the Windows installer inside the same wrapper as Steam. Quit Sneak Out and Windows Steam before continuing.",
                )
                .color(SECONDARY),
            );
            ui.add_space(8.0);

            egui::Frame::new()
                .fill(CARD)
                .corner_radius(10.0)
                .inner_margin(14.0)
                .show(ui, |ui| {
                    ui.label(RichText::new("Sikarugir wrapper").strong());
                    if self.wrappers.is_empty() {
                        ui.label(RichText::new("No wrapper selected").color(SECONDARY));
                    } else {
                        for (index, wrapper) in self.wrappers.iter().enumerate() {
                            let details = match wrapper.game_directory() {
                                Some(game) => format!(
                                    "{} — Sneak Out found at {}",
                                    wrapper.display_name(),
                                    game.display()
                                ),
                                None if wrapper.contains_steam() => {
                                    format!("{} — Windows Steam found", wrapper.display_name())
                                }
                                None => wrapper.display_name(),
                            };
                            ui.radio_value(&mut self.selected, Some(index), details);
                        }
                    }
                    ui.horizontal(|ui| {
                        if ui
                            .add_enabled(!self.busy, egui::Button::new("Choose Wrapper…"))
                            .clicked()
                        {
                            self.choose_wrapper();
                        }
                        if ui
                            .add_enabled(!self.busy, egui::Button::new("Rescan"))
                            .clicked()
                        {
                            self.rescan();
                        }
                    });
                });

            ui.add_space(8.0);
            if self.busy {
                ui.spinner();
            }
            ui.label(
                RichText::new(&self.status).color(if self.error {
                    ERROR
                } else if self.launched {
                    SUCCESS
                } else {
                    SECONDARY
                }),
            );
            ui.add_space(8.0);
            if ui
                .add_enabled(
                    !self.busy && !self.launched && self.selected.is_some(),
                    egui::Button::new(RichText::new("Open Installer").strong())
                        .min_size(egui::vec2(180.0, 42.0)),
                )
                .clicked()
            {
                self.launch();
            }
        });
    }
}

#[cfg(target_os = "macos")]
fn main() -> eframe::Result {
    let options = eframe::NativeOptions {
        viewport: egui::ViewportBuilder::default()
            .with_inner_size([680.0, 430.0])
            .with_min_inner_size([560.0, 360.0]),
        ..Default::default()
    };
    eframe::run_native(
        "Sneak Out Patches",
        options,
        Box::new(|context| Ok(Box::new(MacLauncherApp::new(context)))),
    )
}

#[cfg(not(target_os = "macos"))]
fn main() {
    eprintln!("The Sikarugir launcher is available only on macOS.");
}
