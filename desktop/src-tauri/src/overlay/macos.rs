//! The overlay's window on macOS, as the #1673 spike measured it over live
//! games (`docs/desktop-overlay-spike.md`):
//!
//! - League's "Full Screen" captures the display: its windows sit at
//!   `CGShieldingWindowLevel`, so the panel goes one level above, and shows
//!   only while the game's process is frontmost — at that level it would
//!   otherwise cover every other app.
//! - The panel class can never become key and is `NonactivatingPanel`, so the
//!   game keeps the keyboard; it ignores the mouse outside the preview, so the
//!   game keeps every click.
//! - With the display captured no hotkey reaches the app, so the shortcut —
//!   and TAB, which opens the game's scoreboard and with it the item value —
//!   are read from the keyboard's state instead, no permission involved.
//! - `PanelBuilder::no_activate` stays off: it flips the activation policy and
//!   left the app with none of its windows on screen.

// `tauri_panel!` expands to code clippy flags as a unit return.
#![allow(clippy::unused_unit)]

use std::collections::HashMap;
use std::sync::Mutex;
use std::time::Duration;

use objc2_app_kit::NSWorkspace;
use shell_state::overlay::{OverlayPanel, OverlaySettings, Rect};
use tauri::{
    AppHandle, Emitter, LogicalPosition, LogicalSize, Manager, Position, Size, WebviewUrl,
    WindowEvent,
};
use tauri_nspanel::{
    tauri_panel, CollectionBehavior, ManagerExt, PanelBuilder, PanelLevel, StyleMask,
};

use super::{label, SharedOverlay, VIEW_EVENT};

/// `CGShieldingWindowLevel()`, where a game that captured the display draws.
const SHIELDING_LEVEL: i32 = 2_147_483_628;

/// The game's own process, as opposed to the League client
/// (`com.riotgames.LeagueClient`): the panel belongs over this one only.
const GAME_BUNDLE_ID: &str = "com.riotgames.LeagueofLegends.GameClient";

const KEYS_EVERY: Duration = Duration::from_millis(30);
/// Ticks of `KEYS_EVERY` between two looks at the frontmost app (~240 ms):
/// often enough that the panel follows a cmd-tab without a visible lag.
const FRONTMOST_EVERY: u32 = 8;

tauri_panel! {
    panel!(OverlayWindow {
        config: {
            can_become_key_window: false,
            can_become_main_window: false,
            is_floating_panel: true,
            hides_on_deactivate: false
        }
    })
}

/// What was last applied to a panel's window, so `apply` touches it only on change.
#[derive(Debug, Clone, Copy, PartialEq)]
struct Applied {
    visible: bool,
    interactive: bool,
    origin: (f64, f64),
    size: (f64, f64),
    opacity: f64,
}

static APPLIED: Mutex<Option<HashMap<OverlayPanel, Applied>>> = Mutex::new(None);

pub fn setup(app: &AppHandle) -> Result<(), Box<dyn std::error::Error>> {
    for panel in OverlayPanel::ALL {
        build(app, panel)?;
    }
    let watcher = app.clone();
    std::thread::spawn(move || watch(&watcher));
    Ok(())
}

/// One panel's window, hidden, its page on `#/overlay/<panel>`.
fn build(app: &AppHandle, which: OverlayPanel) -> Result<(), Box<dyn std::error::Error>> {
    let settings = app.state::<SharedOverlay>().settings();
    let label = label(which);
    let panel = PanelBuilder::<_, OverlayWindow>::new(app, &label)
        .url(WebviewUrl::App(
            format!("#/overlay/{}", which.slug()).into(),
        ))
        .title("TrueMain overlay")
        .size(Size::Logical(LogicalSize::new(
            super::INITIAL_SIZE.0,
            super::INITIAL_SIZE.1,
        )))
        .level(PanelLevel::Custom(SHIELDING_LEVEL + 1))
        .floating(true)
        .hides_on_deactivate(false)
        .becomes_key_only_if_needed(true)
        .ignores_mouse_events(true)
        .has_shadow(true)
        .corner_radius(12.0)
        .alpha_value(settings.opacity)
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
    panel.hide();

    if let Some(window) = app.get_webview_window(&label) {
        let handle = app.clone();
        window.on_window_event(move |event| {
            if let WindowEvent::Moved(position) = event {
                dragged(&handle, which, *position);
            }
        });
    }
    Ok(())
}

