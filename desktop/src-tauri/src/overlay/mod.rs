//! The in-game overlay: panels drawn over the game itself — the next item to
//! buy, the win probability, and the item value while TAB is held (#1795) —
//! configured from the app, each panel shown always, while a chord is held or
//! toggled by one (#1915).
//!
//! One window per panel, driven the same way on both platforms
//! (`panels.rs`): never focused, click-through outside the preview, shown
//! only while the game process is frontmost. The window layer is per
//! platform: on macOS (`macos.rs`) a non-activating `NSPanel` one level above
//! the display the game captures in Full Screen, settled by the spike in
//! #1673 (`docs/desktop-overlay-spike.md`); on Windows (`windows.rs`, #1798) a
//! topmost, non-activating layered window, over a game in Borderless or
//! Windowed only. Elsewhere the overlay reports itself unsupported and the
//! settings page says so.
//!
//! What shows when and where comes from `shell_state::overlay`, tested on any
//! platform; this module owns the settings file, the runtime flags the rule
//! reads, and the commands the webviews call.

#[cfg(any(target_os = "macos", windows))]
mod panels;
#[cfg(target_os = "macos")]
#[path = "macos.rs"]
mod platform;
#[cfg(windows)]
#[path = "windows.rs"]
mod platform;

use std::collections::{BTreeMap, HashMap};
use std::path::PathBuf;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};

use serde::Serialize;
use shell_state::keys::{GameBinds, KeysDown};
use shell_state::overlay::{
    OverlayInputs, OverlayPanel, OverlayPoint, OverlaySettings, PanelKeys, PanelTrigger,
};
use tauri::{AppHandle, Emitter, Manager, State};

use crate::game::SharedGame;
use crate::record::SharedClient;

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
#[cfg(target_os = "macos")]
pub const SHORTCUT: &str = "⌥⇧O";
#[cfg(not(target_os = "macos"))]
pub const SHORTCUT: &str = "Alt+Shift+O";

/// Chords are named the way the platform's keyboards are labelled.
const MAC_KEYS: bool = cfg!(target_os = "macos");

/// What the settings page says about where the overlay can show.
#[cfg(windows)]
const NOTICE: Option<&str> = Some(
    "Play League in Borderless or Windowed: Windows draws nothing over a game in Full Screen.",
);
#[cfg(not(windows))]
const NOTICE: Option<&str> = None;

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
    /// The panels' chords as last read over the game, and the toggles' state.
    keys: Mutex<PanelKeys>,
    /// The player's League keybindings, Riot's defaults until the client is read.
    binds: Mutex<GameBinds>,
    /// Each page's measured size, in points, scale included.
    sizes: Mutex<HashMap<OverlayPanel, (f64, f64)>>,
}

pub type SharedOverlay = Arc<Overlay>;

/// What the settings page and the overlay page read.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct OverlayView {
    pub settings: OverlaySettings,
    /// False where there is no window layer (Linux).
    pub supported: bool,
    pub preview: bool,
    pub shortcut: &'static str,
    /// A condition the platform puts on the overlay, for the settings to show.
    pub notice: Option<&'static str>,
    /// Each panel's chord, by slug, as the player presses it and what it also
    /// does in game.
    pub chords: BTreeMap<&'static str, ChordView>,
    /// The warnings come from the player's own keybindings, not Riot's defaults.
    pub own_binds: bool,
}

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ChordView {
    pub label: String,
    /// What the chord's key also does in game.
    pub warning: Option<String>,
}

