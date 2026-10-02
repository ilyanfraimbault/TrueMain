//! truemain-capture — the Windows screen-capture helper for game recording
//! (#1797), the twin of the macOS one (`capture/macos`): the same commands and
//! the same events, so the shell drives either through `crates/capture-helper`
//! without knowing which it runs.
//!
//! A separate process rather than code in the app: a crash in capture or in
//! the encoder must not take the app down. Protocol: arguments in, one JSON
//! object per line out on stdout; `stop` on stdin (or stdin closing) closes
//! the file.
//!
//!   truemain-capture list
//!       every top-level window, as `window` events
//!   truemain-capture probe [--window-id N] [--source window|display]
//!       the game window and the size it captures at, in pixels
//!   truemain-capture record --out FILE --width W --height H --fps F
//!                           --bitrate BPS --keyframe-interval FRAMES
//!                           [--codec h264|hevc] [--window-id N]
//!                           [--source window|display] [--no-audio]
//!       records until told to stop; events: `started`, `progress` every five
//!       seconds, `stopped`, `error`
//!   truemain-capture access [--request] [--open-settings]
//!       whether this Windows can capture a window, as one `access` event —
//!       Windows asks no permission, so the flags change nothing
//!   truemain-capture clip --in FILE --out FILE --start-ms N --end-ms N
//!       the range as its own file, not re-encoded; one `clipped` event
//!   truemain-capture thumbnail --in FILE --out FILE.jpg --at-ms N [--width W]
//!       one frame as a JPEG; one `thumbnail` event
//!
//! The output size, bitrate and keyframe interval are computed by the shell
//! (`game-recording`'s `Quality::output_for`), so the rule lives in one place.

#[cfg_attr(not(windows), allow(dead_code))]
mod args;
#[cfg_attr(not(windows), allow(dead_code))]
mod output;
#[cfg_attr(not(windows), allow(dead_code))]
mod timeline;

#[cfg(windows)]
mod audio;
#[cfg(windows)]
mod convert;
#[cfg(windows)]
mod media;
#[cfg(windows)]
mod platform;
#[cfg(windows)]
mod record;
#[cfg(windows)]
mod window;
#[cfg(windows)]
mod writer;

const USAGE: &str = "usage: truemain-capture list | probe [--window-id N] [--source window|display] | record --out FILE --width W --height H --fps F --bitrate BPS --keyframe-interval FRAMES [--codec h264|hevc] [--window-id N] [--source window|display] [--no-audio] | access [--request] [--open-settings] | clip --in FILE --out FILE --start-ms N --end-ms N | thumbnail --in FILE --out FILE --at-ms N [--width W]";

fn main() {
    let arguments: Vec<String> = std::env::args().skip(1).collect();
    #[cfg(windows)]
    platform::run(&arguments);
    #[cfg(not(windows))]
    {
        let _ = (&arguments, USAGE);
        output::fail(
            "usage",
            "this helper runs on Windows; macOS has its own (capture/macos)",
            1,
        );
    }
}
