//! Spike #1673: a non-activating `NSPanel` over a live game.
//!
//! Built only with `--features overlay-spike`, on macOS. It answers one
//! question — can a panel sit over League without taking the game's input —
//! and is not the overlay itself: the panel shows a static page
//! (`app/public/overlay-spike.html`) and two global shortcuts drive it.
//! `desktop/overlay-spike/README.md` is the test protocol.
//!
//! The focus rule the issue sets is all here: the panel class can never become
//! key, the style mask is `NonactivatingPanel`, and the panel never hides when
//! the app deactivates. A `window_did_become_key` log line during a game is the
//! failure this spike exists to catch.

// `panel_event!` only parses handlers written `-> ()`.
#![allow(clippy::unused_unit)]

use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Mutex;
use std::time::{Duration, Instant};

use tauri::{AppHandle, LogicalPosition, LogicalSize, Manager, Position, Size, WebviewUrl};
use tauri_nspanel::{
    tauri_panel, CollectionBehavior, ManagerExt, PanelBuilder, PanelLevel, StyleMask,
    TrackingAreaOptions,
};
use tauri_plugin_global_shortcut::{
    Code, GlobalShortcutExt, Modifiers, Shortcut, ShortcutEvent, ShortcutState,
};

const LABEL: &str = "overlay-spike";
const WIDTH: f64 = 340.0;
const HEIGHT: f64 = 150.0;
const MARGIN: f64 = 24.0;

/// The game's own process, as opposed to the League client
/// (`com.riotgames.LeagueClient`): the panel belongs over this one only.
const GAME_BUNDLE_ID: &str = "com.riotgames.LeagueofLegends.GameClient";

/// Whether the game is the frontmost application, as last measured.
static GAME_FRONTMOST: AtomicBool = AtomicBool::new(false);

/// Whether the player hid the panel with the shortcut. It stays hidden for
/// as long as the app runs, until they show it again.
static USER_HIDDEN: AtomicBool = AtomicBool::new(false);

/// Whether the panel takes the mouse. Off by default: an overlay is
/// click-through until the player asks for it.
static INTERACTIVE: AtomicBool = AtomicBool::new(false);

tauri_panel! {
    panel!(OverlayPanel {
        config: {
            can_become_key_window: false,
            can_become_main_window: false,
            is_floating_panel: true,
            hides_on_deactivate: false
        }
        with: {
            tracking_area: {
                options: TrackingAreaOptions::new()
                    .active_always()
                    .mouse_entered_and_exited()
                    .mouse_moved(),
                auto_resize: true
            }
        }
    })

    panel_event!(OverlayPanelEvents {
        window_did_become_key(notification: &NSNotification) -> (),
        window_did_resign_key(notification: &NSNotification) -> ()
    })
}

/// `CGShieldingWindowLevel()`: the level League's "Full Screen" mode puts its
/// two screen-sized windows at (it captures the display). Measured in the
/// first in-game run: at `Status` (25) the panel was drawn behind the game.
const SHIELDING_LEVEL: i32 = 2_147_483_628;

/// The window level, from `TRUEMAIN_OVERLAY_LEVEL` so one build can try them
/// all: `shield` (one above the game's captured display, default), `status`,
/// `floating`, `screensaver`, or a raw number.
fn level() -> PanelLevel {
    let above_shield = PanelLevel::Custom(SHIELDING_LEVEL + 1);
    match std::env::var("TRUEMAIN_OVERLAY_LEVEL").as_deref() {
        Ok("status") => PanelLevel::Status,
        Ok("floating") => PanelLevel::Floating,
        Ok("screensaver") => PanelLevel::ScreenSaver,
        Ok(raw) => raw.parse().map(PanelLevel::Custom).unwrap_or(above_shield),
        Err(_) => above_shield,
    }
}

/// Two modifier pairs per action, because the first in-game test reached
/// neither ctrl+shift binding: if only one pair fires over the game, the other
/// collides with something; if neither does, the game swallows the keys.
const MODIFIERS: [Modifiers; 2] = [
    Modifiers::CONTROL.union(Modifiers::SHIFT),
    Modifiers::ALT.union(Modifiers::SHIFT),
];

fn shortcuts(code: Code) -> [Shortcut; 2] {
    MODIFIERS.map(|modifiers| Shortcut::new(Some(modifiers), code))
}

/// Registers the shortcut plugin. Called on the builder, before `setup`.
pub fn plugin() -> tauri::plugin::TauriPlugin<tauri::Wry> {
    tauri_plugin_global_shortcut::Builder::new()
        .with_handler(on_shortcut)
        .build()
}