/// The primary screen, in points: where the game runs in Full Screen.
fn screen(app: &AppHandle) -> Option<Rect> {
    let monitor = app.primary_monitor().ok().flatten()?;
    let scale = monitor.scale_factor();
    let position = monitor.position().to_logical::<f64>(scale);
    let size = monitor.size().to_logical::<f64>(scale);
    Some(Rect {
        x: position.x,
        y: position.y,
        width: size.width,
        height: size.height,
    })
}

/// Where a panel's window should be and look, now.
fn wanted(
    overlay: &SharedOverlay,
    settings: &OverlaySettings,
    inputs: &shell_state::overlay::OverlayInputs,
    screen: Rect,
    panel: OverlayPanel,
) -> Applied {
    let size = overlay.size(panel);
    Applied {
        visible: settings.shows(panel, inputs),
        interactive: inputs.preview,
        origin: settings.origin(panel, screen, size),
        size,
        opacity: settings.opacity,
    }
}

pub fn apply(app: &AppHandle) {
    let handle = app.clone();
    let _ = app.run_on_main_thread(move || apply_now(&handle));
}

/// Main thread only (AppKit).
fn apply_now(app: &AppHandle) {
    let overlay = app.state::<SharedOverlay>().inner().clone();
    let settings = overlay.settings();
    let inputs = overlay.inputs(app);
    let Some(screen) = screen(app) else {
        return;
    };
    for panel in OverlayPanel::ALL {
        let next = wanted(&overlay, &settings, &inputs, screen, panel);
        apply_panel(app, panel, next);
    }
}

/// Main thread only (AppKit).
fn apply_panel(app: &AppHandle, which: OverlayPanel, next: Applied) {
    let label = label(which);
    let mut state = APPLIED.lock().expect("overlay state poisoned");
    let applied = state.get_or_insert_with(HashMap::new);
    let previous = applied.get(&which).copied();
    if previous == Some(next) {
        return;
    }
    let (Ok(panel), Some(window)) = (
        app.get_webview_panel(&label),
        app.get_webview_window(&label),
    ) else {
        return;
    };
    // Recorded before moving the window, so the `Moved` it fires is known as ours.
    applied.insert(which, next);
    drop(state);

    let resized = previous.map(|p| p.size) != Some(next.size);
    if resized {
        let _ = window.set_size(Size::Logical(LogicalSize::new(next.size.0, next.size.1)));
    }
    // AppKit resizes a window about its bottom edge: the top moves with any
    // change of height, so the position is set again after every resize.
    if resized || previous.map(|p| p.origin) != Some(next.origin) {
        let _ = window.set_position(Position::Logical(LogicalPosition::new(
            next.origin.0,
            next.origin.1,
        )));
    }
    if previous.map(|p| p.opacity) != Some(next.opacity) {
        panel.set_alpha_value(next.opacity);
    }
    if previous.map(|p| p.interactive) != Some(next.interactive) {
        // Only the preview takes the mouse, to be dragged into place.
        panel.set_ignores_mouse_events(!next.interactive);
    }
    if previous.map(|p| p.visible) != Some(next.visible) {
        if next.visible {
            panel.show();
        } else {
            panel.hide();
        }
        tracing::info!(panel = which.slug(), visible = next.visible, "overlay");
    }
}

