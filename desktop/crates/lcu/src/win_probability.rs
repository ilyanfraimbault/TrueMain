//! A finished game's timeline reduced to what the win-probability model reads
//! (#1911): who played which lane on which side, each player's creep score and
//! level once a minute, and the kills, buildings and epic monsters.
//!
//! Serialised in the shape of the site's `WinProbabilityTimeline`
//! (`web/shared/types/win-probability.ts`), which the app hands to the shared
//! `buildWinProbability` to draw the curve and its turning points — the model
//! itself lives on the TypeScript side only, so the app and the site never
//! disagree on a game.
//!
//! The roles come from `HistoryGame::position_of`, so a queue without roles
//! leaves them empty and the curve is not drawn (the model pairs the lanes).

use serde::{Deserialize, Serialize};

use crate::detail::{GameTimeline, TimelineEvent};
use crate::record::HistoryGame;

/// The events the model weighs or lists; the rest of the timeline is left out.
const MODEL_EVENTS: [&str; 3] = ["CHAMPION_KILL", "BUILDING_KILL", "ELITE_MONSTER_KILL"];

#[derive(Debug, Clone, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct WinProbabilityTimeline {
    pub duration_ms: i64,
    pub participants: Vec<WinProbabilityParticipant>,
    pub frames: Vec<WinProbabilityFrame>,
    pub events: Vec<WinProbabilityEvent>,
}

#[derive(Debug, Clone, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct WinProbabilityParticipant {
    pub participant_id: i64,
    pub team_id: i64,
    /// `TOP` … `UTILITY`; empty on a queue without roles.
    pub position: String,
}

#[derive(Debug, Clone, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct WinProbabilityFrame {
    pub ms: i64,
    pub players: Vec<WinProbabilityPlayer>,
}

#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct WinProbabilityPlayer {
    pub participant_id: i64,
    /// Lane minions and jungle camps together.
    pub cs: i64,
    pub level: i64,
}

/// One event, with only the fields its kind carries: the others are left out
/// rather than sent as zeros or empty strings.
#[derive(Debug, Clone, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct WinProbabilityEvent {
    #[serde(rename = "type")]
    pub kind: String,
    pub ms: i64,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub killer_id: Option<i64>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub victim_id: Option<i64>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub assist_ids: Option<Vec<i64>>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub killer_team_id: Option<i64>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub team_id: Option<i64>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub building_type: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub lane_type: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub tower_type: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub monster_type: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub monster_sub_type: Option<String>,
    /// Kills: bounty plus shutdown. The client's timeline may not carry
    /// either, and a kill worth nothing does not exist, so both at zero reads
    /// as unknown — never as a free kill.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub bounty: Option<i64>,
}

impl WinProbabilityTimeline {
    /// Reduce a game's timeline. `game` must be the full scoreboard
    /// (`LcuClient::game`): the history list carries the player alone, and the
    /// model needs all ten.
    pub fn from_game(game: &HistoryGame, timeline: &GameTimeline) -> Self {
        let participants = game
            .participants
            .iter()
            .map(|p| WinProbabilityParticipant {
                participant_id: p.participant_id,
                team_id: p.team_id,
                position: game.position_of(p).unwrap_or_default().to_string(),
            })
            .collect();

        let frames = timeline
            .frames
            .iter()
            .map(|frame| {
                let mut players: Vec<WinProbabilityPlayer> = frame
                    .participant_frames
                    .iter()
                    .filter_map(|(id, f)| {
                        Some(WinProbabilityPlayer {
                            participant_id: id.parse().ok()?,
                            cs: f.minions_killed + f.jungle_minions_killed,
                            level: f.level,
                        })
                    })
                    .collect();
                // The client keys them in a map: ordered, the output is stable.
                players.sort_by_key(|player| player.participant_id);
                WinProbabilityFrame {
                    ms: frame.timestamp,
                    players,
                }
            })
            .collect();

        let events = timeline
            .frames
            .iter()
            .flat_map(|frame| &frame.events)
            .filter(|event| MODEL_EVENTS.contains(&event.kind.as_str()))
            .map(event_of)
            .collect();

        Self {
            duration_ms: game.duration_seconds() * 1000,
            participants,
            frames,
            events,
        }
    }
}