/// The settings page's question about a trigger before it saves it.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct TriggerCheck {
    /// Why the trigger cannot be the panel's, if it cannot.
    pub refusal: Option<String>,
    pub chord: Option<ChordView>,
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
            keys: Mutex::new(PanelKeys::default()),
            binds: Mutex::new(GameBinds::defaults()),
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
        let settings = self.settings();
        let binds = self.binds.lock().expect("overlay binds poisoned");
        let chords = OverlayPanel::ALL
            .into_iter()
            .filter_map(|panel| {
                let chord = settings.panel(panel).trigger.chord()?;
                Some((panel.slug(), chord_view(&binds, &chord)))
            })
            .collect();
        OverlayView {
            settings,
            supported: cfg!(any(target_os = "macos", windows)),
            preview: self.preview.load(Ordering::SeqCst),
            shortcut: SHORTCUT,
            notice: NOTICE,
            chords,
            own_binds: binds.own,
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
        let settings = self.settings();
        let mut keys = self.keys.lock().expect("overlay keys poisoned");
        if !in_game {
            // A hide, and each toggle, lasts for the game it was pressed in.
            self.hidden_by_player.store(false, Ordering::SeqCst);
            keys.reset_toggles();
        }
        OverlayInputs {
            preview: self.preview.load(Ordering::SeqCst),
            game_frontmost: self.game_frontmost.load(Ordering::SeqCst),
            in_game,
            dead,
            hidden_by_player: self.hidden_by_player.load(Ordering::SeqCst),
            held: keys.held(),
            toggled: keys.toggled(&settings),
        }
    }

    /// The shortcut: hide the overlay for this game, or bring it back.
    pub fn toggle_hidden(&self) -> bool {
        !self.hidden_by_player.fetch_xor(true, Ordering::SeqCst)
    }

    pub fn set_game_frontmost(&self, front: bool) -> bool {
        self.game_frontmost.swap(front, Ordering::SeqCst) != front
    }

    /// One read of the keyboard over the game; true when a panel's chord
    /// changed what shows.
    pub fn read_keys(&self, keys: &KeysDown) -> bool {
        let settings = self.settings();
        self.keys
            .lock()
            .expect("overlay keys poisoned")
            .read(&settings, keys)
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
    #[cfg(any(target_os = "macos", windows))]
    panels::setup(app)?;
    Ok(())
}

fn chord_view(binds: &GameBinds, chord: &shell_state::keys::Chord) -> ChordView {
    ChordView {
        label: chord.label(MAC_KEYS),
        warning: binds.conflict(chord),
    }
}

/// Bring the panels in line with the settings and the game. Cheap when nothing
/// changed; safe from any thread.
pub fn apply(app: &AppHandle) {
    #[cfg(any(target_os = "macos", windows))]
    panels::apply(app);
    #[cfg(not(any(target_os = "macos", windows)))]
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
    let settings = OverlaySettings::from_app(&settings.to_string());
    overlay
        .replace_settings(settings)
        .map_err(|error| error.to_string())?;
    Ok(publish(&app, &overlay))
}

/// Whether `trigger` can be `panel`'s, and how its chord reads, before the
/// page saves it: the page shows the refusal by the field that recorded it.
#[tauri::command]
pub fn overlay_check_trigger(
    overlay: State<'_, SharedOverlay>,
    panel: String,
    trigger: serde_json::Value,
) -> Result<TriggerCheck, String> {
    let panel = OverlayPanel::from_slug(&panel).ok_or("no such panel")?;
    let trigger: PanelTrigger =
        serde_json::from_value(trigger).map_err(|_| "a key this app does not know".to_string())?;
    let binds = overlay.binds.lock().expect("overlay binds poisoned");
    Ok(TriggerCheck {
        refusal: overlay.settings().refusal(panel, &trigger),
        chord: trigger.chord().map(|chord| chord_view(&binds, &chord)),
    })
}

/// Read the player's League keybindings from the client, for the chords'
/// warnings. Without a client, or with an answer this build cannot read,
/// Riot's defaults stay.
#[tauri::command]
pub async fn overlay_read_binds(
    app: AppHandle,
    overlay: State<'_, SharedOverlay>,
    client: State<'_, SharedClient>,
) -> Result<OverlayView, String> {
    let client = client.read().expect("client lock poisoned").clone();
    if let Some(client) = client {
        match client.input_settings().await {
            Ok(body) => match GameBinds::from_input_settings(&body) {
                Some(binds) => *overlay.binds.lock().expect("overlay binds poisoned") = binds,
                None => tracing::warn!("the client's input settings are not in a known shape"),
            },
            Err(error) => tracing::debug!(%error, "no input settings from the client"),
        }
    }
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
