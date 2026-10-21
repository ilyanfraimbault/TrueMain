//! Where the game draws its TAB scoreboard's rows, for the lane-gold plates
//! (#2186): one plate per row, down the scoreboard's middle.
//!
//! The scoreboard is drawn at a fixed place for a given window size and HUD
//! scale: centred across the window, its two teams side by side (blue side on
//! the left), five rows each in lane order, growing upwards from a fixed bottom
//! edge as the HUD scale grows. Like the rest of the HUD it is sized by the
//! window's height, against a 1920 × 1080 reference — by its width on a screen
//! narrower than 16:9.
//!
//! **The constants are measured, not published, and still to be confirmed in
//! a game played at several HUD scales.** Rows and their pitch come from a real
//! 1920 × 1080 TAB screenshot, whose HUD scale (about 0.3) is read from its
//! ability bar against one of ours at a known 0.29; the growth with the HUD
//! scale is the ability bar's between League's 0 and 100 (×1.52). Mirrored in
//! `app/app/utils/scoreboard-geometry.ts`, whose tests hold the same values.
//! The player's nudge covers what they miss.

use serde::{Deserialize, Serialize};
use serde_json::Value;

use crate::overlay::Rect;

/// The HUD the scoreboard is laid out against, in pixels.
const REFERENCE_WIDTH: f64 = 1920.0;
const REFERENCE_HEIGHT: f64 = 1080.0;
/// The scoreboard's five rows each side.
pub const ROWS: usize = 5;
/// A row's height at HUD scale 0, in reference pixels.
const ROW_PITCH_AT_ZERO: f64 = 66.4;
/// How much the HUD grows from scale 0 to scale 1: ×1.52 at 100.
const HUD_GROWTH: f64 = 0.52;
/// The last row's bottom edge, below the window's middle, in reference pixels:
/// it stays put while the scoreboard grows upwards.
const ROWS_BOTTOM_BELOW_MIDDLE: f64 = 152.8;
/// The board's width at HUD scale 0, in reference pixels.
const BOARD_WIDTH_AT_ZERO: f64 = 996.5;
/// How far the player may move the plates up or down, in reference pixels.
pub const MAX_NUDGE: f64 = 60.0;

/// The client's HUD settings the scoreboard depends on.
#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct HudSettings {
    /// League's "HUD scale", 0 – 1.
    pub global_scale: f64,
    /// "Mirror scoreboard layout": the left team's rows read right to left,
    /// both portraits meeting in the middle. The teams keep their sides and
    /// the rows their places, so the plates do not move with it.
    pub mirrored: bool,
    /// Read from the client rather than Riot's defaults.
    pub own: bool,
}

impl HudSettings {
    /// What a new League install starts with, until the client is read.
    pub const DEFAULTS: HudSettings = HudSettings {
        global_scale: 0.0,
        mirrored: false,
        own: false,
    };

    /// The client's `/lol-game-settings/v1/game-settings` answer: sections of
    /// typed values (`{"HUD": {"GlobalScale": 0.29, "MirroredScoreboard":
    /// false}}`). A value missing or out of range keeps Riot's default; an
    /// answer with no HUD section at all is not one this build can read.
    pub fn from_game_settings(body: &str) -> Option<Self> {
        let value: Value = serde_json::from_str(body).ok()?;
        let hud = value.get("HUD")?.as_object()?;
        let number = |key: &str| match hud.get(key)? {
            Value::Number(n) => n.as_f64(),
            Value::String(s) => s.trim().parse().ok(),
            Value::Bool(b) => Some(f64::from(u8::from(*b))),
            _ => None,
        };
        Some(Self {
            global_scale: number("GlobalScale")
                .filter(|scale| (0.0..=1.0).contains(scale))
                .unwrap_or(Self::DEFAULTS.global_scale),
            mirrored: number("MirroredScoreboard").is_some_and(|on| on >= 0.5),
            own: true,
        })
    }
}

impl Default for HudSettings {
    fn default() -> Self {
        Self::DEFAULTS
    }
}

/// The scoreboard's rows in a game window, in the window's own unit.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct ScoreboardRows {
    /// The board's middle, across.
    pub centre_x: f64,
    /// The first row's top edge.
    pub top: f64,
    /// One row's height.
    pub pitch: f64,
    /// The board's width.
    pub width: f64,
}

impl ScoreboardRows {
    /// The middle of row `index` (0 = top lane), down.
    pub fn row_centre(&self, index: usize) -> f64 {
        self.top + self.pitch * (index as f64 + 0.5)
    }
}

/// Where the scoreboard's rows sit in a game window `frame`, the player's
/// nudge (reference pixels, up when negative) included.
pub fn rows(frame: Rect, hud: &HudSettings, nudge: f64) -> ScoreboardRows {
    let unit = (frame.height / REFERENCE_HEIGHT).min(frame.width / REFERENCE_WIDTH);
    let growth = 1.0 + HUD_GROWTH * hud.global_scale.clamp(0.0, 1.0);
    let pitch = ROW_PITCH_AT_ZERO * growth * unit;
    let bottom = frame.y
        + frame.height / 2.0
        + (ROWS_BOTTOM_BELOW_MIDDLE + nudge.clamp(-MAX_NUDGE, MAX_NUDGE)) * unit;
    ScoreboardRows {
        centre_x: frame.x + frame.width / 2.0,
        top: bottom - pitch * ROWS as f64,
        pitch,
        width: BOARD_WIDTH_AT_ZERO * growth * unit,
    }
}

