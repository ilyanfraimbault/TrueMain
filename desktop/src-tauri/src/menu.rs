//! "Check for Updates…" where each platform puts it: in the application menu
//! on macOS, right under "About TrueMain"; in the menu of a tray icon on
//! Windows, where the window has no menu bar to carry it. Choosing it brings
//! the window forward and asks the webview to check (`useAppUpdate`), which
//! owns the updater and says how the check went.

use tauri::menu::{Menu, MenuEvent, MenuItem, MenuItemKind, PredefinedMenuItem};
use tauri::tray::{MouseButton, MouseButtonState, TrayIconBuilder, TrayIconEvent};
use tauri::{App, AppHandle, Emitter, Manager, Runtime};

const CHECK_FOR_UPDATES: &str = "check-for-updates";
const SHOW: &str = "show";
const QUIT: &str = "quit";
/// Listened to by `useAppUpdate` in the webview.
const CHECK_EVENT: &str = "app://check-for-updates";

pub fn install<R: Runtime>(app: &App<R>) -> tauri::Result<()> {
    // Runtime checks rather than `#[cfg]`, so both paths compile, and are
    // checked, on whichever platform builds.
    if cfg!(target_os = "macos") {
        app_menu(app)?;
    } else if cfg!(windows) {
        tray(app)?;
    }
    app.on_menu_event(on_menu_event);
    Ok(())
}

/// The default macOS menu (app, edit, view, window — copy and paste need it),
/// with the item inserted under "About TrueMain", where macOS apps keep it.
fn app_menu<R: Runtime>(app: &App<R>) -> tauri::Result<()> {
    let menu = Menu::default(app.handle())?;
    if let Some(MenuItemKind::Submenu(app_submenu)) = menu.items()?.into_iter().next() {
        let check = MenuItem::with_id(
            app,
            CHECK_FOR_UPDATES,
            "Check for Updates…",
            true,
            None::<&str>,
        )?;
        app_submenu.insert(&check, 1)?;
    }
    app.set_menu(menu)?;
    Ok(())
}

/// A tray icon whose menu opens the window, checks for updates, or quits;
/// a left click opens the window.
fn tray<R: Runtime>(app: &App<R>) -> tauri::Result<()> {
    let menu = Menu::with_items(
        app,
        &[
            &MenuItem::with_id(app, SHOW, "Open TrueMain", true, None::<&str>)?,
            &MenuItem::with_id(
                app,
                CHECK_FOR_UPDATES,
                "Check for Updates…",
                true,
                None::<&str>,
            )?,
            &PredefinedMenuItem::separator(app)?,
            &MenuItem::with_id(app, QUIT, "Quit TrueMain", true, None::<&str>)?,
        ],
    )?;
    let mut tray = TrayIconBuilder::with_id("main")
        .tooltip("TrueMain")
        .menu(&menu)
        .show_menu_on_left_click(false)
        .on_tray_icon_event(|tray, event| {
            if let TrayIconEvent::Click {
                button: MouseButton::Left,
                button_state: MouseButtonState::Up,
                ..
            } = event
            {
                show_window(tray.app_handle());
            }
        });
    if let Some(icon) = app.default_window_icon() {
        tray = tray.icon(icon.clone());
    }
    tray.build(app)?;
    Ok(())
}

fn on_menu_event<R: Runtime>(app: &AppHandle<R>, event: MenuEvent) {
    match event.id().as_ref() {
        CHECK_FOR_UPDATES => {
            show_window(app);
            if let Err(error) = app.emit(CHECK_EVENT, ()) {
                tracing::warn!(%error, "could not ask the webview to check for updates");
            }
        }
        SHOW => show_window(app),
        QUIT => app.exit(0),
        _ => {}
    }
}

fn show_window<R: Runtime>(app: &AppHandle<R>) {
    if let Some(window) = app.get_webview_window("main") {
        let _ = window.unminimize();
        let _ = window.show();
        let _ = window.set_focus();
    }
}
