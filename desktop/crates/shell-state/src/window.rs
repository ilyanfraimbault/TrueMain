//! Where the app's window opens: where the player left it, as long as that
//! place still exists (#1914).
//!
//! The window is resizable, and its size, position and maximised state are
//! kept across launches in a small file next to the overlay's settings. The
//! position is kept in pixels (the unit both platforms report a window's
//! place in, whatever the monitor's scale) and the size in points, so a
//! window moved from a 4K monitor at 200 % to a 1080p one at 100 % opens at
//! the same apparent size rather than twice as large. A place on a monitor
//! that has since been unplugged is dropped: the window opens centred on the
//! primary monitor instead, never off screen.

use std::fs;
use std::io;
use std::path::Path;

use serde::{Deserialize, Serialize};

use crate::overlay::Rect;

/// The narrowest and shortest the window gets, in points. Below this width
/// the draft's two rows of five pick cards stop fitting; the compact layouts
/// of #1914 will lower it. Mirrors `minWidth` / `minHeight` in `tauri.conf.json`.
pub const MIN_SIZE: (f64, f64) = (960.0, 640.0);

/// How much of the window's top edge must land on a monitor, in pixels, for
/// its saved place to be kept: enough of the title bar to grab it by.
const GRAB_WIDTH: f64 = 120.0;
const GRAB_HEIGHT: f64 = 24.0;

/// The window as the player left it.
#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct WindowPlacement {
    /// The outer top-left corner, in pixels.
    pub x: i32,
    pub y: i32,
    /// The inner size, in points.
    pub width: f64,
    pub height: f64,
    /// Maximised when it was closed. The size and place above are then the
    /// ones it had before, which un-maximising returns to.
    pub maximized: bool,
}

/// A monitor: its frame in pixels and its scale.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct Display {
    pub frame: Rect,
    pub scale: f64,
}

/// What to do with the window at launch.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct Restore {
    /// The outer top-left corner in pixels, or `None` to centre the window.
    pub position: Option<(i32, i32)>,
    /// The inner size, in points.
    pub size: (f64, f64),
    pub maximized: bool,
}

impl WindowPlacement {
    /// Where the window opens. `displays` lists the monitors with the
    /// primary one first; with none at all the saved size alone is kept.
    pub fn restore(&self, displays: &[Display]) -> Restore {
        let on = displays.iter().find(|display| self.grabbable_on(display));
        let display = on.or(displays.first());
        let size = display.map_or((self.width, self.height), |display| {
            fit(
                (self.width, self.height),
                (
                    display.frame.width / display.scale,
                    display.frame.height / display.scale,
                ),
            )
        });
        Restore {
            position: on.map(|_| (self.x, self.y)),
            size,
            maximized: self.maximized,
        }
    }

    /// Whether enough of the window's top edge lies on `display` to grab it.
    fn grabbable_on(&self, display: &Display) -> bool {
        let width = self.width * display.scale;
        let left = f64::from(self.x).max(display.frame.x);
        let right = (f64::from(self.x) + width).min(display.frame.x + display.frame.width);
        let top = f64::from(self.y).max(display.frame.y);
        let bottom = (f64::from(self.y) + GRAB_HEIGHT).min(display.frame.y + display.frame.height);
        right - left >= GRAB_WIDTH.min(width) && bottom - top >= GRAB_HEIGHT
    }

    /// Read the file. Missing, unreadable or nonsensical is `None`: the
    /// window opens at its default size, centred.
    pub fn load(path: &Path) -> Option<Self> {
        let placement: Self = serde_json::from_str(&fs::read_to_string(path).ok()?).ok()?;
        (placement.width.is_finite()
            && placement.height.is_finite()
            && placement.width >= 1.0
            && placement.height >= 1.0)
            .then_some(placement)
    }

    pub fn save(&self, path: &Path) -> io::Result<()> {
        if let Some(parent) = path.parent() {
            fs::create_dir_all(parent)?;
        }
        let body = serde_json::to_string_pretty(self).map_err(io::Error::other)?;
        let temporary = path.with_extension("json.tmp");
        fs::write(&temporary, body)?;
        fs::rename(&temporary, path)
    }
}

