//! The overlay's windows on Windows (#1798): a plain Tauri window per panel,
//! made into what an overlay over a game has to be with the Win32 styles
//! Tauri does not expose together:
//!
//! - `WS_EX_NOACTIVATE`, and shown with `SW_SHOWNOACTIVATE`: the panel never
//!   takes the foreground, so the game keeps the keyboard;
//! - `WS_EX_LAYERED | WS_EX_TRANSPARENT` outside the preview: clicks go
//!   through to the game; the layered alpha is also the panel's opacity;
//! - `WS_EX_TOOLWINDOW` and topmost: out of the taskbar and Alt+Tab, above a
//!   borderless or windowed game.
//!
//! A game in exclusive Full Screen owns the display and nothing is drawn over
//! it — the settings say to play in Borderless, as other League companions
//! do. The panel shows only while the game owns the foreground window, and
//! the shortcut and the panels' chords are read from the keyboard's state, as
//! on macOS: no hook, no hotkey registration, nothing the game could miss.
//!
//! `desktop/tools/overlay-smoke-windows.ps1` drives all of this on a Windows
//! desktop in CI, a stand-in window in the game's place.

use tauri::{AppHandle, Manager, WebviewUrl, WebviewWindowBuilder};
use windows::Win32::Foundation::{CloseHandle, COLORREF, HWND};
use windows::Win32::Graphics::Dwm::{
    DwmSetWindowAttribute, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND,
    DWM_WINDOW_CORNER_PREFERENCE,
};
use windows::Win32::System::Threading::{
    OpenProcess, QueryFullProcessImageNameW, PROCESS_NAME_WIN32, PROCESS_QUERY_LIMITED_INFORMATION,
};
use windows::Win32::UI::WindowsAndMessaging::{
    GetClassNameW, GetForegroundWindow, GetWindowLongPtrW, GetWindowThreadProcessId,
    SetLayeredWindowAttributes, SetWindowLongPtrW, SetWindowPos, ShowWindow, GWL_EXSTYLE,
    HWND_TOPMOST, LWA_ALPHA, SWP_NOACTIVATE, SWP_NOMOVE, SWP_NOSIZE, SW_HIDE, SW_SHOWNOACTIVATE,
    WS_EX_LAYERED, WS_EX_NOACTIVATE, WS_EX_TOOLWINDOW, WS_EX_TOPMOST, WS_EX_TRANSPARENT,
};

/// The game's own process, as opposed to the client (`LeagueClientUx.exe`):
/// the panel belongs over this one only.
const GAME_PROCESS: &str = "League of Legends.exe";
/// The game's window class. Read without a handle on the game's process,
/// which an anti-cheat may refuse even for its image name.
const GAME_CLASS: &str = "RiotWindowClass";

fn hwnd(app: &AppHandle, label: &str) -> Option<HWND> {
    app.get_webview_window(label)?.hwnd().ok()
}

fn alpha(opacity: f64) -> u8 {
    (opacity.clamp(0.0, 1.0) * 255.0).round() as u8
}

/// Add or remove extended styles.
fn set_styles(hwnd: HWND, add: u32, remove: u32) {
    // SAFETY: plain style edits on a window this process owns.
    unsafe {
        let styles = GetWindowLongPtrW(hwnd, GWL_EXSTYLE) as u32;
        SetWindowLongPtrW(hwnd, GWL_EXSTYLE, ((styles | add) & !remove) as isize);
    }
}

/// One panel's window, hidden, its page on `url`.
pub fn build(
    app: &AppHandle,
    label: &str,
    url: &str,
    opacity: f64,
) -> Result<(), Box<dyn std::error::Error>> {
    let window = WebviewWindowBuilder::new(app, label, WebviewUrl::App(url.into()))
        // Named after its panel, for the smoke test and any window inspector.
        .title(format!("TrueMain {label}"))
        .inner_size(super::INITIAL_SIZE.0, super::INITIAL_SIZE.1)
        .decorations(false)
        .resizable(false)
        .shadow(false)
        .always_on_top(true)
        .skip_taskbar(true)
        .focused(false)
        .focusable(false)
        .visible(false)
        .build()?;
    let hwnd = window.hwnd()?;
    set_styles(
        hwnd,
        (WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST).0,
        0,
    );
    // SAFETY: plain calls on the window just built. A layered window draws
    // nothing until its attributes are set.
    unsafe {
        SetLayeredWindowAttributes(hwnd, COLORREF(0), alpha(opacity), LWA_ALPHA)?;
        // Windows 11 rounds a borderless window's corners only when asked;
        // Windows 10 has no such attribute and keeps them square.
        let corners: DWM_WINDOW_CORNER_PREFERENCE = DWMWCP_ROUND;
        let _ = DwmSetWindowAttribute(
            hwnd,
            DWMWA_WINDOW_CORNER_PREFERENCE,
            &corners as *const _ as *const _,
            std::mem::size_of::<DWM_WINDOW_CORNER_PREFERENCE>() as u32,
        );
    }
    Ok(())
}

