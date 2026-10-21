//! The moments the player marked with the mark key while the game recorded
//! (#2079): a press notes the second of the video, and once the video is
//! closed each mark becomes a clip of the length chosen in the settings
//! before it — 30 s, a minute or two (#1767's instant replay) — a saved clip
//! like any other, so the disk budget never deletes it.
//!
//! A mark is the video's own clock, read from the capture when the key was
//! pressed: it needs no anchor to land on the right second, unlike the
//! highlights and objectives, which are in game time.

use serde::{Deserialize, Serialize};

use crate::anchor::Anchor;

/// How much of the game before the press a mark's clip holds by default —
/// and for a mark kept before the length could be chosen.
pub const MARK_WINDOW_MS: u64 = 30_000;

/// How much of the game before a press its clip keeps: the short list of
/// #1767, saved in the settings as seconds.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default, Serialize, Deserialize)]
#[serde(try_from = "u32", into = "u32")]
pub enum MarkLength {
    #[default]
    Seconds30,
    Minute1,
    Minutes2,
}

impl MarkLength {
    pub fn seconds(self) -> u32 {
        match self {
            Self::Seconds30 => 30,
            Self::Minute1 => 60,
            Self::Minutes2 => 120,
        }
    }

    pub fn millis(self) -> u64 {
        u64::from(self.seconds()) * 1000
    }

    /// "30 s", "1 min", "2 min".
    pub fn label(self) -> &'static str {
        match self {
            Self::Seconds30 => "30 s",
            Self::Minute1 => "1 min",
            Self::Minutes2 => "2 min",
        }
    }

    /// What the overlay says once a press is kept.
    pub fn saved(self) -> String {
        format!("Last {} saved", self.label())
    }
}

impl TryFrom<u32> for MarkLength {
    type Error = String;

    fn try_from(seconds: u32) -> Result<Self, Self::Error> {
        match seconds {
            30 => Ok(Self::Seconds30),
            60 => Ok(Self::Minute1),
            120 => Ok(Self::Minutes2),
            other => Err(format!("{other} s is not offered")),
        }
    }
}

impl From<MarkLength> for u32 {
    fn from(length: MarkLength) -> Self {
        length.seconds()
    }
}

/// Two presses closer than this are one: a key held a little long, or pressed
/// twice by reflex.
pub const MIN_MARK_GAP_MS: u64 = 1_000;

/// A clip shorter than this is not cut: the press came on the video's very
/// first second.
const MIN_CLIP_MS: u64 = 1_000;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Mark {
    /// The second of the video the key was pressed at.
    pub video_ms: u64,
    /// How much before the press its clip keeps, as chosen when the key was
    /// pressed; [`MARK_WINDOW_MS`] for a mark kept before it could be.
    #[serde(default = "default_window_ms")]
    pub window_ms: u64,
}

fn default_window_ms() -> u64 {
    MARK_WINDOW_MS
}

impl Mark {
    /// The range of the video its clip covers: its `window_ms` before the
    /// press, held inside the video. `None` when that leaves less than a
    /// second.
    pub fn window(self, duration_ms: Option<u64>) -> Option<(u64, u64)> {
        let end = duration_ms.map_or(self.video_ms, |duration| self.video_ms.min(duration));
        let start = end.saturating_sub(self.window_ms);
        (end >= start + MIN_CLIP_MS).then_some((start, end))
    }

    /// Its clip's title: the champion the player was on and the game clock at
    /// the press, the one they saw in game ("Ahri at 12:34") — or the video's
    /// own when the game clock was never tied to it. "Marked" stands in for
    /// a champion the game never named.
    pub fn title(self, anchor: Option<&Anchor>, champion: Option<&str>) -> String {
        let who = champion
            .map(str::trim)
            .filter(|name| !name.is_empty())
            .unwrap_or("Marked");
        let video_ms = i64::try_from(self.video_ms).unwrap_or(i64::MAX);
        match anchor {
            Some(anchor) => format!("{who} at {}", clock(anchor.game_ms(video_ms))),
            None => format!("{who} at {} of the video", clock(video_ms)),
        }
    }
}

