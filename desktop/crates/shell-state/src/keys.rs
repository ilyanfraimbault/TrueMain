//! The keys a player binds an overlay panel to (#1915): physical keys, the
//! chords built from them, what makes a chord refused, and how the game's own
//! keybindings are read to warn about a chord that also does something in game.
//!
//! The overlay reads the keyboard's state rather than intercepting it (no hook,
//! no registered hotkey — #1673), so the game receives every key of a chord as
//! well: the app cannot swallow one, it can only tell the player.

use std::collections::BTreeMap;

use serde::{Deserialize, Serialize};
use serde_json::Value;

macro_rules! key_codes {
    ($($code:ident => $label:literal, $mac:literal, $scan:literal, [$($bind:literal),*];)*) => {
        /// A physical key, named after the web's `KeyboardEvent.code`, so the
        /// app records it as is and the binding holds on any layout: `KeyQ` is
        /// the key left of W on QWERTY and left of Z on AZERTY alike.
        #[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Hash, Serialize, Deserialize)]
        pub enum KeyCode {
            $($code,)*
        }

        impl KeyCode {
            pub const ALL: &'static [KeyCode] = &[$(KeyCode::$code,)*];

            /// The key's name on a US keyboard.
            pub fn label(self) -> &'static str {
                match self {
                    $(KeyCode::$code => $label,)*
                }
            }

            /// macOS's virtual key code (`kVK_*`): a key position.
            pub fn mac(self) -> u16 {
                match self {
                    $(KeyCode::$code => $mac,)*
                }
            }

            /// Windows' set-1 scan code: a key position, which the shell turns
            /// into the current layout's virtual key before reading its state.
            pub fn scan_code(self) -> u16 {
                match self {
                    $(KeyCode::$code => $scan,)*
                }
            }

            /// The key in the game's keybinding files (`[q]`, `[F1]`, `[Space]`).
            fn from_bind(token: &str) -> Option<KeyCode> {
                let token = token.to_ascii_lowercase();
                $({
                    let binds: &[&str] = &[$($bind),*];
                    if binds.contains(&token.as_str()) {
                        return Some(KeyCode::$code);
                    }
                })*
                None
            }
        }
    };
}

