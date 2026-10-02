//! What each team has taken on the map, read off the game's event feed: the
//! turrets and inhibitors it destroyed, the drakes it slew, and whether it
//! holds the Baron's or the Elder's buff. The overlay's win probability
//! weighs them (#1795).
//!
//! Only what the game itself announces to every player goes in. The feed
//! carries every event since the start on every reading, so the state is
//! rebuilt whole each time — it moves only when a new objective falls, never
//! with the poll.

use serde::{Deserialize, Serialize};

use crate::game::Team;
use crate::model::{AllGameData, Event};

/// An inhibitor stands again five minutes after it falls.
pub const INHIBITOR_RESPAWN: f64 = 300.0;
/// Hand of Baron lasts three minutes (it also ends at the holder's death,
/// which the feed does not tie to the buff — the window is the longest it
/// can last).
pub const BARON_BUFF: f64 = 180.0;
/// Aspect of the Dragon (the Elder's) lasts two and a half minutes, with the
/// same caveat.
pub const ELDER_BUFF: f64 = 150.0;

#[derive(Debug, Clone, Default, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Objectives {
    /// Blue side's.
    pub order: TeamObjectives,
    /// Red side's.
    pub chaos: TeamObjectives,
}

#[derive(Debug, Clone, Default, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct TeamObjectives {
    /// Enemy turrets this team destroyed.
    pub turrets: u32,
    /// For each enemy inhibitor this team destroyed, the game time it stands
    /// again — down while the clock is before it.
    pub inhibitors: Vec<f64>,
    /// Elemental drakes this team slew; the Elder is not one of them.
    pub dragons: u32,
    /// The game time this team's latest Baron buff ends at.
    pub baron_until: Option<f64>,
    /// The game time this team's latest Elder buff ends at.
    pub elder_until: Option<f64>,
}

impl TeamObjectives {
    /// Enemy inhibitors this team has down at `game_time`.
    pub fn inhibitors_down(&self, game_time: f64) -> usize {
        self.inhibitors
            .iter()
            .filter(|respawn| game_time < **respawn)
            .count()
    }
}

impl Objectives {
    pub fn team(&self, team: Team) -> &TeamObjectives {
        match team {
            Team::Order => &self.order,
            Team::Chaos => &self.chaos,
        }
    }

    fn team_mut(&mut self, team: Team) -> &mut TeamObjectives {
        match team {
            Team::Order => &mut self.order,
            Team::Chaos => &mut self.chaos,
        }
    }

    /// Everything the feed says each team has taken so far.
    pub fn from_payload(data: &AllGameData) -> Self {
        let mut objectives = Self::default();
        for event in &data.events.events {
            let time = event.event_time;
            match event.event_name.as_str() {
                // A structure is credited to the side that does not own it.
                "TurretKilled" => {
                    if let Some(owner) = side_in(&event.turret_killed) {
                        objectives.team_mut(owner.other()).turrets += 1;
                    }
                }
                "InhibKilled" => {
                    if let Some(owner) = side_in(&event.inhib_killed) {
                        objectives
                            .team_mut(owner.other())
                            .inhibitors
                            .push(time + INHIBITOR_RESPAWN);
                    }
                }
                "DragonKill" => {
                    if let Some(team) = killer_team(data, event) {
                        let taker = objectives.team_mut(team);
                        if event.dragon_type.eq_ignore_ascii_case("Elder") {
                            taker.elder_until = Some(time + ELDER_BUFF);
                        } else {
                            taker.dragons += 1;
                        }
                    }
                }
                "BaronKill" => {
                    if let Some(team) = killer_team(data, event) {
                        objectives.team_mut(team).baron_until = Some(time + BARON_BUFF);
                    }
                }
                _ => {}
            }
        }
        objectives
    }
}

/// The side a structure or a unit belongs to, from the `T1`/`T2` in its
/// name: `T1` is blue side.
fn side_in(name: &str) -> Option<Team> {
    if name.contains("_T1") {
        Some(Team::Order)
    } else if name.contains("_T2") {
        Some(Team::Chaos)
    } else {
        None
    }
}

/// The team of whoever landed the last hit: a player, by any of the names the
/// game may give them, or else a minion or a turret by the side in its name.
fn killer_team(data: &AllGameData, event: &Event) -> Option<Team> {
    let killer = event.killer_name.as_str();
    if killer.is_empty() {
        return None;
    }
    data.all_players
        .iter()
        .find(|player| {
            [
                &player.riot_id,
                &player.riot_id_game_name,
                &player.summoner_name,
            ]
            .into_iter()
            .any(|name| !name.is_empty() && name.eq_ignore_ascii_case(killer))
                || player
                    .riot_id
                    .split('#')
                    .next()
                    .is_some_and(|name| !name.is_empty() && name.eq_ignore_ascii_case(killer))
        })
        .and_then(|player| Team::from_game(&player.team))
        .or_else(|| side_in(killer))
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
            "gameData": { "gameTime": 1500.0 }
        }))
        .unwrap()
    }

    #[test]
    fn structures_go_to_the_side_that_does_not_own_them() {
        let objectives = Objectives::from_payload(&payload(serde_json::json!([
            { "EventName": "TurretKilled", "EventTime": 842.0, "TurretKilled": "Turret_T2_C_05_A", "KillerName": "Lumen" },
            { "EventName": "TurretKilled", "EventTime": 900.0, "TurretKilled": "Turret_T2_L_03_A", "KillerName": "Minion_T1L1S20N1" },
            { "EventName": "TurretKilled", "EventTime": 950.0, "TurretKilled": "Turret_T1_R_03_A", "KillerName": "Solenne" },
            { "EventName": "InhibKilled", "EventTime": 1200.0, "InhibKilled": "Barracks_T2_L1", "KillerName": "Lumen" }
        ])));
        assert_eq!(objectives.order.turrets, 2);
        assert_eq!(objectives.chaos.turrets, 1);
        assert_eq!(objectives.order.inhibitors, vec![1500.0]);
        assert_eq!(objectives.order.inhibitors_down(1499.0), 1);
        assert_eq!(objectives.order.inhibitors_down(1500.0), 0);
    }

    #[test]
    fn drakes_count_and_the_elder_and_baron_are_timed_buffs() {
        let objectives = Objectives::from_payload(&payload(serde_json::json!([
            { "EventName": "DragonKill", "EventTime": 548.3, "DragonType": "Fire", "Stolen": "False", "KillerName": "Lumen" },
            { "EventName": "DragonKill", "EventTime": 900.0, "DragonType": "Water", "KillerName": "Solenne#0001" },
            { "EventName": "DragonKill", "EventTime": 1250.0, "DragonType": "Elder", "KillerName": "Lumen" },
            { "EventName": "BaronKill", "EventTime": 1300.0, "KillerName": "Solenne" },
            { "EventName": "DragonKill", "EventTime": 1310.0, "DragonType": "Air", "KillerName": "Nobody" }
        ])));
        assert_eq!(objectives.order.dragons, 1);
        assert_eq!(objectives.chaos.dragons, 1);
        assert_eq!(objectives.order.elder_until, Some(1400.0));
        assert_eq!(objectives.chaos.baron_until, Some(1480.0));
        assert_eq!(objectives.order.baron_until, None);
    }

    #[test]
    fn no_feed_is_nothing_taken() {
        let data: AllGameData = serde_json::from_value(serde_json::json!({
            "allPlayers": [], "gameData": { "gameTime": 10.0 }
        }))
        .unwrap();
        assert_eq!(Objectives::from_payload(&data), Objectives::default());
    }
}
