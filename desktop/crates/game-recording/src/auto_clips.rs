//! Automatic clips (#1766): the plays worth keeping, cut from the full game as
//! clips of their own once it is finalised — "Pentakill on Ahri", "Triple
//! kill into Baron", "Elder Dragon stolen", "Teamfight 3/0/1".
//!
//! Each play the player chose in the settings gives a window around it — a
//! lead-in before, a tail after —, in game time, placed on the video through
//! the recording's anchor; windows that overlap become one clip, titled after
//! its most telling play. A title only says what the events show: the
//! mechanics on screen (a flash, an insec) are read neither from the video nor
//! from the game's memory.

use serde::{Deserialize, Serialize};

use crate::highlights::{Highlight, HighlightKind};
use crate::moments::{DragonKind, Objective, ObjectiveKind};
use crate::plays::{Play, PlayKind};
use crate::store::RecordingMeta;

/// Before the first event of a play, so the clip shows it coming.
const LEAD_MS: i64 = 10_000;
/// A steal and an ace start further back: the fight around the pit, the fight
/// that ended in the ace.
const LONG_LEAD_MS: i64 = 20_000;
/// After its last event.
const TAIL_MS: i64 = 5_000;
/// A multi-kill and the objective its side takes this soon after it are one
/// play ("Triple kill into Baron").
const COMBO_WINDOW_MS: i64 = 45_000;
/// An ace is the player's play when they killed or assisted this soon before it.
const ACE_INVOLVEMENT_MS: i64 = 30_000;
/// A multi-kill is clipped from a triple kill: doubles come too often.
const MIN_MULTIKILL: u8 = 3;
/// A clip shorter than this is not cut.
const MIN_CLIP_MS: u64 = 1_000;

/// What the player chooses to clip automatically.
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Hash, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum AutoClipKind {
    /// A triple kill, a quadra kill, a pentakill.
    MultiKill,
    /// An epic monster the player's side stole.
    Steal,
    /// The player's side aced the other, the player in the fight.
    Ace,
    /// The player drew first blood.
    FirstBlood,
    /// A teamfight the player took part in.
    Teamfight,
    /// A multi-kill, then an objective taken.
    Combo,
}

impl AutoClipKind {
    pub const ALL: [Self; 6] = [
        Self::MultiKill,
        Self::Steal,
        Self::Ace,
        Self::FirstBlood,
        Self::Teamfight,
        Self::Combo,
    ];
}

/// One clip to cut, in video time.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct AutoClip {
    /// Every kind of play inside it, in [`AutoClipKind::ALL`]'s order.
    pub kinds: Vec<AutoClipKind>,
    pub title: String,
    pub start_ms: u64,
    pub end_ms: u64,
    /// The second its most telling play happens: its thumbnail.
    pub moment_ms: u64,
}

/// One play worth a clip, in game time.
#[derive(Debug, Clone, PartialEq, Eq)]
struct Candidate {
    kind: AutoClipKind,
    title: String,
    /// The higher, the more telling: the merged clip's title is the highest's.
    rank: u32,
    start_ms: i64,
    end_ms: i64,
    moment_ms: i64,
}

const MULTI_KILLS: [&str; 5] = [
    "Kill",
    "Double kill",
    "Triple kill",
    "Quadra kill",
    "Pentakill",
];

fn multi_kill(kills: u8) -> &'static str {
    MULTI_KILLS[usize::from(kills.clamp(1, 5)) - 1]
}

