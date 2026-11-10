//! Keeps the app attached to whatever client is running.
//!
//! The client's port and token change on every launch, and the player is free
//! to close and reopen it under us. So this is a loop, not a connection: it
//! rediscovers, reconnects, and pushes a fresh state each time, rather than
//! requiring the app to be restarted after the client is.
//!
//! A session can also come from a **tape** instead of a client — see
//! `lcu::tape` for why, and `desktop/README.md` for how — or, in development,
//! from the draft simulator page (`sim.rs`). Each is a `Source`; one driver
//! (`drive`) applies what any of them says, reads the player's pool when the
//! state asks for it, and publishes, so a replay or a simulated draft cannot
//! drift from the live path.
//!
//! While the phase is `InProgress` a session also reads the running game
//! (`game.rs`): live, by polling the game's own API; from a tape, by playing
//! the game readings it recorded. Either way the readings go through the same
//! `live_client::GameFeed`.

use std::path::{Path, PathBuf};
use std::sync::{Arc, Mutex};
use std::time::Duration;

use lcu::tape::{Played, Reading, Recorder, Tape};
use lcu::{
    ChampSelectSession, ChampionMastery, CurrentSummoner, GameflowPhase, LcuClient, LcuEvent,
};
use tauri::AppHandle;
use tokio::sync::mpsc;

use shell_state::{AppState, Screen};

use crate::game::Poller;
use crate::record::SharedClient;

/// The app's state changed.
#[derive(Clone, serde::Serialize, specta::Type, tauri_specta::Event)]
#[tauri_specta(event_name = "lcu://state")]
pub struct StateChanged(pub shell_state::AppView);

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

/// Where this run's sessions come from, fixed for the run by how it was
/// started.
pub(crate) enum Origin {
    /// The League client, whenever one runs.
    Client,
    /// A recorded tape (`TRUEMAIN_LCU_REPLAY`), played once.
    Tape(PathBuf),
    /// The dev server's draft simulator (`TRUEMAIN_LCU_SIM`), debug builds only.
    #[cfg(debug_assertions)]
    Simulator(String),
}

impl Origin {
    pub(crate) fn from_env() -> Self {
        #[cfg(debug_assertions)]
        if let Ok(url) = std::env::var(crate::sim::SIM_VAR) {
            return Self::Simulator(url);
        }
        match std::env::var_os(REPLAY_VAR) {
            Some(path) => Self::Tape(PathBuf::from(path)),
            None => Self::Client,
        }
    }
}

pub(crate) fn publish(app: &AppHandle, shared: &SharedState, next: AppState) {
    let in_game = in_game(&next);
    crate::recording::follow(app, next.phase);
    crate::overlay::follow(app, in_game);
    // Hold the lock only to swap; emitting under it would let a slow listener
    // block the LCU stream.
    let previous = {
        let mut guard = shared.lock().expect("state mutex poisoned");
        std::mem::replace(&mut *guard, next.clone())
    };
    count_phase(app, &previous, &next);
    // The loading screen's roster is read off the phase (#1753).
    crate::loading::follow(app, &next);
    crate::contract::emit(app, StateChanged(next.into()));
    // After the state, so the game page is open before its board fills — and
    // a game that ended is cleared on the same publish that left the phase.
    crate::game::follow(app, in_game);
}

/// A champion select or a game the app followed, counted as it opens (#1805).
fn count_phase(app: &AppHandle, previous: &AppState, next: &AppState) {
    let screen = next.screen();
    if screen == previous.screen() {
        return;
    }
    match screen {
        Screen::Draft => crate::telemetry::feature(app, crate::telemetry::Feature::ChampSelect),
        Screen::InGame => crate::telemetry::feature(app, crate::telemetry::Feature::LiveGame),
        _ => {}
    }
}

/// Whether the game should be read: the screen the phase calls for is the
/// game's, by the same rule that opens that screen.
fn in_game(state: &AppState) -> bool {
    state.screen() == Screen::InGame
}

/// Run until the app exits.
pub async fn run(app: AppHandle, shared: SharedState, client: SharedClient) {
    match Origin::from_env() {
        #[cfg(debug_assertions)]
        Origin::Simulator(url) => {
            tracing::info!(%url, "following the draft simulator instead of a client");
            drive(&app, &shared, &mut crate::sim::SimSource::new(url)).await;
        }
        // A tape stands in for the client, so there is nothing to reconnect to:
        // this plays once and leaves the last state on screen to be looked at.
        Origin::Tape(path) => match TapeSource::load(&path) {
            Ok(mut tape) => {
                drive(&app, &shared, &mut tape).await;
                tracing::info!("tape finished; its last state stays on screen");
            }
            Err(error) => tracing::error!(%error, "could not play the tape"),
        },
        Origin::Client => loop {
            match LiveSource::attach(&client).await {
                Ok(mut live) => {
                    drive(&app, &shared, &mut live).await;
                    tracing::info!("client disconnected, waiting for it to come back");
                }
                Err(error) => tracing::debug!(%error, "no client yet"),
            }
            publish(&app, &shared, AppState::default());
            tokio::time::sleep(RECONNECT_DELAY).await;
        },
    }
}

