//! The in-game overlay: panels drawn over the game itself — the next item to
//! buy, the win probability, and the item value while TAB is held (#1795) —
//! configured from the app, each panel shown always, while a chord is held or
//! toggled by one (#1915). The lane-gold plates (#2186) sit on the game's own
//! scoreboard, placed from the client's HUD settings read at game start.
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
//!
//! One window is no panel: the confirmation that a press of the mark key was
//! kept (#1767, `confirm`), shown a moment over the game, built the first
//! time it is needed and closed with the game.

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
use std::time::{Duration, Instant};

use serde::Serialize;
use shell_state::keys::{GameBinds, KeysDown};
use shell_state::overlay::{
    OverlayInputs, OverlayPanel, OverlayPoint, OverlaySettings, PanelKeys, PanelTrigger,
};
use shell_state::scoreboard::HudSettings;
use tauri::{AppHandle, Emitter, Manager, State};

use crate::game::SharedGame;
use crate::record::SharedClient;
use crate::recording::SharedRecorder;

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

/// The confirmation's window, and the route its webview loads (#1767).
pub const CONFIRMATION_LABEL: &str = "overlay-confirmation";
/// Its page's name in `overlay_fit`.
const CONFIRMATION_SLUG: &str = "confirmation";
/// Its text changed: the payload is the new text.
pub const CONFIRMATION_EVENT: &str = "overlay://confirmation";
/// Long enough to be read at a glance, short enough to leave the game alone.
const CONFIRMATION_FOR: Duration = Duration::from_millis(2_500);
const CONFIRMATION_SIZE: (f64, f64) = (180.0, 36.0);

