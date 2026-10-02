//! The in-game overlay: panels drawn over the game itself — the next item to
//! buy, the win probability, and the item value while TAB is held (#1795) —
//! configured from the app.
//!
//! The window layer is macOS's (`macos.rs`), settled by the spike in #1673
//! (`docs/desktop-overlay-spike.md`): one non-activating `NSPanel` per panel,
//! one level above the display the game captures in Full Screen, never key,
//! click-through, shown only while the game process is frontmost. Elsewhere
//! the overlay reports itself unsupported and the settings page says so.
//!
//! What shows when and where comes from `shell_state::overlay`, tested on any
//! platform; this module owns the settings file, the runtime flags the rule
//! reads, and the commands the webviews call.

#[cfg(target_os = "macos")]
mod macos;

use std::collections::HashMap;
use std::path::PathBuf;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};

use serde::Serialize;
use shell_state::overlay::{OverlayInputs, OverlayPanel, OverlayPoint, OverlaySettings};
use tauri::{AppHandle, Emitter, Manager, State};

use crate::game::SharedGame;

/// A panel's window, and the route its webview loads.
pub fn label(panel: OverlayPanel) -> String {
    format!("overlay-{}", panel.slug())
}
/// The settings or the preview changed: the payload is the new `OverlayView`.
pub const VIEW_EVENT: &str = "overlay://view";

const SETTINGS_FILE: &str = "overlay-settings.json";

/// Shown in the settings and on the game page. The key is read from the
/// keyboard's state rather than registered as a hotkey: with the display
/// captured, the window server delivers no hotkey to the app (#1673).
pub const SHORTCUT: &str = "⌥⇧O";

/// A panel's size before its page has measured itself, in points.
const INITIAL_SIZE: (f64, f64) = (232.0, 64.0);

pub struct Overlay {
    settings_path: PathBuf,
    settings: Mutex<OverlaySettings>,
    /// The settings page shows the panel on screen to place it.
    preview: AtomicBool,
    /// The player hid the overlay with the shortcut; cleared when the game ends.
    hidden_by_player: AtomicBool,
    /// The game's process is the frontmost application, as last measured.
    game_frontmost: AtomicBool,
    /// TAB is held over the game: its scoreboard is open.
    scoreboard: AtomicBool,
    /// Each page's measured size, in points, scale included.
    sizes: Mutex<HashMap<OverlayPanel, (f64, f64)>>,
}

pub type SharedOverlay = Arc<Overlay>;

/// What the settings page and the overlay page read.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct OverlayView {
    pub settings: OverlaySettings,
    /// False where the window layer is not built yet (Windows).
    pub supported: bool,
    pub preview: bool,
    pub shortcut: &'static str,
}

impl Overlay {
    pub fn new(app: &AppHandle) -> SharedOverlay {
        let settings_path = app
            .path()
            .app_config_dir()
            .unwrap_or_else(|_| std::env::temp_dir().join("truemain"))
            .join(SETTINGS_FILE);
        let settings = OverlaySettings::load(&settings_path);
        Arc::new(Self {
            settings_path,
            settings: Mutex::new(settings),
            preview: AtomicBool::new(false),
            hidden_by_player: AtomicBool::new(false),
            game_frontmost: AtomicBool::new(false),
            scoreboard: AtomicBool::new(false),
            sizes: Mutex::new(HashMap::new()),
        })
    }

    pub fn settings(&self) -> OverlaySettings {
        *self.settings.lock().expect("overlay settings poisoned")
    }

    fn replace_settings(&self, settings: OverlaySettings) -> std::io::Result<()> {
        settings.save(&self.settings_path)?;
        *self.settings.lock().expect("overlay settings poisoned") = settings;
        Ok(())
    }

    pub fn view(&self) -> OverlayView {
        OverlayView {
            settings: self.settings(),
            supported: cfg!(target_os = "macos"),
            preview: self.preview.load(Ordering::SeqCst),
            shortcut: SHORTCUT,
        }
    }

    pub fn size(&self, panel: OverlayPanel) -> (f64, f64) {
        self.sizes
            .lock()
            .expect("overlay sizes poisoned")
            .get(&panel)
            .copied()
            .unwrap_or(INITIAL_SIZE)
    }

