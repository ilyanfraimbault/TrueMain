//! The overlay's panels, whatever the window layer: one window per panel
//! switched on, built when a game starts and closed when it ends (#1916),
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

use shell_state::keys::KeysDown;
use shell_state::overlay::{OverlayInputs, OverlayPanel, OverlaySettings, Rect};
use shell_state::screens::{self, Monitor};
use tauri::{
    AppHandle, Emitter, LogicalPosition, LogicalSize, Manager, PhysicalPosition, Position, Size,
    WindowEvent,
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
    /// The scale of the monitor `origin` is on.
    scale: f64,
    size: (f64, f64),
    opacity: f64,
}

/// A monitor's frame in points, and its scale.
#[derive(Debug, Clone, Copy, PartialEq)]
struct Screen {
    area: Rect,
    scale: f64,
}

static APPLIED: Mutex<Option<HashMap<OverlayPanel, Applied>>> = Mutex::new(None);
/// The monitor the game was last seen on (#1914).
static GAME_SCREEN: Mutex<Option<Screen>> = Mutex::new(None);

/// Serialises `rebuild`: two of them interleaved could build a panel twice.
static REBUILDING: Mutex<()> = Mutex::new(());

pub fn setup(app: &AppHandle) {
    let watcher = app.clone();
    std::thread::spawn(move || watch(&watcher));
}

/// Build the windows the panels now need and destroy the others
/// (`OverlaySettings::needs_window`), then bring them in line. Called when
/// the game starts or ends and when the settings or the preview change.
///
/// Off the main thread: on Windows a webview built inside a command's
/// handler deadlocks (wry#583), and the settings arrive through commands.
pub fn rebuild(app: &AppHandle) {
    let handle = app.clone();
    std::thread::spawn(move || {
        let _rebuilding = REBUILDING.lock().expect("overlay rebuild poisoned");
        let overlay = handle.state::<SharedOverlay>().inner().clone();
        let settings = overlay.settings();
        let inputs = overlay.inputs(&handle);
        for panel in OverlayPanel::ALL {
            let label = label(panel);
            let exists = handle.get_webview_window(&label).is_some();
            match (settings.needs_window(panel, &inputs), exists) {
                (true, false) => build(&handle, panel, settings.opacity),
                (false, true) => {
                    platform::destroy(&handle, &label);
                    // Gone from the app once its event loop has closed it:
                    // the next rebuild must not find it still there.
                    for _ in 0..50 {
                        if handle.get_webview_window(&label).is_none() {
                            break;
                        }
                        std::thread::sleep(Duration::from_millis(20));
                    }
                    forget(panel);
                    tracing::info!(panel = panel.slug(), "overlay window closed");
                }
                _ => {}
            }
        }
        apply(&handle);
    });
}

/// Not on the main thread (`rebuild`).
fn build(app: &AppHandle, panel: OverlayPanel, opacity: f64) {
    let label = label(panel);
    let started = std::time::Instant::now();
    if let Err(error) =
        platform::build(app, &label, &format!("#/overlay/{}", panel.slug()), opacity)
    {
        tracing::error!(panel = panel.slug(), %error, "cannot build an overlay window");
        return;
    }
    // A new window starts from nothing: `apply` sets all of it.
    forget(panel);
    if let Some(window) = app.get_webview_window(&label) {
        let handle = app.clone();
        window.on_window_event(move |event| {
            if let WindowEvent::Moved(position) = event {
                dragged(&handle, panel, *position);
            }
        });
    }
    tracing::info!(
        panel = panel.slug(),
        elapsed_ms = started.elapsed().as_millis() as u64,
        "overlay window built"
    );
}

fn forget(panel: OverlayPanel) {
    if let Some(applied) = APPLIED.lock().expect("overlay state poisoned").as_mut() {
        applied.remove(&panel);
    }
}

/// The screen the panels go on, in points: the monitor the game was last seen
/// on while it still exists, else the primary one — before any game, and in
/// the settings' preview on a desk that has changed since.
fn screen(app: &AppHandle) -> Option<Screen> {
    let monitors = app.available_monitors().unwrap_or_default();
    let seen = *GAME_SCREEN.lock().expect("overlay screen poisoned");
    if let Some(seen) = seen.filter(|seen| monitors.iter().any(|monitor| of(monitor) == *seen)) {
        return Some(seen);
    }
    app.primary_monitor()
        .ok()
        .flatten()
        .or_else(|| monitors.into_iter().next())
        .map(|monitor| of(&monitor))
}

