//! One game recorded from start to recap: the same steps the capture spike
//! measured (`crates/capture-spike`), run by the app.
//!
//! When the phase enters a game and the player turned recording on for its
//! queue, the helper is started — retried every few seconds, since it can fail
//! on the loading screen — and the game clock and live events are followed so
//! the highlights land on the video. When the game ends the video is closed,
//! the recap is opened on it, and the recording is finalised once the match
//! history has the game (its timeline gives the definitive highlights and the
//! objectives).

use std::path::{Path, PathBuf};
use std::sync::{Arc, Mutex};
use std::time::{Duration, SystemTime, UNIX_EPOCH};

use capture_helper::{media, HelperCapture};
use game_recording::anchor::ClockSample;
use game_recording::{Change, GameInfo, GameOutcome, MomentKind, RecordingDir, Session};
use lcu::detail::GameTimeline;
use lcu::live::LiveClient;
use lcu::record::HistoryGame;
use lcu::{GameflowPhase, LcuClient};
use tauri::{AppHandle, Manager};
use tokio::sync::watch;

use super::{emit_library, emit_recap, emit_status, Activity, Recorder, SharedRecorder};
use crate::record::SharedClient;

type SharedSession = Arc<Mutex<Session<HelperCapture>>>;

/// The capture can fail to start on the loading screen: it is tried again
/// while the game runs rather than giving up on the game.
const START_ATTEMPTS: u32 = 6;
const START_RETRY: Duration = Duration::from_secs(5);
/// Game-clock reads, for the anchor; live events, as the fallback highlights.
const CLOCK_EVERY: u32 = 5;
const EVENTS_EVERY: u32 = 2;
const HISTORY_WAIT: Duration = Duration::from_secs(300);
const HISTORY_RETRY: Duration = Duration::from_secs(10);
const THUMBNAIL_WIDTH: u32 = 640;

fn in_game(phase: GameflowPhase) -> bool {
    matches!(phase, GameflowPhase::InProgress | GameflowPhase::Reconnect)
}

fn now_ms() -> i64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_millis() as i64)
        .unwrap_or_default()
}

pub async fn run(
    app: AppHandle,
    recorder: SharedRecorder,
    mut phases: watch::Receiver<GameflowPhase>,
) {
    let Some(helper) = recorder.helper().map(Path::to_path_buf) else {
        return;
    };
    loop {
        if phases
            .wait_for(|phase| *phase == GameflowPhase::InProgress)
            .await
            .is_err()
        {
            return;
        }
        if let Err(error) = record_game(&app, &recorder, &helper, &mut phases).await {
            tracing::warn!(%error, "the game was not recorded");
        }
        recorder.set_activity(Activity::default());
        emit_status(&app, &recorder);
        // A game skipped (recording off, another queue) is only looked at
        // again once it is over.
        if phases.wait_for(|phase| !in_game(*phase)).await.is_err() {
            return;
        }
    }
}

async fn record_game(
    app: &AppHandle,
    recorder: &Recorder,
    helper: &Path,
    phases: &mut watch::Receiver<GameflowPhase>,
) -> Result<(), String> {
    let settings = recorder.settings();
    if !settings.enabled {
        return Ok(());
    }
    // A tape or the simulator stands in for the client's events but not its
    // API — and there is no game window to capture behind them.
    let client = app
        .state::<SharedClient>()
        .read()
        .expect("client lock poisoned")
        .clone();
    let Some(client) = client else {
        return Ok(());
    };
    let game = game_info(&client).await.ok_or("the client named no game")?;
    if !settings.records(game.queue_id) {
        return Ok(());
    }
    if recorder.status().availability != "ready" {
        emit_status(app, recorder);
        return Err("the app cannot record the screen".into());
    }

    let root = recorder.folder();
    let live = LiveClient::new().map_err(|e| e.to_string())?;
    let session: SharedSession = Arc::new(Mutex::new(Session::new(HelperCapture::new(
        helper.to_path_buf(),
        None,
        "window".into(),
        true,
    ))));

    let dir = start(&session, game, &settings, &root, phases).await?;
    let id = RecordingDir::new(dir.clone()).id();
    recorder.set_activity(Activity {
        recording: Some(game.game_id),
        processing: None,
        folder: Some(id.clone()),
    });
    emit_status(app, recorder);
    emit_library(app);

    follow(&session, &live, phases).await;

    let stopped = {
        let session = session.clone();
        let root = root.clone();
        let settings = settings.clone();
        tokio::task::spawn_blocking(move || {
            lock(&session).on_phase(GameflowPhase::EndOfGame, None, &settings, &root, now_ms())
        })
        .await
        .map_err(|e| e.to_string())?
    };
    if let Err(error) = &stopped {
        tracing::warn!(%error, "the recording did not stop cleanly; keeping what was written");
    }
    recorder.set_activity(Activity {
        recording: None,
        processing: Some(game.game_id),
        folder: Some(id.clone()),
    });
    emit_status(app, recorder);
    emit_recap(app, id.clone());
    thumbnail(helper, &dir).await;
    emit_library(app);

    let (history, timeline) = wait_for_history(&client, game.game_id, phases).await;
    let budget = recorder.games_budget();
    let finalised = tokio::task::spawn_blocking(move || {
        lock(&session).finalise(
            GameOutcome {
                game: history.as_ref(),
                timeline: timeline.as_ref(),
            },
            budget,
            &root,
        )
    })
    .await
    .map_err(|e| e.to_string())?
    .map_err(|e| e.to_string())?;
    if let Some(finalised) = finalised {
        if !finalised.pruned.is_empty() {
            tracing::info!(
                count = finalised.pruned.len(),
                "recordings deleted to fit the budget"
            );
        }
    }
    thumbnail(helper, &dir).await;
    emit_library(app);
    Ok(())
}

