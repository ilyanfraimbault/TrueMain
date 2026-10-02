//! The overlay's windows on macOS, as the #1673 spike measured them over live
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

use objc2_app_kit::NSWorkspace;
use tauri::{AppHandle, LogicalSize, Size, WebviewUrl};
use tauri_nspanel::{
    tauri_panel, CollectionBehavior, ManagerExt, PanelBuilder, PanelLevel, StyleMask,
};

/// `CGShieldingWindowLevel()`, where a game that captured the display draws.
const SHIELDING_LEVEL: i32 = 2_147_483_628;

/// The game's own process, as opposed to the League client
/// (`com.riotgames.LeagueClient`): the panel belongs over this one only.
const GAME_BUNDLE_ID: &str = "com.riotgames.LeagueofLegends.GameClient";

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

/// One panel's window, hidden, its page on `url`.
pub fn build(
    app: &AppHandle,
    label: &str,
    url: &str,
    opacity: f64,
) -> Result<(), Box<dyn std::error::Error>> {
    let panel = PanelBuilder::<_, OverlayWindow>::new(app, label)
        .url(WebviewUrl::App(url.into()))
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
        .alpha_value(opacity)
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
    Ok(())
}

/// Main thread only (AppKit), like the three below.
pub fn show(app: &AppHandle, label: &str, visible: bool) {
    if let Ok(panel) = app.get_webview_panel(label) {
        if visible {
            panel.show();
        } else {
            panel.hide();
        }
    }
}

pub fn set_opacity(app: &AppHandle, label: &str, opacity: f64) {
    if let Ok(panel) = app.get_webview_panel(label) {
        panel.set_alpha_value(opacity);
    }
}

pub fn set_interactive(app: &AppHandle, label: &str, interactive: bool) {
    if let Ok(panel) = app.get_webview_panel(label) {
        panel.set_ignores_mouse_events(!interactive);
    }
}

pub fn game_frontmost() -> bool {
    NSWorkspace::sharedWorkspace()
        .frontmostApplication()
        .and_then(|front| front.bundleIdentifier())
        .is_some_and(|id| id.to_string() == GAME_BUNDLE_ID)
}

/// The keyboard's state, read rather than delivered: with the display
/// captured, the window server routes no hotkey to the app.
pub mod keys {
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
