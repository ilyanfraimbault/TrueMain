//! When each neutral objective of Summoner's Rift is next on the map (#2078):
//! the dragon pit (a drake, or the Elder once a team holds the soul), the
//! Voidgrubs, the Rift Herald and Baron Nashor.
//!
//! Every time is derived from the game clock's rules and the kills the event
//! feed announces to every player — what the game's own timers show anyone
//! who looks at the pit, nothing read from memory or the screen. The rules are
//! the current season's, in one table ([`RIFT`]); a patch that moves a timer
//! changes that table and nothing else.
//!
//! A timer is a game time, not a countdown: it moves only when an objective
//! falls, so it is sent with the objectives' change and the frontend counts
//! down against the clock it already runs.

use serde::{Deserialize, Serialize};

use crate::model::{AllGameData, Event};
use crate::objectives::killer_team;

/// The neutral objectives' spawn rules on Summoner's Rift, in game seconds.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct SpawnRules {
    pub drake_first: f64,
    /// After a drake falls, while no team holds the soul.
    pub drake_respawn: f64,
    /// Drakes that give a team the soul; from then on the pit holds the Elder.
    pub soul_drakes: u32,
    /// After the drake that gives the soul falls, and after each Elder.
    pub elder_respawn: f64,
    pub grubs_spawn: f64,
    /// The camp leaves the map then if it still stands (ten seconds later in
    /// combat, which the feed does not tell).
    pub grubs_despawn: f64,
    /// Grubs in the camp; it spawns once a game.
    pub grubs_camp: u32,
    /// Spawns once a game, where the grubs were.
    pub herald_spawn: f64,
    pub herald_despawn: f64,
    pub baron_first: f64,
    pub baron_respawn: f64,
}

/// Season 2026 (V26.01 on, Atakhan removed): drakes from 5:00 and 5:00 after
/// each, the Elder 6:00 after the soul and after each Elder, one camp of
/// three grubs from 8:00 to 14:45, the Herald from 15:00 to 19:45, Baron from
/// 20:00 and 6:00 after each.
pub const RIFT: SpawnRules = SpawnRules {
    drake_first: 300.0,
    drake_respawn: 300.0,
    soul_drakes: 4,
    elder_respawn: 360.0,
    grubs_spawn: 480.0,
    grubs_despawn: 885.0,
    grubs_camp: 3,
    herald_spawn: 900.0,
    herald_despawn: 1185.0,
    baron_first: 1200.0,
    baron_respawn: 360.0,
};

/// One objective's next appearance.
#[derive(Debug, Clone, Copy, Default, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Spawn {
    /// The game time it is on the map from — up while the clock is past it.
    /// `None` once it will not come again this game.
    pub at: Option<f64>,
    /// The game time it leaves the map at if nobody takes it, for the camps
    /// that do.
    pub until: Option<f64>,
}

/// A drake's element, named as the Rift's terrain names it.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum DrakeElement {
    Infernal,
    Ocean,
    Mountain,
    Cloud,
    Hextech,
    Chemtech,
}

impl DrakeElement {
    /// The element the Rift turned to, from `gameData.mapTerrain`; `None`
    /// while it is still `Default`.
    pub fn from_terrain(terrain: &str) -> Option<Self> {
        match terrain.to_ascii_lowercase().as_str() {
            "infernal" => Some(Self::Infernal),
            "ocean" => Some(Self::Ocean),
            "mountain" => Some(Self::Mountain),
            "cloud" => Some(Self::Cloud),
            "hextech" => Some(Self::Hextech),
            "chemtech" => Some(Self::Chemtech),
            _ => None,
        }
    }
}

/// The neutral objectives' timers; only on Summoner's Rift's standard rules.
#[derive(Debug, Clone, Default, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct NeutralTimers {
    /// The dragon pit: a drake, or the Elder when `elder`.
    pub dragon: Spawn,
    /// The pit's next is the Elder: a team holds the soul.
    pub elder: bool,
    /// The element of the pit's next drake, known once the Rift turned to it
    /// (after the second drake: every drake after is of the Rift's element).
    /// The API does not name the first two before they fall.
    pub drake: Option<DrakeElement>,
    pub grubs: Spawn,
    pub herald: Spawn,
    pub baron: Spawn,
}