/// What a source says next.
pub(crate) enum Feed {
    /// A change the client pushed.
    Event(LcuEvent),
    /// One reading of the running game, for the game feed.
    Game(serde_json::Value),
    /// A new session on the same source: the state starts over, connected.
    /// Only the draft simulator (debug builds) starts over without reconnecting.
    #[cfg_attr(not(debug_assertions), allow(dead_code))]
    Restart,
    /// The source stopped answering: no client, until it answers again. The
    /// live client's session ends instead; only the simulator reports this.
    #[cfg_attr(not(debug_assertions), allow(dead_code))]
    Closed,
}

/// Where a session's readings come from: the live client, a tape, or the
/// draft simulator.
pub(crate) trait Source {
    /// The state the session opens on.
    async fn open(&mut self) -> AppState;
    /// What the client says next; `None` once the session is over.
    async fn next(&mut self) -> Option<Feed>;
    /// The player's champion mastery, asked when the state wants it
    /// (`AppState::wants_mastery`).
    async fn mastery(&mut self) -> Vec<ChampionMastery>;
    /// The game is on or off: a live source starts or stops reading it.
    fn follow_game(&mut self, _in_game: bool) {}
}

/// One session from `source`, applied and published until it ends.
async fn drive(app: &AppHandle, shared: &SharedState, source: &mut impl Source) {
    let mut state = source.open().await;
    let mut mastery_read_for = None;
    // Before the first publish, so the draft never opens on an empty pool that
    // fills a moment later.
    refresh_pool(source, &mut state, &mut mastery_read_for).await;
    publish(app, shared, state.clone());
    source.follow_game(in_game(&state));

    while let Some(feed) = source.next().await {
        let changed = match feed {
            // What the live poll handed over, or a tape or the simulator
            // recorded: the feed ignores it unless the phase says a game runs.
            Feed::Game(payload) => {
                crate::game::ingest(app, &payload);
                false
            }
            Feed::Restart => {
                state = AppState {
                    connected: true,
                    ..AppState::default()
                };
                mastery_read_for = None;
                true
            }
            Feed::Closed => {
                state = AppState::default();
                mastery_read_for = None;
                true
            }
            Feed::Event(event) => {
                let changed = state.apply(&event);
                // After the event, so a source keeps the login ahead of the
                // read it triggered — the order a replay hands readings out in.
                refresh_pool(source, &mut state, &mut mastery_read_for).await || changed
            }
        };
        if changed {
            publish(app, shared, state.clone());
            source.follow_game(in_game(&state));
        }
    }
}

/// Read the player's pool when `AppState::wants_mastery` says it is due — once
/// per Riot ID. Returns whether the state changed.
///
/// Awaited inside the session's loop rather than spawned: it runs once per
/// login, the events that arrive meanwhile wait, and a spawned read could land
/// after a relog and hand one account the other's pool.
async fn refresh_pool(
    source: &mut impl Source,
    state: &mut AppState,
    read_for: &mut Option<String>,
) -> bool {
    if !state.wants_mastery(read_for.as_deref()) {
        return false;
    }
    read_for.clone_from(&state.riot_id);
    state.set_mastery(&source.mastery().await);
    // The pool was empty for the rule to fire, so it moved only if it filled.
    !state.champion_pool.is_empty()
}

/// The live client: connected, read once in full, then followed through its
/// event stream — and, while a game runs, the game's own API.
struct LiveSource<'a> {
    client: Arc<LcuClient>,
    _lent: Lent<'a>,
    recorder: Option<Recorder>,
    events: mpsc::Receiver<LcuEvent>,
    stream: tokio::task::JoinHandle<lcu::Result<()>>,
    poller: Poller,
    game_sender: mpsc::Sender<serde_json::Value>,
    game_readings: mpsc::Receiver<serde_json::Value>,
}

impl<'a> LiveSource<'a> {
    async fn attach(lent: &'a SharedClient) -> lcu::Result<Self> {
        let client = Arc::new(LcuClient::connect().await?);
        let (sender, events) = mpsc::channel(64);
        let credentials = client.credentials().clone();
        let stream = tokio::spawn(async move { lcu::stream_events(credentials, sender).await });
        // The game's readings come back here rather than being applied by the
        // poll, so this source stays the one place a session's readings are
        // recorded.
        let (game_sender, game_readings) = mpsc::channel(4);
        Ok(Self {
            // Lent to the commands that read the client on demand (the
            // dashboard's record) for as long as this session lasts, and taken
            // back however it ends.
            _lent: Lent::new(lent, client.clone()),
            client,
            recorder: open_recorder(),
            events,
            stream,
            poller: Poller::default(),
            game_sender,
            game_readings,
        })
    }
}

