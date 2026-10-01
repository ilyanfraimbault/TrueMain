//! Everything on a recording's timeline: the player's highlights and the
//! game's objectives, mapped from game time onto the video (#1777).
//!
//! The objectives — epic monsters and buildings, whichever team took them —
//! come from the game's timeline only: the live feed names monsters but not
//! always who took them, and a recap shown with a wrong side is worse than one
//! without. They are read once the match history has the game, like the
//! timeline's highlights.

use lcu::detail::GameTimeline;
use serde::{Deserialize, Serialize};

use crate::anchor::Anchor;
use crate::highlights::{Highlight, HighlightKind};

/// Void grubs come in a camp of three: kills of the same side this close
/// together are one moment.
const GRUBS_WINDOW_MS: i64 = 60_000;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum ObjectiveKind {
    Dragon,
    Elder,
    Baron,
    Herald,
    Grubs,
    Atakhan,
    Tower,
    Inhibitor,
}

/// An objective taken, in game time.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Objective {
    pub kind: ObjectiveKind,
    pub game_time_ms: i64,
    /// Whether the player's team took it.
    pub ally: bool,
}

/// The game's objectives from its timeline. `my_team` is the player's team id
/// (100 or 200); a participant id from 1 to 5 plays for 100.
pub fn objectives(timeline: &GameTimeline, my_team: i64) -> Vec<Objective> {
    let team_of = |participant: i64| match participant {
        1..=5 => Some(100),
        6..=10 => Some(200),
        _ => None,
    };
    let mut objectives: Vec<Objective> = Vec::new();
    for event in timeline.frames.iter().flat_map(|frame| &frame.events) {
        let (kind, taker) = match event.kind.as_str() {
            "ELITE_MONSTER_KILL" => {
                let kind = match (event.monster_type.as_str(), event.monster_sub_type.as_str()) {
                    ("DRAGON", "ELDER_DRAGON") => ObjectiveKind::Elder,
                    ("DRAGON", _) => ObjectiveKind::Dragon,
                    ("BARON_NASHOR", _) => ObjectiveKind::Baron,
                    ("RIFTHERALD", _) => ObjectiveKind::Herald,
                    ("HORDE", _) => ObjectiveKind::Grubs,
                    ("ATAKHAN", _) => ObjectiveKind::Atakhan,
                    _ => continue,
                };
                let taker = Some(event.killer_team_id)
                    .filter(|team| *team > 0)
                    .or_else(|| team_of(event.killer_id));
                (kind, taker)
            }
            "BUILDING_KILL" => {
                let kind = match event.building_type.as_str() {
                    "TOWER_BUILDING" => ObjectiveKind::Tower,
                    "INHIBITOR_BUILDING" => ObjectiveKind::Inhibitor,
                    _ => continue,
                };
                // The event names the team that lost it.
                let taker = match event.team_id {
                    100 => Some(200),
                    200 => Some(100),
                    _ => None,
                };
                (kind, taker)
            }
            _ => continue,
        };
        // A side the timeline does not give is not guessed.
        let Some(taker) = taker else { continue };
        let ally = taker == my_team;
        let same_camp = objectives
            .iter()
            .rev()
            .find(|o| o.kind == ObjectiveKind::Grubs);
        if kind == ObjectiveKind::Grubs
            && same_camp.is_some_and(|o| {
                o.ally == ally && event.timestamp - o.game_time_ms <= GRUBS_WINDOW_MS
            })
        {
            continue;
        }
        objectives.push(Objective {
            kind,
            game_time_ms: event.timestamp,
            ally,
        });
    }
    objectives.sort_by_key(|objective| objective.game_time_ms);
    objectives
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum MomentKind {
    Kill,
    Death,
    Assist,
    Dragon,
    Elder,
    Baron,
    Herald,
    Grubs,
    Atakhan,
    Tower,
    Inhibitor,
}

/// One moment on a video's timeline.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Moment {
    pub kind: MomentKind,
    pub video_ms: i64,
    /// The last kill of a multi-kill; `video_ms` otherwise.
    pub end_video_ms: i64,
    /// Kills in a row for a kill, 0 otherwise.
    pub kills: u8,
    /// Objectives only: whether the player's team took it.
    pub ally: Option<bool>,
}

impl Moment {
    /// The same moment on a clip that starts at `start_ms` of the video.
    pub fn shifted(self, start_ms: i64) -> Self {
        Self {
            video_ms: self.video_ms - start_ms,
            end_video_ms: self.end_video_ms - start_ms,
            ..self
        }
    }
}