impl NeutralTimers {
    /// The timers, from every kill the feed holds; `None` off Summoner's
    /// Rift's standard game, whose rules [`RIFT`] does not describe.
    pub fn from_payload(data: &AllGameData) -> Option<Self> {
        if data.game_data.map_number != 11 || data.game_data.game_mode != "CLASSIC" {
            return None;
        }
        Some(Self::from_events(data, &RIFT))
    }

    fn from_events(data: &AllGameData, rules: &SpawnRules) -> Self {
        let mut timers = Self {
            dragon: Spawn::at(rules.drake_first),
            elder: false,
            drake: DrakeElement::from_terrain(&data.game_data.map_terrain),
            grubs: Spawn {
                until: Some(rules.grubs_despawn),
                ..Spawn::at(rules.grubs_spawn)
            },
            herald: Spawn {
                until: Some(rules.herald_despawn),
                ..Spawn::at(rules.herald_spawn)
            },
            baron: Spawn::at(rules.baron_first),
        };
        let mut drakes = [0u32; 2];
        let mut grubs = 0;
        for event in &data.events.events {
            let time = event.event_time;
            match event.event_name.as_str() {
                "DragonKill" => {
                    if !is_elder(event) {
                        if let Some(side) = killer_team(data, event) {
                            drakes[side as usize] += 1;
                        }
                    }
                    timers.elder = drakes.iter().any(|count| *count >= rules.soul_drakes);
                    let respawn = if timers.elder {
                        rules.elder_respawn
                    } else {
                        rules.drake_respawn
                    };
                    timers.dragon = Spawn::at(time + respawn);
                }
                "HordeKill" => {
                    grubs += 1;
                    if grubs >= rules.grubs_camp {
                        timers.grubs = Spawn::GONE;
                    }
                }
                "HeraldKill" => timers.herald = Spawn::GONE,
                "BaronKill" => timers.baron = Spawn::at(time + rules.baron_respawn),
                _ => {}
            }
        }
        timers
    }
}

impl Spawn {
    /// Taken for good: it will not come again this game.
    pub const GONE: Self = Self {
        at: None,
        until: None,
    };

    fn at(time: f64) -> Self {
        Self {
            at: Some(time),
            until: None,
        }
    }
}

pub(crate) fn is_elder(event: &Event) -> bool {
    event.dragon_type.eq_ignore_ascii_case("Elder")
}

#[cfg(test)]
mod tests {
    use super::*;

    fn payload(events: serde_json::Value) -> AllGameData {
        serde_json::from_value(serde_json::json!({
            "allPlayers": [
                { "riotId": "Lumen#0001", "riotIdGameName": "Lumen", "team": "ORDER" },
                { "riotId": "Solenne#0001", "riotIdGameName": "Solenne", "team": "CHAOS" }
            ],
            "events": { "Events": events },
            "gameData": { "gameTime": 1500.0, "gameMode": "CLASSIC", "mapNumber": 11 }
        }))
        .unwrap()
    }

    fn timers(events: serde_json::Value) -> NeutralTimers {
        NeutralTimers::from_payload(&payload(events)).expect("a Rift game has timers")
    }

    fn drake(time: f64, killer: &str) -> serde_json::Value {
        serde_json::json!({ "EventName": "DragonKill", "EventTime": time, "DragonType": "Fire", "KillerName": killer })
    }

    #[test]
    fn before_anything_falls_every_objective_is_due_at_its_first_spawn() {
        let timers = timers(serde_json::json!([{ "EventName": "GameStart", "EventTime": 0.0 }]));
        assert_eq!(timers.dragon, Spawn::at(300.0));
        assert!(!timers.elder);
        assert_eq!(timers.grubs.at, Some(480.0));
        assert_eq!(timers.grubs.until, Some(885.0));
        assert_eq!(timers.herald.at, Some(900.0));
        assert_eq!(timers.herald.until, Some(1185.0));
        assert_eq!(timers.baron, Spawn::at(1200.0));
    }