/// `size` within `MIN_SIZE` and the monitor's `room`. A monitor smaller than
/// the minimum wins: the window may not open larger than the screen.
fn fit(size: (f64, f64), room: (f64, f64)) -> (f64, f64) {
    let side = |value: f64, min: f64, max: f64| value.max(min).min(max).round();
    (
        side(size.0, MIN_SIZE.0, room.0),
        side(size.1, MIN_SIZE.1, room.1),
    )
}

#[cfg(test)]
mod tests {
    use super::*;

    const fn display(x: f64, y: f64, width: f64, height: f64, scale: f64) -> Display {
        Display {
            frame: Rect {
                x,
                y,
                width,
                height,
            },
            scale,
        }
    }

    /// A 4K primary at 200 %, and a 1080p monitor at 100 % to its right.
    const PRIMARY: Display = display(0.0, 0.0, 3840.0, 2160.0, 2.0);
    const SECOND: Display = display(3840.0, 0.0, 1920.0, 1080.0, 1.0);

    const fn placed(x: i32, y: i32, width: f64, height: f64) -> WindowPlacement {
        WindowPlacement {
            x,
            y,
            width,
            height,
            maximized: false,
        }
    }

    #[test]
    fn a_window_on_a_monitor_still_there_opens_where_it_was() {
        let saved = placed(4000, 100, 1180.0, 760.0);
        assert_eq!(
            saved.restore(&[PRIMARY, SECOND]),
            Restore {
                position: Some((4000, 100)),
                size: (1180.0, 760.0),
                maximized: false,
            }
        );
    }

    #[test]
    fn a_window_on_an_unplugged_monitor_opens_centred_on_the_primary_one() {
        let saved = WindowPlacement {
            maximized: true,
            ..placed(4000, 100, 1400.0, 900.0)
        };
        assert_eq!(
            saved.restore(&[PRIMARY]),
            Restore {
                position: None,
                size: (1400.0, 900.0),
                maximized: true,
            }
        );
    }

    #[test]
    fn the_size_is_kept_in_points_and_never_exceeds_the_monitor() {
        // Saved large on the 4K monitor (1800 × 1000 points), back on the 1080p one alone.
        let saved = placed(100, 100, 1800.0, 1000.0);
        let alone = display(0.0, 0.0, 1920.0, 1080.0, 1.0);
        assert_eq!(saved.restore(&[alone]).size, (1800.0, 1000.0));
        let small = display(0.0, 0.0, 1366.0, 768.0, 1.0);
        assert_eq!(saved.restore(&[small]).size, (1366.0, 768.0));
    }

    #[test]
    fn a_window_dragged_mostly_off_screen_keeps_its_place_only_if_it_can_be_grabbed() {
        // 160 px of its title bar still on the second monitor, then only 60.
        let grabbable = placed(5600, 200, 1180.0, 760.0);
        assert_eq!(
            grabbable.restore(&[PRIMARY, SECOND]).position,
            Some((5600, 200))
        );
        let lost = placed(5700, 200, 1180.0, 760.0);
        assert_eq!(lost.restore(&[PRIMARY, SECOND]).position, None);
        // A title bar above the top of every monitor cannot be grabbed either.
        let above = placed(100, -40, 1180.0, 760.0);
        assert_eq!(above.restore(&[PRIMARY, SECOND]).position, None);
    }

    #[test]
    fn the_size_never_goes_below_the_minimum() {
        let saved = placed(0, 0, 300.0, 200.0);
        assert_eq!(saved.restore(&[PRIMARY]).size, MIN_SIZE);
    }

    #[test]
    fn the_file_round_trips_and_a_broken_one_is_ignored() {
        let directory =
            std::env::temp_dir().join(format!("truemain-window-{}", std::process::id()));
        let path = directory.join("window-state.json");
        let saved = placed(-1200, 40, 1180.0, 760.0);
        saved.save(&path).expect("saved");
        assert_eq!(WindowPlacement::load(&path), Some(saved));
        fs::write(
            &path,
            r#"{"x":0,"y":0,"width":-5,"height":700,"maximized":false}"#,
        )
        .unwrap();
        assert_eq!(WindowPlacement::load(&path), None);
        fs::write(&path, "not json").unwrap();
        assert_eq!(WindowPlacement::load(&path), None);
        let _ = fs::remove_dir_all(directory);
    }
}