/// An objective as the recap names it ("Rift Herald", "Ocean Drake").
fn objective_name(kind: ObjectiveKind, dragon: Option<DragonKind>) -> &'static str {
    match (kind, dragon) {
        (ObjectiveKind::Dragon, Some(DragonKind::Infernal)) => "Infernal Drake",
        (ObjectiveKind::Dragon, Some(DragonKind::Mountain)) => "Mountain Drake",
        (ObjectiveKind::Dragon, Some(DragonKind::Ocean)) => "Ocean Drake",
        (ObjectiveKind::Dragon, Some(DragonKind::Cloud)) => "Cloud Drake",
        (ObjectiveKind::Dragon, Some(DragonKind::Hextech)) => "Hextech Drake",
        (ObjectiveKind::Dragon, Some(DragonKind::Chemtech)) => "Chemtech Drake",
        (ObjectiveKind::Dragon, None) => "Dragon",
        (ObjectiveKind::Elder, _) => "Elder Dragon",
        (ObjectiveKind::Baron, _) => "Baron",
        (ObjectiveKind::Herald, _) => "Rift Herald",
        (ObjectiveKind::Grubs, _) => "Voidgrubs",
        (ObjectiveKind::Atakhan, _) => "Atakhan",
        (ObjectiveKind::Tower, _) => "Tower",
        (ObjectiveKind::Inhibitor, _) => "Inhibitor",
    }
}

/// Most telling first, for a combo's objective: a tower follows a multi-kill
/// too often to be one.
const COMBO_OBJECTIVES: [ObjectiveKind; 7] = [
    ObjectiveKind::Elder,
    ObjectiveKind::Baron,
    ObjectiveKind::Atakhan,
    ObjectiveKind::Dragon,
    ObjectiveKind::Herald,
    ObjectiveKind::Grubs,
    ObjectiveKind::Inhibitor,
];

/// "Pentakill on Ahri" when the champion is known, "Pentakill" otherwise.
fn on_champion(title: &str, champion: Option<&str>) -> String {
    match champion {
        Some(champion) => format!("{title} on {champion}"),
        None => title.to_string(),
    }
}

/// The objectives the player's side took: the timeline's, and the steals the
/// feed told of that the timeline does not hold (a game without its timeline).
fn taken(
    objectives: &[Objective],
    plays: &[Play],
) -> Vec<(i64, ObjectiveKind, Option<DragonKind>)> {
    let mut taken: Vec<_> = objectives
        .iter()
        .filter(|objective| objective.ally)
        .map(|objective| (objective.game_time_ms, objective.kind, objective.dragon))
        .collect();
    for play in plays.iter().filter(|p| p.kind == PlayKind::Steal && p.ally) {
        let Some(kind) = play.objective else { continue };
        let known = taken
            .iter()
            .any(|(at, taken, _)| *taken == kind && (at - play.game_time_ms).abs() <= TAIL_MS);
        if !known {
            taken.push((play.game_time_ms, kind, play.dragon));
        }
    }
    taken
}

