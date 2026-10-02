//! The in-game overlay's settings and the two rules that drive its window:
//! when it shows, and where.
//!
//! The window itself is the shell's (`src-tauri/src/overlay`), macOS-only and
//! impossible to build on the Linux CI box; the decisions it applies live here
//! so they are tested anywhere. The settings file is read the way the
//! recording settings are: a value this build does not know, or one out of
//! range, falls back to its own default rather than failing the file.

use std::fs;
use std::io;
use std::path::Path;

use serde::de::DeserializeOwned;
use serde::{Deserialize, Serialize};
use serde_json::Value;

/// The settings file's format version, written into it so a later format can
/// migrate rather than guess.
pub const SETTINGS_VERSION: u32 = 1;

pub const MIN_SCALE: f64 = 0.8;
pub const MAX_SCALE: f64 = 1.4;
pub const MIN_OPACITY: f64 = 0.5;
pub const MAX_OPACITY: f64 = 1.0;

/// Distance from the screen's side edges, in points.
const EDGE_MARGIN: f64 = 16.0;
/// Distance from the screen's top edge: below where the game prints its
/// announcements and, top right, its own score strip.
const TOP_MARGIN: f64 = 56.0;

/// When the overlay shows during a game.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum OverlayShow {
    /// For the whole game.
    Always,
    /// Only while the player is dead — when the shop is the decision at hand.
    WhileDead,
}

/// A place on the screen the overlay snaps to.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "kebab-case")]
pub enum OverlayAnchor {
    TopLeft,
    TopRight,
    CenterLeft,
    CenterRight,
}

/// Where the player dragged the overlay, as the centre of the panel in
/// fractions of the screen — so it lands in the same place at any resolution.
#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct OverlayPoint {
    pub x: f64,
    pub y: f64,
}

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct OverlaySettings {
    /// On by default: the overlay is the app's in-game half, it costs nothing,
    /// and one shortcut hides it for the game.
    pub enabled: bool,
    pub show: OverlayShow,
    pub anchor: OverlayAnchor,
    /// Set by dragging the panel in the preview; wins over `anchor` until the
    /// player picks an anchor again.
    pub custom: Option<OverlayPoint>,
    /// The panel's size against its natural one.
    pub scale: f64,
    pub opacity: f64,
}

impl Default for OverlaySettings {
    fn default() -> Self {
        Self {
            enabled: true,
            show: OverlayShow::Always,
            anchor: OverlayAnchor::TopLeft,
            custom: None,
            scale: 1.0,
            opacity: 0.95,
        }
    }
}

/// What the overlay's visibility depends on besides the settings, measured by
/// the shell.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct OverlayInputs {
    /// The settings page asked to see the overlay on screen, to place it.
    pub preview: bool,
    /// The game's own process (not the client) is the frontmost application.
    pub game_frontmost: bool,
    /// The app is reading a running game.
    pub in_game: bool,
    /// The player's champion is dead.
    pub dead: bool,
    /// The player hid the overlay with the shortcut during this game.
    pub hidden_by_player: bool,
}

/// A rectangle in points, origin top left.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct Rect {
    pub x: f64,
    pub y: f64,
    pub width: f64,
    pub height: f64,
}

impl OverlaySettings {
    /// Whether the overlay is on screen. Only ever over the game itself: never
    /// over the client, another app or the desktop — except in the preview,
    /// which shows it whatever the settings so it can be placed.
    pub fn shows(&self, inputs: &OverlayInputs) -> bool {
        if inputs.preview {
            return true;
        }
        let moment = match self.show {
            OverlayShow::Always => true,
            OverlayShow::WhileDead => inputs.dead,
        };
        self.enabled
            && inputs.in_game
            && inputs.game_frontmost
            && !inputs.hidden_by_player
            && moment
    }

