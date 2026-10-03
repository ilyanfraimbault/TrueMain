//! The in-game overlay's settings and the two rules that drive its windows:
//! when each panel shows, and where.
//!
//! The overlay is a few independent panels, each its own window placed on its
//! own (#1671's choice over one HUD): the next item, the win probability, the
//! player's own pace, the item value, which shows only while the scoreboard
//! (TAB) is held, and the loading screen's roster, which shows only before the
//! game has loaded. The
//! windows are the shell's (`src-tauri/src/overlay`), macOS-only and
//! impossible to build on the Linux CI box; the decisions they apply live here
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

/// One panel of the overlay, each in its own window.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Serialize, Deserialize)]
#[serde(rename_all = "kebab-case")]
pub enum OverlayPanel {
    /// The next item to buy, and the gold it still needs.
    NextItem,
    /// Each side's chance to win, estimated from the item-gold gap.
    WinProbability,
    /// Each team's item gold and each lane's gap, while TAB is held.
    ItemValue,
    /// Our CS per minute, with its curve, and gold per minute.
    Stats,
    /// The ten players' form on their champion and latest games, on the loading
    /// screen.
    Loading,
}

impl OverlayPanel {
    pub const ALL: [OverlayPanel; 5] = [
        OverlayPanel::NextItem,
        OverlayPanel::WinProbability,
        OverlayPanel::ItemValue,
        OverlayPanel::Stats,
        OverlayPanel::Loading,
    ];

    /// The panel's name in a window label and a route.
    pub fn slug(self) -> &'static str {
        match self {
            OverlayPanel::NextItem => "next-item",
            OverlayPanel::WinProbability => "win-probability",
            OverlayPanel::ItemValue => "item-value",
            OverlayPanel::Stats => "stats",
            OverlayPanel::Loading => "loading",
        }
    }

    pub fn from_slug(slug: &str) -> Option<Self> {
        Self::ALL.into_iter().find(|panel| panel.slug() == slug)
    }
}

/// When the next item shows during a game.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum OverlayShow {
    /// For the whole game.
    Always,
    /// Only while the player is dead — when the shop is the decision at hand.
    WhileDead,
}

/// A place on the screen a panel snaps to.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "kebab-case")]
pub enum OverlayAnchor {
    TopLeft,
    TopCenter,
    TopRight,
    CenterLeft,
    CenterRight,
}

/// Where the player dragged a panel, as its centre in fractions of the screen
/// — so it lands in the same place at any resolution.
#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct OverlayPoint {
    pub x: f64,
    pub y: f64,
}

/// One panel's own settings.
#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PanelSettings {
    pub enabled: bool,
    pub anchor: OverlayAnchor,
    /// Set by dragging the panel in the preview; wins over `anchor` until the
    /// player picks an anchor again.
    pub custom: Option<OverlayPoint>,
}

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct OverlaySettings {
    /// The whole overlay. On by default: it is the app's in-game half, it
    /// costs nothing, and one shortcut hides it for the game.
    pub enabled: bool,
    /// When the next item shows; the other panels have their own moment.
    pub show: OverlayShow,
    /// Every panel's size against its natural one.
    pub scale: f64,
    pub opacity: f64,
    pub next_item: PanelSettings,
    pub win_probability: PanelSettings,
    pub item_value: PanelSettings,
    pub stats: PanelSettings,
    pub loading: PanelSettings,
}

impl Default for OverlaySettings {
    fn default() -> Self {
        let at = |anchor| PanelSettings {
            enabled: true,
            anchor,
            custom: None,
        };
        Self {
            enabled: true,
            show: OverlayShow::Always,
            scale: 1.0,
            opacity: 0.95,
            next_item: at(OverlayAnchor::TopRight),
            win_probability: at(OverlayAnchor::TopLeft),
            item_value: at(OverlayAnchor::TopCenter),
            stats: at(OverlayAnchor::CenterLeft),
            loading: at(OverlayAnchor::TopCenter),
        }
    }
}