key_codes! {
    KeyA => "A", 0x00, 0x1E, ["a"];
    KeyB => "B", 0x0B, 0x30, ["b"];
    KeyC => "C", 0x08, 0x2E, ["c"];
    KeyD => "D", 0x02, 0x20, ["d"];
    KeyE => "E", 0x0E, 0x12, ["e"];
    KeyF => "F", 0x03, 0x21, ["f"];
    KeyG => "G", 0x05, 0x22, ["g"];
    KeyH => "H", 0x04, 0x23, ["h"];
    KeyI => "I", 0x22, 0x17, ["i"];
    KeyJ => "J", 0x26, 0x24, ["j"];
    KeyK => "K", 0x28, 0x25, ["k"];
    KeyL => "L", 0x25, 0x26, ["l"];
    KeyM => "M", 0x2E, 0x32, ["m"];
    KeyN => "N", 0x2D, 0x31, ["n"];
    KeyO => "O", 0x1F, 0x18, ["o"];
    KeyP => "P", 0x23, 0x19, ["p"];
    KeyQ => "Q", 0x0C, 0x10, ["q"];
    KeyR => "R", 0x0F, 0x13, ["r"];
    KeyS => "S", 0x01, 0x1F, ["s"];
    KeyT => "T", 0x11, 0x14, ["t"];
    KeyU => "U", 0x20, 0x16, ["u"];
    KeyV => "V", 0x09, 0x2F, ["v"];
    KeyW => "W", 0x0D, 0x11, ["w"];
    KeyX => "X", 0x07, 0x2D, ["x"];
    KeyY => "Y", 0x10, 0x15, ["y"];
    KeyZ => "Z", 0x06, 0x2C, ["z"];
    Digit0 => "0", 0x1D, 0x0B, ["0"];
    Digit1 => "1", 0x12, 0x02, ["1"];
    Digit2 => "2", 0x13, 0x03, ["2"];
    Digit3 => "3", 0x14, 0x04, ["3"];
    Digit4 => "4", 0x15, 0x05, ["4"];
    Digit5 => "5", 0x17, 0x06, ["5"];
    Digit6 => "6", 0x16, 0x07, ["6"];
    Digit7 => "7", 0x1A, 0x08, ["7"];
    Digit8 => "8", 0x1C, 0x09, ["8"];
    Digit9 => "9", 0x19, 0x0A, ["9"];
    F1 => "F1", 0x7A, 0x3B, ["f1"];
    F2 => "F2", 0x78, 0x3C, ["f2"];
    F3 => "F3", 0x63, 0x3D, ["f3"];
    F4 => "F4", 0x76, 0x3E, ["f4"];
    F5 => "F5", 0x60, 0x3F, ["f5"];
    F6 => "F6", 0x61, 0x40, ["f6"];
    F7 => "F7", 0x62, 0x41, ["f7"];
    F8 => "F8", 0x64, 0x42, ["f8"];
    F9 => "F9", 0x65, 0x43, ["f9"];
    F10 => "F10", 0x6D, 0x44, ["f10"];
    F11 => "F11", 0x67, 0x57, ["f11"];
    F12 => "F12", 0x6F, 0x58, ["f12"];
    Backquote => "`", 0x32, 0x29, ["`"];
    Minus => "-", 0x1B, 0x0C, ["-"];
    Equal => "=", 0x18, 0x0D, ["="];
    BracketLeft => "[", 0x21, 0x1A, [];
    BracketRight => "]", 0x1E, 0x1B, [];
    Backslash => "\\", 0x2A, 0x2B, ["\\"];
    IntlBackslash => "<", 0x0A, 0x56, [];
    Semicolon => ";", 0x29, 0x27, [";"];
    Quote => "'", 0x27, 0x28, ["'"];
    Comma => ",", 0x2B, 0x33, [","];
    Period => ".", 0x2F, 0x34, ["."];
    Slash => "/", 0x2C, 0x35, ["/"];
    Space => "Space", 0x31, 0x39, ["space"];
    Escape => "Esc", 0x35, 0x01, ["esc", "escape"];
    Enter => "Enter", 0x24, 0x1C, ["return", "enter"];
}

/// A combination of keys: the modifiers and TAB held, and at most one key.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Hash, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Chord {
    #[serde(default)]
    pub alt: bool,
    #[serde(default)]
    pub shift: bool,
    #[serde(default)]
    pub ctrl: bool,
    /// ⌘ on macOS, the Windows key on Windows.
    #[serde(default)]
    pub meta: bool,
    #[serde(default)]
    pub tab: bool,
    #[serde(default)]
    pub key: Option<KeyCode>,
}

/// What the keyboard holds, as the shell read it.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct KeysDown {
    pub alt: bool,
    pub shift: bool,
    pub ctrl: bool,
    pub meta: bool,
    pub tab: bool,
    pub keys: Vec<KeyCode>,
}

impl Chord {
    /// TAB alone: the item value's own key, the game's scoreboard.
    pub const TAB: Chord = Chord {
        alt: false,
        shift: false,
        ctrl: false,
        meta: false,
        tab: true,
        key: None,
    };

    /// The global hide shortcut, ⌥⇧O / Alt+Shift+O.
    pub const HIDE: Chord = Chord {
        alt: true,
        shift: true,
        ctrl: false,
        meta: false,
        tab: false,
        key: Some(KeyCode::KeyO),
    };

    pub const fn with(alt: bool, shift: bool, tab: bool, key: KeyCode) -> Chord {
        Chord {
            alt,
            shift,
            ctrl: false,
            meta: false,
            tab,
            key: Some(key),
        }
    }

    /// Whether the chord is held. With a key, the modifiers and TAB must be
    /// exactly the chord's, so Alt+1 and Alt+Shift+1 are two chords; with no
    /// key (TAB alone), only what it names counts, as TAB did before.
    pub fn is_down(&self, keys: &KeysDown) -> bool {
        match self.key {
            Some(key) => {
                keys.keys.contains(&key)
                    && keys.alt == self.alt
                    && keys.shift == self.shift
                    && keys.ctrl == self.ctrl
                    && keys.meta == self.meta
                    && keys.tab == self.tab
            }
            None => {
                self.tab
                    && keys.tab
                    && (!self.alt || keys.alt)
                    && (!self.shift || keys.shift)
                    && (!self.ctrl || keys.ctrl)
                    && (!self.meta || keys.meta)
            }
        }
    }

