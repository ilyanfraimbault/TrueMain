//! The app's window opens where the player left it (#1914): its size, place
//! and maximised state are noted as they change and written to
//! `window-state.json` in the config folder when the app quits.
//!
//! What to do with a saved place on a desk that changed since — a monitor
//! unplugged, another scale — is `shell_state::window`'s rule. The window is
//! created hidden (`tauri.conf.json`) and shown once placed, so it never
//! appears at its default place first.

use std::path::PathBuf;
use std::sync::Mutex;

use shell_state::overlay::Rect;
use shell_state::window::{Display, WindowPlacement};
use tauri::{
    AppHandle, LogicalSize, Manager, PhysicalPosition, Position, Size, WebviewWindow, WindowEvent,
};

const MAIN: &str = "main";
const FILE: &str = "window-state.json";

/// The window as last seen outside full screen and minimised.
static LAST: Mutex<Option<WindowPlacement>> = Mutex::new(None);

fn path(app: &AppHandle) -> PathBuf {
    app.path()
        .app_config_dir()
        .unwrap_or_else(|_| std::env::temp_dir().join("truemain"))
        .join(FILE)
}

/// Place the window as saved, show it, and follow it. Called from `setup`.
pub fn restore(app: &AppHandle) {
    let Some(window) = app.get_webview_window(MAIN) else {
        return;
    };
    if let Some(saved) = WindowPlacement::load(&path(app)) {
        let restore = saved.restore(&displays(app));
        let _ = window.set_size(Size::Logical(LogicalSize::new(
            restore.size.0,
            restore.size.1,
        )));
        let _ = match restore.position {
            Some((x, y)) => window.set_position(Position::Physical(PhysicalPosition::new(x, y))),
            None => window.center(),
        };
        if restore.maximized {
            let _ = window.maximize();
        }
        *LAST.lock().expect("window state poisoned") = Some(saved);
    } else {
        // Nothing saved yet: noted as it opens, so a window maximised before
        // it ever moves still has a size and place to un-maximise to.
        note(&window);
    }
    let _ = window.show();
    let followed = window.clone();
    window.on_window_event(move |event| match event {
        WindowEvent::Moved(_) | WindowEvent::Resized(_) => note(&followed),
        WindowEvent::CloseRequested { .. } => save(followed.app_handle()),
        _ => {}
    });
}

/// The monitors, the primary one first.
fn displays(app: &AppHandle) -> Vec<Display> {
    let primary = app.primary_monitor().ok().flatten();
    let mut monitors = app.available_monitors().unwrap_or_default();
    monitors.sort_by_key(|monitor| {
        primary
            .as_ref()
            .is_none_or(|primary| primary.position() != monitor.position())
    });
    monitors
        .iter()
        .map(|monitor| Display {
            frame: Rect {
                x: f64::from(monitor.position().x),
                y: f64::from(monitor.position().y),
                width: f64::from(monitor.size().width),
                height: f64::from(monitor.size().height),
            },
            scale: monitor.scale_factor(),
        })
        .collect()
}

/// Note the window's size and place. Maximised, only the flag changes: the
/// size and place it un-maximises to stay the ones noted before.
fn note(window: &WebviewWindow) {
    if window.is_fullscreen().unwrap_or(false) || window.is_minimized().unwrap_or(false) {
        return;
    }
    let maximized = window.is_maximized().unwrap_or(false);
    let mut last = LAST.lock().expect("window state poisoned");
    if maximized {
        if let Some(last) = last.as_mut() {
            last.maximized = true;
        }
        return;
    }
    let (Ok(position), Ok(size), Ok(scale)) = (
        window.outer_position(),
        window.inner_size(),
        window.scale_factor(),
    ) else {
        return;
    };
    let size = size.to_logical::<f64>(scale);
    *last = Some(WindowPlacement {
        x: position.x,
        y: position.y,
        width: size.width,
        height: size.height,
        maximized: false,
    });
}

/// Write what was last noted. Called when the window closes and when the app
/// quits (macOS quits from its menu without closing the window first).
pub fn save(app: &AppHandle) {
    let last = *LAST.lock().expect("window state poisoned");
    if let Some(placement) = last {
        if let Err(error) = placement.save(&path(app)) {
            tracing::warn!(%error, "the window's place could not be saved");
        }
    }
}