/// What the panels' visibility depends on besides the settings, measured by
/// the shell.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct OverlayInputs {
    /// The settings page asked to see the panels on screen, to place them.
    pub preview: bool,
    /// The game's own process (not the client) is the frontmost application.
    pub game_frontmost: bool,
    /// The app is reading a running game.
    pub in_game: bool,
    /// The player's champion is dead.
    pub dead: bool,
    /// The player hid the overlay with the shortcut during this game.
    pub hidden_by_player: bool,
    /// TAB is held: the game's scoreboard is open.
    pub scoreboard: bool,
    /// A game is in progress but not read yet: the loading screen.
    pub loading_screen: bool,
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
    pub fn panel(&self, panel: OverlayPanel) -> &PanelSettings {
        match panel {
            OverlayPanel::NextItem => &self.next_item,
            OverlayPanel::WinProbability => &self.win_probability,
            OverlayPanel::ItemValue => &self.item_value,
            OverlayPanel::Stats => &self.stats,
            OverlayPanel::Loading => &self.loading,
        }
    }

    pub fn panel_mut(&mut self, panel: OverlayPanel) -> &mut PanelSettings {
        match panel {
            OverlayPanel::NextItem => &mut self.next_item,
            OverlayPanel::WinProbability => &mut self.win_probability,
            OverlayPanel::ItemValue => &mut self.item_value,
            OverlayPanel::Stats => &mut self.stats,
            OverlayPanel::Loading => &mut self.loading,
        }
    }

    /// Whether `panel` is on screen. Only ever over the game itself: never
    /// over the client, another app or the desktop — except in the preview,
    /// which shows every switched-on panel so it can be placed.
    pub fn shows(&self, panel: OverlayPanel, inputs: &OverlayInputs) -> bool {
        if !self.panel(panel).enabled {
            return false;
        }
        if inputs.preview {
            return true;
        }
        // The loading screen comes before the game is read; every other panel
        // needs the game.
        let moment = match panel {
            OverlayPanel::NextItem => {
                inputs.in_game
                    && match self.show {
                        OverlayShow::Always => true,
                        OverlayShow::WhileDead => inputs.dead,
                    }
            }
            OverlayPanel::WinProbability | OverlayPanel::Stats => inputs.in_game,
            OverlayPanel::ItemValue => inputs.in_game && inputs.scoreboard,
            OverlayPanel::Loading => inputs.loading_screen && !inputs.in_game,
        };
        self.enabled && inputs.game_frontmost && !inputs.hidden_by_player && moment
    }

    /// `panel`'s top-left corner on `screen` for a window of `size`, kept
    /// entirely on the screen.
    pub fn origin(&self, panel: OverlayPanel, screen: Rect, size: (f64, f64)) -> (f64, f64) {
        let settings = self.panel(panel);
        let (width, height) = size;
        let (x, y) = match settings.custom {
            Some(point) => (
                screen.x + point.x * screen.width - width / 2.0,
                screen.y + point.y * screen.height - height / 2.0,
            ),
            None => {
                let left = screen.x + EDGE_MARGIN;
                let center = screen.x + (screen.width - width) / 2.0;
                let right = screen.x + screen.width - width - EDGE_MARGIN;
                let top = screen.y + TOP_MARGIN;
                let middle = screen.y + (screen.height - height) / 2.0;
                match settings.anchor {
                    OverlayAnchor::TopLeft => (left, top),
                    OverlayAnchor::TopCenter => (center, top),
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
        let panel = |key: &str, fallback: PanelSettings| {
            let field = fields.get(key);
            PanelSettings {
                enabled: parse(field.and_then(|f| f.get("enabled"))).unwrap_or(fallback.enabled),
                anchor: parse(field.and_then(|f| f.get("anchor"))).unwrap_or(fallback.anchor),
                custom: parse::<OverlayPoint>(field.and_then(|f| f.get("custom"))).filter(
                    |point| (0.0..=1.0).contains(&point.x) && (0.0..=1.0).contains(&point.y),
                ),
            }
        };

        Self {
            enabled: parse(fields.get("enabled")).unwrap_or(defaults.enabled),
            show: parse(fields.get("show")).unwrap_or(defaults.show),
            scale: parse(fields.get("scale"))
                .filter(in_range(MIN_SCALE, MAX_SCALE))
                .unwrap_or(defaults.scale),
            opacity: parse(fields.get("opacity"))
                .filter(in_range(MIN_OPACITY, MAX_OPACITY))
                .unwrap_or(defaults.opacity),
            next_item: panel("nextItem", defaults.next_item),
            win_probability: panel("winProbability", defaults.win_probability),
            item_value: panel("itemValue", defaults.item_value),
            stats: panel("stats", defaults.stats),
            loading: panel("loading", defaults.loading),
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
    use OverlayPanel::*;

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
        assert!(settings.shows(NextItem, &in_game()));
        assert!(settings.shows(WinProbability, &in_game()));
        // Another app in front, even with a game running.
        let away = OverlayInputs {
            game_frontmost: false,
            ..in_game()
        };
        assert!(OverlayPanel::ALL.iter().all(|p| !settings.shows(*p, &away)));
        // The game process in front but no game read yet (loading screen).
        let loading = OverlayInputs {
            in_game: false,
            ..in_game()
        };
        assert!(!settings.shows(NextItem, &loading));
    }

    #[test]
    fn the_item_value_waits_for_the_scoreboard() {
        let settings = OverlaySettings::default();
        assert!(!settings.shows(ItemValue, &in_game()));
        assert!(settings.shows(
            ItemValue,
            &OverlayInputs {
                scoreboard: true,
                ..in_game()
            }
        ));
    }

    #[test]
    fn the_loading_screen_panel_goes_once_the_game_is_read() {
        let settings = OverlaySettings::default();
        let loading = OverlayInputs {
            game_frontmost: true,
            loading_screen: true,
            ..OverlayInputs::default()
        };
        assert!(settings.shows(Loading, &loading));
        assert!(!settings.shows(NextItem, &loading));
        assert!(!settings.shows(
            Loading,
            &OverlayInputs {
                in_game: true,
                ..loading
            }
        ));
        assert!(!settings.shows(
            Loading,
            &OverlayInputs {
                game_frontmost: false,
                ..loading
            }
        ));
    }

    #[test]
    fn the_shortcut_and_the_switches_hide_it() {
        let settings = OverlaySettings::default();
        let hidden = OverlayInputs {
            hidden_by_player: true,
            scoreboard: true,
            ..in_game()
        };
        assert!(OverlayPanel::ALL
            .iter()
            .all(|p| !settings.shows(*p, &hidden)));
        let off = OverlaySettings {
            enabled: false,
            ..settings
        };
        assert!(!off.shows(WinProbability, &in_game()));
        let mut one_off = settings;
        one_off.win_probability.enabled = false;
        assert!(!one_off.shows(WinProbability, &in_game()));
        assert!(one_off.shows(NextItem, &in_game()));
    }

    #[test]
    fn while_dead_holds_the_next_item_only() {
        let settings = OverlaySettings {
            show: OverlayShow::WhileDead,
            ..OverlaySettings::default()
        };
        assert!(!settings.shows(NextItem, &in_game()));
        assert!(settings.shows(WinProbability, &in_game()));
        assert!(settings.shows(
            NextItem,
            &OverlayInputs {
                dead: true,
                ..in_game()
            }
        ));
    }

    #[test]
    fn the_preview_shows_every_switched_on_panel() {
        let mut settings = OverlaySettings {
            enabled: false,
            ..OverlaySettings::default()
        };
        settings.item_value.enabled = false;
        let preview = OverlayInputs {
            preview: true,
            ..OverlayInputs::default()
        };
        assert!(settings.shows(NextItem, &preview));
        assert!(settings.shows(WinProbability, &preview));
        assert!(!settings.shows(ItemValue, &preview));
    }

    #[test]
    fn anchors_sit_inside_the_margins() {
        let at = |anchor| {
            let mut settings = OverlaySettings::default();
            settings.next_item.anchor = anchor;
            settings.origin(NextItem, SCREEN, SIZE)
        };
        assert_eq!(at(OverlayAnchor::TopLeft), (16.0, 56.0));
        assert_eq!(at(OverlayAnchor::TopCenter), (1130.0, 56.0));
        assert_eq!(at(OverlayAnchor::TopRight), (2244.0, 56.0));
        assert_eq!(at(OverlayAnchor::CenterLeft), (16.0, 660.0));
        assert_eq!(at(OverlayAnchor::CenterRight), (2244.0, 660.0));
    }

    #[test]
    fn a_dragged_position_round_trips_and_stays_on_screen() {
        let mut settings = OverlaySettings::default();
        settings.win_probability.custom =
            Some(OverlaySettings::point_at(SCREEN, (1000.0, 400.0), SIZE));
        assert_eq!(
            settings.origin(WinProbability, SCREEN, SIZE),
            (1000.0, 400.0)
        );
        // Only that panel moved.
        assert_eq!(settings.origin(NextItem, SCREEN, SIZE), (2244.0, 56.0));

        // The same place on a smaller screen, and never off its edge.
        let small = Rect {
            width: 1280.0,
            height: 720.0,
            ..SCREEN
        };
        assert_eq!(settings.origin(WinProbability, small, SIZE), (425.0, 170.0));
        settings.win_probability.custom = Some(OverlayPoint { x: 1.0, y: 1.0 });
        assert_eq!(
            settings.origin(WinProbability, SCREEN, SIZE),
            (2260.0, 1320.0)
        );
    }

    #[test]
    fn reads_leniently_field_by_field() {
        let settings = OverlaySettings::from_json(
            r#"{"enabled":false,"show":"whileDead","scale":9,"opacity":0.7,
                "nextItem":{"anchor":"sideways","custom":{"x":2,"y":0.5}},
                "itemValue":{"enabled":false,"anchor":"center-left"}}"#,
        );
        assert!(!settings.enabled);
        assert_eq!(settings.show, OverlayShow::WhileDead);
        assert_eq!(settings.scale, 1.0);
        assert_eq!(settings.opacity, 0.7);
        assert_eq!(settings.next_item, OverlaySettings::default().next_item);
        assert!(!settings.item_value.enabled);
        assert_eq!(settings.item_value.anchor, OverlayAnchor::CenterLeft);
        assert_eq!(
            settings.win_probability,
            OverlaySettings::default().win_probability
        );
        assert_eq!(
            OverlaySettings::from_json("not json"),
            OverlaySettings::default()
        );
    }

    #[test]
    fn saves_what_it_reads() {
        let dir = std::env::temp_dir().join(format!("overlay-settings-{}", std::process::id()));
        let path = dir.join("overlay-settings.json");
        let mut settings = OverlaySettings {
            scale: 1.2,
            ..OverlaySettings::default()
        };
        settings.item_value.custom = Some(OverlayPoint { x: 0.25, y: 0.75 });
        settings.save(&path).unwrap();
        assert_eq!(OverlaySettings::load(&path), settings);
        let _ = fs::remove_dir_all(dir);
    }

    #[test]
    fn panels_round_trip_through_their_slug() {
        for panel in OverlayPanel::ALL {
            assert_eq!(OverlayPanel::from_slug(panel.slug()), Some(panel));
        }
        assert_eq!(OverlayPanel::from_slug("hud"), None);
    }
}