    /// The chord as the player presses it: `⌃⌥⇧⌘ Tab Q` on macOS,
    /// `Ctrl+Alt+Shift+Win+Tab+Q` elsewhere.
    pub fn label(&self, mac: bool) -> String {
        let mut parts: Vec<&str> = Vec::new();
        if mac {
            let mut symbols = String::new();
            for (on, symbol) in [
                (self.ctrl, "⌃"),
                (self.alt, "⌥"),
                (self.shift, "⇧"),
                (self.meta, "⌘"),
            ] {
                if on {
                    symbols.push_str(symbol);
                }
            }
            if self.tab {
                parts.push("Tab");
            }
            if let Some(key) = self.key {
                parts.push(key.label());
            }
            let rest = parts.join("+");
            return format!("{symbols}{rest}");
        }
        for (on, name) in [
            (self.ctrl, "Ctrl"),
            (self.alt, "Alt"),
            (self.shift, "Shift"),
            (self.meta, "Win"),
            (self.tab, "Tab"),
        ] {
            if on {
                parts.push(name);
            }
        }
        if let Some(key) = self.key {
            parts.push(key.label());
        }
        parts.join("+")
    }

    /// Why this chord cannot be a panel's key, whatever else is bound. `tab_alone`
    /// says whether TAB alone is allowed here (the item value held with it).
    pub fn refusal(&self, tab_alone: bool) -> Option<&'static str> {
        if self.key.is_none() && !self.tab {
            return Some("Press a key: modifiers alone are not a shortcut.");
        }
        if self.key.is_none() && !(tab_alone && *self == Chord::TAB) {
            return Some("TAB alone opens the scoreboard: add a key to it.");
        }
        if matches!(self.key, Some(KeyCode::Escape | KeyCode::Enter)) {
            return Some("Esc and Enter open the game's menu and chat.");
        }
        if *self == Chord::HIDE {
            return Some("This is the shortcut that hides the whole overlay.");
        }
        if self.meta {
            return Some("⌘ and the Windows key belong to the system.");
        }
        if self.alt && (self.tab || self.key == Some(KeyCode::F4)) {
            return Some("Alt+Tab and Alt+F4 belong to the system.");
        }
        None
    }
}

/// One of the game's keybindings: a key with its modifiers, and what it does.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct GameBind {
    pub alt: bool,
    pub shift: bool,
    pub ctrl: bool,
    pub key: KeyCode,
    /// The game's name for the action (`evtCastSpell1`).
    pub event: String,
}

/// The player's League keybindings, read from the client
/// (`/lol-game-settings/v1/input-settings`: sections of `evtName → "[Ctrl][q],[F5]"`),
/// or Riot's defaults when the client is not there to ask.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct GameBinds {
    pub binds: Vec<GameBind>,
    /// Read from the player's own settings rather than Riot's defaults.
    pub own: bool,
}

