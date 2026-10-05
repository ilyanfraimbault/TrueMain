//! A champion select played by hand, for development (#1671).
//!
//! The draft simulator page of the app's dev server (`/dev/draft-sim`) plays a
//! ranked draft one ban and one pick at a time and posts what the League
//! client would send — gameflow phase, summoner, mastery, champion select
//! session — to the dev server's relay (`/__sim/lcu`), as tape readings. With
//! `TRUEMAIN_LCU_SIM` set to that relay (`npm run tauri:sim`), the shell reads
//! them instead of looking for a client and applies each one the way a live
//! session's event is applied, so the app goes through a real champion select,
//! parsing, navigation and all.
//!
//! The game simulator page (`/dev/game-sim`, #1748) does the same for a game:
//! it plays recorded `allgamedata` readings through the same relay, which the
//! shell hands to the game feed as the live poll would.
//!
//! The relay also carries the app's champion select writes the other way
//! (#1909): `SimClient` sends each request the shell would make to the client —
//! the session, the pickable and bannable lists, a hover, a lock — and the page
//! answers it as the client would, then sends the session it changed.
//!
//! Debug builds only. Like a tape, it stays above the transport: a fake client
//! would need a TLS hole in a binary that ships (see `lcu::tape`), and this is
//! not compiled into one.

use std::time::Duration;

use lcu::{ActionKind, ChampSelectSession, ChampionMastery, LcuEvent, Reading};
use serde::Deserialize;
use shell_state::AppState;
use tauri::AppHandle;

use crate::supervisor::{publish, SharedState};

/// The relay to follow instead of a client.
pub const SIM_VAR: &str = "TRUEMAIN_LCU_SIM";

/// Often enough for a click on the page to show at once, on a loopback call.
const POLL: Duration = Duration::from_millis(250);

/// What the relay answers: the readings past the last one seen, or all of a
/// new session's when the page started one over (`reset`).
#[derive(Debug, Deserialize)]
struct Batch {
    generation: u64,
    reset: bool,
    next: usize,
    readings: Vec<Reading>,
}

/// Follow the relay until the app exits. The relay down is a client closed:
/// the app says so, and picks the session back up when it answers again.
pub async fn run(app: &AppHandle, shared: &SharedState, url: &str) {
    tracing::info!(%url, "following the draft simulator instead of a client");
    let http = reqwest::Client::new();
    let mut state = AppState::default();
    let mut generation = 0_u64;
    let mut next = 0_usize;

    loop {
        match fetch(&http, url, generation, next).await {
            Ok(batch) => {
                let mut changed = !state.connected;
                if batch.reset || batch.generation != generation {
                    state = AppState::default();
                    changed = true;
                }
                state.connected = true;
                generation = batch.generation;
                next = batch.next;
                for reading in batch.readings {
                    if let Reading::Game { data } = &reading {
                        // The phase this batch moved to reaches the game feed
                        // first, or a game's opening reading would be dropped
                        // as read outside a game.
                        if changed {
                            publish(app, shared, state.clone());
                            changed = false;
                        }
                        crate::game::ingest(app, data);
                        continue;
                    }
                    changed |= apply(&mut state, reading);
                }
                if changed {
                    publish(app, shared, state.clone());
                }
            }
            Err(error) => {
                if state.connected {
                    tracing::info!(%error, "the draft simulator stopped answering");
                    state = AppState::default();
                    generation = 0;
                    publish(app, shared, state.clone());
                }
            }
        }
        tokio::time::sleep(POLL).await;
    }
}

async fn fetch(
    http: &reqwest::Client,
    url: &str,
    generation: u64,
    after: usize,
) -> reqwest::Result<Batch> {
    http.get(url)
        .query(&[("generation", generation), ("after", after as u64)])
        .send()
        .await?
        .error_for_status()?
        .json()
        .await
}

/// The relay's answer to one request: what the page, standing in for the
/// client, replied.
#[derive(Debug, Deserialize)]
struct Answer {
    status: u16,
    #[serde(default)]
    body: serde_json::Value,
}