pub struct Overlay {
    settings_path: PathBuf,
    settings: Mutex<OverlaySettings>,
    /// The settings page shows the panel on screen to place it.
    preview: AtomicBool,
    /// The player hid the overlay with the shortcut; cleared when the game ends.
    hidden_by_player: AtomicBool,
    /// The phase says a game runs (`follow`): the panels switched on get their window.
    game_running: AtomicBool,
    /// The game's process is the frontmost application, as last measured.
    game_frontmost: AtomicBool,
    /// The panels' chords as last read over the game, and the toggles' state.
    keys: Mutex<PanelKeys>,
    /// The player's League keybindings, Riot's defaults until the client is read.
    binds: Mutex<GameBinds>,
    /// The player's HUD scale and scoreboard layout, Riot's defaults until the
    /// client is read: where the lane-gold plates go.
    hud: Mutex<HudSettings>,
    /// Each page's measured size, in points, scale included.
    sizes: Mutex<HashMap<OverlayPanel, (f64, f64)>>,
    /// What the confirmation says, and until when it shows.
    confirmation: Mutex<Option<(String, Instant)>>,
    /// The confirmation page's measured size, in points.
    confirmation_size: Mutex<(f64, f64)>,
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
    /// The HUD settings the lane-gold plates are placed by.
    pub hud: HudSettings,
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
            game_running: AtomicBool::new(false),
            game_frontmost: AtomicBool::new(false),
            keys: Mutex::new(PanelKeys::default()),
            binds: Mutex::new(GameBinds::defaults()),
            hud: Mutex::new(HudSettings::DEFAULTS),
            sizes: Mutex::new(HashMap::new()),
            confirmation: Mutex::new(None),
            confirmation_size: Mutex::new(CONFIRMATION_SIZE),
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
            hud: self.hud(),
        }
    }

    pub fn hud(&self) -> HudSettings {
        *self.hud.lock().expect("overlay hud poisoned")
    }

    /// The keys bound to cast the summoner spell in `slot`, from the binds read.
    pub fn summoner_spell_keys(&self, slot: u8) -> Vec<shell_state::keys::KeyCode> {
        self.binds
            .lock()
            .expect("overlay binds poisoned")
            .summoner_spell_keys(slot)
    }

    /// How `chord` reads and what it also does in game, from the binds read.
    pub fn chord_view(&self, chord: &shell_state::keys::Chord) -> ChordView {
        chord_view(&self.binds.lock().expect("overlay binds poisoned"), chord)
    }

    /// The confirmation's text while it should show, `None` once its moment
    /// is over.
    pub fn confirmation(&self, now: Instant) -> Option<String> {
        self.confirmation
            .lock()
            .expect("overlay confirmation poisoned")
            .as_ref()
            .filter(|(_, until)| now < *until)
            .map(|(text, _)| text.clone())
    }

    pub fn confirmation_size(&self) -> (f64, f64) {
        *self
            .confirmation_size
            .lock()
            .expect("overlay confirmation poisoned")
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
            game_running: self.game_running.load(Ordering::SeqCst),
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

/// Start following the game. Called from `setup`; the panels' windows are
/// built when a game starts (`follow`), not here.
pub fn setup(app: &AppHandle) -> Result<(), Box<dyn std::error::Error>> {
    let overlay = Overlay::new(app);
    app.manage(overlay);
    #[cfg(any(target_os = "macos", windows))]
    panels::setup(app);
    Ok(())
}

/// Build or close the panels' windows as the game and the settings now call
/// for, then `apply`. Safe from any thread.
fn rebuild(app: &AppHandle) {
    #[cfg(any(target_os = "macos", windows))]
    panels::rebuild(app);
    #[cfg(not(any(target_os = "macos", windows)))]
    let _ = app;
}

fn chord_view(binds: &GameBinds, chord: &shell_state::keys::Chord) -> ChordView {
    ChordView {
        label: chord.label(MAC_KEYS),
        warning: binds.conflict(chord),
    }
}

/// Say over the game that a press was kept (#1767): `text` shows a moment at
/// the top of the game's screen, the first press of a game building its
/// window. Nothing shows where there is no window layer. Safe from any thread.
pub fn confirm(app: &AppHandle, text: String) {
    let overlay = app.state::<SharedOverlay>();
    *overlay
        .confirmation
        .lock()
        .expect("overlay confirmation poisoned") =
        Some((text.clone(), Instant::now() + CONFIRMATION_FOR));
    #[cfg(any(target_os = "macos", windows))]
    panels::confirm(app, text);
    #[cfg(not(any(target_os = "macos", windows)))]
    let _ = text;
}

/// Bring the panels in line with the settings and the game. Cheap when nothing
/// changed; safe from any thread.
pub fn apply(app: &AppHandle) {
    #[cfg(any(target_os = "macos", windows))]
    panels::apply(app);
    #[cfg(not(any(target_os = "macos", windows)))]
    let _ = app;
}

/// The phase says whether a game runs. Called with every state the app
/// publishes (`supervisor::publish`): the panels switched on get their window
/// from the loading screen on, and lose it when the game ends.
pub fn follow(app: &AppHandle, running: bool) {
    let overlay = app.state::<SharedOverlay>();
    if overlay.game_running.swap(running, Ordering::SeqCst) != running {
        rebuild(app);
        if running {
            // The HUD as the game about to start was set up, for the plates.
            let handle = app.clone();
            tauri::async_runtime::spawn(async move {
                let overlay = handle.state::<SharedOverlay>().inner().clone();
                let Some(client) = handle.try_state::<SharedClient>() else {
                    return;
                };
                read_hud(&overlay, client.inner()).await;
                let _ = handle.emit(VIEW_EVENT, overlay.view());
                apply(&handle);
            });
        }
    }
}

/// The player's HUD settings from the client, into `overlay`. Without a
/// client, or with an answer this build cannot read, the last read stays —
/// Riot's defaults before any.
async fn read_hud(overlay: &Overlay, client: &SharedClient) {
    let client = client.read().expect("client lock poisoned").clone();
    let Some(client) = client else {
        return;
    };
    match client.game_settings().await {
        Ok(body) => match HudSettings::from_game_settings(&body) {
            Some(hud) => *overlay.hud.lock().expect("overlay hud poisoned") = hud,
            None => tracing::warn!("the client's game settings are not in a known shape"),
        },
        Err(error) => tracing::debug!(%error, "no game settings from the client"),
    }
}

fn publish(app: &AppHandle, overlay: &Overlay) -> OverlayView {
    let view = overlay.view();
    let _ = app.emit(VIEW_EVENT, &view);
    rebuild(app);
    view
}

#[tauri::command]
pub fn overlay_view(overlay: State<'_, SharedOverlay>) -> OverlayView {
    overlay.view()
}

/// The mark key's chord (#2079), which no panel may have.
fn mark_key(app: &AppHandle) -> Option<shell_state::keys::Chord> {
    app.try_state::<SharedRecorder>()?.settings().mark_key
}

/// Replace the settings. Read leniently, like the file: a value out of range
/// falls back alone — a panel on the mark key's chord too — and the answer is
/// what was kept.
#[tauri::command]
pub fn set_overlay_settings(
    app: AppHandle,
    overlay: State<'_, SharedOverlay>,
    settings: serde_json::Value,
) -> Result<OverlayView, String> {
    let settings = OverlaySettings::from_app(&settings.to_string());
    let settings = match mark_key(&app) {
        Some(chord) => settings.freeing(&chord),
        None => settings,
    };
    overlay
        .replace_settings(settings)
        .map_err(|error| error.to_string())?;
    Ok(publish(&app, &overlay))
}

/// Whether `trigger` can be `panel`'s, and how its chord reads, before the
/// page saves it: the page shows the refusal by the field that recorded it.
#[tauri::command]
pub fn overlay_check_trigger(
    app: AppHandle,
    overlay: State<'_, SharedOverlay>,
    panel: String,
    trigger: serde_json::Value,
) -> Result<TriggerCheck, String> {
    let panel = OverlayPanel::from_slug(&panel).ok_or("no such panel")?;
    let trigger: PanelTrigger =
        serde_json::from_value(trigger).map_err(|_| "a key this app does not know".to_string())?;
    let refusal = overlay.settings().refusal(panel, &trigger).or_else(|| {
        let marks = trigger.chord().is_some() && trigger.chord() == mark_key(&app);
        marks.then(|| "Already the key that marks a moment while a game records.".to_string())
    });
    Ok(TriggerCheck {
        refusal,
        chord: trigger.chord().map(|chord| overlay.chord_view(&chord)),
    })
}

/// Read the player's League keybindings from the client, for the chords'
/// warnings, and their HUD settings, for the plates the Overlay page draws.
/// Without a client, or with an answer this build cannot read, Riot's defaults
/// stay.
#[tauri::command]
pub async fn overlay_read_binds(
    app: AppHandle,
    overlay: State<'_, SharedOverlay>,
    client: State<'_, SharedClient>,
) -> Result<OverlayView, String> {
    read_hud(&overlay, &client).await;
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
    if !(width.is_finite() && height.is_finite()) || width < 1.0 || height < 1.0 {
        return;
    }
    if panel == CONFIRMATION_SLUG {
        *overlay
            .confirmation_size
            .lock()
            .expect("overlay confirmation poisoned") = (width.ceil(), height.ceil());
        apply(&app);
        return;
    }
    let Some(panel) = OverlayPanel::from_slug(&panel) else {
        return;
    };
    overlay
        .sizes
        .lock()
        .expect("overlay sizes poisoned")
        .insert(panel, (width.ceil(), height.ceil()));
    apply(&app);
}

/// What the confirmation's page shows when it opens: the text of the press
/// that built its window, emitted before the page listened.
#[tauri::command]
pub fn overlay_confirmation(overlay: State<'_, SharedOverlay>) -> Option<String> {
    overlay.confirmation(Instant::now())
}
