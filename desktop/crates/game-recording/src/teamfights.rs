//! Teamfights (#1766): a burst of champion kills, each close behind the last,
//! that the player took part in — killing, dying or assisting. Read from the
//! same champion kills as the highlights, from the game's timeline or its
//! live feed, so a teamfight means the same thing whichever source it came
//! from; the player's own line in it is what its clip is titled from
//! ("Teamfight 3/0/1").

use lcu::detail::GameTimeline;
use lcu::live::{ActivePlayer, LiveEvent};
use serde::{Deserialize, Serialize};

use crate::highlights::{self, ChampionKill};

/// A kill belongs to the fight when it lands this soon after the previous one.
const TEAMFIGHT_GAP_MS: i64 = 15_000;

/// Fewer champions dying than this is a skirmish, not a teamfight.
const TEAMFIGHT_MIN_DEATHS: u8 = 4;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Teamfight {
    /// Game time of its first kill.
    pub game_time_ms: i64,
    /// Game time of its last kill.
    pub end_game_time_ms: i64,
    /// Champions who died in it, both sides together.
    pub deaths_total: u8,
    /// The player's own line in it.
    pub kills: u8,
    pub deaths: u8,
    pub assists: u8,
}

/// The teamfights of the game's timeline. `me` is the player's participant id.
pub fn from_timeline(timeline: &GameTimeline, me: i64) -> Vec<Teamfight> {
    group(highlights::timeline_kills(timeline, me))
}

/// The teamfights of the live feed noted during the game.
pub fn from_live(events: &[LiveEvent], me: &ActivePlayer) -> Vec<Teamfight> {
    group(highlights::live_kills(events, me))
}

fn group(mut kills: Vec<ChampionKill>) -> Vec<Teamfight> {
    kills.sort_by_key(|kill| kill.time_ms);
    let mut fights: Vec<Teamfight> = Vec::new();
    let mut open: Option<Teamfight> = None;
    for kill in kills {
        let fight = match open.take() {
            Some(fight) if kill.time_ms - fight.end_game_time_ms <= TEAMFIGHT_GAP_MS => fight,
            other => {
                fights.extend(other);
                Teamfight {
                    game_time_ms: kill.time_ms,
                    end_game_time_ms: kill.time_ms,
                    deaths_total: 0,
                    kills: 0,
                    deaths: 0,
                    assists: 0,
                }
            }
        };
        open = Some(Teamfight {
            end_game_time_ms: kill.time_ms,
            deaths_total: fight.deaths_total.saturating_add(1),
            kills: fight.kills + u8::from(kill.by_me),
            deaths: fight.deaths + u8::from(kill.of_me),
            assists: fight.assists + u8::from(kill.assisted_by_me && !kill.by_me),
            ..fight
        });
    }
    fights.extend(open);
    fights.retain(|fight| {
        fight.deaths_total >= TEAMFIGHT_MIN_DEATHS && fight.kills + fight.deaths + fight.assists > 0
    });
    fights
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::plays::tests::me;

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
            r#"{{ "frames": [{{ "timestamp": 0, "participantFrames": {{}}, "events": [{}] }}] }}"#,
            events.join(",")
        ))
        .unwrap()
    }

    #[test]
    fn a_burst_of_kills_the_player_was_in_is_a_teamfight() {
        let fights = from_timeline(
            &timeline(&[
                // A fight of five deaths, each within 15 s of the last.
                (600_000, ME, 6, &[]),
                (604_000, 7, 2, &[]),
                (611_000, 1, 8, &[ME]),
                (625_000, ME, 9, &[1]),
                (632_000, 10, ME, &[]),
                // 20 s later: another fight, without the player.
                (652_000, 1, 6, &[]),
                (655_000, 7, 1, &[]),
                (656_000, 2, 7, &[]),
                (658_000, 8, 2, &[]),
            ]),
            ME,
        );
        assert_eq!(
            fights,
            vec![Teamfight {
                game_time_ms: 600_000,
                end_game_time_ms: 632_000,
                deaths_total: 5,
                kills: 2,
                deaths: 1,
                assists: 1,
            }]
        );
    }

    #[test]
    fn a_skirmish_is_not_a_teamfight() {
        let fights = from_timeline(
            &timeline(&[
                (600_000, ME, 6, &[]),
                (604_000, 7, ME, &[]),
                (610_000, 1, 7, &[]),
            ]),
            ME,
        );
        assert!(fights.is_empty());
        assert!(from_timeline(&GameTimeline::default(), ME).is_empty());
    }

    #[test]
    fn the_live_feed_gives_the_same_teamfight_by_name() {
        let kill = |time: f64, killer: &str, victim: &str, assisters: &[&str]| LiveEvent {
            event_name: LiveEvent::CHAMPION_KILL.into(),
            event_time: time,
            killer_name: killer.into(),
            victim_name: victim.into(),
            assisters: assisters.iter().map(|name| name.to_string()).collect(),
            ..LiveEvent::default()
        };
        let events = vec![
            kill(1500.0, "Me", "Foe", &[]),
            kill(1503.5, "Ally", "Foe2", &["Me"]),
            kill(1509.0, "Me", "Foe3", &["Ally"]),
            kill(1512.0, "Me", "Foe4", &[]),
        ];
        let fights = from_live(&events, &me());
        assert_eq!(fights.len(), 1);
        let fight = fights[0];
        assert_eq!((fight.kills, fight.deaths, fight.assists), (3, 0, 1));
        assert_eq!(
            (fight.game_time_ms, fight.end_game_time_ms),
            (1_500_000, 1_512_000)
        );
    }
}