/// The client's REST API, as the simulator page plays it: each request goes
/// through the relay to the page, which answers it.
#[derive(Clone)]
pub struct SimClient {
    http: reqwest::Client,
    url: String,
}

impl SimClient {
    pub fn new(url: String) -> Self {
        Self {
            http: reqwest::Client::new(),
            url,
        }
    }

    async fn request(
        &self,
        method: &str,
        path: &str,
        body: Option<serde_json::Value>,
    ) -> lcu::Result<serde_json::Value> {
        let answer: Answer = self
            .http
            .post(&self.url)
            .json(&serde_json::json!({ "op": "request", "method": method, "path": path, "body": body }))
            .send()
            .await?
            .error_for_status()?
            .json()
            .await?;
        if !(200..300).contains(&answer.status) {
            return Err(lcu::Error::UnexpectedStatus {
                status: answer.status,
                path: path.to_string(),
            });
        }
        Ok(answer.body)
    }

    pub async fn champ_select_session(&self) -> lcu::Result<Option<ChampSelectSession>> {
        match self
            .request("GET", lcu::uri::CHAMP_SELECT_SESSION, None)
            .await
        {
            Ok(body) => serde_json::from_value(body)
                .map(Some)
                .map_err(|error| lcu::Error::Decode(error.to_string())),
            Err(lcu::Error::UnexpectedStatus { status: 404, .. }) => Ok(None),
            Err(other) => Err(other),
        }
    }

    pub async fn allowed(&self, kind: ActionKind) -> lcu::Result<Vec<i64>> {
        let path = match kind {
            ActionKind::Pick => "/lol-champ-select/v1/pickable-champion-ids",
            ActionKind::Ban => "/lol-champ-select/v1/bannable-champion-ids",
        };
        let body = self.request("GET", path, None).await?;
        serde_json::from_value(body).map_err(|error| lcu::Error::Decode(error.to_string()))
    }

    pub async fn hover_champion(&self, action_id: i64, champion_id: i64) -> lcu::Result<()> {
        self.request(
            "PATCH",
            &format!("/lol-champ-select/v1/session/actions/{action_id}"),
            Some(serde_json::json!({ "championId": champion_id })),
        )
        .await?;
        Ok(())
    }

    pub async fn complete_action(&self, action_id: i64) -> lcu::Result<()> {
        self.request(
            "POST",
            &format!("/lol-champ-select/v1/session/actions/{action_id}/complete"),
            None,
        )
        .await?;
        Ok(())
    }
}

/// One reading, through the path a live session's events take. Returns whether
/// the state changed.
fn apply(state: &mut AppState, reading: Reading) -> bool {
    let update = |uri: &str, data| LcuEvent {
        uri: uri.to_string(),
        event_type: "Update".to_string(),
        data,
    };
    match reading {
        Reading::Event {
            uri,
            event_type,
            data,
        } => state.apply(&LcuEvent {
            uri,
            event_type,
            data,
        }),
        Reading::Phase { data } => state.apply(&update(lcu::uri::GAMEFLOW_PHASE, data)),
        Reading::Session { data } => state.apply(&update(lcu::uri::CHAMP_SELECT_SESSION, data)),
        Reading::Summoner { data } => state.apply(&update(lcu::uri::CURRENT_SUMMONER, data)),
        Reading::Mastery { data } => {
            let mastery: Vec<ChampionMastery> = serde_json::from_value(data).unwrap_or_default();
            state.set_mastery(&mastery);
            true
        }
        // The game feed's, not the app state's: `run` hands it over.
        Reading::Game { .. } => false,
    }
}

#[cfg(test)]
mod tests {
    use super::{apply, Batch};
    use lcu::GameflowPhase;
    use shell_state::{AppState, Screen};

