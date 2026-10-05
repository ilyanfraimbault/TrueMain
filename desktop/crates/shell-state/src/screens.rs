//! Which of the player's monitors a window is on.
//!
//! The overlay's panels belong on the monitor the game runs on, which is not
//! the primary one on every two-monitor desk (#1914). The shell measures the
//! game's window and lists the monitors; the choice is made here, so it is
//! tested anywhere.

use crate::overlay::Rect;

/// One monitor, measured twice: `bounds` in the unit the platform reports a
/// window's frame in (pixels on Windows, points on macOS), `area` in the
/// points the overlay places its panels in.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct Monitor {
    pub bounds: Rect,
    pub area: Rect,
}

/// The monitor holding the largest part of `window`, or `None` when the
/// window is on none of them (off screen, or minimised to a sentinel frame).
pub fn holding(monitors: &[Monitor], window: Rect) -> Option<Monitor> {
    monitors
        .iter()
        .map(|monitor| (overlap(monitor.bounds, window), monitor))
        .filter(|(shared, _)| *shared > 0.0)
        .max_by(|a, b| a.0.total_cmp(&b.0))
        .map(|(_, monitor)| *monitor)
}

/// The area two rectangles share.
fn overlap(a: Rect, b: Rect) -> f64 {
    let width = (a.x + a.width).min(b.x + b.width) - a.x.max(b.x);
    let height = (a.y + a.height).min(b.y + b.height) - a.y.max(b.y);
    width.max(0.0) * height.max(0.0)
}

#[cfg(test)]
mod tests {
    use super::*;

    const fn rect(x: f64, y: f64, width: f64, height: f64) -> Rect {
        Rect {
            x,
            y,
            width,
            height,
        }
    }

    /// A 4K primary at 200% and, to its left, a 1080p monitor at 100%: the
    /// second one's origin is negative, as Windows and macOS both report it.
    const PRIMARY: Monitor = Monitor {
        bounds: rect(0.0, 0.0, 3840.0, 2160.0),
        area: rect(0.0, 0.0, 1920.0, 1080.0),
    };
    const LEFT: Monitor = Monitor {
        bounds: rect(-1920.0, 0.0, 1920.0, 1080.0),
        area: rect(-1920.0, 0.0, 1920.0, 1080.0),
    };

    #[test]
    fn a_game_off_the_primary_monitor_is_on_its_own() {
        let game = rect(-1920.0, 0.0, 1920.0, 1080.0);
        assert_eq!(holding(&[PRIMARY, LEFT], game), Some(LEFT));
    }

    #[test]
    fn a_window_across_two_monitors_is_on_the_one_holding_most_of_it() {
        let windowed = rect(-400.0, 100.0, 1280.0, 720.0);
        assert_eq!(holding(&[LEFT, PRIMARY], windowed), Some(PRIMARY));
        let mostly_left = rect(-1200.0, 100.0, 1280.0, 720.0);
        assert_eq!(holding(&[PRIMARY, LEFT], mostly_left), Some(LEFT));
    }

    #[test]
    fn a_window_on_no_monitor_is_on_none() {
        // Windows parks a minimised window at -32000.
        let minimised = rect(-32000.0, -32000.0, 160.0, 28.0);
        assert_eq!(holding(&[PRIMARY, LEFT], minimised), None);
        assert_eq!(holding(&[], rect(0.0, 0.0, 10.0, 10.0)), None);
    }

    #[test]
    fn the_overlay_places_its_panels_on_the_game_monitor_area() {
        use crate::overlay::{OverlayPanel, OverlaySettings};
        let game = holding(&[PRIMARY, LEFT], rect(-1920.0, 0.0, 1920.0, 1080.0))
            .expect("the game is on the left monitor");
        let (x, y) =
            OverlaySettings::default().origin(OverlayPanel::NextItem, game.area, (300.0, 120.0));
        assert!((-1920.0..=-300.0).contains(&x), "x = {x}");
        assert!((0.0..=960.0).contains(&y), "y = {y}");
    }
}