/// Builds the panel, shows it without activating the app, and registers the
/// shortcuts. Called from `setup`, on the main thread.
pub fn setup(app: &AppHandle) -> Result<(), Box<dyn std::error::Error>> {
    if std::env::var("TRUEMAIN_OVERLAY_ACCESSORY").as_deref() == Ok("1") {
        app.set_activation_policy(tauri::ActivationPolicy::Accessory)?;
        tracing::info!("overlay spike: activation policy set to Accessory");
    }

    let level = level();
    let x = app
        .primary_monitor()?
        .map(|monitor| {
            let scale = monitor.scale_factor();
            monitor.size().to_logical::<f64>(scale).width - WIDTH - MARGIN
        })
        .unwrap_or(MARGIN);

    let panel = PanelBuilder::<_, OverlayPanel>::new(app, LABEL)
        .url(WebviewUrl::App("overlay-spike.html".into()))
        .title("TrueMain overlay spike")
        .position(Position::Logical(LogicalPosition::new(x, MARGIN)))
        .size(Size::Logical(LogicalSize::new(WIDTH, HEIGHT)))
        .level(level)
        .floating(true)
        .hides_on_deactivate(false)
        .becomes_key_only_if_needed(true)
        .accepts_mouse_moved_events(true)
        .ignores_mouse_events(true)
        .has_shadow(true)
        .corner_radius(12.0)
        .alpha_value(0.92)
        .add_style_mask(StyleMask::empty().nonactivating_panel())
        .collection_behavior(
            CollectionBehavior::new()
                .can_join_all_spaces()
                .full_screen_auxiliary()
                .stationary()
                .ignores_cycle(),
        )
        .with_window(|window| {
            window
                .decorations(false)
                .resizable(false)
                .focused(false)
                .visible(false)
                .skip_taskbar(true)
        })
        .build()?;

    let events = OverlayPanelEvents::new();
    events.window_did_become_key(|_| {
        tracing::warn!("overlay spike: the panel BECAME KEY — it took the keyboard");
    });
    events.window_did_resign_key(|_| {
        tracing::info!("overlay spike: the panel resigned key");
    });
    panel.set_event_handler(Some(events.as_ref()));

    tracing::info!(
        level = level.value(),
        can_become_key = panel.can_become_key_window(),
        "overlay spike: panel built (click-through), shown only while the game is frontmost"
    );

    let registry = app.global_shortcut();
    for shortcut in shortcuts(Code::KeyO)
        .into_iter()
        .chain(shortcuts(Code::KeyI))
    {
        registry.register(shortcut)?;
    }
    tracing::info!(
        "overlay spike: ctrl+shift+O or option+shift+O toggles the panel, ctrl+shift+I or option+shift+I its mouse"
    );

    let poller = app.clone();
    std::thread::spawn(move || poll_keys(&poller));
    Ok(())
}

/// The second test showed the hotkeys reach the app everywhere but in a Full
/// Screen game: with the display captured, the window server stops delivering
/// `RegisterEventHotKey` events. This reads the keyboard's state instead, which
/// no event routing stands in front of.
mod key_state {
    #[link(name = "CoreGraphics", kind = "framework")]
    extern "C" {
        fn CGEventSourceKeyState(state: i32, key: u16) -> bool;
        fn CGEventSourceFlagsState(state: i32) -> u64;
    }

    /// `kCGEventSourceStateHIDSystemState`: the hardware's state, whichever app
    /// the events are routed to.
    const HID_SYSTEM_STATE: i32 = 1;

    const FLAG_SHIFT: u64 = 0x0002_0000;
    const FLAG_CONTROL: u64 = 0x0004_0000;
    const FLAG_OPTION: u64 = 0x0008_0000;

    /// `kVK_ANSI_O` and `kVK_ANSI_I`: physical key positions, not characters.
    pub const KEY_O: u16 = 0x1F;
    pub const KEY_I: u16 = 0x22;

    /// Whether shift and ctrl or option are held with `key`.
    pub fn chord_down(key: u16) -> bool {
        // SAFETY: plain C calls on value arguments, no pointers involved.
        let (flags, down) = unsafe {
            (
                CGEventSourceFlagsState(HID_SYSTEM_STATE),
                CGEventSourceKeyState(HID_SYSTEM_STATE, key),
            )
        };
        down && flags & FLAG_SHIFT != 0 && flags & (FLAG_CONTROL | FLAG_OPTION) != 0
    }
}