    /// A player (cell 2, mid) with our top and jungle locked, two enemies locked
    /// and every ban done — what the simulator page sends, in its shape.
    fn session(my_hover: i64) -> serde_json::Value {
        let ally = |cell: i64, champion: i64, intent: i64, position: &str| serde_json::json!({ "cellId": cell, "championId": champion, "championPickIntent": intent, "assignedPosition": position });
        let enemy = |cell: i64, champion: i64| serde_json::json!({ "cellId": cell, "championId": champion, "championPickIntent": 0, "assignedPosition": "" });
        serde_json::json!({
            "localPlayerCellId": 2,
            "myTeam": [ally(0, 777, 0, "top"), ally(1, 233, 0, "jungle"), ally(2, 0, my_hover, "middle"), ally(3, 0, 0, "bottom"), ally(4, 0, 0, "utility")],
            "theirTeam": [enemy(5, 8), enemy(6, 24), enemy(7, 0), enemy(8, 0), enemy(9, 0)],
            "bans": { "myTeamBans": [75, 112, 238, 51, 54], "theirTeamBans": [805, 35, 119, 517, 111] },
            "actions": [[{ "actorCellId": 2, "championId": my_hover, "completed": false, "isAllyAction": true, "isInProgress": true, "type": "pick" }]],
            "timer": { "adjustedTimeLeftInPhase": 26000, "phase": "BAN_PICK" }
        })
    }

    fn batch(readings: serde_json::Value) -> Batch {
        serde_json::from_value(
            serde_json::json!({ "generation": 2, "reset": true, "next": 1, "readings": readings }),
        )
        .expect("the relay's answer parses")
    }

    fn event(uri: &str, data: serde_json::Value, event_type: &str) -> serde_json::Value {
        serde_json::json!({ "kind": "event", "uri": uri, "event_type": event_type, "data": data })
    }

    #[test]
    fn a_simulated_champion_select_reaches_the_draft() {
        let mut state = AppState {
            connected: true,
            ..AppState::default()
        };
        let opening = batch(serde_json::json!([
            event("/lol-summoner/v1/current-summoner", serde_json::json!({ "gameName": "Simulated", "tagLine": "DEV", "summonerLevel": 30, "profileIconId": 29 }), "Update"),
            { "kind": "mastery", "data": [{ "championId": 103, "championPoints": 120000 }, { "championId": 238, "championPoints": 112000 }] },
            event("/lol-gameflow/v1/gameflow-phase", serde_json::json!("ChampSelect"), "Update"),
            event("/lol-champ-select/v1/session", session(103), "Update"),
        ]));
        for reading in opening.readings {
            apply(&mut state, reading);
        }

        assert_eq!(state.phase, GameflowPhase::ChampSelect);
        assert_eq!(state.screen(), Screen::Draft);
        assert_eq!(state.riot_id.as_deref(), Some("Simulated#DEV"));
        assert_eq!(state.champion_pool, vec![103, 238]);
        let draft = state.draft.as_ref().expect("a draft");
        assert_eq!(draft.my_position, "MIDDLE");
        assert_eq!(draft.my_champion, Some(103));
        assert!(!draft.my_champion_locked, "a hover is not a lock");
        assert_eq!(draft.enemy_champions, vec![8, 24]);
        assert_eq!(draft.ally_bans.len(), 5);
        assert_eq!(draft.seconds_left, 26);
    }

    #[test]
    fn the_game_starting_ends_the_draft() {
        let mut state = AppState {
            connected: true,
            ..AppState::default()
        };
        let readings = batch(serde_json::json!([
            event(
                "/lol-gameflow/v1/gameflow-phase",
                serde_json::json!("ChampSelect"),
                "Update"
            ),
            event("/lol-champ-select/v1/session", session(0), "Update"),
            event(
                "/lol-champ-select/v1/session",
                serde_json::Value::Null,
                "Delete"
            ),
            event(
                "/lol-gameflow/v1/gameflow-phase",
                serde_json::json!("InProgress"),
                "Update"
            ),
        ]));
        for reading in readings.readings {
            apply(&mut state, reading);
        }
        assert!(state.draft.is_none());
        assert_eq!(state.screen(), Screen::InGame);
    }
}
