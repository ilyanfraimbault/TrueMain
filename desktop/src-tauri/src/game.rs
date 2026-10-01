//! The running game, read through its own API while the phase is
//! `InProgress` (#1748), and handed to the frontend as changes.
//!
//! The derivation, the diff and the rule for when a game ends live in
//! `live_client::GameFeed`, shared by the live poll, a tape replay and the
//! simulator. This module only owns the poll's task and the two events.

use std::sync::{Arc, Mutex};

use lcu::LiveClient;
use live_client::{Emission, GameFeed, GameState};
use tauri::{AppHandle, Emitter, Manager};
use tokio::sync::mpsc;
use tokio::task::JoinHandle;

/// The whole game state, replacing the frontend's — `null` once no game runs.
pub const SNAPSHOT_EVENT: &str = "game://snapshot";
/// The changes one reading brought (`live_client::GameUpdate`).
pub const UPDATE_EVENT: &str = "game://update";

pub type SharedGame = Arc<Mutex<GameFeed>>;

/// Run one step of the feed and send what it yields. The lock is released
/// before emitting, so a slow listener never holds up `current_game`.
fn step(app: &AppHandle, run: impl FnOnce(&mut GameFeed) -> Option<Emission>) {
    let emission = {
        let game = app.state::<SharedGame>();
        let mut feed = game.lock().expect("game mutex poisoned");
        run(&mut feed)
    };
    match emission {
        Some(Emission::Snapshot(state)) => {
            let _ = app.emit(SNAPSHOT_EVENT, state);
        }
        Some(Emission::Update(update)) => {
            let _ = app.emit(UPDATE_EVENT, update);
        }
        None => {}
    }
}

/// Tell the feed whether a game runs. Called with every state the app
/// publishes (`supervisor::publish`), so the feed follows the phase on every
/// path — live, tape and simulator — through the one rule `AppState::screen`.
pub fn follow(app: &AppHandle, in_game: bool) {
    step(app, |feed| feed.follow(in_game));
}

/// One `allgamedata` reading, from the poll, a tape or the simulator.
pub fn ingest(app: &AppHandle, payload: &serde_json::Value) {
    step(app, |feed| feed.ingest(payload));
}

/// The game as the frontend should have it, read on mount — the updates it
/// then follows carry the revisions after this one.
#[tauri::command]
pub fn current_game(game: tauri::State<'_, SharedGame>) -> Option<GameState> {
    game.lock().expect("game mutex poisoned").current().cloned()
}

/// The poll of the game's API, running exactly while a game does.
#[derive(Default)]
pub struct Poller {
    task: Option<JoinHandle<()>>,
}

impl Poller {
    /// Start polling when a game starts, stop when it ends. Each reading is
    /// sent back raw, so the session loop records it before it is derived.
    pub fn follow(&mut self, in_game: bool, readings: &mpsc::Sender<serde_json::Value>) {
        match (in_game, self.task.is_some()) {
            (true, false) => self.task = Some(tokio::spawn(poll(readings.clone()))),
            (false, true) => self.stop(),
            _ => {}
        }
    }

    pub fn stop(&mut self) {
        if let Some(task) = self.task.take() {
            task.abort();
        }
    }
}

impl Drop for Poller {
    fn drop(&mut self) {
        self.stop();
    }
}

async fn poll(readings: mpsc::Sender<serde_json::Value>) {
    let client = match LiveClient::new() {
        Ok(client) => client,
        Err(error) => {
            tracing::error!(%error, "cannot read the game");
            return;
        }
    };
    tracing::info!("a game is running; reading it");
    let mut failures = 0_u32;
    loop {
        match client.all_game_data().await {
            Ok(payload) => {
                failures = 0;
                if readings.send(payload).await.is_err() {
                    return;
                }
            }
            // The usual answer until the game has loaded: not worth more
            // than a debug line, every couple of seconds.
            Err(error) => {
                failures = failures.saturating_add(1);
                tracing::debug!(%error, failures, "the game does not answer yet");
            }
        }
        tokio::time::sleep(live_client::next_poll(failures)).await;
    }
}