fn event_of(event: &TimelineEvent) -> WinProbabilityEvent {
    let text = |value: &str| Some(value.to_string()).filter(|v| !v.is_empty());
    let mut out = WinProbabilityEvent {
        kind: event.kind.clone(),
        ms: event.timestamp,
        ..Default::default()
    };
    match event.kind.as_str() {
        "CHAMPION_KILL" => {
            out.killer_id = Some(event.killer_id);
            out.victim_id = Some(event.victim_id);
            out.assist_ids = Some(event.assisting_participant_ids.clone());
            out.bounty = (event.bounty > 0 || event.shutdown_bounty > 0)
                .then_some(event.bounty + event.shutdown_bounty);
        }
        "BUILDING_KILL" => {
            out.killer_id = Some(event.killer_id);
            out.team_id = Some(event.team_id);
            out.building_type = text(&event.building_type);
            out.lane_type = text(&event.lane_type);
            out.tower_type = text(&event.tower_type);
        }
        _ => {
            out.killer_id = Some(event.killer_id);
            out.killer_team_id = Some(event.killer_team_id).filter(|team| *team > 0);
            out.monster_type = text(&event.monster_type);
            out.monster_sub_type = text(&event.monster_sub_type);
        }
    }
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    fn game() -> HistoryGame {
        serde_json::from_str(
            r#"{ "gameId": 42, "gameDuration": 1842, "queueId": 420, "mapId": 11,
              "participants": [
                { "participantId": 1, "teamId": 100, "championId": 103 },
                { "participantId": 3, "teamId": 100, "championId": 64 },
                { "participantId": 8, "teamId": 200, "championId": 7 }
              ] }"#,
        )
        .unwrap()
    }

    fn timeline() -> GameTimeline {
        serde_json::from_str(
            r#"{ "frames": [
              { "timestamp": 0, "events": [], "participantFrames": {
                "8": { "minionsKilled": 0, "level": 1 },
                "1": { "minionsKilled": 0, "level": 1 }
              } },
              { "timestamp": 60000, "participantFrames": {
                "1": { "minionsKilled": 7, "jungleMinionsKilled": 2, "level": 3 }
              }, "events": [
                { "type": "ITEM_PURCHASED", "timestamp": 61000, "participantId": 1, "itemId": 1056 },
                { "type": "CHAMPION_KILL", "timestamp": 62000, "killerId": 1, "victimId": 8,
                  "assistingParticipantIds": [3], "bounty": 300, "shutdownBounty": 150 },
                { "type": "CHAMPION_KILL", "timestamp": 63000, "killerId": 0, "victimId": 3 },
                { "type": "BUILDING_KILL", "timestamp": 64000, "killerId": 8, "teamId": 100,
                  "buildingType": "TOWER_BUILDING", "laneType": "MID_LANE", "towerType": "OUTER_TURRET" },
                { "type": "ELITE_MONSTER_KILL", "timestamp": 65000, "killerId": 3, "killerTeamId": 100,
                  "monsterType": "DRAGON", "monsterSubType": "FIRE_DRAGON" }
              ] }
            ] }"#,
        )
        .unwrap()
    }

    #[test]
    fn reads_the_sides_lanes_and_frames() {
        let reduced = WinProbabilityTimeline::from_game(&game(), &timeline());
        assert_eq!(reduced.duration_ms, 1_842_000);
        assert_eq!(
            reduced.participants[1],
            WinProbabilityParticipant {
                participant_id: 3,
                team_id: 100,
                position: "MIDDLE".into()
            }
        );
        let first: Vec<i64> = reduced.frames[0]
            .players
            .iter()
            .map(|p| p.participant_id)
            .collect();
        assert_eq!(first, vec![1, 8]);
        assert_eq!(
            reduced.frames[1].players[0],
            WinProbabilityPlayer {
                participant_id: 1,
                cs: 9,
                level: 3
            }
        );
    }

    #[test]
    fn keeps_only_the_events_the_model_reads() {
        let reduced = WinProbabilityTimeline::from_game(&game(), &timeline());
        let kinds: Vec<&str> = reduced.events.iter().map(|e| e.kind.as_str()).collect();
        assert_eq!(
            kinds,
            vec![
                "CHAMPION_KILL",
                "CHAMPION_KILL",
                "BUILDING_KILL",
                "ELITE_MONSTER_KILL"
            ]
        );
        let kill = &reduced.events[0];
        assert_eq!(kill.assist_ids, Some(vec![3]));
        assert_eq!(kill.bounty, Some(450));
        assert_eq!(reduced.events[1].bounty, None);
        let tower = &reduced.events[2];
        assert_eq!(tower.team_id, Some(100));
        assert_eq!(tower.lane_type.as_deref(), Some("MID_LANE"));
        assert_eq!(tower.tower_type.as_deref(), Some("OUTER_TURRET"));
        assert_eq!(reduced.events[3].killer_team_id, Some(100));
    }

    #[test]
    fn serialises_in_the_sites_shape() {
        let reduced = WinProbabilityTimeline::from_game(&game(), &timeline());
        let json = serde_json::to_value(&reduced).unwrap();
        assert_eq!(json["durationMs"], 1_842_000);
        assert_eq!(json["participants"][0]["teamId"], 100);
        assert_eq!(json["frames"][1]["players"][0]["participantId"], 1);
        let kill = &json["events"][0];
        assert_eq!(kill["type"], "CHAMPION_KILL");
        assert_eq!(kill["assistIds"][0], 3);
        assert!(kill.get("buildingType").is_none());
        assert_eq!(json["events"][3]["monsterSubType"], "FIRE_DRAGON");
    }

    #[test]
    fn a_queue_without_roles_leaves_the_positions_empty() {
        let mut game = game();
        game.queue_id = 450;
        let reduced = WinProbabilityTimeline::from_game(&game, &timeline());
        assert!(reduced.participants.iter().all(|p| p.position.is_empty()));
    }
}