/// Riot's default keybindings for the keys a chord would use, the fallback
/// when the client cannot be read. Not every bind: those of keys the
/// picker could offer.
const DEFAULT_BINDS: &[(&str, &str)] = &[
    ("evtCastSpell1", "[q]"),
    ("evtCastSpell2", "[w]"),
    ("evtCastSpell3", "[e]"),
    ("evtCastSpell4", "[r]"),
    ("evtCastAvatarSpell1", "[d]"),
    ("evtCastAvatarSpell2", "[f]"),
    ("evtLevelSpell1", "[Ctrl][q]"),
    ("evtLevelSpell2", "[Ctrl][w]"),
    ("evtLevelSpell3", "[Ctrl][e]"),
    ("evtLevelSpell4", "[Ctrl][r]"),
    ("evtSelfCastSpell1", "[Alt][q]"),
    ("evtSelfCastSpell2", "[Alt][w]"),
    ("evtSelfCastSpell3", "[Alt][e]"),
    ("evtSelfCastSpell4", "[Alt][r]"),
    ("evtUseItem1", "[1]"),
    ("evtUseItem2", "[2]"),
    ("evtUseItem3", "[3]"),
    ("evtUseVisionItem", "[4]"),
    ("evtUseItem4", "[5]"),
    ("evtUseItem5", "[6]"),
    ("evtUseItem6", "[7]"),
    ("evtUseItem7", "[b]"),
    ("evtOpenShop", "[p]"),
    ("evtShowScoreBoard", "[Tab]"),
    ("evtCameraLockToggle", "[y]"),
    ("evtCameraSnap", "[Space]"),
    ("evtPlayerAttackMove", "[a]"),
    ("evtPlayerStopPosition", "[s]"),
    ("evtPlayerHoldPosition", "[h]"),
    ("evtShowCharacterMenu", "[c]"),
    ("evtChampionOnly", "[`]"),
    ("evtSelectSelf", "[F1]"),
    ("evtSelectAlly1", "[F2]"),
    ("evtSelectAlly2", "[F3]"),
    ("evtSelectAlly3", "[F4]"),
    ("evtSelectAlly4", "[F5]"),
    ("evtEmoteJoke", "[Ctrl][1]"),
    ("evtEmoteTaunt", "[Ctrl][2]"),
    ("evtEmoteDance", "[Ctrl][3]"),
    ("evtEmoteLaugh", "[Ctrl][4]"),
    ("evtEmoteToggle", "[Ctrl][5]"),
    ("evtRadialEmoteOpen", "[t]"),
    ("evtChatHistory", "[z]"),
    ("evtToggleFPSAndLatency", "[Ctrl][f]"),
];

impl GameBinds {
    /// Riot's defaults.
    pub fn defaults() -> Self {
        let mut binds = Vec::new();
        for (event, value) in DEFAULT_BINDS {
            binds.extend(parse_binds(event, value));
        }
        Self { binds, own: false }
    }

    /// The client's `input-settings` answer. An event the player left at its
    /// default is absent from it, so the defaults fill in what is not set;
    /// `None` if the answer is not that shape.
    pub fn from_input_settings(body: &str) -> Option<Self> {
        let Ok(Value::Object(sections)) = serde_json::from_str::<Value>(body) else {
            return None;
        };
        let mut set: BTreeMap<String, String> = DEFAULT_BINDS
            .iter()
            .map(|(event, value)| (event.to_string(), value.to_string()))
            .collect();
        for section in sections.values() {
            let Value::Object(events) = section else {
                continue;
            };
            for (event, value) in events {
                if let Value::String(value) = value {
                    set.insert(event.clone(), value.clone());
                }
            }
        }
        let binds = set
            .iter()
            .flat_map(|(event, value)| parse_binds(event, value))
            .collect();
        Some(Self { binds, own: true })
    }

    /// What pressing `chord` also does in game, if anything: the game sees its
    /// key with its modifiers (TAB is no modifier to it, so Tab+Q casts Q).
    pub fn conflict(&self, chord: &Chord) -> Option<String> {
        let key = chord.key?;
        let bind = self.binds.iter().find(|bind| {
            bind.key == key
                && bind.alt == chord.alt
                && bind.shift == chord.shift
                && bind.ctrl == chord.ctrl
        })?;
        Some(format!(
            "{} in game: the shortcut will also {}.",
            capitalise(&action(&bind.event)),
            action(&bind.event)
        ))
    }
}

/// `[Ctrl][q],[F5]` → one bind per alternative. `[<Unbound>]` and keys no
/// chord can use are skipped.
fn parse_binds(event: &str, value: &str) -> Vec<GameBind> {
    value
        .split(',')
        .filter_map(|alternative| {
            let mut bind = GameBind {
                alt: false,
                shift: false,
                ctrl: false,
                key: KeyCode::Space,
                event: event.to_string(),
            };
            let mut key = None;
            for token in alternative
                .trim()
                .trim_start_matches('[')
                .trim_end_matches(']')
                .split("][")
            {
                match token.to_ascii_lowercase().as_str() {
                    "alt" => bind.alt = true,
                    "shift" => bind.shift = true,
                    "ctrl" => bind.ctrl = true,
                    other => key = KeyCode::from_bind(other).or(key),
                }
            }
            bind.key = key?;
            Some(bind)
        })
        .collect()
}

