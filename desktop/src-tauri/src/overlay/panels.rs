//! The overlay's panels, whatever the window layer: one window per panel,
//! sized to its page and placed by the settings, shown or hidden by
//! `shell_state::overlay`'s rule, dragged into place in the preview — and the
//! watch that feeds the rule the game's frontmost state and the keys held.
//!
//! The platform module (`macos.rs`, `windows.rs`) builds the windows and
//! owns what differs: how a window stays above the game without taking its
//! focus, ignores the mouse, fades, and how the frontmost app and the keys
//! are read.

use std::collections::HashMap;
use std::sync::atomic::Ordering;
use std::sync::Mutex;
use std::time::Duration;

use shell_state::overlay::{OverlayInputs, OverlayPanel, OverlaySettings, Rect};
use tauri::{
    AppHandle, Emitter, LogicalPosition, LogicalSize, Manager, Position, Size, WindowEvent,
};

use super::platform;
use super::{label, SharedOverlay, VIEW_EVENT};

const KEYS_EVERY: Duration = Duration::from_millis(30);
/// Ticks of `KEYS_EVERY` between two looks at the frontmost app (~240 ms):
/// often enough that the panels follow a switch away without a visible lag.
const FRONTMOST_EVERY: u32 = 8;

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
    let opacity = app.state::<SharedOverlay>().settings().opacity;
    for panel in OverlayPanel::ALL {
        let label = label(panel);
        platform::build(app, &label, &format!("#/overlay/{}", panel.slug()), opacity)?;
        if let Some(window) = app.get_webview_window(&label) {
            let handle = app.clone();
            window.on_window_event(move |event| {
                if let WindowEvent::Moved(position) = event {
                    dragged(&handle, panel, *position);
                }
            });
        }
    }
    let watcher = app.clone();
    std::thread::spawn(move || watch(&watcher));
    Ok(())
}

/// The primary screen, in points: where the game runs.
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
    inputs: &OverlayInputs,
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

/// Main thread only (AppKit on macOS; the windows' own thread on Windows).
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

/// Main thread only.
fn apply_panel(app: &AppHandle, which: OverlayPanel, next: Applied) {
    let label = label(which);
    let mut state = APPLIED.lock().expect("overlay state poisoned");
    let applied = state.get_or_insert_with(HashMap::new);
    let previous = applied.get(&which).copied();
    if previous == Some(next) {
        return;
    }
    let Some(window) = app.get_webview_window(&label) else {
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
        platform::set_opacity(app, &label, next.opacity);
    }
    if previous.map(|p| p.interactive) != Some(next.interactive) {
        // Only the preview takes the mouse, to be dragged into place.
        platform::set_interactive(app, &label, next.interactive);
    }
    if previous.map(|p| p.visible) != Some(next.visible) {
        platform::show(app, &label, next.visible);
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

/// Follows the frontmost app and the keys for the app's life.
fn watch(app: &AppHandle) {
    let mut held = false;
    let mut tick: u32 = 0;
    loop {
        tick = tick.wrapping_add(1);
        if tick.is_multiple_of(FRONTMOST_EVERY) {
            let handle = app.clone();
            let _ = app.run_on_main_thread(move || {
                let front = platform::game_frontmost();
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
        let front = overlay.game_frontmost.load(Ordering::SeqCst);
        let down = front && platform::keys::shortcut_down();
        if down && !held {
            let hidden = overlay.toggle_hidden();
            tracing::info!(hidden, "overlay shortcut");
            apply(app);
        }
        held = down;
        if overlay.set_scoreboard(front && platform::keys::tab_down()) {
            apply(app);
        }
        std::thread::sleep(KEYS_EVERY);
    }
}
