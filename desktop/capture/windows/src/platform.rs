//! The commands, once the process is set up the way every one of them needs:
//! COM on a multithreaded apartment (Windows.Graphics.Capture and Media
//! Foundation call back on their own threads), Media Foundation started, and
//! per-monitor DPI awareness, so window sizes are read in pixels.

use windows::Win32::Media::MediaFoundation::{MFStartup, MFSTARTUP_FULL, MF_VERSION};
use windows::Win32::System::Com::{CoInitializeEx, COINIT_MULTITHREADED};
use windows::Win32::UI::HiDpi::{
    SetProcessDpiAwarenessContext, DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2,
};

use crate::output::fail;
use crate::{media, record, window, USAGE};

pub fn run(arguments: &[String]) {
    // SAFETY: process-wide initialisation, before any other call.
    unsafe {
        let _ = SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        if CoInitializeEx(None, COINIT_MULTITHREADED).is_err() {
            fail("capture", "COM could not be initialised", 5);
        }
        if let Err(error) = MFStartup(MF_VERSION, MFSTARTUP_FULL) {
            fail(
                "capture",
                &format!("Media Foundation is not available: {error}"),
                5,
            );
        }
    }
    match arguments.first().map(String::as_str) {
        Some("list") => window::list(),
        Some("probe") => window::probe(arguments),
        Some("record") => record::record(arguments),
        Some("access") => media::access(),
        Some("clip") => media::clip(arguments),
        Some("thumbnail") => media::thumbnail(arguments),
        _ => fail("usage", USAGE, 1),
    }
}
