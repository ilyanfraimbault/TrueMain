//! The moments of a game worth jumping to: the player's kills, deaths and
//! assists, in game time.
//!
//! Read from the game's timeline once the match history has it — its
//! `CHAMPION_KILL` events number every participant — or, when it never comes,
//! from the game's live event feed noted during the game, which names them
//! instead. Both feed the same grouping, so a highlight means the same thing
//! whichever source it came from.

use lcu::detail::GameTimeline;
use lcu::live::{ActivePlayer, LiveEvent};
use serde::{Deserialize, Serialize};

/// A kill counts towards a multi-kill when it lands this soon after the
/// previous one — Riot's window for a double, triple and quadra kill.
const MULTIKILL_WINDOW_MS: i64 = 10_000;

/// The fifth kill gets longer, as in the game.
const PENTAKILL_WINDOW_MS: i64 = 30_000;

const MAX_MULTIKILL: u8 = 5;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum HighlightKind {
    Kill,
    Death,
    Assist,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum HighlightSource {
    /// The match history's timeline: authoritative, every participant numbered.
    Timeline,
    /// The game's live feed, kept for a game whose timeline never arrived.
    Live,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Highlight {
    pub kind: HighlightKind,
    /// Game time of the moment — of the first kill, for a multi-kill.
    pub game_time_ms: i64,
    /// Game time of the last kill of a multi-kill; the same as `game_time_ms`
    /// otherwise.
    pub end_game_time_ms: i64,
    /// Kills in a row, 1 to 5, for a kill; 0 for a death or an assist.
    pub kills: u8,
    /// Who landed the kill, for a death or an assist. `None` for an execution
    /// (a tower, minions, a monster) or when the live feed named someone the
    /// timeline would have numbered.
    pub killer_id: Option<i64>,
    /// Who died: every victim of a multi-kill, the one victim of an assist.
    /// Empty for a death, and from the live feed.
    pub victim_ids: Vec<i64>,
}

/// One champion kill, whoever was in it, from either source.
struct ChampionKill {
    time_ms: i64,
    by_me: bool,
    of_me: bool,
    assisted_by_me: bool,
    killer_id: Option<i64>,
    victim_id: Option<i64>,
}

/// The player's highlights from the game's timeline. `me` is the player's
/// participant id in that game.
pub fn from_timeline(timeline: &GameTimeline, me: i64) -> Vec<Highlight> {
    let kills = timeline
        .frames
        .iter()
        .flat_map(|frame| &frame.events)
        .filter(|event| event.kind == "CHAMPION_KILL")
        .map(|event| ChampionKill {
            time_ms: event.timestamp,
            by_me: event.killer_id == me,
            of_me: event.victim_id == me,
            assisted_by_me: event.assisting_participant_ids.contains(&me),
            killer_id: Some(event.killer_id).filter(|id| *id > 0),
            victim_id: Some(event.victim_id).filter(|id| *id > 0),
        });
    group(kills)
}

/// The player's highlights from the live feed noted during the game.
pub fn from_live(events: &[LiveEvent], me: &ActivePlayer) -> Vec<Highlight> {
    let kills = events
        .iter()
        .filter(|event| event.event_name == LiveEvent::CHAMPION_KILL)
        .map(|event| ChampionKill {
            time_ms: (event.event_time * 1000.0).round() as i64,
            by_me: me.is_named(&event.killer_name),
            of_me: me.is_named(&event.victim_name),
            assisted_by_me: event.assisters.iter().any(|name| me.is_named(name)),
            killer_id: None,
            victim_id: None,
        });
    group(kills)
}

fn group(kills: impl Iterator<Item = ChampionKill>) -> Vec<Highlight> {
    let mut kills: Vec<ChampionKill> = kills.collect();
    kills.sort_by_key(|kill| kill.time_ms);

    let mut highlights: Vec<Highlight> = Vec::new();
    // Index into `highlights` of the multi-kill still open, if any.
    let mut streak: Option<usize> = None;

    for kill in kills {
        if kill.by_me {
            let extends = streak.and_then(|index| {
                let open = &highlights[index];
                let window = if open.kills == MAX_MULTIKILL - 1 {
                    PENTAKILL_WINDOW_MS
                } else {
                    MULTIKILL_WINDOW_MS
                };
                (open.kills < MAX_MULTIKILL && kill.time_ms - open.end_game_time_ms <= window)
                    .then_some(index)
            });
            match extends {
                Some(index) => {
                    let open = &mut highlights[index];
                    open.kills += 1;
                    open.end_game_time_ms = kill.time_ms;
                    open.victim_ids.extend(kill.victim_id);
                }
                None => {
                    highlights.push(Highlight {
                        kind: HighlightKind::Kill,
                        game_time_ms: kill.time_ms,
                        end_game_time_ms: kill.time_ms,
                        kills: 1,
                        killer_id: None,
                        victim_ids: kill.victim_id.into_iter().collect(),
                    });
                    streak = Some(highlights.len() - 1);
                }
            }
        } else if kill.of_me {
            // Dying ends a streak, as in the game.
            streak = None;
            highlights.push(Highlight {
                kind: HighlightKind::Death,
                game_time_ms: kill.time_ms,
                end_game_time_ms: kill.time_ms,
                kills: 0,
                killer_id: kill.killer_id,
                victim_ids: Vec::new(),
            });
        } else if kill.assisted_by_me {
            highlights.push(Highlight {
                kind: HighlightKind::Assist,
                game_time_ms: kill.time_ms,
                end_game_time_ms: kill.time_ms,
                kills: 0,
                killer_id: kill.killer_id,
                victim_ids: kill.victim_id.into_iter().collect(),
            });
        }
    }

    highlights.sort_by_key(|highlight| highlight.game_time_ms);
    highlights
}

#[cfg(test)]
mod tests {
    use super::*;

    const ME: i64 = 3;

    fn timeline(kills: &[(i64, i64, i64, &[i64])]) -> GameTimeline {
        let events: Vec<String> = kills
            .iter()
            .map(|(at, killer, victim, assisters)| {
                format!(
                    r#"{{ "type": "CHAMPION_KILL", "timestamp": {at}, "killerId": {killer},
                         "victimId": {victim}, "assistingParticipantIds": {assisters:?} }}"#
                )
            })
            .collect();
        serde_json::from_str(&format!(
            r#"{{ "frames": [
              {{ "timestamp": 0, "participantFrames": {{}}, "events": [
                {{ "type": "ITEM_PURCHASED", "timestamp": 1000, "participantId": 3, "itemId": 1055 }}
              ] }},
              {{ "timestamp": 60000, "participantFrames": {{}}, "events": [{}] }}
            ] }}"#,
            events.join(",")
        ))
        .unwrap()
    }

    #[test]
    fn kills_deaths_and_assists_in_game_order() {
        let highlights = from_timeline(
            &timeline(&[
                (300_000, 7, ME, &[8]),
                (120_000, ME, 6, &[]),
                (200_000, 1, 9, &[ME, 2]),
                (250_000, 1, 10, &[2]),
            ]),
            ME,
        );

        let kinds: Vec<_> = highlights
            .iter()
            .map(|h| (h.kind, h.game_time_ms))
            .collect();
        assert_eq!(
            kinds,
            vec![
                (HighlightKind::Kill, 120_000),
                (HighlightKind::Assist, 200_000),
                (HighlightKind::Death, 300_000),
            ]
        );
        assert_eq!(highlights[0].victim_ids, vec![6]);
        assert_eq!(highlights[1].killer_id, Some(1));
        assert_eq!(highlights[1].victim_ids, vec![9]);
        assert_eq!(highlights[2].killer_id, Some(7));
    }

    #[test]
    fn kills_in_quick_succession_are_one_multikill() {
        let highlights = from_timeline(
            &timeline(&[
                (100_000, ME, 6, &[]),
                (108_000, ME, 7, &[]),
                (117_500, ME, 8, &[]),
                // Too late for a quadra: a kill of its own.
                (130_000, ME, 9, &[]),
            ]),
            ME,
        );

        assert_eq!(highlights.len(), 2);
        assert_eq!(highlights[0].kills, 3);
        assert_eq!(highlights[0].game_time_ms, 100_000);
        assert_eq!(highlights[0].end_game_time_ms, 117_500);
        assert_eq!(highlights[0].victim_ids, vec![6, 7, 8]);
        assert_eq!(highlights[1].kills, 1);
    }

    #[test]
    fn the_fifth_kill_has_longer_and_a_sixth_starts_over() {
        let highlights = from_timeline(
            &timeline(&[
                (100_000, ME, 6, &[]),
                (105_000, ME, 7, &[]),
                (110_000, ME, 8, &[]),
                (115_000, ME, 9, &[]),
                // 25 s after the quadra: still the penta.
                (140_000, ME, 10, &[]),
                (142_000, ME, 6, &[]),
            ]),
            ME,
        );

        assert_eq!(highlights.len(), 2);
        assert_eq!(highlights[0].kills, 5);
        assert_eq!(highlights[1].kills, 1);
        assert_eq!(highlights[1].game_time_ms, 142_000);
    }

    #[test]
    fn dying_ends_a_streak() {
        let highlights = from_timeline(
            &timeline(&[
                (100_000, ME, 6, &[]),
                (103_000, 7, ME, &[]),
                (106_000, ME, 8, &[]),
            ]),
            ME,
        );

        let kinds: Vec<_> = highlights.iter().map(|h| (h.kind, h.kills)).collect();
        assert_eq!(
            kinds,
            vec![
                (HighlightKind::Kill, 1),
                (HighlightKind::Death, 0),
                (HighlightKind::Kill, 1),
            ]
        );
    }

    #[test]
    fn an_execution_has_no_killer() {
        let highlights = from_timeline(&timeline(&[(100_000, 0, ME, &[])]), ME);
        assert_eq!(highlights[0].kind, HighlightKind::Death);
        assert_eq!(highlights[0].killer_id, None);
    }

    #[test]
    fn a_quiet_game_has_no_highlights() {
        assert!(from_timeline(&timeline(&[(100_000, 1, 6, &[2])]), ME).is_empty());
        assert!(from_timeline(&GameTimeline::default(), ME).is_empty());
    }

    #[test]
    fn the_live_feed_gives_the_same_moments_by_name() {
        let me = ActivePlayer {
            riot_id: "Me#EUW".into(),
            riot_id_game_name: "Me".into(),
            summoner_name: String::new(),
        };
        let kill = |id, time, killer: &str, victim: &str, assisters: &[&str]| LiveEvent {
            event_id: id,
            event_name: LiveEvent::CHAMPION_KILL.into(),
            event_time: time,
            killer_name: killer.into(),
            victim_name: victim.into(),
            assisters: assisters.iter().map(|name| name.to_string()).collect(),
        };
        let events = vec![
            LiveEvent {
                event_id: 0,
                event_name: "GameStart".into(),
                ..LiveEvent::default()
            },
            kill(1, 100.0, "Me", "Foe", &[]),
            kill(2, 104.2, "Me", "Foe2", &[]),
            kill(3, 200.0, "Ally", "Foe", &["Me"]),
            kill(4, 300.0, "Foe", "Me", &[]),
        ];

        let highlights = from_live(&events, &me);
        let kinds: Vec<_> = highlights
            .iter()
            .map(|h| (h.kind, h.game_time_ms, h.kills))
            .collect();
        assert_eq!(
            kinds,
            vec![
                (HighlightKind::Kill, 100_000, 2),
                (HighlightKind::Assist, 200_000, 0),
                (HighlightKind::Death, 300_000, 0),
            ]
        );
        assert!(highlights.iter().all(|h| h.victim_ids.is_empty()));
    }
}