fn of(monitor: &tauri::Monitor) -> Screen {
    Screen {
        area: area(monitor),
        scale: monitor.scale_factor(),
    }
}

/// A monitor's frame in points.
fn area(monitor: &tauri::Monitor) -> Rect {
    let scale = monitor.scale_factor();
    let position = monitor.position().to_logical::<f64>(scale);
    let size = monitor.size().to_logical::<f64>(scale);
    Rect {
        x: position.x,
        y: position.y,
        width: size.width,
        height: size.height,
    }
}

/// A monitor's frame in pixels.
fn pixels(monitor: &tauri::Monitor) -> Rect {
    let position = monitor.position();
    let size = monitor.size();
    Rect {
        x: f64::from(position.x),
        y: f64::from(position.y),
        width: f64::from(size.width),
        height: f64::from(size.height),
    }
}

/// Note which monitor the game's window is on. Main thread, and only while
/// the game is frontmost: its window is the one the platform measures.
fn locate_game(app: &AppHandle) {
    let Some(frame) = platform::game_frame() else {
        return;
    };
    let available = app.available_monitors().unwrap_or_default();
    let monitors: Vec<Monitor> = available
        .iter()
        .map(|monitor| Monitor {
            bounds: if platform::FRAME_IN_POINTS {
                area(monitor)
            } else {
                pixels(monitor)
            },
            area: area(monitor),
        })
        .collect();
    let Some(held) = screens::holding(&monitors, frame) else {
        return;
    };
    let Some(screen) = available
        .iter()
        .map(of)
        .find(|screen| screen.area == held.area)
    else {
        return;
    };
    let previous = GAME_SCREEN
        .lock()
        .expect("overlay screen poisoned")
        .replace(screen);
    if previous != Some(screen) {
        tracing::info!(x = screen.area.x, y = screen.area.y, "the game's monitor");
    }
}

/// Where a panel's window should be and look, now.
fn wanted(
    overlay: &SharedOverlay,
    settings: &OverlaySettings,
    inputs: &OverlayInputs,
    screen: Screen,
    panel: OverlayPanel,
) -> Applied {
    let size = overlay.size(panel);
    Applied {
        visible: settings.shows(panel, inputs),
        interactive: inputs.preview,
        origin: settings.origin(panel, screen.area, size),
        scale: screen.scale,
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
    if resized || previous.map(|p| (p.origin, p.scale)) != Some((next.origin, next.scale)) {
        let _ = window.set_position(place(next.origin, next.scale));
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
        // Over a game, not in the settings' preview (#1805).
        if next.visible && !next.interactive {
            crate::telemetry::feature(app, crate::telemetry::Feature::OverlayShown);
        }
        tracing::info!(panel = which.slug(), visible = next.visible, "overlay");
    }
}

/// Where to put a window whose top-left corner is `origin`, in the points of a
/// monitor of `scale`. On macOS points are one space across every display. On
/// Windows a logical position is scaled by the monitor the window is on now,
/// not the one it is going to: on a desk with two scales, a panel crossing
/// over would land elsewhere, so it is placed in pixels instead.
fn place(origin: (f64, f64), scale: f64) -> Position {
    if cfg!(windows) {
        Position::Physical(PhysicalPosition::new(
            (origin.0 * scale).round() as i32,
            (origin.1 * scale).round() as i32,
        ))
    } else {
        Position::Logical(LogicalPosition::new(origin.0, origin.1))
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
        OverlaySettings::point_at(screen.area, origin, applied.size),
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
                if front {
                    locate_game(&handle);
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
        // Only the keys the panels' chords name: a few reads per tick.
        let keys = if front {
            platform::keys::read(&overlay.settings().chord_keys())
        } else {
            KeysDown::default()
        };
        if overlay.read_keys(&keys) {
            apply(app);
        }
        std::thread::sleep(KEYS_EVERY);
    }
}