/// Whether a press at `video_ms` adds a mark to `marks`, or repeats the last one.
pub fn is_new(marks: &[Mark], video_ms: u64) -> bool {
    marks
        .last()
        .is_none_or(|last| video_ms.abs_diff(last.video_ms) >= MIN_MARK_GAP_MS)
}

/// `m:ss`, or `h:mm:ss` past the hour.
fn clock(ms: i64) -> String {
    let seconds = ms.max(0) / 1000;
    let (hours, minutes, seconds) = (seconds / 3600, seconds / 60 % 60, seconds % 60);
    if hours > 0 {
        format!("{hours}:{minutes:02}:{seconds:02}")
    } else {
        format!("{minutes}:{seconds:02}")
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::anchor::Segment;

    fn mark(video_s: u64) -> Mark {
        Mark {
            video_ms: video_s * 1000,
            window_ms: MARK_WINDOW_MS,
        }
    }

    #[test]
    fn a_marks_clip_is_the_thirty_seconds_before_the_press() {
        assert_eq!(mark(754).window(Some(1_800_000)), Some((724_000, 754_000)));
        // Before the video's 30th second: from its start.
        assert_eq!(mark(12).window(None), Some((0, 12_000)));
        // Pressed on the last frame, read a moment after the video closed.
        assert_eq!(
            mark(1_801).window(Some(1_800_000)),
            Some((1_770_000, 1_800_000))
        );
        assert_eq!(
            Mark {
                video_ms: 400,
                window_ms: MARK_WINDOW_MS
            }
            .window(None),
            None
        );
    }

    #[test]
    fn a_marks_clip_keeps_the_length_chosen_at_the_press() {
        let two_minutes = Mark {
            window_ms: MarkLength::Minutes2.millis(),
            ..mark(754)
        };
        assert_eq!(
            two_minutes.window(Some(1_800_000)),
            Some((634_000, 754_000))
        );
        assert_eq!(two_minutes.window(None).map(|w| w.0), Some(634_000));
        // A mark kept before the length could be chosen is 30 s.
        let old: Mark = serde_json::from_str(r#"{ "videoMs": 60000 }"#).unwrap();
        assert_eq!(old.window(None), Some((30_000, 60_000)));
    }

    #[test]
    fn the_lengths_offered_are_saved_as_seconds() {
        assert_eq!(serde_json::to_value(MarkLength::Minute1).unwrap(), 60);
        assert_eq!(
            serde_json::from_str::<MarkLength>("120").unwrap(),
            MarkLength::Minutes2
        );
        assert!(serde_json::from_str::<MarkLength>("90").is_err());
        assert_eq!(MarkLength::default().millis(), MARK_WINDOW_MS);
        assert_eq!(MarkLength::Seconds30.saved(), "Last 30 s saved");
        assert_eq!(MarkLength::Minutes2.saved(), "Last 2 min saved");
    }

    #[test]
    fn a_marks_title_is_the_champion_and_the_game_clock_at_the_press() {
        // The video runs 20 s ahead of the game clock.
        let anchor = Anchor {
            segments: vec![Segment {
                from_game_ms: 0,
                offset_ms: 20_000,
            }],
        };
        assert_eq!(
            mark(774).title(Some(&anchor), Some("Ahri")),
            "Ahri at 12:34"
        );
        assert_eq!(
            mark(3_700).title(Some(&anchor), Some("Lee Sin")),
            "Lee Sin at 1:01:20"
        );
        assert_eq!(mark(774).title(Some(&anchor), None), "Marked at 12:34");
        assert_eq!(mark(774).title(Some(&anchor), Some(" ")), "Marked at 12:34");
        assert_eq!(
            mark(65).title(None, Some("Ahri")),
            "Ahri at 1:05 of the video"
        );
    }

    #[test]
    fn a_press_right_after_the_last_is_the_same_mark() {
        assert!(is_new(&[], 0));
        assert!(!is_new(&[mark(24)], 24_600));
        assert!(is_new(&[mark(24)], 25_000));
        assert!(is_new(&[mark(24), mark(33)], 60_000));
    }
}
