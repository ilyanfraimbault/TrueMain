//! A player's form, read off their recent games for the loading screen
//! (#1753): how often they played the champion they are on and how it went,
//! and the streak they are on in ranked. Counts only — no composite player
//! score, which #1671 rules out as an alternative to Riot's ranking.

use serde::Serialize;

use crate::record::{HistoryGame, MatchHistory};

/// How many of a player's games the loading screen reads.
pub const FORM_GAMES: usize = 20;

/// Solo/Duo and Flex: the games a streak counts.
const RANKED: [i64; 2] = [420, 440];
const SUMMONERS_RIFT: i64 = 11;
/// A game this short was a remake: neither a win nor a loss for anyone.
const REMAKE_SECONDS: i64 = 300;

#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct PlayerForm {
    /// Summoner's Rift games read, remakes left out.
    pub games: u32,
    /// Of those, the ones on the champion the player is on now.
    pub champion_games: u32,
    pub champion_wins: u32,
    /// Ranked games won (positive) or lost (negative) in a row, newest first;
    /// zero with no ranked game read.
    pub streak: i32,
}

impl PlayerForm {
    /// The form `history` shows for a player on `champion_id`. The history
    /// list carries the player alone in each game's participants.
    pub fn from_history(history: &MatchHistory, champion_id: i64) -> Self {
        let mut form = Self::default();
        let mut streak_open = true;
        for game in &history.games.games {
            let Some(me) = counted(game) else {
                continue;
            };
            form.games += 1;
            if me.champion_id == champion_id {
                form.champion_games += 1;
                form.champion_wins += u32::from(me.stats.win);
            }
            if streak_open && RANKED.contains(&game.queue_id) {
                let step = if me.stats.win { 1 } else { -1 };
                if form.streak == 0 || form.streak.signum() == step {
                    form.streak += step;
                } else {
                    streak_open = false;
                }
            }
        }
        form
    }
}

/// The player's own line in a game that counts: on Summoner's Rift, and not
/// a remake.
fn counted(game: &HistoryGame) -> Option<&crate::record::HistoryParticipant> {
    if game.map_id != SUMMONERS_RIFT || game.duration_seconds() < REMAKE_SECONDS {
        return None;
    }
    game.participants.first()
}

#[cfg(test)]
mod tests {
    use super::*;

    fn history(games: &[(i64, i64, bool, i64)]) -> MatchHistory {
        let games: Vec<serde_json::Value> = games
            .iter()
            .map(|(queue, champion, win, duration)| {
                serde_json::json!({
                    "queueId": queue, "mapId": 11, "gameDuration": duration,
                    "participants": [{ "championId": champion, "stats": { "win": win } }]
                })
            })
            .collect();
        serde_json::from_value(serde_json::json!({ "games": { "games": games } })).unwrap()
    }

    #[test]
    fn counts_the_champion_and_the_ranked_streak() {
        // Newest first: two ranked wins, a normal loss (not in the streak), then a ranked loss.
        let form = PlayerForm::from_history(
            &history(&[
                (420, 103, true, 1800),
                (420, 7, true, 1700),
                (400, 103, false, 1600),
                (420, 103, false, 1900),
                (440, 103, true, 2000),
            ]),
            103,
        );
        assert_eq!(form.games, 5);
        assert_eq!(form.champion_games, 4);
        assert_eq!(form.champion_wins, 2);
        assert_eq!(form.streak, 2);
    }

    #[test]
    fn a_losing_streak_is_negative_and_remakes_do_not_count() {
        let form = PlayerForm::from_history(
            &history(&[
                (420, 64, false, 1500),
                (420, 64, true, 200),
                (440, 64, false, 1500),
                (420, 64, false, 1500),
                (420, 64, true, 1500),
            ]),
            64,
        );
        assert_eq!(form.games, 4);
        assert_eq!(form.streak, -3);
    }

    #[test]
    fn no_ranked_game_is_no_streak() {
        let form = PlayerForm::from_history(&history(&[(400, 1, true, 1500)]), 2);
        assert_eq!(form.streak, 0);
        assert_eq!(form.champion_games, 0);
    }
}
