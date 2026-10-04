//! How a player fares on the champion they are on (#1863): over their last
//! games on it, how often they won and where they stood against their own lane
//! opponent at fifteen minutes — gold, creep score and experience. The Game
//! page weighs it, per role, into the lane's chance; the figures themselves are
//! not shown.
//!
//! The lane opponent of a past game is read from its slot: on a queue that
//! assigns roles both teams are listed in role order (`HistoryGame::position_of`),
//! so participant `n` on blue meets `n + 5` on red. The gaps then come from the
//! game's timeline alone — one client request a game.

use serde::Serialize;

use crate::detail::GameTimeline;
use crate::record::{HistoryGame, MatchHistory};

/// How many of a player's games on the champion are weighed.
pub const LANING_GAMES: usize = 10;

/// How deep into a player's history the games on the champion are looked for.
pub const LANING_DEPTH: usize = 100;

/// The minute the lane gaps are read at.
const LANING_MINUTE: i64 = 15;

const SUMMONERS_RIFT: i64 = 11;
/// A game this short was a remake: neither a win nor a loss for anyone.
const REMAKE_SECONDS: i64 = 300;

/// One of a player's games on the champion, as the history list gives it.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct LaningGame {
    pub game_id: i64,
    pub win: bool,
    /// The player's slot and their lane opponent's; `None` on a queue that
    /// assigns no roles, where nobody is the lane opponent.
    pub slots: Option<(i64, i64)>,
}

/// The player's lead over their lane opponent at fifteen minutes. Creep score
/// counts the jungle camps, so a jungler's is their farm against the other
/// jungler's.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct LaneGaps {
    pub gold: i64,
    pub cs: i64,
    pub xp: i64,
}

#[derive(Debug, Clone, Default, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct LaningForm {
    /// Games on the champion weighed, at most `LANING_GAMES`.
    pub games: u32,
    pub wins: u32,
    /// Of those, the ones whose fifteen-minute gaps were read: a role queue,
    /// a timeline the client served, and a game that reached the minute.
    pub measured: u32,
    /// Average gaps over the measured games; `None` with none measured.
    pub gold_diff15: Option<f64>,
    pub cs_diff15: Option<f64>,
    pub xp_diff15: Option<f64>,
}

/// The player's latest games on `champion_id` in `history`, newest first, at
/// most `LANING_GAMES` — Summoner's Rift, remakes left out.
pub fn laning_games(history: &MatchHistory, champion_id: i64) -> Vec<LaningGame> {
    history
        .games
        .games
        .iter()
        .filter_map(|game| laning_game(game, champion_id))
        .take(LANING_GAMES)
        .collect()
}

fn laning_game(game: &HistoryGame, champion_id: i64) -> Option<LaningGame> {
    if game.map_id != SUMMONERS_RIFT || game.duration_seconds() < REMAKE_SECONDS {
        return None;
    }
    let me = game.participants.first()?;
    if me.champion_id != champion_id {
        return None;
    }
    let slots = game.position_of(me).map(|_| {
        let opponent = if me.team_id == 100 {
            me.participant_id + 5
        } else {
            me.participant_id - 5
        };
        (me.participant_id, opponent)
    });
    Some(LaningGame {
        game_id: game.game_id,
        win: me.stats.win,
        slots,
    })
}

/// `me`'s lead over `opponent` at fifteen minutes; `None` when the game ended
/// before it or the timeline lacks either of them.
pub fn gaps_at_fifteen(timeline: &GameTimeline, me: i64, opponent: i64) -> Option<LaneGaps> {
    let at = LANING_MINUTE * 60_000;
    let frame = timeline.frames.iter().find(|frame| frame.timestamp >= at)?;
    let mine = frame.participant_frames.get(&me.to_string())?;
    let theirs = frame.participant_frames.get(&opponent.to_string())?;
    let cs = |frame: &crate::detail::ParticipantFrame| {
        frame.minions_killed + frame.jungle_minions_killed
    };
    Some(LaneGaps {
        gold: mine.total_gold - theirs.total_gold,
        cs: cs(mine) - cs(theirs),
        xp: mine.xp - theirs.xp,
    })
}