fn candidates(meta: &RecordingMeta) -> Vec<Candidate> {
    let champion = meta
        .champion_name
        .as_deref()
        .filter(|name| !name.trim().is_empty());
    let kills: Vec<&Highlight> = meta
        .highlights
        .iter()
        .filter(|highlight| highlight.kind == HighlightKind::Kill)
        .collect();
    let mut found = Vec::new();

    for kill in kills.iter().filter(|kill| kill.kills >= MIN_MULTIKILL) {
        found.push(Candidate {
            kind: AutoClipKind::MultiKill,
            title: on_champion(multi_kill(kill.kills), champion),
            rank: 40 + u32::from(kill.kills),
            start_ms: kill.game_time_ms - LEAD_MS,
            end_ms: kill.end_game_time_ms + TAIL_MS,
            moment_ms: kill.end_game_time_ms,
        });
    }

    let taken = taken(&meta.objectives, &meta.plays);
    for kill in kills.iter().filter(|kill| kill.kills >= 2) {
        let after =
            |at: i64| at >= kill.end_game_time_ms && at - kill.end_game_time_ms <= COMBO_WINDOW_MS;
        let objective = COMBO_OBJECTIVES.iter().find_map(|wanted| {
            taken
                .iter()
                .filter(|(at, kind, _)| kind == wanted && after(*at))
                .min_by_key(|(at, _, _)| *at)
        });
        if let Some((at, kind, dragon)) = objective {
            found.push(Candidate {
                kind: AutoClipKind::Combo,
                title: format!(
                    "{} into {}",
                    multi_kill(kill.kills),
                    objective_name(*kind, *dragon)
                ),
                rank: 60 + u32::from(kill.kills),
                start_ms: kill.game_time_ms - LEAD_MS,
                end_ms: at + TAIL_MS,
                moment_ms: *at,
            });
        }
    }

    for play in &meta.plays {
        let at = play.game_time_ms;
        let candidate = match play.kind {
            PlayKind::Steal if play.ally => play.objective.map(|objective| Candidate {
                kind: AutoClipKind::Steal,
                title: format!("{} stolen", objective_name(objective, play.dragon)),
                rank: 50,
                start_ms: at - LONG_LEAD_MS,
                end_ms: at + TAIL_MS,
                moment_ms: at,
            }),
            PlayKind::Ace if play.ally => {
                // The player's own kill or assist in the fight it ended.
                let involved = meta.highlights.iter().any(|highlight| {
                    matches!(highlight.kind, HighlightKind::Kill | HighlightKind::Assist)
                        && highlight.end_game_time_ms <= at
                        && at - highlight.end_game_time_ms <= ACE_INVOLVEMENT_MS
                });
                involved.then(|| Candidate {
                    kind: AutoClipKind::Ace,
                    title: "Ace".to_string(),
                    rank: 35,
                    start_ms: at - LONG_LEAD_MS,
                    end_ms: at + TAIL_MS,
                    moment_ms: at,
                })
            }
            PlayKind::FirstBlood if play.by_me => Some(Candidate {
                kind: AutoClipKind::FirstBlood,
                title: on_champion("First blood", champion),
                rank: 20,
                start_ms: at - LEAD_MS,
                end_ms: at + TAIL_MS,
                moment_ms: at,
            }),
            _ => None,
        };
        found.extend(candidate);
    }

    for fight in &meta.teamfights {
        found.push(Candidate {
            kind: AutoClipKind::Teamfight,
            title: format!(
                "Teamfight {}/{}/{}",
                fight.kills, fight.deaths, fight.assists
            ),
            rank: 30,
            start_ms: fight.game_time_ms - LEAD_MS,
            end_ms: fight.end_game_time_ms + TAIL_MS,
            moment_ms: fight.end_game_time_ms,
        });
    }
    found
}

/// The clips to cut from a finalised recording, for the kinds of play the
/// player chose, in video order. None without an anchor: every play is in
/// game time, and a clip of the wrong seconds is worse than none.
pub fn auto_clips(meta: &RecordingMeta, kinds: &[AutoClipKind]) -> Vec<AutoClip> {
    let Some(anchor) = meta.anchor.as_ref() else {
        return Vec::new();
    };
    let last = meta.duration_ms.unwrap_or(u64::MAX);
    let place = |game_ms: i64| {
        u64::try_from(anchor.video_ms(game_ms))
            .unwrap_or(0)
            .min(last)
    };
    let mut wanted: Vec<Candidate> = candidates(meta)
        .into_iter()
        .filter(|candidate| kinds.contains(&candidate.kind))
        .collect();
    wanted.sort_by_key(|candidate| candidate.start_ms);

    let mut merged: Vec<(Vec<Candidate>, i64, i64)> = Vec::new();
    for candidate in wanted {
        match merged.last_mut() {
            Some((group, _, end)) if candidate.start_ms <= *end => {
                *end = (*end).max(candidate.end_ms);
                group.push(candidate);
            }
            _ => {
                let (start, end) = (candidate.start_ms, candidate.end_ms);
                merged.push((vec![candidate], start, end));
            }
        }
    }

    merged
        .into_iter()
        .filter_map(|(group, start, end)| {
            let (start_ms, end_ms) = (place(start), place(end));
            if end_ms < start_ms + MIN_CLIP_MS {
                return None;
            }
            // The earliest of the most telling plays names the clip.
            let headline = group.iter().rev().max_by_key(|candidate| candidate.rank)?;
            let mut kinds: Vec<AutoClipKind> =
                group.iter().map(|candidate| candidate.kind).collect();
            kinds.sort();
            kinds.dedup();
            Some(AutoClip {
                kinds,
                title: headline.title.clone(),
                start_ms,
                end_ms,
                moment_ms: place(headline.moment_ms).clamp(start_ms, end_ms),
            })
        })
        .collect()
}

