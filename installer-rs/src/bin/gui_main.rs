#![cfg_attr(windows, windows_subsystem = "windows")]

use clap::Parser;

#[path = "gui.rs"]
mod gui;

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

fn main() -> eframe::Result {
    let args = GuiArgs::parse();
    gui::run(args.no_update)
}
