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

use std::collections::VecDeque;

use lcu::{ActionKind, ChampSelectSession, ChampionMastery, LcuEvent, Reading, SpellSelection};
use serde::Deserialize;
use shell_state::AppState;

use crate::champselect::{ChampSelectPort, Reply};
use crate::supervisor::{Feed, Source};

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

/// The relay, as a session's source: it answers the readings the page posted
/// since the last ask. The relay down is a client closed: the app says so, and
/// picks the session back up when it answers again.
pub struct SimSource {
    http: reqwest::Client,
    url: String,
    connected: bool,
    generation: u64,
    next: usize,
    pending: VecDeque<Reading>,
    /// The page's latest mastery reading, handed out when the state asks.
    mastery: Option<serde_json::Value>,
}

impl SimSource {
    pub fn new(url: String) -> Self {
        Self {
            http: reqwest::Client::new(),
            url,
            connected: false,
            generation: 0,
            next: 0,
            pending: VecDeque::new(),
            mastery: None,
        }
    }
}

impl Source for SimSource {
    async fn open(&mut self) -> AppState {
        AppState::default()
    }

    async fn next(&mut self) -> Option<Feed> {
        loop {
            while let Some(reading) = self.pending.pop_front() {
                let update = |uri: &str, data| {
                    Feed::Event(LcuEvent {
                        uri: uri.to_string(),
                        event_type: "Update".to_string(),
                        data,
                    })
                };
                return Some(match reading {
                    Reading::Event {
                        uri,
                        event_type,
                        data,
                    } => Feed::Event(LcuEvent {
                        uri,
                        event_type,
                        data,
                    }),
                    Reading::Phase { data } => update(lcu::uri::GAMEFLOW_PHASE, data),
                    Reading::Session { data } => update(lcu::uri::CHAMP_SELECT_SESSION, data),
                    Reading::Summoner { data } => update(lcu::uri::CURRENT_SUMMONER, data),
                    Reading::Game { data } => Feed::Game(data),
                    Reading::Mastery { data } => {
                        self.mastery = Some(data);
                        continue;
                    }
                });
            }
            match fetch(&self.http, &self.url, self.generation, self.next).await {
                Ok(batch) => {
                    let restart =
                        !self.connected || batch.reset || batch.generation != self.generation;
                    self.connected = true;
                    self.generation = batch.generation;
                    self.next = batch.next;
                    if restart {
                        self.mastery = None;
                        self.pending = batch.readings.into();
                        return Some(Feed::Restart);
                    }
                    self.pending.extend(batch.readings);
                }
                Err(error) if self.connected => {
                    tracing::info!(%error, "the draft simulator stopped answering");
                    self.connected = false;
                    self.generation = 0;
                    return Some(Feed::Closed);
                }
                Err(_) => {}
            }
            if self.pending.is_empty() {
                tokio::time::sleep(POLL).await;
            }
        }
    }

    /// The page posts the player's mastery with the login: the one still to be
    /// handed out in this batch, else the latest seen.
    async fn mastery(&mut self) -> Vec<ChampionMastery> {
        let queued = self
            .pending
            .iter()
            .position(|reading| matches!(reading, Reading::Mastery { .. }))
            .and_then(|index| self.pending.remove(index));
        let data = match queued {
            Some(Reading::Mastery { data }) => Some(data),
            _ => self.mastery.clone(),
        };
        data.and_then(|data| serde_json::from_value(data).ok())
            .unwrap_or_default()
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

    async fn champ_select_session(&self) -> lcu::Result<Option<ChampSelectSession>> {
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

    async fn allowed(&self, kind: ActionKind) -> lcu::Result<Vec<i64>> {
        let path = match kind {
            ActionKind::Pick => "/lol-champ-select/v1/pickable-champion-ids",
            ActionKind::Ban => "/lol-champ-select/v1/bannable-champion-ids",
        };
        let body = self.request("GET", path, None).await?;
        serde_json::from_value(body).map_err(|error| lcu::Error::Decode(error.to_string()))
    }

    async fn hover_champion(&self, action_id: i64, champion_id: i64) -> lcu::Result<()> {
        self.request(
            "PATCH",
            &format!("/lol-champ-select/v1/session/actions/{action_id}"),
            Some(serde_json::json!({ "championId": champion_id })),
        )
        .await?;
        Ok(())
    }

    async fn complete_action(&self, action_id: i64) -> lcu::Result<()> {
        self.request(
            "POST",
            &format!("/lol-champ-select/v1/session/actions/{action_id}/complete"),
            None,
        )
        .await?;
        Ok(())
    }
    async fn spells(&self) -> lcu::Result<()> {
        Err(lcu::Error::Decode(
            "the draft simulator does not take summoner spells".to_string(),
        ))
    }
}

impl ChampSelectPort for SimClient {
    fn session(&self) -> Reply<'_, Option<ChampSelectSession>> {
        Box::pin(self.champ_select_session())
    }

    fn allowed(&self, kind: ActionKind) -> Reply<'_, Vec<i64>> {
        Box::pin(SimClient::allowed(self, kind))
    }

    fn hover(&self, action_id: i64, champion_id: i64) -> Reply<'_, ()> {
        Box::pin(self.hover_champion(action_id, champion_id))
    }

    fn complete(&self, action_id: i64) -> Reply<'_, ()> {
        Box::pin(self.complete_action(action_id))
    }

    fn set_spells<'a>(&'a self, _: &'a SpellSelection) -> Reply<'a, ()> {
        Box::pin(self.spells())
    }
}

#[cfg(test)]
mod tests {
    use super::{Batch, SimSource};
    use crate::supervisor::{Feed, Source};
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

    /// A source already connected, holding `readings` as the relay's answer.
    fn source(readings: Batch) -> SimSource {
        let mut source = SimSource::new("http://127.0.0.1:9/unused".into());
        source.connected = true;
        source.generation = readings.generation;
        source.pending = readings.readings.into();
        source
    }

    /// Every event the source hands out, applied as the driver applies them,
    /// the pool read when the state asks for it.
    async fn play(mut source: SimSource) -> AppState {
        let mut state = AppState {
            connected: true,
            ..AppState::default()
        };
        let mut read_for = None;
        while !source.pending.is_empty() {
            match source.next().await {
                Some(Feed::Event(event)) => {
                    state.apply(&event);
                    if state.wants_mastery(read_for.as_deref()) {
                        read_for.clone_from(&state.riot_id);
                        state.set_mastery(&source.mastery().await);
                    }
                }
                Some(_) => {}
                None => break,
            }
        }
        state
    }

    #[tokio::test]
    async fn a_simulated_champion_select_reaches_the_draft() {
        let opening = batch(serde_json::json!([
            event("/lol-summoner/v1/current-summoner", serde_json::json!({ "gameName": "Simulated", "tagLine": "DEV", "summonerLevel": 30, "profileIconId": 29 }), "Update"),
            { "kind": "mastery", "data": [{ "championId": 103, "championPoints": 120000 }, { "championId": 238, "championPoints": 112000 }] },
            event("/lol-gameflow/v1/gameflow-phase", serde_json::json!("ChampSelect"), "Update"),
            event("/lol-champ-select/v1/session", session(103), "Update"),
        ]));
        let state = play(source(opening)).await;

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

    #[tokio::test]
    async fn the_game_starting_ends_the_draft() {
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
        let state = play(source(readings)).await;
        assert!(state.draft.is_none());
        assert_eq!(state.screen(), Screen::InGame);
    }
}