/// What a game event does, worded to follow "the shortcut will also".
fn action(event: &str) -> String {
    let slot = |prefix: &str| {
        event
            .strip_prefix(prefix)
            .and_then(|n| n.parse::<u8>().ok())
    };
    let ability = |n: u8| ["Q", "W", "E", "R"].get(usize::from(n.max(1)) - 1).copied();
    if let Some(name) = slot("evtCastSpell").and_then(ability) {
        return format!("cast your {name} ability");
    }
    if let Some(name) = slot("evtSelfCastSpell").and_then(ability) {
        return format!("self-cast your {name} ability");
    }
    if let Some(name) = slot("evtLevelSpell").and_then(ability) {
        return format!("level up your {name} ability");
    }
    if let Some(n) = slot("evtCastAvatarSpell") {
        return format!("cast your summoner spell {n}");
    }
    if event == "evtUseItem7" {
        return "recall".to_string();
    }
    if let Some(n) = slot("evtUseItem") {
        return format!("use your item {n}");
    }
    match event {
        "evtUseVisionItem" => "use your trinket",
        "evtOpenShop" => "open the shop",
        "evtShowScoreBoard" => "open the scoreboard",
        "evtCameraLockToggle" => "lock or unlock the camera",
        "evtCameraSnap" => "center the camera",
        "evtPlayerAttackMove" => "attack-move",
        "evtPlayerStopPosition" => "stop your champion",
        "evtPlayerHoldPosition" => "hold your position",
        "evtShowCharacterMenu" => "open your stats",
        "evtChampionOnly" => "target champions only",
        "evtSelectSelf" => "center the camera on you",
        "evtRadialEmoteOpen" => "open the emote wheel",
        "evtChatHistory" => "show the chat history",
        "evtToggleFPSAndLatency" => "show FPS and latency",
        _ if event.starts_with("evtSelectAlly") => "center the camera on an ally",
        _ if event.starts_with("evtEmote") => "play an emote",
        _ => "trigger a game bind",
    }
    .to_string()
}