impl Source for LiveSource<'_> {
    /// A full reading before following events: events only carry *changes*, so
    /// an app started mid-champ-select would otherwise show nothing until the
    /// next pick.
    async fn open(&mut self) -> AppState {
        let phase = self
            .client
            .gameflow_phase()
            .await
            .unwrap_or(GameflowPhase::None);
        // Empty whenever the app attached before the player logged in — the
        // usual order. Not an error: the login pushes `CURRENT_SUMMONER`.
        let summoner = self
            .client
            .current_summoner()
            .await
            .inspect_err(|error| tracing::debug!(%error, "no summoner yet"))
            .ok();
        let session = self.client.champ_select_session().await.ok().flatten();

        if let Some(recorder) = &mut self.recorder {
            // Recorded from the parsed models rather than the raw bodies: the
            // app only ever reads these fields, and everything else the client
            // returns — puuids, account ids, the rest of the lobby's names —
            // has no reason to be written to disk.
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
        state
    }

    async fn next(&mut self) -> Option<Feed> {
        tokio::select! {
            event = self.events.recv() => {
                let event = event?;
                // Recorded whether or not the state takes it: an event ignored
                // today is still part of what the client sent. Only the
                // endpoints the app acts on are kept — the client's socket also
                // carries the player's friends, chat and notifications.
                if let Some(recorder) = &mut self.recorder {
                    if lcu::uri::FOLLOWED.contains(&event.uri.as_str()) {
                        recorder.write(Reading::Event {
                            uri: event.uri.clone(),
                            event_type: event.event_type.clone(),
                            data: event.data.clone(),
                        });
                    }
                }
                Some(Feed::Event(event))
            }
            Some(payload) = self.game_readings.recv() => {
                if let Some(recorder) = &mut self.recorder {
                    recorder.write(Reading::Game { data: payload.clone() });
                }
                Some(Feed::Game(payload))
            }
        }
    }

    /// A failed read is an empty pool, not a failed session: the draft works
    /// without it and only ranks less personally.
    async fn mastery(&mut self) -> Vec<ChampionMastery> {
        let mastery = self
            .client
            .champion_mastery()
            .await
            .inspect_err(|error| tracing::debug!(%error, "no champion mastery"))
            .unwrap_or_default();
        if let Some(recorder) = &mut self.recorder {
            record_reading(recorder, |data| Reading::Mastery { data }, &mastery);
        }
        mastery
    }

    fn follow_game(&mut self, in_game: bool) {
        self.poller.follow(in_game, &self.game_sender);
    }
}

impl Drop for LiveSource<'_> {
    fn drop(&mut self) {
        self.poller.stop();
        self.stream.abort();
    }
}

/// The attached client, lent to the on-demand commands until dropped.
struct Lent<'a>(&'a SharedClient);

impl<'a> Lent<'a> {
    fn new(slot: &'a SharedClient, client: Arc<LcuClient>) -> Self {
        *slot.write().expect("client lock poisoned") = Some(client);
        Self(slot)
    }
}

impl Drop for Lent<'_> {
    fn drop(&mut self) {
        *self.0.write().expect("client lock poisoned") = None;
    }
}

/// A session read from a tape: its first reading, then its events and game
/// readings with the pacing they were recorded at. The tape only stands in
/// for the client's answers: the live rule decides when a pool is read, and
/// the tape hands out the readings in the order they were recorded — a tape
/// recorded before the app read mastery replays with an empty pool.
struct TapeSource {
    tape: Tape,
    timeline: std::vec::IntoIter<(Duration, Played)>,
    masteries: std::vec::IntoIter<serde_json::Value>,
    speed: f64,
}

impl TapeSource {
    fn load(path: &Path) -> lcu::Result<Self> {
        let tape = Tape::load(path)?;
        let initial = tape.initial();
        let masteries: Vec<_> = initial
            .mastery
            .into_iter()
            .chain(tape.later_masteries())
            .collect();
        let timeline = tape.timeline();
        let speed = replay_speed();
        tracing::info!(readings = timeline.len(), speed, "playing a tape");
        Ok(Self {
            tape,
            timeline: timeline.into_iter(),
            masteries: masteries.into_iter(),
            speed,
        })
    }
}

impl Source for TapeSource {
    async fn open(&mut self) -> AppState {
        let initial = self.tape.initial();
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
        state
    }

    async fn next(&mut self) -> Option<Feed> {
        let (gap, played) = self.timeline.next()?;
        if self.speed > 0.0 {
            tokio::time::sleep(gap.div_f64(self.speed)).await;
        }
        Some(match played {
            Played::Event(event) => Feed::Event(event),
            Played::Game(payload) => Feed::Game(payload),
        })
    }

    async fn mastery(&mut self) -> Vec<ChampionMastery> {
        tape_mastery(self.masteries.next())
    }
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
