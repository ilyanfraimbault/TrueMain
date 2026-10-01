//! Keeps the app attached to whatever client is running.
//!
//! The client's port and token change on every launch, and the player is free
//! to close and reopen it under us. So this is a loop, not a connection: it
//! rediscovers, reconnects, and pushes a fresh state each time, rather than
//! requiring the app to be restarted after the client is.
//!
//! A session can also come from a **tape** instead of a client — see
//! `lcu::tape` for why, and `desktop/README.md` for how. Both paths derive the
//! state through the same `AppState::apply`, so a replay cannot drift from the
//! live path.

use std::path::Path;
use std::sync::{Arc, Mutex};
use std::time::Duration;

use lcu::tape::{Reading, Recorder, Tape};
use lcu::{ChampSelectSession, ChampionMastery, CurrentSummoner, GameflowPhase, LcuClient};
use tauri::{AppHandle, Emitter};
use tokio::sync::mpsc;

use shell_state::AppState;

/// The event the frontend listens on.
pub const STATE_EVENT: &str = "lcu://state";

/// How long to wait before looking for the client again. Long enough not to
/// spin on a machine where League is simply not installed, short enough that
/// launching the game feels like it connects immediately.
const RECONNECT_DELAY: Duration = Duration::from_secs(2);

/// Write every reading of the live session to this path.
const RECORD_VAR: &str = "TRUEMAIN_LCU_RECORD";
/// Play this tape instead of looking for a client.
const REPLAY_VAR: &str = "TRUEMAIN_LCU_REPLAY";
/// How fast to play it. `1` is real time, `0` skips every wait.
const SPEED_VAR: &str = "TRUEMAIN_LCU_REPLAY_SPEED";

pub type SharedState = Arc<Mutex<AppState>>;

pub(crate) fn publish(app: &AppHandle, shared: &SharedState, next: AppState) {
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
    // Development only: a champion select played by hand on the dev server's
    // simulator page stands in for the client (`sim.rs`).
    #[cfg(debug_assertions)]
    if let Ok(url) = std::env::var(crate::sim::SIM_VAR) {
        crate::sim::run(&app, &shared, &url).await;
        return;
    }

    if let Some(path) = std::env::var_os(REPLAY_VAR) {
        // A tape stands in for the client, so there is nothing to reconnect to:
        // this plays once and leaves the last state on screen to be looked at.
        match replay(&app, &shared, Path::new(&path)).await {
            Ok(()) => tracing::info!("tape finished; its last state stays on screen"),
            Err(error) => tracing::error!(%error, "could not play the tape"),
        }
        return;
    }

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
    let mut recorder = open_recorder();

    // Take a full reading before subscribing. Events only carry *changes*, so
    // an app started mid-champ-select would otherwise show nothing until the
    // next pick.
    let phase = client.gameflow_phase().await.unwrap_or(GameflowPhase::None);
    // Empty whenever the app attached before the player logged in — the usual
    // order. Not an error: the login pushes `CURRENT_SUMMONER`, which fills it.
    let summoner = client
        .current_summoner()
        .await
        .inspect_err(|error| tracing::debug!(%error, "no summoner yet"))
        .ok();
    let session = client.champ_select_session().await.ok().flatten();

    if let Some(recorder) = &mut recorder {
        // Recorded from the parsed models rather than the raw bodies: the app
        // only ever reads these fields, and everything else the client returns
        // — puuids, account ids, the rest of the lobby's names — has no reason
        // to be written to disk.
        record_reading(recorder, |data| Reading::Phase { data }, &phase);
        if let Some(summoner) = &summoner {
            record_reading(recorder, |data| Reading::Summoner { data }, summoner);
        }
        record_reading(recorder, |data| Reading::Session { data }, &session);
    }

    let mut state = AppState {
        connected: true,
        phase,
        draft: session.map(|session| session.draft_state()),
        ..AppState::default()
    };
    state.set_summoner(summoner.as_ref().filter(|s| !s.game_name.is_empty()));
    // Only when a player is already logged in; otherwise the login event
    // triggers the same read in the loop below. Before the first publish, so
    // the draft never opens on an empty pool that fills a moment later.
    let mut mastery_read_for = None;
    refresh_pool(&client, &mut state, &mut mastery_read_for, &mut recorder).await;
    publish(app, shared, state.clone());

    let (sender, mut receiver) = mpsc::channel(64);
    let credentials = client.credentials().clone();
    let stream = tokio::spawn(async move { lcu::stream_events(credentials, sender).await });

    while let Some(event) = receiver.recv().await {
        let mut changed = state.apply(&event);

        // Recorded before the `changed` test: an event the state ignores today
        // is still part of what the client sent, and a tape that dropped it
        // could not be used to investigate why it was ignored. Only the
        // endpoints the app acts on are kept — the client's socket also carries
        // the player's friends, chat and notifications.
        if let Some(recorder) = &mut recorder {
            if lcu::uri::FOLLOWED.contains(&event.uri.as_str()) {
                recorder.write(Reading::Event {
                    uri: event.uri.clone(),
                    event_type: event.event_type.clone(),
                    data: event.data.clone(),
                });
            }
        }

        // After the event is recorded, so a tape keeps the login ahead of the
        // read it triggered — the order a replay hands readings out in.
        changed |= refresh_pool(&client, &mut state, &mut mastery_read_for, &mut recorder).await;

        if changed {
            publish(app, shared, state.clone());
        }
    }

    stream.abort();
    Ok(())
}