fn lock(session: &SharedSession) -> std::sync::MutexGuard<'_, Session<HelperCapture>> {
    session.lock().expect("session poisoned")
}

async fn game_info(client: &LcuClient) -> Option<GameInfo> {
    // The game id can trail the phase by a moment.
    for _ in 0..5 {
        if let Ok(Some(session)) = client.gameflow_session().await {
            if session.game_data.game_id > 0 {
                return Some(GameInfo {
                    game_id: session.game_data.game_id,
                    queue_id: session.game_data.queue.id,
                });
            }
        }
        tokio::time::sleep(Duration::from_secs(1)).await;
    }
    None
}

/// Start the helper, again every few seconds while the game runs.
async fn start(
    session: &SharedSession,
    game: GameInfo,
    settings: &game_recording::RecordingSettings,
    root: &Path,
    phases: &watch::Receiver<GameflowPhase>,
) -> Result<PathBuf, String> {
    let mut attempt = 0;
    loop {
        attempt += 1;
        let (session, settings, root) = (session.clone(), settings.clone(), root.to_path_buf());
        let change = tokio::task::spawn_blocking(move || {
            lock(&session).on_phase(
                GameflowPhase::InProgress,
                Some(game),
                &settings,
                &root,
                now_ms(),
            )
        })
        .await
        .map_err(|e| e.to_string())?;
        match change {
            Ok(Change::Started(dir)) => return Ok(dir),
            Ok(other) => return Err(format!("the recording did not start: {other:?}")),
            Err(error) => {
                tracing::warn!(%error, attempt, "the capture did not start");
                if attempt >= START_ATTEMPTS || !in_game(*phases.borrow()) {
                    return Err(format!("the capture never started: {error}"));
                }
                tokio::time::sleep(START_RETRY).await;
            }
        }
    }
}

/// Follow the game until it ends or the capture fails: the clock for the
/// anchor, the live feed for the fallback highlights.
async fn follow(
    session: &SharedSession,
    live: &LiveClient,
    phases: &mut watch::Receiver<GameflowPhase>,
) {
    let mut tick: u32 = 0;
    let mut player_known = false;
    loop {
        tokio::select! {
            _ = tokio::time::sleep(Duration::from_secs(1)) => {}
            _ = phases.changed() => {}
        }
        if !in_game(*phases.borrow()) {
            return;
        }
        tick += 1;
        let failure = lock(session).capture().failure.lock().unwrap().clone();
        if let Some(failure) = failure {
            tracing::warn!(%failure, "the capture stopped on its own");
            return;
        }
        if !player_known {
            if let Ok(player) = live.active_player().await {
                lock(session).note_player(player);
                player_known = true;
            }
        }
        if tick.is_multiple_of(CLOCK_EVERY) {
            let before = lock(session).video_clock();
            let stats = live.game_stats().await;
            let after = lock(session).video_clock();
            if let (Some(before), Ok(stats), Some(after)) = (before, stats, after) {
                lock(session).note_clock(ClockSample {
                    video_before_ms: before,
                    video_after_ms: after,
                    game_time_s: stats.game_time,
                });
            }
        }
        if tick.is_multiple_of(EVENTS_EVERY) {
            if let Ok(events) = live.events().await {
                lock(session).note_events(events);
            }
        }
    }
}

/// The game's line in the player's history and its timeline, once the
/// history lists the game — or nothing, if it never does or the next game
/// starts first.
async fn wait_for_history(
    client: &LcuClient,
    game_id: i64,
    phases: &watch::Receiver<GameflowPhase>,
) -> (Option<HistoryGame>, Option<GameTimeline>) {
    let deadline = tokio::time::Instant::now() + HISTORY_WAIT;
    while tokio::time::Instant::now() < deadline && !in_game(*phases.borrow()) {
        if let Ok(history) = client.match_history(0, 5).await {
            if let Some(game) = history
                .games
                .games
                .into_iter()
                .find(|g| g.game_id == game_id)
            {
                let timeline = client.game_timeline(game_id).await.ok();
                return (Some(game), timeline);
            }
        }
        tokio::time::sleep(HISTORY_RETRY).await;
    }
    (None, None)
}

/// The recording's card image: the player's first kill if there is one, else
/// its first moment, else a third of the way in.
async fn thumbnail(helper: &Path, dir: &Path) {
    let dir = RecordingDir::new(dir.to_path_buf());
    let Ok(meta) = dir.read_meta() else { return };
    let moments = meta.moments();
    let at = moments
        .iter()
        .find(|m| m.kind == MomentKind::Kill)
        .or_else(|| moments.first())
        .map(|m| m.end_video_ms.max(0) as u64)
        .unwrap_or_else(|| meta.duration_ms.unwrap_or(0) / 3);
    let helper = helper.to_path_buf();
    let result = tokio::task::spawn_blocking(move || {
        media::thumbnail(
            &helper,
            &dir.video_path(),
            &dir.thumbnail_path(),
            at,
            THUMBNAIL_WIDTH,
        )
    })
    .await;
    if let Ok(Err(error)) = result {
        tracing::warn!(%error, "no thumbnail for the recording");
    }
}