/// A panel moved. Ours when it lands where `apply` put it; otherwise the
/// player dragged it in the preview, and that becomes its custom place.
fn dragged(app: &AppHandle, which: OverlayPanel, position: tauri::PhysicalPosition<i32>) {
    let overlay = app.state::<SharedOverlay>().inner().clone();
    let applied = APPLIED
        .lock()
        .expect("overlay state poisoned")
        .as_ref()
        .and_then(|applied| applied.get(&which).copied());
    let Some(applied) = applied else {
        return;
    };
    if !applied.interactive {
        return;
    }
    let Some(window) = app.get_webview_window(&label(which)) else {
        return;
    };
    let scale = window.scale_factor().unwrap_or(1.0);
    let origin = position.to_logical::<f64>(scale);
    let origin = (origin.x, origin.y);
    if (origin.0 - applied.origin.0).abs() < 1.0 && (origin.1 - applied.origin.1).abs() < 1.0 {
        return;
    }
    let Some(screen) = screen(app) else {
        return;
    };
    overlay.place(
        which,
        OverlaySettings::point_at(screen, origin, applied.size),
    );
    let _ = app.emit(VIEW_EVENT, overlay.view());
    apply(app);
}

/// Follows the frontmost app and the shortcut for the app's life.
fn watch(app: &AppHandle) {
    let mut held = false;
    let mut tick: u32 = 0;
    loop {
        tick = tick.wrapping_add(1);
        if tick.is_multiple_of(FRONTMOST_EVERY) {
            let handle = app.clone();
            let _ = app.run_on_main_thread(move || {
                let front = NSWorkspace::sharedWorkspace()
                    .frontmostApplication()
                    .and_then(|front| front.bundleIdentifier())
                    .is_some_and(|id| id.to_string() == GAME_BUNDLE_ID);
                let overlay = handle.state::<SharedOverlay>().inner().clone();
                if overlay.set_game_frontmost(front) {
                    tracing::info!(front, "the game's frontmost state changed");
                }
                // Also picks up a death, a respawn and the game's end.
                apply_now(&handle);
            });
        }

        // The keys only mean something over the game.
        let overlay = app.state::<SharedOverlay>().inner().clone();
        let front = overlay
            .game_frontmost
            .load(std::sync::atomic::Ordering::SeqCst);
        let down = front && key_state::shortcut_down();
        if down && !held {
            let hidden = overlay.toggle_hidden();
            tracing::info!(hidden, "overlay shortcut");
            apply(app);
        }
        held = down;
        if overlay.set_scoreboard(front && key_state::tab_down()) {
            apply(app);
        }
        std::thread::sleep(KEYS_EVERY);
    }
}

/// The keyboard's state, read rather than delivered: with the display
/// captured, the window server routes no hotkey to the app.
mod key_state {
    #[link(name = "CoreGraphics", kind = "framework")]
    extern "C" {
        fn CGEventSourceKeyState(state: i32, key: u16) -> bool;
        fn CGEventSourceFlagsState(state: i32) -> u64;
    }

    /// `kCGEventSourceStateHIDSystemState`: the hardware's state, whichever
    /// app the events go to.
    const HID_SYSTEM_STATE: i32 = 1;
    const FLAG_SHIFT: u64 = 0x0002_0000;
    const FLAG_OPTION: u64 = 0x0008_0000;
    /// `kVK_ANSI_O`: a key position, so it holds on any keyboard layout.
    const KEY_O: u16 = 0x1F;
    /// `kVK_Tab`, the game's scoreboard key.
    const KEY_TAB: u16 = 0x30;

    /// Whether TAB is held — the game's scoreboard is open while it is.
    pub fn tab_down() -> bool {
        // SAFETY: a plain C call on value arguments.
        unsafe { CGEventSourceKeyState(HID_SYSTEM_STATE, KEY_TAB) }
    }

    /// Whether ⌥⇧O is held.
    pub fn shortcut_down() -> bool {
        // SAFETY: plain C calls on value arguments.
        let (flags, down) = unsafe {
            (
                CGEventSourceFlagsState(HID_SYSTEM_STATE),
                CGEventSourceKeyState(HID_SYSTEM_STATE, KEY_O),
            )
        };
        down && flags & FLAG_SHIFT != 0 && flags & FLAG_OPTION != 0
    }
}