    /// The panel's top-left corner on `screen` for a panel of `size`, kept
    /// entirely on the screen.
    pub fn origin(&self, screen: Rect, size: (f64, f64)) -> (f64, f64) {
        let (width, height) = size;
        let (x, y) = match self.custom {
            Some(point) => (
                screen.x + point.x * screen.width - width / 2.0,
                screen.y + point.y * screen.height - height / 2.0,
            ),
            None => {
                let left = screen.x + EDGE_MARGIN;
                let right = screen.x + screen.width - width - EDGE_MARGIN;
                let top = screen.y + TOP_MARGIN;
                let middle = screen.y + (screen.height - height) / 2.0;
                match self.anchor {
                    OverlayAnchor::TopLeft => (left, top),
                    OverlayAnchor::TopRight => (right, top),
                    OverlayAnchor::CenterLeft => (left, middle),
                    OverlayAnchor::CenterRight => (right, middle),
                }
            }
        };
        // Whole points: a fraction of a point blurs the panel's text.
        (
            clamp_into(x, screen.x, screen.width, width).round(),
            clamp_into(y, screen.y, screen.height, height).round(),
        )
    }

    /// The custom position a panel dragged to `origin` stands for.
    pub fn point_at(screen: Rect, origin: (f64, f64), size: (f64, f64)) -> OverlayPoint {
        let fraction = |start: f64, length: f64, at: f64, extent: f64| {
            if length <= 0.0 {
                return 0.5;
            }
            ((at + extent / 2.0 - start) / length).clamp(0.0, 1.0)
        };
        OverlayPoint {
            x: fraction(screen.x, screen.width, origin.0, size.0),
            y: fraction(screen.y, screen.height, origin.1, size.1),
        }
    }

    /// Read the settings file. A missing or unreadable file is the defaults.
    pub fn load(path: &Path) -> Self {
        fs::read_to_string(path)
            .map(|body| Self::from_json(&body))
            .unwrap_or_default()
    }

    /// Parse leniently: each field that does not parse, or is out of range,
    /// is that field's default.
    pub fn from_json(body: &str) -> Self {
        let Ok(Value::Object(fields)) = serde_json::from_str::<Value>(body) else {
            return Self::default();
        };
        let defaults = Self::default();
        let in_range = |min: f64, max: f64| move |value: &f64| (min..=max).contains(value);

        Self {
            enabled: parse(fields.get("enabled")).unwrap_or(defaults.enabled),
            show: parse(fields.get("show")).unwrap_or(defaults.show),
            anchor: parse(fields.get("anchor")).unwrap_or(defaults.anchor),
            custom: parse::<OverlayPoint>(fields.get("custom"))
                .filter(|point| (0.0..=1.0).contains(&point.x) && (0.0..=1.0).contains(&point.y)),
            scale: parse(fields.get("scale"))
                .filter(in_range(MIN_SCALE, MAX_SCALE))
                .unwrap_or(defaults.scale),
            opacity: parse(fields.get("opacity"))
                .filter(in_range(MIN_OPACITY, MAX_OPACITY))
                .unwrap_or(defaults.opacity),
        }
    }

    /// Write the settings, versioned, through a temporary file so a crash
    /// mid-write never leaves half a file behind.
    pub fn save(&self, path: &Path) -> io::Result<()> {
        let mut value = serde_json::to_value(self).map_err(io::Error::other)?;
        value["version"] = SETTINGS_VERSION.into();
        let body = serde_json::to_string_pretty(&value).map_err(io::Error::other)?;
        if let Some(parent) = path.parent() {
            fs::create_dir_all(parent)?;
        }
        let temporary = path.with_extension("json.tmp");
        fs::write(&temporary, body)?;
        fs::rename(&temporary, path)
    }
}

fn parse<T: DeserializeOwned>(value: Option<&Value>) -> Option<T> {
    value.and_then(|value| serde_json::from_value(value.clone()).ok())
}

/// Keep a span of `extent` starting at `at` inside `[start, start + length]`.
fn clamp_into(at: f64, start: f64, length: f64, extent: f64) -> f64 {
    let last = start + (length - extent).max(0.0);
    at.clamp(start, last)
}

#[cfg(test)]
mod tests {
    use super::*;

    const SCREEN: Rect = Rect {
        x: 0.0,
        y: 0.0,
        width: 2560.0,
        height: 1440.0,
    };
    const SIZE: (f64, f64) = (300.0, 120.0);

    fn in_game() -> OverlayInputs {
        OverlayInputs {
            in_game: true,
            game_frontmost: true,
            ..OverlayInputs::default()
        }
    }

