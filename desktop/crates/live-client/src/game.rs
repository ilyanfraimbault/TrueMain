//! The in-game state the app renders, and the changes between two readings.
//!
//! Derived from one `allgamedata` payload ([`GameState::from_payload`]) and
//! reduced to what moves at the pace of the game — items, levels, K/D/A, deaths
//! — rather than at the pace of the poll. Creep score, gold and champion stats
//! change on nearly every reading; carrying them would turn every poll into a
//! change, which is exactly what the frontend is not to receive. A panel that
//! needs one of them adds it here with the pace it actually needs — our gold,
//! for the next-item panel (#1751), moves in steps of [`GOLD_STEP`]; every
//! player's creep score, for the win probability, in steps of [`CS_STEP`].

use serde::{Deserialize, Serialize};

use crate::model::{self, AllGameData};
use crate::objectives::Objectives;
use crate::pace::Pace;

/// What the frontend holds about the running game.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct GameState {
    /// Counts the changes the frontend has been sent, so it can tell a missed
    /// update from a stale one (see `GameFeed`).
    pub revision: u64,
    /// Seconds of game time at the latest reading.
    pub game_time: f64,
    pub game_mode: String,
    pub map_number: i64,
    /// The side the player is on; absent when spectating, where no player is
    /// ours.
    pub my_team: Option<Team>,
    /// All ten players, in the game's order (blue side, then red side). An
    /// index into this list is how a change names a player.
    pub players: Vec<GamePlayer>,
    /// Our unspent gold, floored to [`GOLD_STEP`]; 0 when spectating. Only
    /// the player's own gold exists in the API.
    pub gold: i64,
    /// What each team has taken on the map; moves only when one falls.
    #[serde(default)]
    pub objectives: Objectives,
    /// Our creep score and gold earned at each whole minute so far; kept by
    /// the feed, since one reading holds only the present.
    #[serde(default)]
    pub pace: Pace,
}

/// The pace our gold moves at on the frontend. Income alone crosses a step
/// every ten seconds or so, a last hit or a kill at once — fine enough to say
/// what can be bought now, coarse enough not to make every poll a change.
pub const GOLD_STEP: i64 = 50;

/// The pace a player's creep score moves at on the frontend: a wave is about
/// a step, so a lane sends a change every half minute or so rather than at
/// each last hit — the win probability weighs it per ten anyway.
pub const CS_STEP: i64 = 10;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "UPPERCASE")]
pub enum Team {
    /// Blue side.
    Order,
    /// Red side.
    Chaos,
}

impl Team {
    pub(crate) fn from_game(value: &str) -> Option<Self> {
        match value {
            "ORDER" => Some(Self::Order),
            "CHAOS" => Some(Self::Chaos),
            _ => None,
        }
    }