    #[test]
    fn a_drake_comes_back_five_minutes_after_it_falls_and_baron_six() {
        let timers = timers(serde_json::json!([
            drake(548.3, "Lumen"),
            { "EventName": "BaronKill", "EventTime": 1300.0, "KillerName": "Solenne" }
        ]));
        assert_eq!(timers.dragon.at, Some(848.3));
        assert_eq!(timers.baron.at, Some(1660.0));
    }

    #[test]
    fn a_drake_no_player_landed_still_restarts_the_pit() {
        let timers = timers(serde_json::json!([drake(600.0, "SRU_Dragon_Fire")]));
        assert_eq!(timers.dragon.at, Some(900.0));
    }

    #[test]
    fn the_soul_turns_the_pit_to_the_elder_six_minutes_on_and_after_each_elder() {
        let drakes = [
            drake(330.0, "Lumen"),
            drake(700.0, "Lumen"),
            drake(1050.0, "Solenne"),
            drake(1400.0, "Lumen"),
            drake(1750.0, "Lumen"),
        ];
        let at_soul_point = timers(serde_json::json!(drakes[..4]));
        assert!(!at_soul_point.elder, "three drakes are not the soul");
        assert_eq!(at_soul_point.dragon.at, Some(1700.0));

        let soul = timers(serde_json::json!(drakes));
        assert!(soul.elder);
        assert_eq!(soul.dragon.at, Some(2110.0));

        let mut after_elder = drakes.to_vec();
        after_elder.push(serde_json::json!({ "EventName": "DragonKill", "EventTime": 2150.0, "DragonType": "Elder", "KillerName": "Solenne" }));
        let after_elder = timers(serde_json::json!(after_elder));
        assert!(after_elder.elder);
        assert_eq!(after_elder.dragon.at, Some(2510.0));
    }

    #[test]
    fn the_grubs_camp_is_done_at_its_third_grub_and_the_herald_at_its_kill() {
        let grub = |time: f64, killer: &str| serde_json::json!({ "EventName": "HordeKill", "EventTime": time, "KillerName": killer });
        let two = timers(serde_json::json!([
            grub(490.0, "Lumen"),
            grub(495.0, "Lumen")
        ]));
        assert_eq!(two.grubs.at, Some(480.0), "one grub still stands");

        let done = timers(serde_json::json!([
            grub(490.0, "Lumen"),
            grub(495.0, "Lumen"),
            grub(499.0, "Solenne"),
            { "EventName": "HeraldKill", "EventTime": 940.0, "KillerName": "Solenne" }
        ]));
        assert_eq!(done.grubs, Spawn::GONE);
        assert_eq!(done.herald, Spawn::GONE);
    }

    #[test]
    fn the_next_drake_is_of_the_element_the_rift_turned_to() {
        let mut data = payload(serde_json::json!([]));
        assert_eq!(NeutralTimers::from_payload(&data).unwrap().drake, None);
        data.game_data.map_terrain = "Default".into();
        assert_eq!(NeutralTimers::from_payload(&data).unwrap().drake, None);
        data.game_data.map_terrain = "Mountain".into();
        assert_eq!(
            NeutralTimers::from_payload(&data).unwrap().drake,
            Some(DrakeElement::Mountain)
        );
    }

    #[test]
    fn other_modes_and_maps_have_no_timers() {
        let mut aram = payload(serde_json::json!([]));
        aram.game_data.map_number = 12;
        assert_eq!(NeutralTimers::from_payload(&aram), None);
        let mut swiftplay = payload(serde_json::json!([]));
        swiftplay.game_data.game_mode = "SWIFTPLAY".into();
        assert_eq!(NeutralTimers::from_payload(&swiftplay), None);
    }
}
