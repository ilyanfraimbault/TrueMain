//! The moments the player marked with the mark key while the game recorded
//! (#2079): a press notes the second of the video, and once the video is
//! closed each mark becomes a clip of the 30 seconds before it — a saved clip
//! like any other, so the disk budget never deletes it.
//!
//! A mark is the video's own clock, read from the capture when the key was
//! pressed: it needs no anchor to land on the right second, unlike the
//! highlights and objectives, which are in game time.

use serde::{Deserialize, Serialize};

use crate::anchor::Anchor;

/// How much of the game before the press a mark's clip holds.
pub const MARK_WINDOW_MS: u64 = 30_000;

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
}

impl Mark {
    /// The range of the video its clip covers: the [`MARK_WINDOW_MS`] before
    /// the press, held inside the video. `None` when that leaves less than a
    /// second.
    pub fn window(self, duration_ms: Option<u64>) -> Option<(u64, u64)> {
        let end = duration_ms.map_or(self.video_ms, |duration| self.video_ms.min(duration));
        let start = end.saturating_sub(MARK_WINDOW_MS);
        (end >= start + MIN_CLIP_MS).then_some((start, end))
    }

    /// Its clip's title: the game clock at the press, the one the player saw
    /// in game — or the video's own when the game clock was never tied to it.
    pub fn title(self, anchor: Option<&Anchor>) -> String {
        let video_ms = i64::try_from(self.video_ms).unwrap_or(i64::MAX);
        match anchor {
            Some(anchor) => format!("Marked at {}", clock(anchor.game_ms(video_ms))),
            None => format!("Marked at {} of the video", clock(video_ms)),
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
        assert_eq!(Mark { video_ms: 400 }.window(None), None);
    }

    #[test]
    fn a_marks_title_is_the_game_clock_at_the_press() {
        // The video runs 20 s ahead of the game clock.
        let anchor = Anchor {
            segments: vec![Segment {
                from_game_ms: 0,
                offset_ms: 20_000,
            }],
        };
        assert_eq!(mark(774).title(Some(&anchor)), "Marked at 12:34");
        assert_eq!(mark(3_700).title(Some(&anchor)), "Marked at 1:01:20");
        assert_eq!(mark(65).title(None), "Marked at 1:05 of the video");
    }

    #[test]
    fn a_press_right_after_the_last_is_the_same_mark() {
        assert!(is_new(&[], 0));
        assert!(!is_new(&[mark(24)], 24_600));
        assert!(is_new(&[mark(24)], 25_000));
        assert!(is_new(&[mark(24), mark(33)], 60_000));
    }
}