#[cfg(test)]
mod tests {
    use lcu::detail::GameTimeline;

    use super::*;
    use crate::anchor::{Anchor, Segment};
    use crate::settings::RecordingSettings;
    use crate::teamfights::Teamfight;
    use crate::{highlights, moments, plays, teamfights};

    /// A recording whose video runs 20 s ahead of the game clock.
    fn recording() -> RecordingMeta {
        let mut meta = RecordingMeta::new(1, 420, RecordingSettings::default().quality, 0);
        meta.duration_ms = Some(2_400_000);
        meta.anchor = Some(Anchor {
            segments: vec![Segment {
                from_game_ms: 0,
                offset_ms: 20_000,
            }],
        });
        meta.champion_name = Some("Ahri".into());
        meta
    }

    fn kill(at_s: i64, end_s: i64, kills: u8) -> Highlight {
        Highlight {
            kind: HighlightKind::Kill,
            game_time_ms: at_s * 1000,
            end_game_time_ms: end_s * 1000,
            kills,
            killer_id: None,
            victim_ids: Vec::new(),
        }
    }

    fn play(kind: PlayKind, at_s: i64, ally: bool, by_me: bool) -> Play {
        Play {
            kind,
            game_time_ms: at_s * 1000,
            ally,
            by_me,
            objective: (kind == PlayKind::Steal).then_some(ObjectiveKind::Baron),
            dragon: None,
        }
    }

    fn summary(clips: &[AutoClip]) -> Vec<(&str, u64, u64)> {
        clips
            .iter()
            .map(|clip| {
                (
                    clip.title.as_str(),
                    clip.start_ms / 1000,
                    clip.end_ms / 1000,
                )
            })
            .collect()
    }

    #[test]
    fn a_multikill_from_a_triple_is_clipped_with_a_lead_in_and_a_tail() {
        let mut meta = recording();
        meta.highlights = vec![kill(300, 300, 1), kill(600, 608, 2), kill(900, 925, 5)];
        let clips = auto_clips(&meta, &AutoClipKind::ALL);
        // Game 900 s is video 920 s: 10 s before the first kill, 5 s after the last.
        assert_eq!(summary(&clips), vec![("Pentakill on Ahri", 910, 950)]);
        assert_eq!(clips[0].kinds, vec![AutoClipKind::MultiKill]);
        assert_eq!(clips[0].moment_ms, 945_000);

        meta.champion_name = None;
        assert_eq!(auto_clips(&meta, &AutoClipKind::ALL)[0].title, "Pentakill");
    }

    #[test]
    fn a_multikill_into_an_objective_is_one_combo_clip() {
        let mut meta = recording();
        meta.highlights = vec![kill(1200, 1206, 3)];
        meta.objectives = vec![
            Objective {
                kind: ObjectiveKind::Tower,
                game_time_ms: 1_215_000,
                ally: true,
                dragon: None,
            },
            Objective {
                kind: ObjectiveKind::Baron,
                game_time_ms: 1_240_000,
                ally: true,
                dragon: None,
            },
        ];
        let clips = auto_clips(&meta, &AutoClipKind::ALL);
        assert_eq!(
            summary(&clips),
            vec![("Triple kill into Baron", 1210, 1265)]
        );
        assert_eq!(
            clips[0].kinds,
            vec![AutoClipKind::MultiKill, AutoClipKind::Combo]
        );
        // The thumbnail is the Baron, the combo's last play.
        assert_eq!(clips[0].moment_ms, 1_260_000);

        // An objective too long after, or the enemy's, is no combo.
        meta.objectives[1].game_time_ms = 1_300_000;
        assert_eq!(auto_clips(&meta, &[AutoClipKind::Combo]), Vec::new());
        meta.objectives[1].game_time_ms = 1_240_000;
        meta.objectives[1].ally = false;
        assert_eq!(auto_clips(&meta, &[AutoClipKind::Combo]), Vec::new());
    }