pub fn show(app: &AppHandle, label: &str, visible: bool) {
    let Some(hwnd) = hwnd(app, label) else {
        return;
    };
    // SAFETY: plain calls on a window this process owns.
    unsafe {
        if visible {
            let _ = ShowWindow(hwnd, SW_SHOWNOACTIVATE);
            // Back on top: a game brought forward since may have covered it.
            let _ = SetWindowPos(
                hwnd,
                Some(HWND_TOPMOST),
                0,
                0,
                0,
                0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE,
            );
        } else {
            let _ = ShowWindow(hwnd, SW_HIDE);
        }
    }
}

pub fn set_opacity(app: &AppHandle, label: &str, opacity: f64) {
    if let Some(hwnd) = hwnd(app, label) {
        // SAFETY: the window is layered since `build`.
        let _ = unsafe { SetLayeredWindowAttributes(hwnd, COLORREF(0), alpha(opacity), LWA_ALPHA) };
    }
}

pub fn set_interactive(app: &AppHandle, label: &str, interactive: bool) {
    if let Some(hwnd) = hwnd(app, label) {
        if interactive {
            set_styles(hwnd, 0, WS_EX_TRANSPARENT.0);
        } else {
            set_styles(hwnd, WS_EX_TRANSPARENT.0, 0);
        }
    }
}

/// Whether the foreground window is the game's, by its class or its process.
pub fn game_frontmost() -> bool {
    // SAFETY: a plain query.
    let window = unsafe { GetForegroundWindow() };
    !window.0.is_null() && (class_is_game(window) || process_is_game(window))
}

fn class_is_game(window: HWND) -> bool {
    let mut class = [0u16; 64];
    // SAFETY: a plain query into a buffer it is given the length of.
    let length = unsafe { GetClassNameW(window, &mut class) };
    usize::try_from(length).is_ok_and(|length| {
        String::from_utf16_lossy(&class[..length.min(class.len())]) == GAME_CLASS
    })
}

fn process_is_game(window: HWND) -> bool {
    // SAFETY: plain queries; the process handle is closed before returning.
    unsafe {
        let mut pid = 0;
        GetWindowThreadProcessId(window, Some(&mut pid));
        let Ok(process) = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid) else {
            return false;
        };
        let mut path = [0u16; 1024];
        let mut length = path.len() as u32;
        let read = QueryFullProcessImageNameW(
            process,
            PROCESS_NAME_WIN32,
            windows::core::PWSTR(path.as_mut_ptr()),
            &mut length,
        );
        let _ = CloseHandle(process);
        read.is_ok()
            && String::from_utf16_lossy(&path[..length as usize])
                .rsplit('\\')
                .next()
                .is_some_and(|name| name.eq_ignore_ascii_case(GAME_PROCESS))
    }
}

/// The keyboard's state, read rather than delivered, whichever window has
/// the focus.
pub mod keys {
    use shell_state::keys::{KeyCode, KeysDown};
    use windows::Win32::UI::Input::KeyboardAndMouse::{
        GetAsyncKeyState, GetKeyboardLayout, MapVirtualKeyExW, MAPVK_VSC_TO_VK_EX, VIRTUAL_KEY,
        VK_CONTROL, VK_LWIN, VK_MENU, VK_O, VK_RWIN, VK_SHIFT, VK_TAB,
    };
    use windows::Win32::UI::WindowsAndMessaging::{GetForegroundWindow, GetWindowThreadProcessId};

    fn down(key: VIRTUAL_KEY) -> bool {
        // SAFETY: a plain call on a value argument.
        unsafe { GetAsyncKeyState(i32::from(key.0)) as u16 & 0x8000 != 0 }
    }

    /// The modifiers, TAB, and which of `keys` are held. Each key is a
    /// position (`KeyCode::scan_code`), turned into the virtual key the game's
    /// keyboard layout gives it, so a chord holds on any layout.
    pub fn read(keys: &[KeyCode]) -> KeysDown {
        // SAFETY: plain queries; a thread without a layout answers a null
        // one, which `MapVirtualKeyExW` takes as the current thread's.
        let layout = unsafe {
            let thread = GetWindowThreadProcessId(GetForegroundWindow(), None);
            GetKeyboardLayout(thread)
        };
        let held = |key: &KeyCode| {
            // SAFETY: a plain call on value arguments.
            let virtual_key = unsafe {
                MapVirtualKeyExW(u32::from(key.scan_code()), MAPVK_VSC_TO_VK_EX, Some(layout))
            };
            u16::try_from(virtual_key).is_ok_and(|vk| vk != 0 && down(VIRTUAL_KEY(vk)))
        };
        KeysDown {
            alt: down(VK_MENU),
            shift: down(VK_SHIFT),
            ctrl: down(VK_CONTROL),
            meta: down(VK_LWIN) || down(VK_RWIN),
            tab: down(VK_TAB),
            keys: keys.iter().copied().filter(held).collect(),
        }
    }

    /// Whether Alt+Shift+O is held.
    pub fn shortcut_down() -> bool {
        down(VK_O) && down(VK_SHIFT) && down(VK_MENU)
    }
}