fn capitalise(text: &str) -> String {
    let mut chars = text.chars();
    match chars.next() {
        Some(first) => first.to_uppercase().chain(chars).collect(),
        None => String::new(),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn held(tab: bool, alt: bool, shift: bool, keys: &[KeyCode]) -> KeysDown {
        KeysDown {
            alt,
            shift,
            tab,
            keys: keys.to_vec(),
            ..KeysDown::default()
        }
    }

    #[test]
    fn every_key_has_its_own_positions() {
        let mut macs: Vec<u16> = KeyCode::ALL.iter().map(|k| k.mac()).collect();
        let mut scans: Vec<u16> = KeyCode::ALL.iter().map(|k| k.scan_code()).collect();
        // TAB, which is part of a chord rather than its key.
        macs.push(0x30);
        scans.push(0x0F);
        let count = macs.len();
        macs.sort_unstable();
        macs.dedup();
        scans.sort_unstable();
        scans.dedup();
        assert_eq!(macs.len(), count);
        assert_eq!(scans.len(), count);
        assert_eq!(KeyCode::KeyQ.mac(), 0x0C);
        assert_eq!(KeyCode::KeyO.mac(), 0x1F);
        assert_eq!(KeyCode::KeyQ.scan_code(), 0x10);
        assert_eq!(KeyCode::Backquote.scan_code(), 0x29);
    }

    #[test]
    fn key_codes_are_the_webs() {
        let chord: Chord = serde_json::from_str(r#"{"alt":true,"key":"Digit1"}"#).unwrap();
        assert_eq!(chord.key, Some(KeyCode::Digit1));
        assert!(chord.alt && !chord.shift && !chord.tab);
        assert_eq!(
            serde_json::to_value(KeyCode::Backquote).unwrap(),
            "Backquote"
        );
        assert!(serde_json::from_str::<Chord>(r#"{"key":"NumpadEnter"}"#).is_err());
    }

    #[test]
    fn a_chord_is_down_with_exactly_its_modifiers() {
        let chord = Chord::with(true, true, false, KeyCode::Digit1);
        assert!(chord.is_down(&held(false, true, true, &[KeyCode::Digit1])));
        assert!(!chord.is_down(&held(false, true, false, &[KeyCode::Digit1])));
        assert!(!chord.is_down(&held(true, true, true, &[KeyCode::Digit1])));
        assert!(!chord.is_down(&held(false, true, true, &[KeyCode::Digit2])));
        let tab_q = Chord::with(false, false, true, KeyCode::KeyQ);
        assert!(tab_q.is_down(&held(true, false, false, &[KeyCode::KeyQ])));
        assert!(!tab_q.is_down(&held(false, false, false, &[KeyCode::KeyQ])));
    }

    #[test]
    fn tab_alone_holds_whatever_else_is_down() {
        assert!(Chord::TAB.is_down(&held(true, false, true, &[KeyCode::KeyQ])));
        assert!(!Chord::TAB.is_down(&held(false, false, false, &[])));
    }

    #[test]
    fn labels_follow_the_platform() {
        let chord = Chord::with(true, true, false, KeyCode::Digit1);
        assert_eq!(chord.label(true), "⌥⇧1");
        assert_eq!(chord.label(false), "Alt+Shift+1");
        assert_eq!(Chord::TAB.label(false), "Tab");
        assert_eq!(
            Chord::with(false, false, true, KeyCode::Backquote).label(true),
            "Tab+`"
        );
    }

    #[test]
    fn refuses_what_cannot_be_a_shortcut() {
        let modifiers_only = Chord {
            alt: true,
            ..Chord::default()
        };
        assert!(modifiers_only.refusal(true).is_some());
        assert!(Chord::TAB.refusal(false).is_some());
        assert!(Chord::TAB.refusal(true).is_none());
        let shift_tab = Chord {
            shift: true,
            ..Chord::TAB
        };
        assert!(shift_tab.refusal(true).is_some());
        assert!(Chord::with(false, false, true, KeyCode::Escape)
            .refusal(false)
            .is_some());
        assert!(Chord::with(false, false, false, KeyCode::Enter)
            .refusal(false)
            .is_some());
        assert!(Chord::HIDE.refusal(false).is_some());
        assert!(Chord::with(true, false, false, KeyCode::F4)
            .refusal(false)
            .is_some());
        assert!(Chord::with(true, false, true, KeyCode::KeyQ)
            .refusal(false)
            .is_some());
        let command_q = Chord {
            meta: true,
            ..Chord::with(false, false, false, KeyCode::KeyQ)
        };
        assert!(command_q.refusal(false).is_some());
        assert!(Chord::with(true, true, false, KeyCode::Digit1)
            .refusal(false)
            .is_none());
        assert!(Chord::with(false, false, true, KeyCode::KeyQ)
            .refusal(false)
            .is_none());
    }

    #[test]
    fn warns_about_a_key_the_game_also_uses() {
        let binds = GameBinds::defaults();
        assert!(!binds.own);
        assert_eq!(
            binds
                .conflict(&Chord::with(false, false, true, KeyCode::KeyQ))
                .as_deref(),
            Some("Cast your Q ability in game: the shortcut will also cast your Q ability.")
        );
        assert!(binds
            .conflict(&Chord::with(true, false, false, KeyCode::KeyW))
            .unwrap()
            .contains("self-cast your W"));
        assert!(binds
            .conflict(&Chord::with(true, true, false, KeyCode::Digit1))
            .is_none());
        assert!(binds.conflict(&Chord::TAB).is_none());
    }

    #[test]
    fn reads_the_players_own_binds_over_the_defaults() {
        let binds = GameBinds::from_input_settings(
            r#"{"GameEvents":{"evtCastSpell1":"[<Unbound>]","evtUseItem1":"[Alt][Shift][1],[F9]","evtCustom":7},
                "HUDEvents":{"evtSomethingNew":"[Shift][2]"}}"#,
        )
        .unwrap();
        assert!(binds.own);
        let tab_q = Chord::with(false, false, true, KeyCode::KeyQ);
        assert!(binds.conflict(&tab_q).is_none());
        assert!(binds
            .conflict(&Chord::with(true, true, false, KeyCode::Digit1))
            .unwrap()
            .contains("use your item 1"));
        assert!(binds
            .conflict(&Chord::with(false, true, false, KeyCode::Digit2))
            .unwrap()
            .contains("trigger a game bind"));
        // Not set by the player: still Riot's default.
        assert!(binds
            .conflict(&Chord::with(false, false, false, KeyCode::KeyW))
            .is_some());
        assert_eq!(GameBinds::from_input_settings("[]"), None);
    }
}