const POLL_EVERY: Duration = Duration::from_millis(30);

fn poll_keys(app: &AppHandle) {
    let mut held = [false; 2];
    let mut tick: u32 = 0;
    loop {
        for (slot, (key, code)) in [
            (key_state::KEY_O, Code::KeyO),
            (key_state::KEY_I, Code::KeyI),
        ]
        .into_iter()
        .enumerate()
        {
            let down = key_state::chord_down(key);
            if down && !held[slot] {
                trigger(app, code, "key-state poll");
            }
            held[slot] = down;
        }
        tick = tick.wrapping_add(1);
        if tick.is_multiple_of(FRONTMOST_EVERY) {
            let handle = app.clone();
            let _ = app.run_on_main_thread(move || watch_frontmost(&handle));
        }
        std::thread::sleep(POLL_EVERY);
    }
}

/// Checks the frontmost application every ~240 ms: often enough that the
/// panel follows a cmd-tab without a visible lag.
const FRONTMOST_EVERY: u32 = 8;

/// Main thread only (AppKit).
fn watch_frontmost(app: &AppHandle) {
    use objc2_app_kit::NSWorkspace;

    let front = NSWorkspace::sharedWorkspace()
        .frontmostApplication()
        .and_then(|front| front.bundleIdentifier())
        .is_some_and(|id| id.to_string() == GAME_BUNDLE_ID);
    if GAME_FRONTMOST.swap(front, Ordering::SeqCst) != front {
        tracing::info!(
            "overlay spike: the game is {}",
            if front {
                "frontmost"
            } else {
                "no longer frontmost"
            }
        );
        apply_visibility(app);
    }
}

/// Shows the panel when the game is frontmost and the player has not hidden
/// it, hides it otherwise. Main thread only.
fn apply_visibility(app: &AppHandle) {
    let Ok(panel) = app.get_webview_panel(LABEL) else {
        return;
    };
    let visible = GAME_FRONTMOST.load(Ordering::SeqCst) && !USER_HIDDEN.load(Ordering::SeqCst);
    if visible == panel.is_visible() {
        return;
    }
    if visible {
        panel.show();
        tracing::info!("overlay spike: panel shown");
    } else {
        panel.hide();
        tracing::info!("overlay spike: panel hidden");
    }
}

fn on_shortcut(app: &AppHandle, shortcut: &Shortcut, event: ShortcutEvent) {
    tracing::info!(
        "overlay spike: shortcut {} {:?}",
        shortcut.into_string(),
        event.state()
    );
    if event.state() == ShortcutState::Pressed {
        trigger(app, shortcut.key, "hotkey");
    }
}

/// Outside a game both paths see the same keypress; the second to arrive
/// within this window is logged and dropped, so the log still says which
/// paths work.
const DEDUP_WINDOW: Duration = Duration::from_millis(400);

/// The last action taken, per key.
static LAST_ACTION: Mutex<Vec<(Code, Instant)>> = Mutex::new(Vec::new());

fn trigger(app: &AppHandle, code: Code, source: &'static str) {
    {
        let mut last = LAST_ACTION.lock().expect("last-action mutex poisoned");
        let now = Instant::now();
        if last
            .iter()
            .any(|(key, at)| *key == code && now.duration_since(*at) < DEDUP_WINDOW)
        {
            tracing::info!("overlay spike: {code:?} via {source} (duplicate, ignored)");
            return;
        }
        last.retain(|(key, _)| *key != code);
        last.push((code, now));
    }
    tracing::info!("overlay spike: {code:?} via {source}");

    let handle = app.clone();
    // Panel methods are AppKit calls: main thread only.
    let _ = app.run_on_main_thread(move || {
        let Ok(panel) = handle.get_webview_panel(LABEL) else {
            return;
        };
        if code == Code::KeyO {
            let hidden = !USER_HIDDEN.fetch_xor(true, Ordering::SeqCst);
            tracing::info!(
                "overlay spike: the player {} the panel",
                if hidden { "hid" } else { "showed" }
            );
            apply_visibility(&handle);
        } else if code == Code::KeyI {
            let now = !INTERACTIVE.fetch_xor(true, Ordering::SeqCst);
            panel.set_ignores_mouse_events(!now);
            let mode = if now { "interactive" } else { "click-through" };
            tracing::info!("overlay spike: panel is {mode}");
            if let Some(window) = handle.get_webview_window(LABEL) {
                let _ = window.eval(format!("window.overlaySpike?.setMode('{mode}')"));
            }
        }
    });
}
