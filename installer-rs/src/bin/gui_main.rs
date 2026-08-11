#![cfg_attr(windows, windows_subsystem = "windows")]

use clap::Parser;
use sneakout_installer::{BinaryKind, run_self_update_helper_if_requested};

#[path = "gui.rs"]
mod gui;

#[derive(Debug, Parser)]
#[command(
    name = "SneakOutPatches",
    version,
    about = "Sneak Out patches graphical installer"
)]
struct GuiArgs {
    /// Use the embedded payload without checking GitHub for installer or mod updates.
    #[arg(long, alias = "offline")]
    no_update: bool,
}

fn main() -> eframe::Result {
    if let Some(result) = run_self_update_helper_if_requested() {
        if let Err(error) = result {
            eprintln!("Could not apply installer update: {error:#}");
        }
        return Ok(());
    }
    let args = GuiArgs::parse();
    gui::run(args.no_update, BinaryKind::Gui)
}
