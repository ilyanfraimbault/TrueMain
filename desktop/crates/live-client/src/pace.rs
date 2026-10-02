//! The player's own pace, minute by minute: creep score and gold earned at
//! each whole minute of the game, for the overlay's stats panel (#1795) — the
//! CS per minute and its curve, the gold per minute.
//!
//! One reading holds only the present, so the history is the feed's to keep
//! (`GameFeed`), one sample appended when the clock crosses a new minute:
//! a change a minute rather than one per poll. Gold *earned* is not in the
//! API — only the gold in hand is — so it is read as what the inventory cost
//! plus the gold in hand: close, but blind to consumables used up and to the
//! part of an item's price lost by selling it.

use serde::{Deserialize, Serialize};

use crate::model::{self, AllGameData};

/// What the player had at one whole minute of the game.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PaceSample {
    pub minute: u32,
    pub cs: i64,
    pub gold: i64,
}

/// The player's samples so far, oldest first; empty when spectating.
#[derive(Debug, Clone, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Pace {
    pub samples: Vec<PaceSample>,
}

impl Pace {
    /// Add `data`'s sample when its clock has reached a minute the history
    /// does not hold yet. Returns whether one was added.
    pub fn record(&mut self, data: &AllGameData) -> bool {
        let minute = (data.game_data.game_time / 60.0).floor();
        if minute < 1.0 {
            return false;
        }
        let minute = minute as u32;
        if self
            .samples
            .last()
            .is_some_and(|last| last.minute >= minute)
        {
            return false;
        }
        let Some(me) = data
            .all_players
            .iter()
            .find(|player| crate::game::is_active_player(player, &data.active_player))
        else {
            return false;
        };
        self.samples.push(PaceSample {
            minute,
            cs: me.scores.creep_score,
            gold: earned(me, &data.active_player),
        });
        true
    }
}

/// Gold earned so far, as far as the API lets it be read: what the inventory
/// cost plus the gold in hand.
fn earned(me: &model::Player, active: &model::ActivePlayer) -> i64 {
    let inventory: i64 = me
        .items
        .iter()
        .map(|item| item.price * item.count.max(1))
        .sum();
    inventory + active.current_gold.floor() as i64
}

#[cfg(test)]
mod tests {
    use super::*;

    fn reading(game_time: f64, cs: i64, gold: f64) -> AllGameData {
        serde_json::from_value(serde_json::json!({
            "activePlayer": { "riotId": "Me#EUW", "currentGold": gold },
            "allPlayers": [{
                "riotId": "Me#EUW", "team": "ORDER",
                "scores": { "creepScore": cs },
                "items": [
                    { "itemID": 1055, "slot": 0, "count": 1, "price": 450 },
                    { "itemID": 2003, "slot": 1, "count": 2, "price": 50 }
                ]
            }],
            "gameData": { "gameTime": game_time }
        }))
        .unwrap()
    }

    #[test]
    fn one_sample_per_whole_minute_from_the_first() {
        let mut pace = Pace::default();
        assert!(!pace.record(&reading(45.0, 0, 500.0)));
        assert!(pace.record(&reading(61.0, 6, 120.4)));
        assert!(!pace.record(&reading(110.0, 14, 300.0)));
        assert!(pace.record(&reading(185.0, 20, 75.0)));
        assert_eq!(
            pace.samples,
            vec![
                PaceSample {
                    minute: 1,
                    cs: 6,
                    gold: 670
                },
                PaceSample {
                    minute: 3,
                    cs: 20,
                    gold: 625
                },
            ]
        );
    }

    #[test]
    fn a_spectator_has_no_pace() {
        let mut pace = Pace::default();
        let mut data = reading(300.0, 40, 200.0);
        data.active_player.riot_id.clear();
        assert!(!pace.record(&data));
        assert!(pace.samples.is_empty());
    }
}
