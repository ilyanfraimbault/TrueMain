//! The in-game overlay's settings and the two rules that drive its windows:
//! when each panel shows, and where.
//!
//! The overlay is a few independent panels, each its own window placed on its
//! own (#1671's choice over one HUD): the next item, the win probability, the
//! player's own pace, the item value, which by default shows only while the
//! scoreboard (TAB) is held. Each panel shows always, while a chord is held,
//! or toggled by one (#1915, chords in `keys`). The windows are the shell's (`src-tauri/src/overlay`), macOS-only and
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

use crate::keys::{Chord, KeysDown};

/// The settings file's format version, written into it so a later format can
/// migrate rather than guess. Version 2 reads a dragged place against the
/// screen's free space rather than as the panel's centre (`OverlayPoint`).
/// Version 3 adds each panel's trigger; a version 2 file reads as today's
/// behaviour, which is every panel's default trigger.
pub const SETTINGS_VERSION: u32 = 3;

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
}

impl OverlayPanel {
    pub const ALL: [OverlayPanel; 4] = [
        OverlayPanel::NextItem,
        OverlayPanel::WinProbability,
        OverlayPanel::ItemValue,
        OverlayPanel::Stats,
    ];

    /// The panel's name in a window label and a route.
    pub fn slug(self) -> &'static str {
        match self {
            OverlayPanel::NextItem => "next-item",
            OverlayPanel::WinProbability => "win-probability",
            OverlayPanel::ItemValue => "item-value",
            OverlayPanel::Stats => "stats",
        }
    }

    pub fn from_slug(slug: &str) -> Option<Self> {
        Self::ALL.into_iter().find(|panel| panel.slug() == slug)
    }

    /// The panel's name for the player, as the Overlay page shows it.
    pub fn name(self) -> &'static str {
        match self {
            OverlayPanel::NextItem => "Next item",
            OverlayPanel::WinProbability => "Win probability",
            OverlayPanel::ItemValue => "Item value",
            OverlayPanel::Stats => "Your pace",
        }
    }

    fn index(self) -> usize {
        match self {
            OverlayPanel::NextItem => 0,
            OverlayPanel::WinProbability => 1,
            OverlayPanel::ItemValue => 2,
            OverlayPanel::Stats => 3,
        }
    }
}

/// What brings a panel on screen during a game, on top of its own moment
/// (the next item's `OverlayShow`) and the global hide shortcut.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(tag = "kind", rename_all = "camelCase")]
pub enum PanelTrigger {
    /// For the whole game.
    Always,
    /// Only while the chord is held.
    WhileHeld { chord: Chord },
    /// Each press of the chord shows or hides the panel, for this game: each
    /// game starts from `start_shown`.
    Toggle {
        chord: Chord,
        #[serde(rename = "startShown")]
        start_shown: bool,
    },
}

impl PanelTrigger {
    /// Before triggers existed, the item value showed while TAB was held and
    /// the others always: each panel's default is that.
    pub fn default_for(panel: OverlayPanel) -> Self {
        match panel {
            OverlayPanel::ItemValue => PanelTrigger::WhileHeld { chord: Chord::TAB },
            _ => PanelTrigger::Always,
        }
    }

    pub fn chord(&self) -> Option<Chord> {
        match self {
            PanelTrigger::Always => None,
            PanelTrigger::WhileHeld { chord } | PanelTrigger::Toggle { chord, .. } => Some(*chord),
        }
    }
}

/// One flag per panel.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct PanelFlags([bool; 4]);

impl PanelFlags {
    pub fn get(&self, panel: OverlayPanel) -> bool {
        self.0[panel.index()]
    }