/// Read the player's pool when `AppState::wants_mastery` says it is due — once
/// per Riot ID. Returns whether the state changed.
///
/// Awaited inside the event loop rather than spawned: it runs once per login,
/// the events that arrive meanwhile wait in the channel, and a spawned read
/// could land after a relog and hand one account the other's pool.
async fn refresh_pool(
    client: &LcuClient,
    state: &mut AppState,
    read_for: &mut Option<String>,
    recorder: &mut Option<Recorder>,
) -> bool {
    if !state.wants_mastery(read_for.as_deref()) {
        return false;
    }
    read_for.clone_from(&state.riot_id);

    // A failed read is an empty pool, not a failed attach: the draft works
    // without it and only ranks less personally.
    let mastery = client
        .champion_mastery()
        .await
        .inspect_err(|error| tracing::debug!(%error, "no champion mastery"))
        .unwrap_or_default();
    if let Some(recorder) = recorder {
        record_reading(recorder, |data| Reading::Mastery { data }, &mastery);
    }
    state.set_mastery(&mastery);
    // The pool was empty for the rule to fire, so it moved only if it filled.
    !state.champion_pool.is_empty()
}

/// One session read from a tape: open on its first reading, then deliver its
/// events with the pacing they were recorded at.
async fn replay(app: &AppHandle, shared: &SharedState, path: &Path) -> lcu::Result<()> {
    let tape = Tape::load(path)?;
    let initial = tape.initial();

    let mut state = AppState {
        // A tape only exists because a client was there when it was recorded.
        connected: true,
        phase: initial
            .phase
            .and_then(|data| serde_json::from_value(data).ok())
            .unwrap_or(GameflowPhase::None),
        draft: initial
            .session
            .and_then(|data| serde_json::from_value::<ChampSelectSession>(data).ok())
            .map(|session| session.draft_state()),
        ..AppState::default()
    };
    let summoner = initial
        .summoner
        .and_then(|data| serde_json::from_value::<CurrentSummoner>(data).ok());
    state.set_summoner(summoner.as_ref());

    // The live rule decides when a pool is read; the tape only stands in for
    // the client's answers, handed out in the order they were recorded. A tape
    // recorded before the app read mastery has none, and replays with an empty
    // pool.
    let mut masteries = initial.mastery.into_iter().chain(tape.later_masteries());
    let mut mastery_read_for = None;
    if state.wants_mastery(None) {
        mastery_read_for.clone_from(&state.riot_id);
        state.set_mastery(&tape_mastery(masteries.next()));
    }
    publish(app, shared, state.clone());

    let speed = replay_speed();
    let events = tape.events();
    tracing::info!(events = events.len(), speed, "playing a tape");

    for (gap, event) in events {
        if speed > 0.0 {
            tokio::time::sleep(gap.div_f64(speed)).await;
        }
        let mut changed = state.apply(&event);
        if state.wants_mastery(mastery_read_for.as_deref()) {
            mastery_read_for.clone_from(&state.riot_id);
            state.set_mastery(&tape_mastery(masteries.next()));
            changed |= !state.champion_pool.is_empty();
        }
        if changed {
            publish(app, shared, state.clone());
        }
    }

    Ok(())
}

fn tape_mastery(data: Option<serde_json::Value>) -> Vec<ChampionMastery> {
    data.and_then(|data| serde_json::from_value(data).ok())
        .unwrap_or_default()
}

fn open_recorder() -> Option<Recorder> {
    let path = std::env::var_os(RECORD_VAR)?;
    match Recorder::create(&path) {
        Ok(recorder) => {
            tracing::info!(path = ?path, "recording this session");
            Some(recorder)
        }
        // Recording is a development aid: failing to start one must not stop
        // the app from attaching to the client.
        Err(error) => {
            tracing::error!(%error, path = ?path, "could not start recording");
            None
        }
    }
}

fn record_reading<T: serde::Serialize>(
    recorder: &mut Recorder,
    wrap: impl FnOnce(serde_json::Value) -> Reading,
    value: &T,
) {
    match serde_json::to_value(value) {
        Ok(data) => recorder.write(wrap(data)),
        Err(error) => tracing::warn!(%error, "could not record a reading"),
    }
}

/// `1` is the pace the tape was recorded at; `0` plays it with no waits at all,
/// which is how a scenario is opened on its end state.
fn replay_speed() -> f64 {
    std::env::var(SPEED_VAR)
        .ok()
        .and_then(|raw| raw.parse::<f64>().ok())
        .filter(|speed| *speed >= 0.0)
        .unwrap_or(1.0)
}