    #[test]
    fn shows_only_over_the_game_itself() {
        let settings = OverlaySettings::default();
        assert!(settings.shows(&in_game()));
        // Another app in front, even with a game running.
        assert!(!settings.shows(&OverlayInputs {
            game_frontmost: false,
            ..in_game()
        }));
        // The game process in front but no game read yet (loading screen).
        assert!(!settings.shows(&OverlayInputs {
            in_game: false,
            ..in_game()
        }));
        assert!(!settings.shows(&OverlayInputs::default()));
    }

    #[test]
    fn the_shortcut_and_the_switch_hide_it() {
        let settings = OverlaySettings::default();
        assert!(!settings.shows(&OverlayInputs {
            hidden_by_player: true,
            ..in_game()
        }));
        let off = OverlaySettings {
            enabled: false,
            ..settings
        };
        assert!(!off.shows(&in_game()));
    }

    #[test]
    fn while_dead_waits_for_a_death() {
        let settings = OverlaySettings {
            show: OverlayShow::WhileDead,
            ..OverlaySettings::default()
        };
        assert!(!settings.shows(&in_game()));
        assert!(settings.shows(&OverlayInputs {
            dead: true,
            ..in_game()
        }));
    }

    #[test]
    fn the_preview_shows_whatever_the_settings() {
        let off = OverlaySettings {
            enabled: false,
            ..OverlaySettings::default()
        };
        assert!(off.shows(&OverlayInputs {
            preview: true,
            ..OverlayInputs::default()
        }));
    }

    #[test]
    fn anchors_sit_inside_the_margins() {
        let at = |anchor| {
            OverlaySettings {
                anchor,
                ..OverlaySettings::default()
            }
            .origin(SCREEN, SIZE)
        };
        assert_eq!(at(OverlayAnchor::TopLeft), (16.0, 56.0));
        assert_eq!(at(OverlayAnchor::TopRight), (2244.0, 56.0));
        assert_eq!(at(OverlayAnchor::CenterLeft), (16.0, 660.0));
        assert_eq!(at(OverlayAnchor::CenterRight), (2244.0, 660.0));
    }

    #[test]
    fn a_dragged_position_round_trips_and_stays_on_screen() {
        let point = OverlaySettings::point_at(SCREEN, (1000.0, 400.0), SIZE);
        let settings = OverlaySettings {
            custom: Some(point),
            ..OverlaySettings::default()
        };
        assert_eq!(settings.origin(SCREEN, SIZE), (1000.0, 400.0));

        // The same place on a smaller screen, and never off its edge.
        let small = Rect {
            width: 1280.0,
            height: 720.0,
            ..SCREEN
        };
        assert_eq!(settings.origin(small, SIZE), (425.0, 170.0));
        let corner = OverlaySettings {
            custom: Some(OverlayPoint { x: 1.0, y: 1.0 }),
            ..OverlaySettings::default()
        };
        assert_eq!(corner.origin(SCREEN, SIZE), (2260.0, 1320.0));
    }

    #[test]
    fn reads_leniently_field_by_field() {
        let settings = OverlaySettings::from_json(
            r#"{"enabled":false,"show":"whileDead","anchor":"sideways","scale":9,
                "opacity":0.7,"custom":{"x":2,"y":0.5}}"#,
        );
        assert!(!settings.enabled);
        assert_eq!(settings.show, OverlayShow::WhileDead);
        assert_eq!(settings.anchor, OverlayAnchor::TopLeft);
        assert_eq!(settings.scale, 1.0);
        assert_eq!(settings.opacity, 0.7);
        assert_eq!(settings.custom, None);
        assert_eq!(
            OverlaySettings::from_json("not json"),
            OverlaySettings::default()
        );
    }

    #[test]
    fn saves_what_it_reads() {
        let dir = std::env::temp_dir().join(format!("overlay-settings-{}", std::process::id()));
        let path = dir.join("overlay-settings.json");
        let settings = OverlaySettings {
            anchor: OverlayAnchor::CenterRight,
            custom: Some(OverlayPoint { x: 0.25, y: 0.75 }),
            scale: 1.2,
            ..OverlaySettings::default()
        };
        settings.save(&path).unwrap();
        assert_eq!(OverlaySettings::load(&path), settings);
        let _ = fs::remove_dir_all(dir);
    }
}
