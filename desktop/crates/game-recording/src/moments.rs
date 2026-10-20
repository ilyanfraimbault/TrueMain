//! Everything on a recording's timeline: the player's highlights and the
//! game's objectives, mapped from game time onto the video (#1777), and the
//! moments the player marked with the mark key, already in video time (#2079).
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
use crate::marks::Mark;

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

/// An elemental drake's element, from the timeline's `monsterSubType`.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum DragonKind {
    Infernal,
    Mountain,
    Ocean,
    Cloud,
    Hextech,
    Chemtech,
}

impl DragonKind {
    fn from_sub_type(sub_type: &str) -> Option<Self> {
        match sub_type {
            "FIRE_DRAGON" => Some(Self::Infernal),
            "EARTH_DRAGON" => Some(Self::Mountain),
            "WATER_DRAGON" => Some(Self::Ocean),
            "AIR_DRAGON" => Some(Self::Cloud),
            "HEXTECH_DRAGON" => Some(Self::Hextech),
            "CHEMTECH_DRAGON" => Some(Self::Chemtech),
            _ => None,
        }
    }
}

/// An objective taken, in game time.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Objective {
    pub kind: ObjectiveKind,
    pub game_time_ms: i64,
    /// Whether the player's team took it.
    pub ally: bool,
    /// An elemental drake's element. `None` for every other objective, and
    /// for drakes kept by a build that did not record it.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub dragon: Option<DragonKind>,
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
            dragon: (kind == ObjectiveKind::Dragon)
                .then(|| DragonKind::from_sub_type(&event.monster_sub_type))
                .flatten(),
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
    /// The player pressed the mark key.
    Marked,
}

/// One moment on a video's timeline. The participant ids name the players in
/// it (1 to 10, the game's timeline numbering); the recap draws their
/// champions from the recording's scoreboard. Moments kept by an older build,
/// and those read from the live feed, have none.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
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
    /// Who landed the kill, for a death or an assist; `None` for the player's
    /// own kills, an execution, or an objective.
    #[serde(default)]
    pub killer_id: Option<i64>,
    /// Who died: every victim of a multi-kill, the one of an assist.
    #[serde(default)]
    pub victim_ids: Vec<i64>,
    /// An elemental drake's element.
    #[serde(default)]
    pub dragon: Option<DragonKind>,
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

/// The highlights, objectives and marks on the video, in order. No highlight
/// or objective without an anchor — a moment placed on the wrong second is
/// worse than none — but the marks, read on the video's own clock, always.
pub fn on_video(
    anchor: Option<&Anchor>,
    highlights: &[Highlight],
    objectives: &[Objective],
    marks: &[Mark],
    duration_ms: Option<u64>,
) -> Vec<Moment> {
    let last = duration_ms.and_then(|ms| i64::try_from(ms).ok());
    let hold = |video: i64| last.map_or(video, |last| video.min(last));
    let marked = marks.iter().map(|mark| {
        let video_ms = hold(i64::try_from(mark.video_ms).unwrap_or(i64::MAX));
        Moment {
            kind: MomentKind::Marked,
            video_ms,
            end_video_ms: video_ms,
            kills: 0,
            ally: None,
            killer_id: None,
            victim_ids: Vec::new(),
            dragon: None,
        }
    });
    let Some(anchor) = anchor else {
        return marked.collect();
    };
    let place = |game_ms: i64| hold(anchor.video_ms(game_ms));
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
            killer_id: highlight.killer_id,
            victim_ids: highlight.victim_ids.clone(),
            dragon: None,
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
                killer_id: None,
                victim_ids: Vec::new(),
                dragon: objective.dragon,
            }
        }))
        .chain(marked)
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
        assert_eq!(found[1].dragon, Some(DragonKind::Infernal));
        assert_eq!(found[4].dragon, None);
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
            victim_ids: vec![7, 9],
        }];
        let objectives = [Objective {
            kind: ObjectiveKind::Dragon,
            game_time_ms: 60_000,
            ally: true,
            dragon: Some(DragonKind::Ocean),
        }];
        let moments = on_video(Some(&anchor), &highlights, &objectives, &[], Some(110_000));
        assert_eq!(moments[0].kind, MomentKind::Dragon);
        assert_eq!(moments[0].dragon, Some(DragonKind::Ocean));
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
        assert_eq!(moments[1].victim_ids, vec![7, 9]);
        assert!(on_video(None, &highlights, &objectives, &[], None).is_empty());
        assert_eq!(
            moments[0].clone().shifted(70_000).video_ms,
            moments[0].video_ms - 70_000
        );
    }

    #[test]
    fn marks_land_on_the_video_with_or_without_an_anchor() {
        let marks = [
            Mark { video_ms: 24_000 },
            Mark { video_ms: 33_000 },
            // Read a moment after the video closed.
            Mark { video_ms: 61_000 },
        ];
        let moments = on_video(None, &[], &[], &marks, Some(60_000));
        let at: Vec<(MomentKind, i64)> = moments.iter().map(|m| (m.kind, m.video_ms)).collect();
        assert_eq!(
            at,
            vec![
                (MomentKind::Marked, 24_000),
                (MomentKind::Marked, 33_000),
                (MomentKind::Marked, 60_000),
            ]
        );

        let anchor = Anchor {
            segments: vec![crate::anchor::Segment {
                from_game_ms: 0,
                offset_ms: 20_000,
            }],
        };
        let objectives = [Objective {
            kind: ObjectiveKind::Dragon,
            game_time_ms: 10_000,
            ally: true,
            dragon: None,
        }];
        let kinds: Vec<MomentKind> = on_video(Some(&anchor), &[], &objectives, &marks[..2], None)
            .iter()
            .map(|m| m.kind)
            .collect();
        assert_eq!(
            kinds,
            vec![MomentKind::Marked, MomentKind::Dragon, MomentKind::Marked]
        );
        assert_eq!(serde_json::to_value(MomentKind::Marked).unwrap(), "marked");
    }
}
