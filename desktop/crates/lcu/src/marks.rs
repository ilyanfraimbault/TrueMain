//! Each player's standing at the timeline's canonical marks — 5, 10, 15, 20 and
//! 30 minutes — the timeline half of the site's performance score (#2075).
//!
//! Read by the ingestor's own rule (`TimelineSnapshotBuilder.SelectFrame`), so
//! a game TrueMain holds gets the same marks from either side: the frame
//! nearest the mark, only within half a minute of it. A game over before a mark
//! has no frame near it and simply lacks that mark — never a zeroed one. The
//! lead over the lane opponent is the reader's to take, as on the site.

use std::collections::HashMap;

use serde::{Deserialize, Serialize};

use crate::detail::{GameTimeline, TimelineFrame};

/// The canonical marks, in minutes — `TimelineSnapshotMarks.Minutes` on the site.
pub const MARK_MINUTES: [i64; 5] = [5, 10, 15, 20, 30];

/// A mark is only read from a frame this close to it.
const FRAME_TOLERANCE_MS: i64 = 30_000;

/// One player at one mark.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct TimelineMark {
    pub minute: i64,
    pub gold: i64,
    /// Lane minions and jungle camps together.
    pub cs: i64,
    pub xp: i64,
}

/// Every player's marks, keyed by participant id, in ascending minutes.
pub fn marks_by_participant(timeline: &GameTimeline) -> HashMap<i64, Vec<TimelineMark>> {
    let mut by_participant: HashMap<i64, Vec<TimelineMark>> = HashMap::new();
    for minute in MARK_MINUTES {
        let Some(frame) = frame_near(&timeline.frames, minute * 60_000) else {
            continue;
        };
        for (id, player) in &frame.participant_frames {
            let Ok(id) = id.parse::<i64>() else { continue };
            by_participant.entry(id).or_default().push(TimelineMark {
                minute,
                gold: player.total_gold,
                cs: player.minions_killed + player.jungle_minions_killed,
                xp: player.xp,
            });
        }
    }
    by_participant
}

/// The frame nearest `target_ms`, when one sits within the tolerance. Frames
/// come in ascending time, so the distance falls then rises: the walk stops
/// once it grows, and of two frames equally near the later one is kept — the
/// ingestor's walk, step for step.
fn frame_near(frames: &[TimelineFrame], target_ms: i64) -> Option<&TimelineFrame> {
    let mut best = None;
    let mut best_delta = i64::MAX;
    for frame in frames {
        let delta = (frame.timestamp - target_ms).abs();
        if delta > best_delta {
            break;
        }
        best_delta = delta;
        best = Some(frame);
    }
    best.filter(|_| best_delta <= FRAME_TOLERANCE_MS)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn timeline(json: &str) -> GameTimeline {
        serde_json::from_str(json).unwrap()
    }

    #[test]
    fn reads_each_mark_from_the_frame_nearest_it() {
        let marks = marks_by_participant(&timeline(
            r#"{ "frames": [
              { "timestamp": 0, "participantFrames": { "1": { "totalGold": 500 } } },
              { "timestamp": 300040, "participantFrames": {
                "1": { "totalGold": 1800, "xp": 1500, "minionsKilled": 40, "jungleMinionsKilled": 2 },
                "6": { "totalGold": 1600, "xp": 1400, "minionsKilled": 35 } } },
              { "timestamp": 600050, "participantFrames": { "1": { "totalGold": 3600, "xp": 3900, "minionsKilled": 80 } } }
            ] }"#,
        ));
        assert_eq!(
            marks[&1],
            vec![
                TimelineMark {
                    minute: 5,
                    gold: 1800,
                    cs: 42,
                    xp: 1500
                },
                TimelineMark {
                    minute: 10,
                    gold: 3600,
                    cs: 80,
                    xp: 3900
                },
            ]
        );
        assert_eq!(marks[&6].len(), 1);
    }

    #[test]
    fn a_game_over_before_a_mark_lacks_it() {
        // The last frame is the game's end: 29:40 still stands for the 30, 19:20 does not stand for the 20.
        let ends_at = |ms: i64| {
            timeline(&format!(
                r#"{{ "frames": [ {{ "timestamp": {ms}, "participantFrames": {{ "1": {{ "totalGold": 9000 }} }} }} ] }}"#
            ))
        };
        let near = marks_by_participant(&ends_at(1_780_000));
        assert_eq!(
            near[&1].iter().map(|m| m.minute).collect::<Vec<_>>(),
            vec![30]
        );
        assert!(marks_by_participant(&ends_at(1_160_000)).is_empty());
    }

    #[test]
    fn of_two_frames_equally_near_the_later_is_read() {
        let marks = marks_by_participant(&timeline(
            r#"{ "frames": [
              { "timestamp": 290000, "participantFrames": { "1": { "totalGold": 1 } } },
              { "timestamp": 310000, "participantFrames": { "1": { "totalGold": 2 } } }
            ] }"#,
        ));
        assert_eq!(marks[&1][0].gold, 2);
    }
}