    /// Everything the visibility rule reads, measured now.
    pub fn inputs(&self, app: &AppHandle) -> OverlayInputs {
        let (in_game, dead) = {
            let game = app.state::<SharedGame>();
            let feed = game.lock().expect("game mutex poisoned");
            match feed.current() {
                Some(state) => (
                    true,
                    state
                        .players
                        .iter()
                        .any(|player| player.is_me && player.dead),
                ),
                None => (false, false),
            }
        };
        if !in_game {
            // A hide lasts for the game it was asked in.
            self.hidden_by_player.store(false, Ordering::SeqCst);
        }
        OverlayInputs {
            preview: self.preview.load(Ordering::SeqCst),
            game_frontmost: self.game_frontmost.load(Ordering::SeqCst),
            in_game,
            dead,
            hidden_by_player: self.hidden_by_player.load(Ordering::SeqCst),
            scoreboard: self.scoreboard.load(Ordering::SeqCst),
        }
    }

    /// The shortcut: hide the overlay for this game, or bring it back.
    pub fn toggle_hidden(&self) -> bool {
        !self.hidden_by_player.fetch_xor(true, Ordering::SeqCst)
    }

    pub fn set_game_frontmost(&self, front: bool) -> bool {
        self.game_frontmost.swap(front, Ordering::SeqCst) != front
    }

    pub fn set_scoreboard(&self, open: bool) -> bool {
        self.scoreboard.swap(open, Ordering::SeqCst) != open
    }

    /// Where the player dragged a panel in the preview, kept in memory and
    /// saved when the preview ends.
    pub fn place(&self, panel: OverlayPanel, point: OverlayPoint) {
        self.settings
            .lock()
            .expect("overlay settings poisoned")
            .panel_mut(panel)
            .custom = Some(point);
    }
}

/// Build the panels, hidden, and start following the game. Called from `setup`.
pub fn setup(app: &AppHandle) -> Result<(), Box<dyn std::error::Error>> {
    let overlay = Overlay::new(app);
    app.manage(overlay);
    #[cfg(target_os = "macos")]
    macos::setup(app)?;
    Ok(())
}

/// Bring the panels in line with the settings and the game. Cheap when nothing
/// changed; safe from any thread.
pub fn apply(app: &AppHandle) {
    #[cfg(target_os = "macos")]
    macos::apply(app);
    #[cfg(not(target_os = "macos"))]
    let _ = app;
}

fn publish(app: &AppHandle, overlay: &Overlay) -> OverlayView {
    let view = overlay.view();
    let _ = app.emit(VIEW_EVENT, &view);
    apply(app);
    view
}

#[tauri::command]
pub fn overlay_view(overlay: State<'_, SharedOverlay>) -> OverlayView {
    overlay.view()
}

/// Replace the settings. Read leniently, like the file: a value out of range
/// falls back alone, and the answer is what was kept.
#[tauri::command]
pub fn set_overlay_settings(
    app: AppHandle,
    overlay: State<'_, SharedOverlay>,
    settings: serde_json::Value,
) -> Result<OverlayView, String> {
    let settings = OverlaySettings::from_json(&settings.to_string());
    overlay
        .replace_settings(settings)
        .map_err(|error| error.to_string())?;
    Ok(publish(&app, &overlay))
}

/// Show the panels on screen to place them, or stop. Ending the preview saves
/// where they were dragged.
#[tauri::command]
pub fn overlay_preview(
    app: AppHandle,
    overlay: State<'_, SharedOverlay>,
    on: bool,
) -> Result<OverlayView, String> {
    let was = overlay.preview.swap(on, Ordering::SeqCst);
    if was && !on {
        overlay
            .replace_settings(overlay.settings())
            .map_err(|error| error.to_string())?;
    }
    Ok(publish(&app, &overlay))
}

/// A panel page's measured size, in points. Its window takes it, so the
/// panel never covers more of the game than its content.
#[tauri::command]
pub fn overlay_fit(
    app: AppHandle,
    overlay: State<'_, SharedOverlay>,
    panel: String,
    width: f64,
    height: f64,
) {
    let Some(panel) = OverlayPanel::from_slug(&panel) else {
        return;
    };
    if !(width.is_finite() && height.is_finite()) || width < 1.0 || height < 1.0 {
        return;
    }
    overlay
        .sizes
        .lock()
        .expect("overlay sizes poisoned")
        .insert(panel, (width.ceil(), height.ceil()));
    apply(&app);
}