    pub fn set(&mut self, panel: OverlayPanel, on: bool) {
        self.0[panel.index()] = on;
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

/// Where the player dragged a panel, as a fraction of the room the screen
/// leaves around it on each axis: 0 against the left (top) edge, 1 against
/// the right (bottom) one. So it lands in the same place at any resolution,
/// and a panel against an edge stays against it whatever size it takes —
/// its centre would drift off the edge when it grows or shrinks.
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
    pub trigger: PanelTrigger,
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
}

impl Default for OverlaySettings {
    fn default() -> Self {
        let at = |panel, anchor| PanelSettings {
            enabled: true,
            anchor,
            custom: None,
            trigger: PanelTrigger::default_for(panel),
        };
        Self {
            enabled: true,
            show: OverlayShow::Always,
            scale: 1.0,
            opacity: 0.95,
            next_item: at(OverlayPanel::NextItem, OverlayAnchor::TopRight),
            win_probability: at(OverlayPanel::WinProbability, OverlayAnchor::TopLeft),
            item_value: at(OverlayPanel::ItemValue, OverlayAnchor::TopCenter),
            stats: at(OverlayPanel::Stats, OverlayAnchor::CenterLeft),
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
    /// The phase says a game runs, from its loading screen on.
    pub game_running: bool,
    /// The app is reading a running game.
    pub in_game: bool,
    /// The player's champion is dead.
    pub dead: bool,
    /// The player hid the overlay with the shortcut during this game.
    pub hidden_by_player: bool,
    /// The panels whose chord is held (`PanelKeys`).
    pub held: PanelFlags,
    /// The toggled panels shown, as this game's presses left them.
    pub toggled: PanelFlags,
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
        }
    }