impl LaningForm {
    /// The form `games` show, with `gaps[i]` the gaps read for `games[i]`.
    pub fn new(games: &[LaningGame], gaps: &[Option<LaneGaps>]) -> Self {
        let measured: Vec<LaneGaps> = gaps.iter().flatten().copied().collect();
        let average = |pick: fn(&LaneGaps) -> i64| {
            (!measured.is_empty())
                .then(|| measured.iter().map(pick).sum::<i64>() as f64 / measured.len() as f64)
        };
        Self {
            games: games.len() as u32,
            wins: games.iter().filter(|game| game.win).count() as u32,
            measured: measured.len() as u32,
            gold_diff15: average(|gaps| gaps.gold),
            cs_diff15: average(|gaps| gaps.cs),
            xp_diff15: average(|gaps| gaps.xp),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn history(games: &[(i64, i64, i64, i64, bool, i64)]) -> MatchHistory {
        let games: Vec<serde_json::Value> = games
            .iter()
            .map(|(id, queue, champion, slot, win, duration)| {
                serde_json::json!({
                    "gameId": id, "queueId": queue, "mapId": 11, "gameDuration": duration,
                    "participants": [{
                        "participantId": slot,
                        "teamId": if *slot <= 5 { 100 } else { 200 },
                        "championId": champion,
                        "stats": { "win": win }
                    }]
                })
            })
            .collect();
        serde_json::from_value(serde_json::json!({ "games": { "games": games } })).unwrap()
    }

    /// A minute, then each participant's id, gold, creep score and experience.
    type Frame<'a> = (i64, &'a [(i64, i64, i64, i64)]);

    fn timeline(frames: &[Frame]) -> GameTimeline {
        let frames: Vec<serde_json::Value> = frames
            .iter()
            .map(|(minute, players)| {
                let participants: serde_json::Map<String, serde_json::Value> = players
                    .iter()
                    .map(|(id, gold, cs, xp)| {
                        (
                            id.to_string(),
                            serde_json::json!({
                                "totalGold": gold, "minionsKilled": cs,
                                "jungleMinionsKilled": 0, "xp": xp
                            }),
                        )
                    })
                    .collect();
                serde_json::json!({ "timestamp": minute * 60_000 + 30, "participantFrames": participants })
            })
            .collect();
        serde_json::from_value(serde_json::json!({ "frames": frames })).unwrap()
    }

    #[test]
    fn keeps_the_latest_ten_games_on_the_champion() {
        let mut games = vec![(1, 420, 7, 2, true, 1800), (2, 420, 64, 2, true, 200)];
        games.extend((3..20).map(|id| (id, 420, 64, 2, id % 2 == 0, 1800)));
        let picked = laning_games(&history(&games), 64);
        assert_eq!(picked.len(), LANING_GAMES);
        assert_eq!(
            picked[0].game_id, 3,
            "another champion and a remake are skipped"
        );
    }

    #[test]
    fn the_lane_opponent_is_the_same_slot_on_the_other_team() {
        let picked = laning_games(
            &history(&[(1, 420, 64, 2, true, 1800), (2, 420, 64, 8, false, 1800)]),
            64,
        );
        assert_eq!(picked[0].slots, Some((2, 7)));
        assert_eq!(picked[1].slots, Some((8, 3)));
    }

    #[test]
    fn a_queue_without_roles_counts_for_the_win_rate_only() {
        let picked = laning_games(&history(&[(1, 450, 64, 2, true, 1800)]), 0);
        assert!(picked.is_empty(), "ARAM is not Summoner's Rift");
        let mut blind = history(&[(1, 430, 64, 2, true, 1800)]);
        blind.games.games[0].map_id = 11;
        assert_eq!(laning_games(&blind, 64)[0].slots, None);
    }

    #[test]
    fn reads_the_gaps_at_the_first_frame_past_fifteen_minutes() {
        let line = timeline(&[
            (14, &[(2, 5000, 100, 6000), (7, 5000, 100, 6000)]),
            (15, &[(2, 6000, 130, 7400), (7, 5200, 118, 7000)]),
        ]);
        assert_eq!(
            gaps_at_fifteen(&line, 2, 7),
            Some(LaneGaps {
                gold: 800,
                cs: 12,
                xp: 400
            })
        );
        assert_eq!(
            gaps_at_fifteen(&line, 7, 2).map(|gaps| gaps.gold),
            Some(-800)
        );
    }

    #[test]
    fn a_game_over_before_fifteen_minutes_has_no_gaps() {
        let line = timeline(&[(14, &[(2, 5000, 100, 6000), (7, 5000, 100, 6000)])]);
        assert_eq!(gaps_at_fifteen(&line, 2, 7), None);
    }

    #[test]
    fn averages_the_measured_games_only() {
        let games = [
            LaningGame {
                game_id: 1,
                win: true,
                slots: Some((2, 7)),
            },
            LaningGame {
                game_id: 2,
                win: false,
                slots: Some((2, 7)),
            },
            LaningGame {
                game_id: 3,
                win: true,
                slots: None,
            },
        ];
        let gaps = [
            Some(LaneGaps {
                gold: 1000,
                cs: 10,
                xp: 300,
            }),
            Some(LaneGaps {
                gold: -200,
                cs: -4,
                xp: -100,
            }),
            None,
        ];
        let form = LaningForm::new(&games, &gaps);
        assert_eq!((form.games, form.wins, form.measured), (3, 2, 2));
        assert_eq!(form.gold_diff15, Some(400.0));
        assert_eq!(form.cs_diff15, Some(3.0));
        assert_eq!(form.xp_diff15, Some(100.0));
        assert_eq!(LaningForm::new(&[], &[]).gold_diff15, None);
    }
}
