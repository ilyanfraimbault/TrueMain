//! Which second of the video a second of the game is.
//!
//! The highlights are in game time; the player seeks in video time. While a
//! game records, the shell reads the game clock (Live Client Data) and notes
//! how long the video had been recording just before and just after the read.
//! Each such sample says where game time zero sits in the video — the offset —
//! and the anchor is fitted from all of them.
//!
//! The offset is not one number for the whole game. A paused game stops the
//! game clock while the video runs on, and a crash and reconnect does the
//! opposite; either way the offset moves once and holds again. So the fit is a
//! list of segments, each with the median offset of its samples, and a new
//! segment only opens when several samples in a row agree on a new offset — a
//! single read that came back late is noise, not a pause.

use serde::{Deserialize, Serialize};

/// A read slower than this round trip says little about when the clock was
/// read, and is dropped.
const MAX_ROUND_TRIP_MS: u64 = 250;

/// An offset this far from its segment's median is not the same segment.
const SHIFT_MS: i64 = 1_000;

/// Samples in a row that must agree on a new offset before it is believed.
const SHIFT_CONFIRMATION: usize = 2;

/// One reading of the game clock, bracketed by the video clock.
#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ClockSample {
    /// Milliseconds of video recorded when the request went out.
    pub video_before_ms: u64,
    /// Milliseconds of video recorded when the answer came back.
    pub video_after_ms: u64,
    /// The game clock the answer carried, in seconds.
    pub game_time_s: f64,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Segment {
    /// The game time this segment starts at.
    pub from_game_ms: i64,
    /// Where game time zero sits in the video, for this segment.
    pub offset_ms: i64,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Anchor {
    /// Never empty; ordered by `from_game_ms`.
    pub segments: Vec<Segment>,
}

impl Anchor {
    /// Fit the anchor from the samples of one game. `None` when no sample is
    /// usable — a game the clock was never read during.
    pub fn fit(samples: &[ClockSample]) -> Option<Self> {
        let mut points: Vec<(i64, i64)> = samples
            .iter()
            .filter(|s| s.game_time_s > 0.0 && s.game_time_s.is_finite())
            .filter(|s| {
                s.video_after_ms >= s.video_before_ms
                    && s.video_after_ms - s.video_before_ms <= MAX_ROUND_TRIP_MS
            })
            .map(|s| {
                let game_ms = (s.game_time_s * 1000.0).round() as i64;
                let video_ms = ((s.video_before_ms + s.video_after_ms) / 2) as i64;
                (game_ms, video_ms - game_ms)
            })
            .collect();
        points.sort_by_key(|(game_ms, _)| *game_ms);

        let mut segments: Vec<(i64, Vec<i64>)> = Vec::new();
        let mut pending: Vec<(i64, i64)> = Vec::new();
        for (game_ms, offset) in points {
            let Some((_, offsets)) = segments.last_mut() else {
                segments.push((game_ms, vec![offset]));
                continue;
            };
            if (offset - median(offsets)).abs() <= SHIFT_MS {
                offsets.push(offset);
                pending.clear();
                continue;
            }
            if pending
                .first()
                .is_some_and(|(_, first)| (offset - first).abs() > SHIFT_MS)
            {
                pending.clear();
            }
            pending.push((game_ms, offset));
            if pending.len() >= SHIFT_CONFIRMATION {
                let mut from = pending[0].0;
                // A segment that never got confirmed itself — a lone first
                // read that was off — is replaced, not kept.
                if offsets.len() < SHIFT_CONFIRMATION {
                    from = segments.pop().map_or(from, |(start, _)| start);
                }
                segments.push((from, pending.drain(..).map(|(_, o)| o).collect()));
            }
        }

        let segments: Vec<Segment> = segments
            .into_iter()
            .map(|(from_game_ms, offsets)| Segment {
                from_game_ms,
                offset_ms: median(&offsets),
            })
            .collect();
        (!segments.is_empty()).then_some(Self { segments })
    }

    /// The video timestamp of a moment of the game, never before the video's
    /// start.
    pub fn video_ms(&self, game_ms: i64) -> i64 {
        let segment = self
            .segments
            .iter()
            .rev()
            .find(|segment| segment.from_game_ms <= game_ms)
            .unwrap_or(&self.segments[0]);
        (game_ms + segment.offset_ms).max(0)
    }
}

fn median(values: &[i64]) -> i64 {
    let mut sorted = values.to_vec();
    sorted.sort_unstable();
    sorted[sorted.len() / 2]
}

#[cfg(test)]
mod tests {
    use super::*;

    /// A read at `video_ms` of the video, `game_s` into the game, taking
    /// `round_trip` milliseconds.
    fn sample(video_ms: u64, game_s: f64, round_trip: u64) -> ClockSample {
        ClockSample {
            video_before_ms: video_ms,
            video_after_ms: video_ms + round_trip,
            game_time_s: game_s,
        }
    }

    /// Reads every five seconds of a game whose clock started 12.4 s into the
    /// video, with a few milliseconds of jitter.
    fn steady(from_s: u64, to_s: u64, offset_ms: u64) -> Vec<ClockSample> {
        (from_s..to_s)
            .step_by(5)
            .enumerate()
            .map(|(i, s)| sample(s * 1000 + offset_ms, s as f64, 4 + (i as u64 % 3) * 7))
            .collect()
    }

    #[test]
    fn a_steady_game_is_one_segment() {
        let anchor = Anchor::fit(&steady(5, 1800, 12_400)).unwrap();
        assert_eq!(anchor.segments.len(), 1);
        // Within the jitter of the midpoint.
        assert!((anchor.segments[0].offset_ms - 12_407).abs() <= 10);
        assert!((anchor.video_ms(600_000) - 612_407).abs() <= 10);
    }

    #[test]
    fn a_late_answer_is_noise_not_a_pause() {
        let mut samples = steady(5, 600, 12_400);
        // The answer came back within the round-trip limit but read a clock
        // far off the others.
        samples.push(sample(300_000 + 12_400 + 3_000, 300.5, 10));
        let anchor = Anchor::fit(&samples).unwrap();
        assert_eq!(anchor.segments.len(), 1);
    }

    #[test]
    fn slow_reads_and_the_loading_screen_are_dropped() {
        let mut samples = vec![
            // Loading screen: the clock has not started.
            sample(4_000, 0.0, 5),
            // Took a second to answer.
            sample(20_000, 2.0, 1_000),
        ];
        samples.extend(steady(5, 100, 12_400));
        let anchor = Anchor::fit(&samples).unwrap();
        assert_eq!(anchor.segments.len(), 1);
        assert!((anchor.segments[0].offset_ms - 12_407).abs() <= 10);
    }

    #[test]
    fn a_pause_opens_a_new_segment() {
        // The game paused for 90 s at 10:00: the video ran on, so from then
        // on game time zero sits 90 s further into the video.
        let mut samples = steady(5, 600, 12_400);
        samples.extend(steady(600, 1200, 102_400));
        let anchor = Anchor::fit(&samples).unwrap();

        assert_eq!(anchor.segments.len(), 2);
        assert!((anchor.video_ms(300_000) - 312_407).abs() <= 10);
        assert!((anchor.video_ms(900_000) - 1_002_407).abs() <= 10);
    }

    #[test]
    fn a_lone_first_read_that_was_off_is_replaced() {
        let mut samples = vec![sample(5_000 + 40_000, 5.0, 5)];
        samples.extend(steady(10, 300, 12_400));
        let anchor = Anchor::fit(&samples).unwrap();

        assert_eq!(anchor.segments.len(), 1);
        assert_eq!(anchor.segments[0].from_game_ms, 5_000);
        assert!((anchor.segments[0].offset_ms - 12_407).abs() <= 10);
    }

    #[test]
    fn a_moment_before_the_first_read_uses_the_first_segment() {
        let anchor = Anchor::fit(&steady(60, 300, 12_400)).unwrap();
        assert!((anchor.video_ms(1_000) - 13_407).abs() <= 10);
    }

    #[test]
    fn never_before_the_video_starts() {
        // The recording started after the game clock did.
        let anchor = Anchor::fit(&[sample(1_000, 30.0, 4), sample(6_000, 35.0, 4)]).unwrap();
        assert_eq!(anchor.video_ms(2_000), 0);
    }

    #[test]
    fn no_usable_sample_is_no_anchor() {
        assert_eq!(Anchor::fit(&[]), None);
        assert_eq!(Anchor::fit(&[sample(4_000, 0.0, 5)]), None);
    }
}