    pub fn panel_mut(&mut self, panel: OverlayPanel) -> &mut PanelSettings {
        match panel {
            OverlayPanel::NextItem => &mut self.next_item,
            OverlayPanel::WinProbability => &mut self.win_probability,
            OverlayPanel::ItemValue => &mut self.item_value,
            OverlayPanel::Stats => &mut self.stats,
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
        // Every panel needs the game read: never over its loading screen.
        let moment = match panel {
            OverlayPanel::NextItem => match self.show {
                OverlayShow::Always => true,
                OverlayShow::WhileDead => inputs.dead,
            },
            _ => true,
        };
        let triggered = match self.panel(panel).trigger {
            PanelTrigger::Always => true,
            PanelTrigger::WhileHeld { .. } => inputs.held.get(panel),
            PanelTrigger::Toggle { .. } => inputs.toggled.get(panel),
        };
        self.enabled
            && inputs.game_frontmost
            && !inputs.hidden_by_player
            && inputs.in_game
            && moment
            && triggered
    }

    /// Why `trigger` cannot be `panel`'s: a chord refused on its own
    /// (`Chord::refusal`), or one another panel already uses.
    pub fn refusal(&self, panel: OverlayPanel, trigger: &PanelTrigger) -> Option<String> {
        let chord = trigger.chord()?;
        let tab_alone =
            panel == OverlayPanel::ItemValue && matches!(trigger, PanelTrigger::WhileHeld { .. });
        if let Some(reason) = chord.refusal(tab_alone) {
            return Some(reason.to_string());
        }
        OverlayPanel::ALL
            .into_iter()
            .filter(|other| *other != panel)
            .find(|other| self.panel(*other).trigger.chord() == Some(chord))
            .map(|other| format!("Already the shortcut of {}.", other.name()))
    }

    /// The keys the panels' chords name, for the shell to read and no other.
    pub fn chord_keys(&self) -> Vec<crate::keys::KeyCode> {
        let mut keys: Vec<_> = OverlayPanel::ALL
            .into_iter()
            .filter_map(|panel| self.panel(panel).trigger.chord()?.key)
            .collect();
        keys.sort_unstable();
        keys.dedup();
        keys
    }

    /// The panels whose trigger starts shown, a game's first toggle state.
    pub fn toggles_at_start(&self) -> PanelFlags {
        let mut flags = PanelFlags::default();
        for panel in OverlayPanel::ALL {
            if let PanelTrigger::Toggle { start_shown, .. } = self.panel(panel).trigger {
                flags.set(panel, start_shown);
            }
        }
        flags
    }

    /// Whether `panel` has a window at all (#1916). Each one is a webview
    /// running the whole app bundle, so only the panels switched on get one,
    /// and only while it can be shown: for the preview, or from the game's
    /// loading screen — which leaves the page time to load before the panel
    /// is due — to its end.
    pub fn needs_window(&self, panel: OverlayPanel, inputs: &OverlayInputs) -> bool {
        self.panel(panel).enabled && (inputs.preview || (self.enabled && inputs.game_running))
    }

    /// `panel`'s top-left corner on `screen` for a window of `size`, kept
    /// entirely on the screen.
    pub fn origin(&self, panel: OverlayPanel, screen: Rect, size: (f64, f64)) -> (f64, f64) {
        let settings = self.panel(panel);
        let (width, height) = size;
        let (x, y) = match settings.custom {
            Some(point) => (
                screen.x + point.x * (screen.width - width).max(0.0),
                screen.y + point.y * (screen.height - height).max(0.0),
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
            let room = length - extent;
            if room <= 0.0 {
                return 0.5;
            }
            ((at - start) / room).clamp(0.0, 1.0)
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

    /// Parse the settings file leniently: each field that does not parse, or
    /// is out of range, is that field's default.
    pub fn from_json(body: &str) -> Self {
        let Ok(Value::Object(fields)) = serde_json::from_str::<Value>(body) else {
            return Self::default();
        };
        // Before version 2 a dragged place was the panel's centre: read as
        // version 2 it would land elsewhere, so the panel goes back to its spot.
        let current = parse::<u32>(fields.get("version")).is_some_and(|v| v >= 2);
        Self::from_fields(&fields, current)
    }

    /// Parse settings the app sends, as leniently as the file. They are
    /// always this build's format: the app holds what the shell gave it, which
    /// carries no version — only the file does.
    pub fn from_app(body: &str) -> Self {
        let Ok(Value::Object(fields)) = serde_json::from_str::<Value>(body) else {
            return Self::default();
        };
        Self::from_fields(&fields, true)
    }

    fn from_fields(fields: &serde_json::Map<String, Value>, current: bool) -> Self {
        let defaults = Self::default();
        let in_range = |min: f64, max: f64| move |value: &f64| (min..=max).contains(value);
        let panel = |key: &str, fallback: PanelSettings| {
            let field = fields.get(key);
            PanelSettings {
                trigger: parse(field.and_then(|f| f.get("trigger"))).unwrap_or(fallback.trigger),
                enabled: parse(field.and_then(|f| f.get("enabled"))).unwrap_or(fallback.enabled),
                anchor: parse(field.and_then(|f| f.get("anchor"))).unwrap_or(fallback.anchor),
                custom: parse::<OverlayPoint>(field.and_then(|f| f.get("custom"))).filter(
                    |point| {
                        current && (0.0..=1.0).contains(&point.x) && (0.0..=1.0).contains(&point.y)
                    },
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
        }
        .with_valid_triggers()
    }

    /// Each refused trigger back to its panel's default, in panel order: of
    /// two panels on one chord, the first keeps it.
    fn with_valid_triggers(mut self) -> Self {
        for panel in OverlayPanel::ALL {
            let trigger = self.panel(panel).trigger;
            let mut earlier = self;
            for later in OverlayPanel::ALL
                .into_iter()
                .skip_while(|p| *p != panel)
                .skip(1)
            {
                earlier.panel_mut(later).trigger = PanelTrigger::Always;
            }
            if earlier.refusal(panel, &trigger).is_some() {
                self.panel_mut(panel).trigger = PanelTrigger::default_for(panel);
            }
        }
        self
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

/// The panels' chords as the keyboard holds them, read on every tick of the
/// shell's watch: which are held, and what each toggle's presses left.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct PanelKeys {
    held: PanelFlags,
    /// `None` until a toggle is pressed this game: each starts from its default.
    toggled: Option<PanelFlags>,
}

impl PanelKeys {
    /// One read of the keyboard — empty while the game is not frontmost, so
    /// nothing reacts there. True when a panel's visibility may have changed.
    pub fn read(&mut self, settings: &OverlaySettings, keys: &KeysDown) -> bool {
        let mut held = PanelFlags::default();
        let mut changed = false;
        for panel in OverlayPanel::ALL {
            let trigger = settings.panel(panel).trigger;
            let down = trigger.chord().is_some_and(|chord| chord.is_down(keys));
            held.set(panel, down);
            match trigger {
                PanelTrigger::Always => {}
                PanelTrigger::WhileHeld { .. } => changed |= down != self.held.get(panel),
                PanelTrigger::Toggle { .. } => {
                    if down && !self.held.get(panel) {
                        let mut toggled = self.toggled(settings);
                        toggled.set(panel, !toggled.get(panel));
                        self.toggled = Some(toggled);
                        changed = true;
                    }
                }
            }
        }
        self.held = held;
        changed
    }

    pub fn held(&self) -> PanelFlags {
        self.held
    }

    pub fn toggled(&self, settings: &OverlaySettings) -> PanelFlags {
        self.toggled.unwrap_or_else(|| settings.toggles_at_start())
    }

    /// The game ended: the next starts from each toggle's default.
    pub fn reset_toggles(&mut self) {
        self.toggled = None;
    }
}

/// Keep a span of `extent` starting at `at` inside `[start, start + length]`.
fn clamp_into(at: f64, start: f64, length: f64, extent: f64) -> f64 {
    let last = start + (length - extent).max(0.0);
    at.clamp(start, last)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::keys::KeyCode;
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
    fn builds_a_window_only_for_a_panel_that_can_show() {
        let mut settings = OverlaySettings::default();
        let running = OverlayInputs {
            game_running: true,
            ..OverlayInputs::default()
        };
        // No game, no preview: no window, whatever the settings.
        assert!(OverlayPanel::ALL
            .iter()
            .all(|p| !settings.needs_window(*p, &OverlayInputs::default())));
        // From the loading screen on, before the game is read.
        assert!(OverlayPanel::ALL
            .iter()
            .all(|p| settings.needs_window(*p, &running)));
        settings.stats.enabled = false;
        assert!(!settings.needs_window(Stats, &running));
        assert!(settings.needs_window(NextItem, &running));
        // The whole overlay off: nothing over a game, the preview still places the panels.
        settings.enabled = false;
        assert!(!settings.needs_window(NextItem, &running));
        let preview = OverlayInputs {
            preview: true,
            ..OverlayInputs::default()
        };
        assert!(settings.needs_window(NextItem, &preview));
        assert!(!settings.needs_window(Stats, &preview));
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

    fn holding(settings: &OverlaySettings, keys: &KeysDown) -> OverlayInputs {
        let mut panel_keys = PanelKeys::default();
        panel_keys.read(settings, keys);
        OverlayInputs {
            held: panel_keys.held(),
            toggled: panel_keys.toggled(settings),
            ..in_game()
        }
    }

    fn tab() -> KeysDown {
        KeysDown {
            tab: true,
            ..KeysDown::default()
        }
    }

    fn alt_shift(key: KeyCode) -> KeysDown {
        KeysDown {
            alt: true,
            shift: true,
            keys: vec![key],
            ..KeysDown::default()
        }
    }

    #[test]
    fn the_item_value_waits_for_the_scoreboard() {
        let settings = OverlaySettings::default();
        assert!(!settings.shows(ItemValue, &in_game()));
        assert!(settings.shows(ItemValue, &holding(&settings, &tab())));
    }

    #[test]
    fn a_held_chord_shows_its_panel_while_down() {
        let mut settings = OverlaySettings::default();
        let chord = Chord::with(true, true, false, KeyCode::Digit2);
        settings.win_probability.trigger = PanelTrigger::WhileHeld { chord };
        assert!(!settings.shows(WinProbability, &in_game()));
        let down = holding(&settings, &alt_shift(KeyCode::Digit2));
        assert!(settings.shows(WinProbability, &down));
        // The others are not held by it.
        assert!(!settings.shows(ItemValue, &down));
        assert!(settings.shows(Stats, &down));
        // Over another app, a held chord shows nothing.
        let away = OverlayInputs {
            game_frontmost: false,
            ..down
        };
        assert!(!settings.shows(WinProbability, &away));
    }

    #[test]
    fn a_toggle_flips_on_each_press_and_resets_with_the_game() {
        let mut settings = OverlaySettings::default();
        let chord = Chord::with(true, true, false, KeyCode::Digit3);
        settings.stats.trigger = PanelTrigger::Toggle {
            chord,
            start_shown: true,
        };
        let mut keys = PanelKeys::default();
        let inputs = |keys: &PanelKeys| OverlayInputs {
            held: keys.held(),
            toggled: keys.toggled(&settings),
            ..in_game()
        };
        assert!(settings.shows(Stats, &inputs(&keys)));
        assert!(keys.read(&settings, &alt_shift(KeyCode::Digit3)));
        assert!(!settings.shows(Stats, &inputs(&keys)));
        // Held down across ticks: one press, one flip.
        assert!(!keys.read(&settings, &alt_shift(KeyCode::Digit3)));
        assert!(!settings.shows(Stats, &inputs(&keys)));
        assert!(!keys.read(&settings, &KeysDown::default()));
        assert!(keys.read(&settings, &alt_shift(KeyCode::Digit3)));
        assert!(settings.shows(Stats, &inputs(&keys)));
        keys.read(&settings, &KeysDown::default());
        keys.read(&settings, &alt_shift(KeyCode::Digit3));
        assert!(!settings.shows(Stats, &inputs(&keys)));
        keys.reset_toggles();
        assert!(settings.shows(Stats, &inputs(&keys)));
    }

    #[test]
    fn a_toggled_next_item_still_waits_for_death() {
        let mut settings = OverlaySettings {
            show: OverlayShow::WhileDead,
            ..OverlaySettings::default()
        };
        settings.next_item.trigger = PanelTrigger::Toggle {
            chord: Chord::with(true, true, false, KeyCode::Digit1),
            start_shown: true,
        };
        let shown = holding(&settings, &KeysDown::default());
        assert!(!settings.shows(NextItem, &shown));
        assert!(settings.shows(
            NextItem,
            &OverlayInputs {
                dead: true,
                ..shown
            }
        ));
        let hidden = OverlayInputs {
            dead: true,
            toggled: PanelFlags::default(),
            ..shown
        };
        assert!(!settings.shows(NextItem, &hidden));
    }

    #[test]
    fn the_hide_shortcut_wins_over_every_trigger() {
        let mut settings = OverlaySettings::default();
        settings.stats.trigger = PanelTrigger::Toggle {
            chord: Chord::with(true, true, false, KeyCode::Digit3),
            start_shown: true,
        };
        let inputs = OverlayInputs {
            hidden_by_player: true,
            ..holding(&settings, &tab())
        };
        assert!(OverlayPanel::ALL
            .iter()
            .all(|p| !settings.shows(*p, &inputs)));
    }

    #[test]
    fn refuses_a_chord_another_panel_uses() {
        let mut settings = OverlaySettings::default();
        let chord = Chord::with(true, true, false, KeyCode::Digit1);
        settings.next_item.trigger = PanelTrigger::WhileHeld { chord };
        let toggle = PanelTrigger::Toggle {
            chord,
            start_shown: false,
        };
        assert_eq!(
            settings.refusal(Stats, &toggle).as_deref(),
            Some("Already the shortcut of Next item.")
        );
        assert_eq!(settings.refusal(NextItem, &toggle), None);
        // TAB alone is the item value's own, held.
        let tab = PanelTrigger::WhileHeld { chord: Chord::TAB };
        assert!(settings.refusal(Stats, &tab).is_some());
        let mut free = OverlaySettings::default();
        free.item_value.trigger = PanelTrigger::Always;
        assert_eq!(free.refusal(ItemValue, &tab), None);
        assert!(free
            .refusal(
                ItemValue,
                &PanelTrigger::Toggle {
                    chord: Chord::TAB,
                    start_shown: false
                }
            )
            .is_some());
        assert_eq!(settings.refusal(Stats, &PanelTrigger::Always), None);
    }

    #[test]
    fn a_version_2_file_keeps_todays_behaviour() {
        let old = r#"{"version":2,"stats":{"enabled":true,"anchor":"top-left","custom":{"x":0.9,"y":0.5}},
                      "itemValue":{"enabled":true,"anchor":"top-center","custom":null}}"#;
        let settings = OverlaySettings::from_json(old);
        assert_eq!(settings.stats.trigger, PanelTrigger::Always);
        assert_eq!(settings.next_item.trigger, PanelTrigger::Always);
        assert_eq!(
            settings.item_value.trigger,
            PanelTrigger::WhileHeld { chord: Chord::TAB }
        );
        assert_eq!(settings.stats.custom, Some(OverlayPoint { x: 0.9, y: 0.5 }));
    }

    #[test]
    fn a_refused_or_unknown_trigger_falls_back_to_the_default() {
        let settings = OverlaySettings::from_json(
            r#"{"version":3,
                "nextItem":{"trigger":{"kind":"toggle","chord":{"alt":true,"shift":true,"key":"Digit1"},"startShown":false}},
                "winProbability":{"trigger":{"kind":"whileHeld","chord":{"alt":true,"shift":true,"key":"Digit1"}}},
                "stats":{"trigger":{"kind":"whileHeld","chord":{"alt":true,"shift":true,"key":"KeyO"}}},
                "itemValue":{"trigger":{"kind":"sometimes"}}}"#,
        );
        assert_eq!(
            settings.next_item.trigger,
            PanelTrigger::Toggle {
                chord: Chord::with(true, true, false, KeyCode::Digit1),
                start_shown: false
            }
        );
        // The same chord as the next item's, and the hide shortcut.
        assert_eq!(settings.win_probability.trigger, PanelTrigger::Always);
        assert_eq!(settings.stats.trigger, PanelTrigger::Always);
        assert_eq!(
            settings.item_value.trigger,
            PanelTrigger::default_for(ItemValue)
        );
    }

    #[test]
    fn the_shortcut_and_the_switches_hide_it() {
        let settings = OverlaySettings::default();
        let hidden = OverlayInputs {
            hidden_by_player: true,
            ..holding(&settings, &tab())
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
        assert_eq!(settings.origin(WinProbability, small, SIZE), (434.0, 182.0));
        settings.win_probability.custom = Some(OverlayPoint { x: 1.0, y: 1.0 });
        assert_eq!(
            settings.origin(WinProbability, SCREEN, SIZE),
            (2260.0, 1320.0)
        );
    }

    #[test]
    fn a_panel_against_an_edge_stays_there_whatever_its_size() {
        let mut settings = OverlaySettings::default();
        settings.stats.custom = Some(OverlaySettings::point_at(SCREEN, (2260.0, 0.0), SIZE));
        let (x, y) = settings.origin(Stats, SCREEN, (176.0, 90.0));
        assert_eq!((x + 176.0, y), (SCREEN.width, 0.0));
    }

    #[test]
    fn a_dragged_place_from_before_version_2_is_dropped() {
        let old = r#"{"version":1,"stats":{"enabled":true,"anchor":"top-left","custom":{"x":0.9,"y":0.5}}}"#;
        assert_eq!(OverlaySettings::from_json(old).stats.custom, None);
        let new = r#"{"version":2,"stats":{"enabled":true,"anchor":"top-left","custom":{"x":0.9,"y":0.5}}}"#;
        assert_eq!(
            OverlaySettings::from_json(new).stats.custom,
            Some(OverlayPoint { x: 0.9, y: 0.5 })
        );
    }

    #[test]
    fn a_place_the_app_sends_is_kept_without_a_version() {
        // What the shell hands the app, and the app sends back with a change.
        let mut settings = OverlaySettings::default();
        settings.stats.custom = Some(OverlayPoint { x: 0.9, y: 0.5 });
        let sent = serde_json::to_string(&settings).unwrap();
        assert_eq!(OverlaySettings::from_app(&sent), settings);
        assert_eq!(
            OverlaySettings::from_app(r#"{"stats":{"custom":{"x":2,"y":0.5}}}"#)
                .stats
                .custom,
            None
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
        settings.stats.trigger = PanelTrigger::Toggle {
            chord: Chord::with(false, false, true, KeyCode::Backquote),
            start_shown: false,
        };
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
