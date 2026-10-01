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

/// The window level, from `TRUEMAIN_OVERLAY_LEVEL` so one build can try them
/// all: `status` (the issue's choice, default), `floating`, `screensaver`, or a
/// raw number.
fn level() -> PanelLevel {
    match std::env::var("TRUEMAIN_OVERLAY_LEVEL").as_deref() {
        Ok("floating") => PanelLevel::Floating,
        Ok("screensaver") => PanelLevel::ScreenSaver,
        Ok(raw) => raw
            .parse()
            .map(PanelLevel::Custom)
            .unwrap_or(PanelLevel::Status),
        Err(_) => PanelLevel::Status,
    }
}

fn shortcut_toggle_visible() -> Shortcut {
    Shortcut::new(Some(Modifiers::CONTROL | Modifiers::SHIFT), Code::KeyO)
}

fn shortcut_toggle_interactive() -> Shortcut {
    Shortcut::new(Some(Modifiers::CONTROL | Modifiers::SHIFT), Code::KeyI)
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

    panel.show();
    tracing::info!(
        level = level.value(),
        can_become_key = panel.can_become_key_window(),
        "overlay spike: panel shown (click-through)"
    );

    let shortcuts = app.global_shortcut();
    shortcuts.register(shortcut_toggle_visible())?;
    shortcuts.register(shortcut_toggle_interactive())?;
    tracing::info!("overlay spike: ctrl+shift+O toggles the panel, ctrl+shift+I its mouse");
    Ok(())
}

fn on_shortcut(app: &AppHandle, shortcut: &Shortcut, event: ShortcutEvent) {
    if event.state() != ShortcutState::Pressed {
        return;
    }
    let visible = *shortcut == shortcut_toggle_visible();
    let interactive = *shortcut == shortcut_toggle_interactive();
    let handle = app.clone();
    // Panel methods are AppKit calls: main thread only.
    let _ = app.run_on_main_thread(move || {
        let Ok(panel) = handle.get_webview_panel(LABEL) else {
            return;
        };
        if visible {
            if panel.is_visible() {
                panel.hide();
                tracing::info!("overlay spike: panel hidden");
            } else {
                panel.show();
                tracing::info!("overlay spike: panel shown");
            }
        } else if interactive {
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
