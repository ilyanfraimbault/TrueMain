//! A player's form, read off their recent games for the loading screen
//! (#1753) and the Game page (#1828): how often they played the champion they
//! are on and how it went, the roles they were given, the streak they are on,
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
    /// Kills, deaths and assists summed over the games on the champion.
    pub champion_kills: i64,
    pub champion_deaths: i64,
    pub champion_assists: i64,
    /// The roles the player was given, most played first, over the games of
    /// a queue that assigns them — what tells a main role from an autofill.
    pub positions: Vec<PositionGames>,
    /// The run the latest games make: `3` for three wins in a row, `-2` for
    /// two losses, `0` with no game read.
    pub streak: i32,
    /// The latest counted games, newest first, at most `RECENT_GAMES`.
    pub recent: Vec<RecentGame>,
}

/// How many of a player's games were played in one role.
#[derive(Debug, Clone, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct PositionGames {
    pub position: &'static str,
    pub games: u32,
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
        let mut streak_open = true;
        for game in &history.games.games {
            let Some(me) = counted(game) else {
                continue;
            };
            form.games += 1;
            if me.champion_id == champion_id {
                form.champion_games += 1;
                form.champion_wins += u32::from(me.stats.win);
                form.champion_kills += me.stats.kills;
                form.champion_deaths += me.stats.deaths;
                form.champion_assists += me.stats.assists;
            }
            let position = game.position_of(me);
            if let Some(position) = position {
                match form.positions.iter_mut().find(|p| p.position == position) {
                    Some(seen) => seen.games += 1,
                    None => form.positions.push(PositionGames { position, games: 1 }),
                }
            }
            // Newest first: the streak runs until the first game that breaks it.
            let step = if me.stats.win { 1 } else { -1 };
            if streak_open && (form.streak == 0 || form.streak.signum() == step) {
                form.streak += step;
            } else {
                streak_open = false;
            }
            if form.recent.len() < RECENT_GAMES {
                form.recent.push(RecentGame {
                    champion_id: me.champion_id,
                    position,
                    win: me.stats.win,
                    kills: me.stats.kills,
                    deaths: me.stats.deaths,
                    assists: me.stats.assists,
                    played_at: game.game_creation,
                });
            }
        }
        // A stable sort: a tie keeps the role played most recently first.
        form.positions.sort_by_key(|p| std::cmp::Reverse(p.games));
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
    fn the_champion_kda_sums_its_games_only() {
        let mut games = history(&[
            (420, 103, true, 1800),
            (420, 7, true, 1700),
            (420, 103, false, 1600),
        ]);
        for (game, (k, d, a)) in
            games
                .games
                .games
                .iter_mut()
                .zip([(5, 1, 7), (20, 0, 0), (2, 6, 3)])
        {
            let stats = &mut game.participants[0].stats;
            (stats.kills, stats.deaths, stats.assists) = (k, d, a);
        }
        let form = PlayerForm::from_history(&games, 103);
        assert_eq!(
            (
                form.champion_kills,
                form.champion_deaths,
                form.champion_assists
            ),
            (7, 7, 10)
        );
    }

    #[test]
    fn the_roles_given_most_played_first() {
        let mut games = history(&[
            (420, 1, true, 1500),
            (420, 1, true, 1500),
            (440, 1, true, 1500),
            (420, 1, true, 1500),
            (450, 1, true, 1500),
        ]);
        // Slots on blue: 1 top, 2 jungle, 3 mid. The fifth game assigns none.
        for (game, slot) in games.games.games.iter_mut().zip([3, 2, 3, 3, 1]) {
            let me = &mut game.participants[0];
            (me.participant_id, me.team_id) = (slot, 100);
        }
        let form = PlayerForm::from_history(&games, 1);
        assert_eq!(
            form.positions,
            [
                PositionGames {
                    position: "MIDDLE",
                    games: 3
                },
                PositionGames {
                    position: "JUNGLE",
                    games: 1
                },
            ]
        );
    }

    #[test]
    fn the_streak_runs_from_the_latest_game() {
        let wins = |pattern: &[bool]| {
            let games: Vec<(i64, i64, bool, i64)> =
                pattern.iter().map(|win| (420, 1, *win, 1500)).collect();
            PlayerForm::from_history(&history(&games), 1).streak
        };
        assert_eq!(wins(&[true, true, true, false, true]), 3);
        assert_eq!(wins(&[false, false, true, false]), -2);
        assert_eq!(wins(&[true]), 1);
        assert_eq!(wins(&[]), 0);
    }

    #[test]
    fn keeps_only_the_latest_ten() {
        let form = PlayerForm::from_history(&history(&[(420, 1, true, 1500); 14]), 1);
        assert_eq!(form.games, 14);
        assert_eq!(form.recent.len(), RECENT_GAMES);
    }
}
