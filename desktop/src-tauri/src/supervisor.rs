//! Keeps the app attached to whatever client is running.
//!
//! The client's port and token change on every launch, and the player is free
//! to close and reopen it under us. So this is a loop, not a connection: it
//! rediscovers, reconnects, and pushes a fresh state each time, rather than
//! requiring the app to be restarted after the client is.

use std::sync::{Arc, Mutex};
use std::time::Duration;

use lcu::{ChampSelectSession, GameflowPhase, LcuClient};
use tauri::{AppHandle, Emitter};
use tokio::sync::mpsc;

use shell_state::AppState;

/// The event the frontend listens on.
pub const STATE_EVENT: &str = "lcu://state";

/// How long to wait before looking for the client again. Long enough not to
/// spin on a machine where League is simply not installed, short enough that
/// launching the game feels like it connects immediately.
const RECONNECT_DELAY: Duration = Duration::from_secs(2);

pub type SharedState = Arc<Mutex<AppState>>;

fn publish(app: &AppHandle, shared: &SharedState, next: AppState) {
    // Hold the lock only to swap; emitting under it would let a slow listener
    // block the LCU stream.
    {
        let mut guard = shared.lock().expect("state mutex poisoned");
        *guard = next.clone();
    }
    let _ = app.emit(STATE_EVENT, next);
}

/// Run until the app exits.
pub async fn run(app: AppHandle, shared: SharedState) {
    loop {
        match attach(&app, &shared).await {
            Ok(()) => tracing::info!("client disconnected, waiting for it to come back"),
            Err(error) => tracing::debug!(%error, "no client yet"),
        }

        publish(&app, &shared, AppState::default());
        tokio::time::sleep(RECONNECT_DELAY).await;
    }
}

/// One client session: connect, take a first full reading, then follow events
/// until the socket closes.
async fn attach(app: &AppHandle, shared: &SharedState) -> lcu::Result<()> {
    let client = LcuClient::connect().await?;

    // Take a full reading before subscribing. Events only carry *changes*, so
    // an app started mid-champ-select would otherwise show nothing until the
    // next pick.
    let mut state = AppState {
        connected: true,
        phase: client.gameflow_phase().await.unwrap_or(GameflowPhase::None),
        riot_id: client.current_summoner().await.ok().map(|s| s.riot_id()),
        draft: client
            .champ_select_session()
            .await
            .ok()
            .flatten()
            .map(|session| session.draft_state()),
    };
    publish(app, shared, state.clone());

    let (sender, mut receiver) = mpsc::channel(64);
    let credentials = client.credentials().clone();
    let stream = tokio::spawn(async move { lcu::stream_events(credentials, sender).await });

    while let Some(event) = receiver.recv().await {
        let changed = match event.uri.as_str() {
            lcu::uri::GAMEFLOW_PHASE => {
                let phase = GameflowPhase::from_client_value(&event.data.to_string());
                let changed = phase != state.phase;
                state.phase = phase;
                // Leaving champ select must clear the draft, or the panel keeps
                // showing the draft of a game that already started.
                if phase != GameflowPhase::ChampSelect {
                    state.draft = None;
                }
                changed
            }
            lcu::uri::CHAMP_SELECT_SESSION => {
                state.draft = serde_json::from_value::<ChampSelectSession>(event.data.clone())
                    .ok()
                    .map(|session| session.draft_state());
                true
            }
            _ => false,
        };

        if changed {
            publish(app, shared, state.clone());
        }
    }

    stream.abort();
    Ok(())
}
