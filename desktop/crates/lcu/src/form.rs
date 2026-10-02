//! A player's form, read off their recent games for the loading screen
//! (#1753): how often they played the champion they are on and how it went,
//! and their latest games one by one. Counts only — no composite player
//! score, which #1671 rules out as an alternative to Riot's ranking.

use serde::Serialize;

use crate::record::{HistoryGame, MatchHistory};

/// How many of a player's games the loading screen reads.
pub const FORM_GAMES: usize = 20;

/// How many of those the loading screen draws one by one.
pub const RECENT_GAMES: usize = 10;

const SUMMONERS_RIFT: i64 = 11;
/// A game this short was a remake: neither a win nor a loss for anyone.
const REMAKE_SECONDS: i64 = 300;

#[derive(Debug, Clone, Default, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct PlayerForm {
    /// Summoner's Rift games read, remakes left out.
    pub games: u32,
    /// Of those, the ones on the champion the player is on now.
    pub champion_games: u32,
    pub champion_wins: u32,
    /// The latest counted games, newest first, at most `RECENT_GAMES`.
    pub recent: Vec<RecentGame>,
}

/// One of a player's latest games, as the loading screen's bar and its
/// tooltip show it.
#[derive(Debug, Clone, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct RecentGame {
    pub champion_id: i64,
    /// The assigned role (`HistoryGame::position_of`); `None` on a queue
    /// without roles.
    pub position: Option<&'static str>,
    pub win: bool,
    pub kills: i64,
    pub deaths: i64,
    pub assists: i64,
    /// When the game was created, in epoch milliseconds.
    pub played_at: i64,
}

impl PlayerForm {
    /// The form `history` shows for a player on `champion_id`. The history
    /// list carries the player alone in each game's participants.
    pub fn from_history(history: &MatchHistory, champion_id: i64) -> Self {
        let mut form = Self::default();
        for game in &history.games.games {
            let Some(me) = counted(game) else {
                continue;
            };
            form.games += 1;
            if me.champion_id == champion_id {
                form.champion_games += 1;
                form.champion_wins += u32::from(me.stats.win);
            }
            if form.recent.len() < RECENT_GAMES {
                form.recent.push(RecentGame {
                    champion_id: me.champion_id,
                    position: game.position_of(me),
                    win: me.stats.win,
                    kills: me.stats.kills,
                    deaths: me.stats.deaths,
                    assists: me.stats.assists,
                    played_at: game.game_creation,
                });
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
    fn counts_the_champion_on_every_rift_game() {
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
    }

    #[test]
    fn remakes_and_other_maps_are_left_out() {
        let mut games = history(&[
            (420, 64, false, 1500),
            (420, 64, true, 200),
            (440, 64, false, 1500),
        ]);
        games.games.games[2].map_id = 12;
        let form = PlayerForm::from_history(&games, 64);
        assert_eq!(form.games, 1);
        assert_eq!(form.recent.len(), 1);
        assert!(!form.recent[0].win);
    }

    #[test]
    fn the_latest_games_newest_first_with_their_line() {
        let mut games = history(&[(420, 103, true, 1800), (430, 7, false, 1500)]);
        // Our slot on blue in a ranked game: participant 3 plays mid.
        let me = &mut games.games.games[0].participants[0];
        me.participant_id = 3;
        me.team_id = 100;
        (me.stats.kills, me.stats.deaths, me.stats.assists) = (7, 2, 9);
        games.games.games[0].game_creation = 1_790_000_000_000;

        let form = PlayerForm::from_history(&games, 103);
        assert_eq!(
            form.recent[0],
            RecentGame {
                champion_id: 103,
                position: Some("MIDDLE"),
                win: true,
                kills: 7,
                deaths: 2,
                assists: 9,
                played_at: 1_790_000_000_000,
            }
        );
        // Blind pick still counts, without a role.
        assert_eq!(form.recent[1].position, None);
        assert_eq!(form.recent[1].champion_id, 7);
    }

    #[test]
    fn keeps_only_the_latest_ten() {
        let form = PlayerForm::from_history(&history(&[(420, 1, true, 1500); 14]), 1);
        assert_eq!(form.games, 14);
        assert_eq!(form.recent.len(), RECENT_GAMES);
    }
}
