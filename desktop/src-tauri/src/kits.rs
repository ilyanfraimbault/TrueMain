//! The player's kits (#2185): the store the champion page edits, champion
//! select imports and the overlay follows, kept in the shell's own file
//! (`shell_state::kits`, next to the overlay's settings).
//!
//! The app reads and writes it whole. Every write is published to every
//! window, so a kit saved in the app reaches the overlay's panels during a
//! game. Nothing here writes to the League client: a kit reaches the client
//! only through the import buttons (`itemsets.rs`, `runes.rs`,
//! `champselect.rs`), on a click.

use std::path::PathBuf;
use std::sync::Mutex;

use shell_state::kits::KitStore;
use tauri::{AppHandle, Emitter, Manager, State};

const FILE: &str = "kits.json";

/// What every window hears when the kits change.
const CHANGED_EVENT: &str = "kits://changed";

pub struct Kits {
    path: PathBuf,
    store: Mutex<KitStore>,
}

impl Kits {
    pub fn new(app: &AppHandle) -> Self {
        let path = app
            .path()
            .app_config_dir()
            .unwrap_or_else(|_| std::env::temp_dir().join("truemain"))
            .join(FILE);
        let store = KitStore::load(&path);
        Self {
            path,
            store: Mutex::new(store),
        }
    }
}

#[tauri::command]
pub fn kits_get(kits: State<'_, Kits>) -> KitStore {
    kits.store.lock().expect("kits poisoned").clone()
}

/// Replace the store. Read as leniently as the file — a kit that breaks a
/// rule is dropped alone — and the answer is what was kept.
#[tauri::command]
pub fn kits_set(
    app: AppHandle,
    kits: State<'_, Kits>,
    store: serde_json::Value,
) -> Result<KitStore, String> {
    let store = KitStore::from_json(&store.to_string());
    // Held across the write, so two windows saving at once leave the file and
    // the memory on the same store.
    let mut kept = kits.store.lock().expect("kits poisoned");
    store.save(&kits.path).map_err(|error| {
        tracing::warn!(%error, "could not save the kits");
        error.to_string()
    })?;
    *kept = store.clone();
    drop(kept);
    let _ = app.emit(CHANGED_EVENT, &store);
    Ok(store)
}