    #[test]
    fn a_double_kill_counts_towards_a_combo_but_not_alone() {
        let mut meta = recording();
        meta.highlights = vec![kill(1200, 1204, 2)];
        assert!(auto_clips(&meta, &AutoClipKind::ALL).is_empty());
        // A steal the timeline does not hold still makes the combo.
        meta.plays = vec![Play {
            dragon: Some(DragonKind::Ocean),
            objective: Some(ObjectiveKind::Dragon),
            ..play(PlayKind::Steal, 1230, true, true)
        }];
        let titles: Vec<String> = auto_clips(&meta, &AutoClipKind::ALL)
            .into_iter()
            .map(|clip| clip.title)
            .collect();
        assert_eq!(titles, vec!["Double kill into Ocean Drake"]);
    }

    #[test]
    fn steals_aces_and_first_blood_are_clipped_when_they_are_the_players() {
        let mut meta = recording();
        meta.highlights = vec![
            kill(185, 185, 1),
            Highlight {
                kind: HighlightKind::Assist,
                ..kill(1590, 1590, 0)
            },
        ];
        meta.plays = vec![
            play(PlayKind::FirstBlood, 185, true, true),
            play(PlayKind::Steal, 840, false, false),
            play(PlayKind::Steal, 1020, true, false),
            play(PlayKind::Ace, 1612, true, false),
            // The enemy's ace, and one the player had no part in.
            play(PlayKind::Ace, 1800, false, false),
            play(PlayKind::Ace, 2100, true, false),
        ];
        let clips = auto_clips(&meta, &AutoClipKind::ALL);
        assert_eq!(
            summary(&clips),
            vec![
                ("First blood on Ahri", 195, 210),
                ("Baron stolen", 1020, 1045),
                ("Ace", 1612, 1637),
            ]
        );

        // First blood drawn by an ally is not the player's.
        meta.plays[0].by_me = false;
        assert_eq!(auto_clips(&meta, &[AutoClipKind::FirstBlood]), Vec::new());
    }

    #[test]
    fn a_teamfight_is_titled_with_the_players_line_in_it() {
        let mut meta = recording();
        meta.teamfights = vec![Teamfight {
            game_time_ms: 1_500_000,
            end_game_time_ms: 1_512_000,
            deaths_total: 5,
            kills: 3,
            deaths: 0,
            assists: 1,
        }];
        let clips = auto_clips(&meta, &AutoClipKind::ALL);
        assert_eq!(summary(&clips), vec![("Teamfight 3/0/1", 1510, 1537)]);
    }

    #[test]
    fn overlapping_windows_are_one_clip_named_after_its_most_telling_play() {
        let mut meta = recording();
        meta.highlights = vec![kill(1500, 1512, 3)];
        meta.teamfights = vec![Teamfight {
            game_time_ms: 1_495_000,
            end_game_time_ms: 1_520_000,
            deaths_total: 6,
            kills: 3,
            deaths: 1,
            assists: 2,
        }];
        meta.plays = vec![
            play(PlayKind::Ace, 1525, true, false),
            // Long after: a clip of its own.
            play(PlayKind::Steal, 2000, true, true),
        ];
        let clips = auto_clips(&meta, &AutoClipKind::ALL);
        assert_eq!(
            summary(&clips),
            vec![
                ("Triple kill on Ahri", 1505, 1550),
                ("Baron stolen", 2000, 2025),
            ]
        );
        assert_eq!(
            clips[0].kinds,
            vec![
                AutoClipKind::MultiKill,
                AutoClipKind::Ace,
                AutoClipKind::Teamfight
            ]
        );

        // A kind turned off neither clips nor names: the fight alone is left.
        let clips = auto_clips(&meta, &[AutoClipKind::Teamfight]);
        assert_eq!(summary(&clips), vec![("Teamfight 3/1/2", 1505, 1545)]);
        assert!(auto_clips(&meta, &[]).is_empty());
    }