/// The highlights and objectives on the video, in order. Nothing without an
/// anchor: a moment placed on the wrong second is worse than none.
pub fn on_video(
    anchor: Option<&Anchor>,
    highlights: &[Highlight],
    objectives: &[Objective],
    duration_ms: Option<u64>,
) -> Vec<Moment> {
    let Some(anchor) = anchor else {
        return Vec::new();
    };
    let last = duration_ms.and_then(|ms| i64::try_from(ms).ok());
    let place = |game_ms: i64| {
        let video = anchor.video_ms(game_ms);
        last.map_or(video, |last| video.min(last))
    };
    let mut moments: Vec<Moment> = highlights
        .iter()
        .map(|highlight| Moment {
            kind: match highlight.kind {
                HighlightKind::Kill => MomentKind::Kill,
                HighlightKind::Death => MomentKind::Death,
                HighlightKind::Assist => MomentKind::Assist,
            },
            video_ms: place(highlight.game_time_ms),
            end_video_ms: place(highlight.end_game_time_ms),
            kills: highlight.kills,
            ally: None,
        })
        .chain(objectives.iter().map(|objective| {
            let video_ms = place(objective.game_time_ms);
            Moment {
                kind: match objective.kind {
                    ObjectiveKind::Dragon => MomentKind::Dragon,
                    ObjectiveKind::Elder => MomentKind::Elder,
                    ObjectiveKind::Baron => MomentKind::Baron,
                    ObjectiveKind::Herald => MomentKind::Herald,
                    ObjectiveKind::Grubs => MomentKind::Grubs,
                    ObjectiveKind::Atakhan => MomentKind::Atakhan,
                    ObjectiveKind::Tower => MomentKind::Tower,
                    ObjectiveKind::Inhibitor => MomentKind::Inhibitor,
                },
                video_ms,
                end_video_ms: video_ms,
                kills: 0,
                ally: Some(objective.ally),
            }
        }))
        .collect();
    moments.sort_by_key(|moment| moment.video_ms);
    moments
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::anchor::ClockSample;

    fn timeline() -> GameTimeline {
        serde_json::from_str(
            r#"{ "frames": [{ "timestamp": 0, "participantFrames": {}, "events": [
                 { "type": "ELITE_MONSTER_KILL", "timestamp": 300000, "killerId": 2, "monsterType": "HORDE" },
                 { "type": "ELITE_MONSTER_KILL", "timestamp": 320000, "killerId": 3, "monsterType": "HORDE" },
                 { "type": "ELITE_MONSTER_KILL", "timestamp": 330000, "killerId": 2, "monsterType": "HORDE" },
                 { "type": "ELITE_MONSTER_KILL", "timestamp": 600000, "killerId": 7, "monsterType": "DRAGON", "monsterSubType": "FIRE_DRAGON" },
                 { "type": "BUILDING_KILL", "timestamp": 700000, "killerId": 4, "teamId": 200, "buildingType": "TOWER_BUILDING" },
                 { "type": "BUILDING_KILL", "timestamp": 710000, "killerId": 0, "teamId": 100, "buildingType": "INHIBITOR_BUILDING" },
                 { "type": "ELITE_MONSTER_KILL", "timestamp": 1500000, "killerId": 1, "monsterType": "DRAGON", "monsterSubType": "ELDER_DRAGON" },
                 { "type": "ELITE_MONSTER_KILL", "timestamp": 1600000, "killerId": 0, "monsterType": "BARON_NASHOR" },
                 { "type": "ELITE_MONSTER_KILL", "timestamp": 1700000, "killerId": 0, "killerTeamId": 100, "monsterType": "BARON_NASHOR" },
                 { "type": "WARD_PLACED", "timestamp": 1800000 }
               ] }] }"#,
        )
        .unwrap()
    }

    #[test]
    fn objectives_name_the_side_that_took_them() {
        let found = objectives(&timeline(), 100);
        let summary: Vec<(ObjectiveKind, i64, bool)> = found
            .iter()
            .map(|o| (o.kind, o.game_time_ms / 1000, o.ally))
            .collect();
        assert_eq!(
            summary,
            vec![
                // One camp of grubs, not three.
                (ObjectiveKind::Grubs, 300, true),
                (ObjectiveKind::Dragon, 600, false),
                // A building names the side that lost it.
                (ObjectiveKind::Tower, 700, true),
                (ObjectiveKind::Inhibitor, 710, false),
                (ObjectiveKind::Elder, 1500, true),
                // The Baron with no killer and no team is left out.
                (ObjectiveKind::Baron, 1700, true),
            ]
        );
        assert!(!objectives(&timeline(), 200)[0].ally);
    }

    #[test]
    fn moments_land_on_the_video_in_order() {
        // The video runs 20 s ahead of the game clock.
        let samples: Vec<ClockSample> = [30u64, 60, 90]
            .iter()
            .map(|s| ClockSample {
                video_before_ms: s * 1000 + 20_000,
                video_after_ms: s * 1000 + 20_004,
                game_time_s: *s as f64,
            })
            .collect();
        let anchor = Anchor::fit(&samples).unwrap();
        let highlights = [Highlight {
            kind: HighlightKind::Kill,
            game_time_ms: 95_000,
            end_game_time_ms: 99_000,
            kills: 2,
            killer_id: None,
            victim_ids: vec![],
        }];
        let objectives = [Objective {
            kind: ObjectiveKind::Dragon,
            game_time_ms: 60_000,
            ally: true,
        }];
        let moments = on_video(Some(&anchor), &highlights, &objectives, Some(110_000));
        assert_eq!(moments[0].kind, MomentKind::Dragon);
        assert_eq!(moments[1].kind, MomentKind::Kill);
        assert_eq!(moments[1].kills, 2);
        let near = |ms: i64, at: i64| (ms - at).abs() <= 5;
        assert!(near(moments[0].video_ms, 80_000), "{moments:?}");
        assert!(
            // Game 95 s is video 115 s, held to the video's 110 s.
            near(moments[1].video_ms, 110_000),
            "{moments:?}"
        );
        assert_eq!(moments[1].ally, None);
        assert!(on_video(None, &highlights, &objectives, None).is_empty());
        assert_eq!(
            moments[0].shifted(70_000).video_ms,
            moments[0].video_ms - 70_000
        );
    }
}