    pub fn other(self) -> Self {
        match self {
            Self::Order => Self::Chaos,
            Self::Chaos => Self::Order,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct GamePlayer {
    /// `Name#TAG`; a bot's display name.
    pub riot_id: String,
    /// The champion's alias, as Data Dragon files it (`MonkeyKing`).
    pub champion: String,
    /// Its display name in the game's language (`Wukong`).
    pub champion_name: String,
    pub team: Team,
    /// Upper-case lane; empty in queues without lanes.
    pub position: String,
    pub is_me: bool,
    pub is_bot: bool,
    pub spells: Vec<GameSpell>,
    pub level: i64,
    /// By slot: `0..=5` the inventory, `6` the trinket.
    pub items: Vec<GameItem>,
    pub kills: i64,
    pub deaths: i64,
    pub assists: i64,
    /// Minions and monsters killed, as the scoreboard counts them, floored
    /// to [`CS_STEP`].
    pub creep_score: i64,
    pub dead: bool,
    /// The game time the player comes back at, while dead.
    pub respawn_at: Option<f64>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct GameItem {
    pub item_id: i64,
    pub slot: i64,
    pub count: i64,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct GameSpell {
    /// Data Dragon's id (`SummonerFlash`); empty when the game named it in a
    /// way this does not recognise.
    pub key: String,
    /// Display name in the game's language, the fallback when `key` is empty.
    pub name: String,
}

/// One thing that moved for one player. Values are absolute — the new item
/// list, the new level — so applying a change twice is harmless.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(
    tag = "kind",
    rename_all = "camelCase",
    rename_all_fields = "camelCase"
)]
pub enum GameChange {
    /// Bought, sold, used up, combined or swapped between slots.
    Items {
        player: usize,
        items: Vec<GameItem>,
    },
    LevelUp {
        player: usize,
        level: i64,
    },
    Score {
        player: usize,
        kills: i64,
        deaths: i64,
        assists: i64,
        creep_score: i64,
    },
    Died {
        player: usize,
        respawn_at: f64,
    },
    Respawned {
        player: usize,
    },
    /// Our gold crossed a [`GOLD_STEP`].
    Gold {
        gold: i64,
    },
    /// A turret, an inhibitor, a drake or a Baron fell.
    Objectives {
        objectives: Objectives,
    },
    /// A new minute's sample of our pace.
    Pace {
        pace: Pace,
    },
}

const CHAMPION_PREFIX: &str = "game_character_displayname_";
const SPELL_PREFIX: &str = "GeneratedTip_SummonerSpell_";
const SPELL_SUFFIX: &str = "_DisplayName";

impl GameState {
    /// The state one payload describes, or `None` when it describes no game
    /// yet — no players, as around the loading screen.
    pub fn from_payload(data: &AllGameData) -> Option<Self> {
        let game_time = data.game_data.game_time;
        let players: Vec<GamePlayer> = data
            .all_players
            .iter()
            .filter_map(|player| GamePlayer::from_payload(player, &data.active_player, game_time))
            .collect();
        if players.is_empty() {
            return None;
        }
        Some(Self {
            revision: 0,
            game_time,
            game_mode: data.game_data.game_mode.clone(),
            map_number: data.game_data.map_number,
            my_team: players.iter().find(|player| player.is_me).map(|me| me.team),
            players,
            gold: floor_to_step(data.active_player.current_gold),
            objectives: Objectives::from_payload(data),
            pace: Pace::default(),
        })
    }

    /// Whether `next` is the same ten players, each on the same champion and
    /// side — the condition for describing it as changes to this one.
    pub fn same_roster(&self, next: &GameState) -> bool {
        self.players.len() == next.players.len()
            && self.players.iter().zip(&next.players).all(|(a, b)| {
                a.riot_id == b.riot_id && a.champion == b.champion && a.team == b.team
            })
    }

    /// What moved between this state and `next`, player by player. `None`
    /// when the roster itself differs, which no list of changes describes.
    ///
    /// A death is reported once, with the respawn time read at that moment:
    /// the game's timer counts down on every poll, and repeating it would be
    /// a change per poll for as long as the player is dead.
    pub fn diff(&self, next: &GameState) -> Option<Vec<GameChange>> {
        if !self.same_roster(next) {
            return None;
        }
        let mut changes = Vec::new();
        for (player, (before, after)) in self.players.iter().zip(&next.players).enumerate() {
            if before.items != after.items {
                changes.push(GameChange::Items {
                    player,
                    items: after.items.clone(),
                });
            }
            if before.level != after.level {
                changes.push(GameChange::LevelUp {
                    player,
                    level: after.level,
                });
            }
            if (
                before.kills,
                before.deaths,
                before.assists,
                before.creep_score,
            ) != (after.kills, after.deaths, after.assists, after.creep_score)
            {
                changes.push(GameChange::Score {
                    player,
                    kills: after.kills,
                    deaths: after.deaths,
                    assists: after.assists,
                    creep_score: after.creep_score,
                });
            }
            match (before.dead, after.dead) {
                (false, true) => changes.push(GameChange::Died {
                    player,
                    respawn_at: after.respawn_at.unwrap_or(next.game_time),
                }),
                (true, false) => changes.push(GameChange::Respawned { player }),
                _ => {}
            }
        }
        if self.gold != next.gold {
            changes.push(GameChange::Gold { gold: next.gold });
        }
        if self.objectives != next.objectives {
            changes.push(GameChange::Objectives {
                objectives: next.objectives.clone(),
            });
        }
        if self.pace != next.pace {
            changes.push(GameChange::Pace {
                pace: next.pace.clone(),
            });
        }
        Some(changes)
    }

    /// Apply changes in order. The frontend does the same with the same
    /// changes (`useLiveGame.ts`), which is what keeps the two copies equal.
    pub fn apply(&mut self, changes: &[GameChange]) {
        for change in changes {
            let index = match change {
                GameChange::Items { player, .. }
                | GameChange::LevelUp { player, .. }
                | GameChange::Score { player, .. }
                | GameChange::Died { player, .. }
                | GameChange::Respawned { player } => *player,
                GameChange::Gold { gold } => {
                    self.gold = *gold;
                    continue;
                }
                GameChange::Objectives { objectives } => {
                    self.objectives.clone_from(objectives);
                    continue;
                }
                GameChange::Pace { pace } => {
                    self.pace.clone_from(pace);
                    continue;
                }
            };
            let Some(player) = self.players.get_mut(index) else {
                continue;
            };
            match change {
                GameChange::Items { items, .. } => player.items.clone_from(items),
                GameChange::LevelUp { level, .. } => player.level = *level,
                GameChange::Score {
                    kills,
                    deaths,
                    assists,
                    creep_score,
                    ..
                } => {
                    player.kills = *kills;
                    player.deaths = *deaths;
                    player.assists = *assists;
                    player.creep_score = *creep_score;
                }
                GameChange::Died { respawn_at, .. } => {
                    player.dead = true;
                    player.respawn_at = Some(*respawn_at);
                }
                GameChange::Respawned { .. } => {
                    player.dead = false;
                    player.respawn_at = None;
                }
                GameChange::Gold { .. }
                | GameChange::Objectives { .. }
                | GameChange::Pace { .. } => {}
            }
        }
    }
}

impl GamePlayer {
    /// `None` for a player on neither side, which the board has no place for.
    fn from_payload(
        player: &model::Player,
        me: &model::ActivePlayer,
        game_time: f64,
    ) -> Option<Self> {
        let team = Team::from_game(&player.team)?;
        let champion = player
            .raw_champion_name
            .strip_prefix(CHAMPION_PREFIX)
            .filter(|alias| !alias.is_empty())
            .unwrap_or(&player.champion_name)
            .to_string();
        let riot_id = [&player.riot_id, &player.summoner_name]
            .into_iter()
            .find(|name| !name.is_empty())
            .cloned()
            .unwrap_or_default();

        let mut items: Vec<GameItem> = player
            .items
            .iter()
            .filter(|item| item.item_id > 0)
            .map(|item| GameItem {
                item_id: item.item_id,
                slot: item.slot,
                count: item.count.max(1),
            })
            .collect();
        items.sort_by_key(|item| item.slot);

        let spells = [
            &player.summoner_spells.summoner_spell_one,
            &player.summoner_spells.summoner_spell_two,
        ]
        .into_iter()
        .map(|spell| GameSpell {
            key: spell
                .raw_display_name
                .strip_prefix(SPELL_PREFIX)
                .and_then(|rest| rest.strip_suffix(SPELL_SUFFIX))
                .unwrap_or_default()
                .to_string(),
            name: spell.display_name.clone(),
        })
        .collect();

        Some(Self {
            is_me: is_active_player(player, me),
            riot_id,
            champion,
            champion_name: player.champion_name.clone(),
            team,
            position: player.position.to_uppercase(),
            is_bot: player.is_bot,
            spells,
            level: player.level,
            items,
            kills: player.scores.kills,
            deaths: player.scores.deaths,
            assists: player.scores.assists,
            creep_score: player.scores.creep_score.max(0) / CS_STEP * CS_STEP,
            dead: player.is_dead,
            respawn_at: player
                .is_dead
                .then_some(game_time + player.respawn_timer.max(0.0)),
        })
    }
}

fn floor_to_step(gold: f64) -> i64 {
    // Truncation is the intent: gold not yet in hand buys nothing.
    #[allow(clippy::cast_possible_truncation)]
    let whole = gold.max(0.0) as i64;
    whole / GOLD_STEP * GOLD_STEP
}

/// Whether a player is the one this game client belongs to, on whichever name
/// both sides carry.
pub(crate) fn is_active_player(player: &model::Player, me: &model::ActivePlayer) -> bool {
    let same = |a: &str, b: &str| !a.is_empty() && a.eq_ignore_ascii_case(b);
    same(&me.riot_id, &player.riot_id)
        || same(&me.summoner_name, &player.summoner_name)
        || (player.riot_id.is_empty() && same(&me.riot_id_game_name, &player.riot_id_game_name))
}

#[cfg(test)]
mod tests {
    use super::*;

    fn payload(players: serde_json::Value, game_time: f64) -> AllGameData {
        serde_json::from_value(serde_json::json!({
            "activePlayer": { "riotId": "Me#EUW", "riotIdGameName": "Me", "summonerName": "Me#EUW" },
            "allPlayers": players,
            "gameData": { "gameTime": game_time, "gameMode": "CLASSIC", "mapNumber": 11 }
        }))
        .unwrap()
    }

    fn player(riot_id: &str, alias: &str, team: &str) -> serde_json::Value {
        serde_json::json!({
            "championName": alias,
            "rawChampionName": format!("game_character_displayname_{alias}"),
            "riotId": riot_id,
            "summonerName": riot_id,
            "team": team,
            "position": "MIDDLE",
            "level": 1,
            "items": [],
            "scores": { "kills": 0, "deaths": 0, "assists": 0, "creepScore": 0, "wardScore": 0.0 },
            "summonerSpells": {
                "summonerSpellOne": { "displayName": "Flash", "rawDisplayName": "GeneratedTip_SummonerSpell_SummonerFlash_DisplayName" },
                "summonerSpellTwo": { "displayName": "Ignite", "rawDisplayName": "GeneratedTip_SummonerSpell_SummonerDot_DisplayName" }
            }
        })
    }

    fn duo() -> Vec<serde_json::Value> {
        vec![
            player("Me#EUW", "Ahri", "ORDER"),
            player("Them#EUW", "Orianna", "CHAOS"),
        ]
    }

    fn state(players: Vec<serde_json::Value>, game_time: f64) -> GameState {
        GameState::from_payload(&payload(serde_json::Value::Array(players), game_time)).unwrap()
    }

    #[test]
    fn reads_the_champion_alias_the_spells_and_who_we_are() {
        let state = state(
            vec![
                player("Me#EUW", "MonkeyKing", "ORDER"),
                player("Them#EUW", "Orianna", "CHAOS"),
            ],
            61.0,
        );
        assert_eq!(state.my_team, Some(Team::Order));
        let me = &state.players[0];
        assert!(me.is_me);
        assert!(!state.players[1].is_me);
        assert_eq!(me.champion, "MonkeyKing");
        assert_eq!(me.spells[0].key, "SummonerFlash");
        assert_eq!(me.spells[1].key, "SummonerDot");
    }

    #[test]
    fn a_payload_without_players_is_no_game_yet() {
        assert!(GameState::from_payload(&payload(serde_json::json!([]), 0.0)).is_none());
    }

    #[test]
    fn an_unrecognised_spell_name_keeps_its_display_name() {
        let mut ahri = player("Me#EUW", "Ahri", "ORDER");
        ahri["summonerSpells"]["summonerSpellTwo"] = serde_json::json!({ "displayName": "Unleashed Smite", "rawDisplayName": "SomethingNew" });
        let state = state(vec![ahri], 0.0);
        assert_eq!(state.players[0].spells[1].key, "");
        assert_eq!(state.players[0].spells[1].name, "Unleashed Smite");
    }

    #[test]
    fn items_are_kept_by_slot_and_empty_slots_dropped() {
        let mut ahri = player("Me#EUW", "Ahri", "ORDER");
        ahri["items"] = serde_json::json!([
            { "itemID": 3340, "slot": 6, "count": 1 },
            { "itemID": 2003, "slot": 1, "count": 2 },
            { "itemID": 0, "slot": 2, "count": 0 },
            { "itemID": 1056, "slot": 0, "count": 1 }
        ]);
        let items = &state(vec![ahri], 0.0).players[0].items;
        assert_eq!(
            items
                .iter()
                .map(|i| (i.slot, i.item_id))
                .collect::<Vec<_>>(),
            vec![(0, 1056), (1, 2003), (6, 3340)]
        );
        assert_eq!(items[1].count, 2);
    }

    fn with_gold(gold: f64, game_time: f64) -> GameState {
        let mut data = payload(serde_json::Value::Array(duo()), game_time);
        data.active_player.current_gold = gold;
        GameState::from_payload(&data).unwrap()
    }

    #[test]
    fn our_gold_moves_in_steps_not_by_the_tick() {
        let start = with_gold(512.7, 100.0);
        assert_eq!(start.gold, 500);
        assert_eq!(start.diff(&with_gold(549.9, 102.0)), Some(vec![]));

        let crossed = with_gold(550.0, 104.0);
        assert_eq!(
            start.diff(&crossed),
            Some(vec![GameChange::Gold { gold: 550 }])
        );

        let mut applied = start.clone();
        applied.apply(&[GameChange::Gold { gold: 550 }]);
        assert_eq!(applied.gold, 550);
    }

    #[test]
    fn creep_score_is_a_change_only_when_it_crosses_a_step() {
        let before = state(duo(), 100.0);
        let mut players = duo();
        players[0]["scores"]["creepScore"] = serde_json::json!(9);
        let within = state(players.clone(), 102.0);
        assert_eq!(before.diff(&within), Some(vec![]));

        players[0]["scores"]["creepScore"] = serde_json::json!(14);
        let crossed = state(players, 104.0);
        assert_eq!(
            before.diff(&crossed),
            Some(vec![GameChange::Score {
                player: 0,
                kills: 0,
                deaths: 0,
                assists: 0,
                creep_score: 10
            }])
        );
    }

    #[test]
    fn a_purchase_a_level_and_a_kill_are_one_change_each() {
        let before = state(duo(), 300.0);
        let mut players = duo();
        players[0]["items"] = serde_json::json!([{ "itemID": 3802, "slot": 0, "count": 1 }]);
        players[0]["level"] = serde_json::json!(2);
        players[0]["scores"]["kills"] = serde_json::json!(1);
        let after = state(players, 302.0);
        assert_eq!(
            before.diff(&after).unwrap(),
            vec![
                GameChange::Items {
                    player: 0,
                    items: vec![GameItem {
                        item_id: 3802,
                        slot: 0,
                        count: 1
                    }]
                },
                GameChange::LevelUp {
                    player: 0,
                    level: 2
                },
                GameChange::Score {
                    player: 0,
                    kills: 1,
                    deaths: 0,
                    assists: 0,
                    creep_score: 0
                },
            ]
        );
    }

    #[test]
    fn a_death_is_reported_once_with_its_respawn_time_then_the_respawn() {
        let alive = state(duo(), 300.0);
        let dead_at = |game_time: f64, timer: f64| {
            let mut players = duo();
            players[1]["isDead"] = serde_json::json!(true);
            players[1]["respawnTimer"] = serde_json::json!(timer);
            players[1]["scores"]["deaths"] = serde_json::json!(1);
            state(players, game_time)
        };

        let died = dead_at(302.0, 12.0);
        let changes = alive.diff(&died).unwrap();
        assert!(changes.contains(&GameChange::Died {
            player: 1,
            respawn_at: 314.0
        }));

        let mut current = alive.clone();
        current.apply(&changes);
        // Still dead two seconds later, the timer two seconds lower: nothing new.
        assert_eq!(current.diff(&dead_at(304.0, 10.0)), Some(vec![]));

        let mut players = duo();
        players[1]["scores"]["deaths"] = serde_json::json!(1);
        let back = state(players, 315.0);
        assert_eq!(
            current.diff(&back),
            Some(vec![GameChange::Respawned { player: 1 }])
        );
    }

    #[test]
    fn applying_the_diff_reaches_the_next_state() {
        let before = state(duo(), 300.0);
        let mut players = duo();
        players[0]["items"] = serde_json::json!([{ "itemID": 1056, "slot": 0, "count": 1 }]);
        players[1]["level"] = serde_json::json!(3);
        players[1]["isDead"] = serde_json::json!(true);
        players[1]["respawnTimer"] = serde_json::json!(8.0);
        let after = state(players, 302.0);

        let mut current = before.clone();
        current.apply(&before.diff(&after).unwrap());
        assert_eq!(current.players, after.players);
    }

    #[test]
    fn another_roster_cannot_be_described_as_changes() {
        let before = state(duo(), 0.0);
        let after = state(
            vec![
                player("Me#EUW", "Ahri", "ORDER"),
                player("Them#EUW", "Syndra", "CHAOS"),
            ],
            0.0,
        );
        assert_eq!(before.diff(&after), None);
    }

    #[test]
    fn changes_reach_the_frontend_in_its_own_casing() {
        let change = GameChange::Died {
            player: 3,
            respawn_at: 321.5,
        };
        assert_eq!(
            serde_json::to_value(change).unwrap(),
            serde_json::json!({ "kind": "died", "player": 3, "respawnAt": 321.5 })
        );
        let level = GameChange::LevelUp {
            player: 0,
            level: 6,
        };
        assert_eq!(
            serde_json::to_value(level).unwrap()["kind"],
            serde_json::json!("levelUp")
        );
    }
}