    #[test]
    fn no_clip_without_an_anchor_and_none_past_the_video() {
        let mut meta = recording();
        meta.highlights = vec![kill(900, 925, 4)];
        meta.duration_ms = Some(940_000);
        // Held to the video's end.
        assert_eq!(
            summary(&auto_clips(&meta, &AutoClipKind::ALL)),
            vec![("Quadra kill on Ahri", 910, 940)]
        );
        // The whole window past the end of the video.
        meta.duration_ms = Some(600_000);
        assert!(auto_clips(&meta, &AutoClipKind::ALL).is_empty());
        meta.duration_ms = None;
        meta.anchor = None;
        assert!(auto_clips(&meta, &AutoClipKind::ALL).is_empty());
    }

    /// A game's timeline in the client's shape: the player is participant 3,
    /// on the blue side (100).
    fn timeline() -> GameTimeline {
        serde_json::from_str(
            r#"{ "frames": [{ "timestamp": 0, "participantFrames": {}, "events": [
                 { "type": "CHAMPION_KILL", "timestamp": 185400, "killerId": 3, "victimId": 8, "assistingParticipantIds": [] },
                 { "type": "CHAMPION_KILL", "timestamp": 1200000, "killerId": 3, "victimId": 6, "assistingParticipantIds": [] },
                 { "type": "CHAMPION_KILL", "timestamp": 1204000, "killerId": 3, "victimId": 7, "assistingParticipantIds": [1] },
                 { "type": "CHAMPION_KILL", "timestamp": 1209000, "killerId": 3, "victimId": 9, "assistingParticipantIds": [] },
                 { "type": "CHAMPION_KILL", "timestamp": 1212000, "killerId": 10, "victimId": 2, "assistingParticipantIds": [] },
                 { "type": "CHAMPION_KILL", "timestamp": 1215000, "killerId": 1, "victimId": 10, "assistingParticipantIds": [3] },
                 { "type": "ELITE_MONSTER_KILL", "timestamp": 1240000, "killerId": 1, "monsterType": "BARON_NASHOR" },
                 { "type": "ELITE_MONSTER_KILL", "timestamp": 1900000, "killerId": 3, "monsterType": "DRAGON", "monsterSubType": "ELDER_DRAGON" }
               ] }] }"#,
        )
        .unwrap()
    }

    #[test]
    fn a_game_read_from_its_timeline_and_its_feed_gives_its_clips() {
        let mut meta = recording();
        let timeline = timeline();
        meta.highlights = highlights::from_timeline(&timeline, 3);
        meta.objectives = moments::objectives(&timeline, 100);
        meta.teamfights = teamfights::from_timeline(&timeline, 3);
        meta.plays = plays::from_live(
            &plays::tests::feed(),
            &plays::tests::me(),
            &plays::tests::players(),
        );

        let clips = auto_clips(&meta, &AutoClipKind::ALL);
        assert_eq!(
            summary(&clips),
            vec![
                ("First blood on Ahri", 195, 210),
                ("Ocean Drake stolen", 1020, 1045),
                ("Triple kill into Baron", 1210, 1265),
                // The ace at 1612 s came long after the player's last kill.
                ("Elder Dragon stolen", 1900, 1925),
            ]
        );
        assert_eq!(
            clips[2].kinds,
            vec![
                AutoClipKind::MultiKill,
                AutoClipKind::Teamfight,
                AutoClipKind::Combo
            ]
        );
    }
}
