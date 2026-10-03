//! Finding the game's window. The game is its own process, `League of
//! Legends.exe`, apart from the client (`LeagueClientUx.exe`) and the Riot
//! Client; its render window is titled "League of Legends (TM) Client". The
//! guess ranks that title first, then a window on screen, then the largest,
//! and `list` prints every window so a wrong guess can be corrected with
//! `--window-id`.

use serde_json::{json, Value};
use windows::core::BOOL;
use windows::Graphics::Capture::GraphicsCaptureItem;
use windows::Win32::Foundation::{CloseHandle, HWND, LPARAM, RECT};
use windows::Win32::Graphics::Dwm::{
    DwmGetWindowAttribute, DWMWA_CLOAKED, DWMWA_EXTENDED_FRAME_BOUNDS,
};
use windows::Win32::Graphics::Gdi::{MonitorFromWindow, HMONITOR, MONITOR_DEFAULTTONEAREST};
use windows::Win32::System::Threading::{
    OpenProcess, QueryFullProcessImageNameW, PROCESS_NAME_WIN32, PROCESS_QUERY_LIMITED_INFORMATION,
};
use windows::Win32::System::WinRT::Graphics::Capture::IGraphicsCaptureItemInterop;
use windows::Win32::UI::WindowsAndMessaging::{
    EnumWindows, GetClassNameW, GetWindowTextW, GetWindowThreadProcessId, IsIconic, IsWindowVisible,
};

use crate::args::{self, Source};
use crate::output::{emit, fail};

const GAME_PROCESS: &str = "league of legends.exe";
const GAME_TITLE: &str = "(tm) client";

#[derive(Debug, Clone)]
pub struct Window {
    pub hwnd: HWND,
    pub title: String,
    pub class: String,
    pub process: String,
    pub pid: u32,
    pub on_screen: bool,
    pub width: i32,
    pub height: i32,
}

impl Window {
    /// The id the shell passes back with `--window-id`. Window handles are
    /// 32-bit values even in a 64-bit process.
    pub fn id(&self) -> u32 {
        self.hwnd.0 as usize as u32
    }

    pub fn describe(&self) -> Value {
        json!({
            "windowId": self.id(),
            "title": self.title,
            "app": self.process,
            "className": self.class,
            "pid": self.pid,
            "onScreen": self.on_screen,
            "widthPoints": self.width,
            "heightPoints": self.height,
        })
    }

    pub fn is_game(&self) -> bool {
        self.process.eq_ignore_ascii_case(GAME_PROCESS) && self.width >= 640 && self.height >= 360
    }

    fn rank(&self) -> (bool, bool, i64) {
        (
            self.title.to_lowercase().contains(GAME_TITLE),
            self.on_screen,
            i64::from(self.width) * i64::from(self.height),
        )
    }
}

/// Every visible top-level window.
pub fn windows() -> Vec<Window> {
    unsafe extern "system" fn each(hwnd: HWND, found: LPARAM) -> BOOL {
        // SAFETY: `found` is the vector `windows` passed in, alive for the call.
        let found = unsafe { &mut *(found.0 as *mut Vec<HWND>) };
        found.push(hwnd);
        true.into()
    }
    let mut handles: Vec<HWND> = Vec::new();
    // SAFETY: the callback only pushes into the vector while EnumWindows runs.
    unsafe {
        let _ = EnumWindows(Some(each), LPARAM(&mut handles as *mut Vec<HWND> as isize));
    }
    handles.into_iter().filter_map(read).collect()
}

fn read(hwnd: HWND) -> Option<Window> {
    // SAFETY: plain queries on a window handle EnumWindows just gave.
    unsafe {
        if !IsWindowVisible(hwnd).as_bool() {
            return None;
        }
        let mut text = [0u16; 512];
        let length = GetWindowTextW(hwnd, &mut text).max(0) as usize;
        let title = String::from_utf16_lossy(&text[..length]);
        let length = GetClassNameW(hwnd, &mut text).max(0) as usize;
        let class = String::from_utf16_lossy(&text[..length]);
        let mut pid = 0;
        GetWindowThreadProcessId(hwnd, Some(&mut pid));
        let mut cloaked = 0u32;
        let _ = DwmGetWindowAttribute(
            hwnd,
            DWMWA_CLOAKED,
            &mut cloaked as *mut u32 as *mut _,
            std::mem::size_of::<u32>() as u32,
        );
        let mut bounds = RECT::default();
        DwmGetWindowAttribute(
            hwnd,
            DWMWA_EXTENDED_FRAME_BOUNDS,
            &mut bounds as *mut RECT as *mut _,
            std::mem::size_of::<RECT>() as u32,
        )
        .ok()?;
        Some(Window {
            hwnd,
            title,
            class,
            process: process_name(pid).unwrap_or_default(),
            pid,
            on_screen: cloaked == 0 && !IsIconic(hwnd).as_bool(),
            width: bounds.right - bounds.left,
            height: bounds.bottom - bounds.top,
        })
    }
}

/// The executable's file name, `League of Legends.exe`.
pub fn process_name(pid: u32) -> Option<String> {
    // SAFETY: the handle is closed before returning; the buffer outlives the call.
    unsafe {
        let process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid).ok()?;
        let mut path = [0u16; 1024];
        let mut length = path.len() as u32;
        let read = QueryFullProcessImageNameW(
            process,
            PROCESS_NAME_WIN32,
            windows::core::PWSTR(path.as_mut_ptr()),
            &mut length,
        );
        let _ = CloseHandle(process);
        read.ok()?;
        let path = String::from_utf16_lossy(&path[..length as usize]);
        path.rsplit('\\').next().map(str::to_string)
    }
}

/// The window `--window-id` names, else the best guess at the game's.
pub fn find(id: Option<u32>) -> Option<Window> {
    let all = windows();
    match id {
        Some(id) => all.into_iter().find(|window| window.id() == id),
        None => all
            .into_iter()
            .filter(Window::is_game)
            .max_by_key(Window::rank),
    }
}

pub fn find_or_fail(arguments: &[String]) -> Window {
    find(args::window_id(arguments)).unwrap_or_else(|| {
        fail(
            "no-window",
            "no League game window found (run `list` and pass --window-id)",
            4,
        )
    })
}

/// What Windows.Graphics.Capture records for this source: the window, or the
/// monitor it is on.
pub fn capture_item(window: &Window, source: Source) -> windows::core::Result<GraphicsCaptureItem> {
    let interop = windows::core::factory::<GraphicsCaptureItem, IGraphicsCaptureItemInterop>()?;
    // SAFETY: valid window and monitor handles.
    unsafe {
        match source {
            Source::Window => interop.CreateForWindow(window.hwnd),
            Source::Display => {
                let monitor: HMONITOR = MonitorFromWindow(window.hwnd, MONITOR_DEFAULTTONEAREST);
                interop.CreateForMonitor(monitor)
            }
        }
    }
}

pub fn list() {
    for window in windows() {
        let mut described = window.describe();
        described["gameGuess"] = Value::from(window.is_game());
        emit("window", described);
    }
}

pub fn probe(arguments: &[String]) {
    let window = find_or_fail(arguments);
    let source = Source::parse(arguments);
    let item = capture_item(&window, source).unwrap_or_else(|error| {
        fail(
            "capture",
            &format!("this window cannot be captured: {error}"),
            5,
        )
    });
    let size = item.Size().unwrap_or_default();
    let mut described = window.describe();
    described["width"] = Value::from(size.Width);
    described["height"] = Value::from(size.Height);
    described["source"] = Value::from(source.name());
    emit("window", described);
}