/// The lane-gold panel's window over `frame`: `width` wide, centred on the
/// board, as tall as its five rows, in whole points.
pub fn plates_rect(frame: Rect, hud: &HudSettings, nudge: f64, width: f64) -> Rect {
    let rows = rows(frame, hud, nudge);
    Rect {
        x: (rows.centre_x - width / 2.0).round(),
        y: rows.top.round(),
        width: width.round(),
        height: (rows.pitch * ROWS as f64).round(),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const fn frame(width: f64, height: f64) -> Rect {
        Rect {
            x: 0.0,
            y: 0.0,
            width,
            height,
        }
    }

    fn hud(global_scale: f64) -> HudSettings {
        HudSettings {
            global_scale,
            ..HudSettings::DEFAULTS
        }
    }

    fn close(a: f64, b: f64) -> bool {
        (a - b).abs() < 0.5
    }

    #[test]
    fn matches_the_measured_1080p_scoreboard() {
        // The screenshot the constants come from: rows 309 – 693, 76.8 each, at about 0.3.
        let rows = rows(frame(1920.0, 1080.0), &hud(0.3), 0.0);
        assert!(close(rows.centre_x, 960.0));
        assert!(close(rows.pitch, 76.8), "{}", rows.pitch);
        assert!(close(rows.top, 308.8), "{}", rows.top);
        assert!(close(rows.row_centre(4), 654.4));
        assert!(close(rows.width, 1152.0), "{}", rows.width);
    }

    #[test]
    fn grows_upwards_from_a_fixed_bottom_with_the_hud_scale() {
        let small = rows(frame(1920.0, 1080.0), &hud(0.0), 0.0);
        let large = rows(frame(1920.0, 1080.0), &hud(1.0), 0.0);
        assert!(close(small.pitch, 66.4));
        assert!(close(large.pitch, 66.4 * 1.52));
        let bottom = |r: ScoreboardRows| r.top + r.pitch * ROWS as f64;
        assert!(close(bottom(small), bottom(large)));
        assert!(large.top < small.top);
    }

    #[test]
    fn scales_with_the_height_on_every_aspect_ratio() {
        // (width, height, centre x, first row's top, pitch) at Riot's default scale.
        let table = [
            (1920.0, 1080.0, 960.0, 360.8, 66.4),
            (2560.0, 1440.0, 1280.0, 481.1, 88.5),
            (3840.0, 2160.0, 1920.0, 721.6, 132.8),
            // Ultrawide: the height sets the size, the board stays in the middle.
            (3440.0, 1440.0, 1720.0, 481.1, 88.5),
            // 16:10 (a MacBook's points): narrower than 16:9, the width sets it.
            (1512.0, 982.0, 756.0, 349.9, 52.3),
        ];
        for (width, height, centre, top, pitch) in table {
            let rows = rows(frame(width, height), &HudSettings::DEFAULTS, 0.0);
            assert!(
                close(rows.centre_x, centre),
                "{width}x{height}: {}",
                rows.centre_x
            );
            assert!(close(rows.top, top), "{width}x{height}: {}", rows.top);
            assert!(close(rows.pitch, pitch), "{width}x{height}: {}", rows.pitch);
        }
    }

    #[test]
    fn follows_the_game_window_and_the_nudge() {
        let window = Rect {
            x: 100.0,
            y: 50.0,
            width: 1280.0,
            height: 720.0,
        };
        let rows_at = |nudge| rows(window, &HudSettings::DEFAULTS, nudge);
        assert!(close(rows_at(0.0).centre_x, 740.0));
        // A nudge is in reference pixels: half of it on a 720p window, and capped.
        assert!(close(rows_at(-20.0).top, rows_at(0.0).top - 13.3));
        assert!(close(rows_at(500.0).top, rows_at(MAX_NUDGE).top));
    }

    #[test]
    fn the_plates_window_spans_the_five_rows_on_the_middle() {
        let rect = plates_rect(frame(1920.0, 1080.0), &hud(0.3), 0.0, 56.0);
        assert_eq!(
            rect,
            Rect {
                x: 932.0,
                y: 309.0,
                width: 56.0,
                height: 384.0
            }
        );
    }

    #[test]
    fn reads_the_clients_hud_settings() {
        let read = HudSettings::from_game_settings(
            r#"{"General":{"WindowMode":2},"HUD":{"GlobalScale":0.29,"MirroredScoreboard":true,"ShowSummonerNamesInScoreboard":true}}"#,
        )
        .unwrap();
        assert_eq!(
            read,
            HudSettings {
                global_scale: 0.29,
                mirrored: true,
                own: true
            }
        );
        // The game's own config spells them as strings.
        let strings = HudSettings::from_game_settings(
            r#"{"HUD":{"GlobalScale":"0.5000","MirroredScoreboard":"0"}}"#,
        )
        .unwrap();
        assert_eq!(strings.global_scale, 0.5);
        assert!(!strings.mirrored);
        // Out of range or missing: Riot's default, still the player's file.
        let odd = HudSettings::from_game_settings(r#"{"HUD":{"GlobalScale":7}}"#).unwrap();
        assert_eq!(odd.global_scale, 0.0);
        assert!(odd.own);
        assert_eq!(HudSettings::from_game_settings(r#"{"General":{}}"#), None);
        assert_eq!(HudSettings::from_game_settings("not json"), None);
    }
}
