//! One game's recording, from the game window opening to the highlights
//! written beside the video.
//!
//! Driven by the gameflow phase the supervisor already follows: a game in
//! progress starts a recording, leaving it stops the video, and the
//! recording is then finalised once the match history has the game. The
//! capture itself sits behind [`Capture`], so this runs — and is tested —
//! without a screen, a GPU or a game.

use std::path::{Path, PathBuf};

use lcu::detail::GameTimeline;
use lcu::live::{ActivePlayer, LiveEvent};
use lcu::record::HistoryGame;
use lcu::GameflowPhase;

use crate::anchor::{Anchor, ClockSample};
use crate::highlights::{self, HighlightSource};
use crate::moments;
use crate::settings::{Quality, RecordingSettings};
use crate::store::{RecordingDir, RecordingMeta, RecordingStatus, Store, StoredRecording};

/// The screen capture and encoder, whichever #1745 settles on.
pub trait Capture {
    /// Start recording the game window into `video_path`, at `quality`
    /// (derived into an encoder setting by [`Quality::output_for`]).
    fn start(&mut self, video_path: &Path, quality: Quality) -> Result<(), CaptureError>;

    /// Milliseconds of video written so far; `None` when not recording. The
    /// video clock the game clock is anchored against.
    fn elapsed_ms(&self) -> Option<u64>;

    /// Stop and close the file. Returns the video's duration.
    fn stop(&mut self) -> Result<u64, CaptureError>;
}

#[derive(Debug, Clone, PartialEq, Eq, thiserror::Error)]
#[error("capture failed: {0}")]
pub struct CaptureError(pub String);

#[derive(Debug, thiserror::Error)]
pub enum SessionError {
    #[error(transparent)]
    Capture(#[from] CaptureError),
    #[error("could not write the recording: {0}")]
    Io(#[from] std::io::Error),
}

/// The game a phase change is about, read from the gameflow session.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct GameInfo {
    pub game_id: i64,
    pub queue_id: i64,
}

/// What a phase change did.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum Change {
    None,
    /// A recording started in this folder.
    Started(PathBuf),
    /// The video was closed; the recording waits for [`Session::finalise`].
    Stopped(PathBuf),
}

/// What the match history says about the game once it has it.
#[derive(Debug, Clone, Copy, Default)]
pub struct GameOutcome<'a> {
    /// The game as the player's own history lists it — their line alone, which
    /// gives their participant id, champion and result.
    pub game: Option<&'a HistoryGame>,
    pub timeline: Option<&'a GameTimeline>,
}

struct Active {
    dir: RecordingDir,
    meta: RecordingMeta,
    samples: Vec<ClockSample>,
    kills: Vec<LiveEvent>,
    last_event_id: Option<i64>,
    player: Option<ActivePlayer>,
}

enum State {
    Idle,
    Recording(Active),
    /// The video is closed and the recording awaits the match history.
    Stopped(Active),
}

pub struct Session<C: Capture> {
    capture: C,
    state: State,
}

impl<C: Capture> Session<C> {
    pub fn new(capture: C) -> Self {
        Self {
            capture,
            state: State::Idle,
        }
    }

    pub fn capture(&self) -> &C {
        &self.capture
    }

    pub fn is_recording(&self) -> bool {
        matches!(self.state, State::Recording(_))
    }

    /// Follow the gameflow. `game` is the gameflow session's game, read when
    /// the phase moved into a game; `default_root` is where recordings go when
    /// the player chose no folder.
    pub fn on_phase(
        &mut self,
        phase: GameflowPhase,
        game: Option<GameInfo>,
        settings: &RecordingSettings,
        default_root: &Path,
        now_ms: i64,
    ) -> Result<Change, SessionError> {
        let in_game = matches!(phase, GameflowPhase::InProgress | GameflowPhase::Reconnect);
        match (&self.state, in_game) {
            (State::Recording(_), true) => Ok(Change::None),
            (State::Recording(_), false) => self.stop(),
            (_, true) if phase == GameflowPhase::InProgress => {
                let Some(game) = game.filter(|g| g.game_id > 0 && settings.records(g.queue_id))
                else {
                    return Ok(Change::None);
                };
                let root = settings.folder.as_deref().unwrap_or(default_root);
                // A new game before the last one was finalised: keep what the
                // last one has rather than lose it. A failure there is the
                // last game's, and must not cost this one its recording.
                if matches!(self.state, State::Stopped(_)) {
                    if let Err(error) =
                        self.finalise(GameOutcome::default(), settings.budget_bytes, root)
                    {
                        tracing::warn!(%error, "could not finalise the previous recording");
                    }
                }
                self.start(game, settings.quality, root, now_ms)
            }
            _ => Ok(Change::None),
        }
    }

    fn start(
        &mut self,
        game: GameInfo,
        quality: Quality,
        root: &Path,
        now_ms: i64,
    ) -> Result<Change, SessionError> {
        let meta = RecordingMeta::new(game.game_id, game.queue_id, quality, now_ms);
        let dir = Store::new(root).create(&meta)?;
        if let Err(error) = self.capture.start(&dir.video_path(), quality) {
            // Nothing was recorded: leave no empty recording behind.
            let _ = dir.delete();
            return Err(error.into());
        }
        let path = dir.path().to_path_buf();
        self.state = State::Recording(Active {
            dir,
            meta,
            samples: Vec::new(),
            kills: Vec::new(),
            last_event_id: None,
            player: None,
        });
        Ok(Change::Started(path))
    }

    fn stop(&mut self) -> Result<Change, SessionError> {
        let State::Recording(mut active) = std::mem::replace(&mut self.state, State::Idle) else {
            return Ok(Change::None);
        };
        let stopped = self.capture.stop();
        active.meta.status = RecordingStatus::Processing;
        active.meta.duration_ms = stopped.as_ref().ok().copied();
        active.meta.anchor = Anchor::fit(&active.samples);
        // Written now, so a crash before the history has the game still
        // leaves the anchor and the live highlights.
        active.meta.highlights = active
            .player
            .as_ref()
            .map(|player| highlights::from_live(&active.kills, player))
            .unwrap_or_default();
        active.meta.highlights_source = active.player.as_ref().map(|_| HighlightSource::Live);
        let written = active.dir.write_meta(&active.meta);
        let path = active.dir.path().to_path_buf();
        self.state = State::Stopped(active);
        stopped?;
        written?;
        Ok(Change::Stopped(path))
    }

    /// The video clock, while a game records — read just before and just after
    /// a game-clock read to make a [`ClockSample`].
    pub fn video_clock(&self) -> Option<u64> {
        match self.state {
            State::Recording(_) => self.capture.elapsed_ms(),
            _ => None,
        }
    }

    pub fn note_clock(&mut self, sample: ClockSample) {
        if let State::Recording(active) = &mut self.state {
            active.samples.push(sample);
        }
    }

    pub fn note_player(&mut self, player: ActivePlayer) {
        if let State::Recording(active) = &mut self.state {
            active.player = Some(player);
        }
    }

    /// The live feed as read; only champion kills not seen yet are kept.
    pub fn note_events(&mut self, events: Vec<LiveEvent>) {
        let State::Recording(active) = &mut self.state else {
            return;
        };
        for event in events {
            if active
                .last_event_id
                .is_some_and(|last| event.event_id <= last)
            {
                continue;
            }
            active.last_event_id = Some(event.event_id);
            if event.event_name == LiveEvent::CHAMPION_KILL {
                active.kills.push(event);
            }
        }
    }

    /// Resolve the stopped recording's highlights from the match history, mark
    /// it ready and bring the folder within budget. Without a timeline the
    /// live highlights written at the stop stay. `None` when no recording was
    /// waiting.
    pub fn finalise(
        &mut self,
        outcome: GameOutcome<'_>,
        budget_bytes: u64,
        root: &Path,
    ) -> Result<Option<Finalised>, SessionError> {
        let State::Stopped(mut active) = std::mem::replace(&mut self.state, State::Idle) else {
            return Ok(None);
        };
        let me = outcome.game.and_then(|game| game.participants.first());
        if let Some(me) = me {
            active.meta.champion_id = Some(me.champion_id).filter(|id| *id > 0);
            active.meta.win = Some(me.stats.win);
            active.meta.kills = Some(me.stats.kills);
            active.meta.deaths = Some(me.stats.deaths);
            active.meta.assists = Some(me.stats.assists);
        }
        if let (Some(me), Some(timeline)) = (me, outcome.timeline) {
            active.meta.highlights = highlights::from_timeline(timeline, me.participant_id);
            active.meta.highlights_source = Some(HighlightSource::Timeline);
            let my_team = match me.team_id {
                100 | 200 => me.team_id,
                _ if me.participant_id <= 5 => 100,
                _ => 200,
            };
            active.meta.objectives = moments::objectives(timeline, my_team);
        }
        active.meta.status = RecordingStatus::Ready;
        active.dir.write_meta(&active.meta)?;

        let pruned = Store::new(root).prune(budget_bytes, Some(active.dir.path()))?;
        Ok(Some(Finalised {
            dir: active.dir,
            meta: active.meta,
            pruned,
        }))
    }

    /// The game the stopped recording waits on, so the shell knows which game
    /// to ask the history for.
    pub fn awaiting(&self) -> Option<i64> {
        match &self.state {
            State::Stopped(active) => Some(active.meta.game_id),
            _ => None,
        }
    }
}

#[derive(Debug)]
pub struct Finalised {
    pub dir: RecordingDir,
    pub meta: RecordingMeta,
    /// Recordings deleted to fit the budget.
    pub pruned: Vec<StoredRecording>,
}

#[cfg(test)]
mod tests {
    use std::fs;
    use std::sync::{Arc, Mutex};

    use super::*;
    use crate::highlights::HighlightKind;
    use crate::settings::{FrameRate, Queues, Resolution};

    /// Writes a placeholder video and keeps a clock the test sets by hand.
    #[derive(Clone, Default)]
    struct FakeCapture {
        clock: Arc<Mutex<Option<u64>>>,
        fail_start: bool,
        started: Arc<Mutex<Vec<(PathBuf, Quality)>>>,
    }

    impl FakeCapture {
        fn set_clock(&self, ms: u64) {
            *self.clock.lock().unwrap() = Some(ms);
        }
    }

    impl Capture for FakeCapture {
        fn start(&mut self, video_path: &Path, quality: Quality) -> Result<(), CaptureError> {
            if self.fail_start {
                return Err(CaptureError("no game window".into()));
            }
            fs::write(video_path, b"video").unwrap();
            self.started
                .lock()
                .unwrap()
                .push((video_path.to_path_buf(), quality));
            *self.clock.lock().unwrap() = Some(0);
            Ok(())
        }

        fn elapsed_ms(&self) -> Option<u64> {
            *self.clock.lock().unwrap()
        }

        fn stop(&mut self) -> Result<u64, CaptureError> {
            Ok(self.clock.lock().unwrap().take().unwrap_or_default())
        }
    }

    fn settings() -> RecordingSettings {
        RecordingSettings {
            enabled: true,
            queues: Queues::All,
            quality: Quality {
                resolution: Resolution::P720,
                frame_rate: FrameRate::Fps60,
            },
            ..RecordingSettings::default()
        }
    }

    const GAME: GameInfo = GameInfo {
        game_id: 7_100_000_001,
        queue_id: 420,
    };

    fn history_game() -> HistoryGame {
        serde_json::from_str(
            r#"{ "gameId": 7100000001, "queueId": 420,
                 "participantIdentities": [{ "participantId": 4, "player": { "gameName": "Me", "tagLine": "EUW" } }],
                 "participants": [{ "participantId": 4, "teamId": 100, "championId": 103, "stats": { "win": true } }] }"#,
        )
        .unwrap()
    }

    fn timeline() -> GameTimeline {
        serde_json::from_str(
            r#"{ "frames": [{ "timestamp": 0, "participantFrames": {}, "events": [
                 { "type": "CHAMPION_KILL", "timestamp": 95000, "killerId": 4, "victimId": 8, "assistingParticipantIds": [] },
                 { "type": "CHAMPION_KILL", "timestamp": 400000, "killerId": 9, "victimId": 4, "assistingParticipantIds": [] }
               ] }] }"#,
        )
        .unwrap()
    }

    fn live_kill(id: i64, time: f64) -> LiveEvent {
        LiveEvent {
            event_id: id,
            event_name: LiveEvent::CHAMPION_KILL.into(),
            event_time: time,
            killer_name: "Me".into(),
            victim_name: "Foe".into(),
            assisters: Vec::new(),
        }
    }

    fn me() -> ActivePlayer {
        ActivePlayer {
            riot_id: "Me#EUW".into(),
            riot_id_game_name: "Me".into(),
            summoner_name: String::new(),
        }
    }

    /// Play a game through: start, a few clock reads with the video 20 s
    /// ahead of the game clock, two live kills, and the end of the game.
    fn play(session: &mut Session<FakeCapture>, root: &Path) -> PathBuf {
        let change = session
            .on_phase(
                GameflowPhase::InProgress,
                Some(GAME),
                &settings(),
                root,
                1_000,
            )
            .unwrap();
        let Change::Started(path) = change else {
            panic!("expected a start, got {change:?}");
        };
        session.note_player(me());
        for second in [30u64, 60, 90, 120] {
            let capture = session.capture().clone();
            capture.set_clock(second * 1000 + 20_000);
            let before = session.video_clock().unwrap();
            session.note_clock(ClockSample {
                video_before_ms: before,
                video_after_ms: before + 6,
                game_time_s: second as f64,
            });
        }
        session.note_events(vec![live_kill(3, 95.0)]);
        // The feed is read whole every time: a kill already seen is skipped.
        session.note_events(vec![live_kill(3, 95.0), live_kill(5, 210.0)]);
        session.capture().set_clock(1_500_000);
        let change = session
            .on_phase(GameflowPhase::EndOfGame, None, &settings(), root, 2_000)
            .unwrap();
        assert_eq!(change, Change::Stopped(path.clone()));
        path
    }

    #[test]
    fn records_a_game_and_resolves_its_highlights_from_the_timeline() {
        let root = tempfile::tempdir().unwrap();
        let mut session = Session::new(FakeCapture::default());
        let path = play(&mut session, root.path());

        // Stopped: the live highlights are already on disk.
        let waiting = RecordingDir::new(path.clone()).read_meta().unwrap();
        assert_eq!(waiting.status, RecordingStatus::Processing);
        assert_eq!(waiting.duration_ms, Some(1_500_000));
        assert_eq!(waiting.highlights_source, Some(HighlightSource::Live));
        assert_eq!(waiting.highlights.len(), 2);
        assert_eq!(session.awaiting(), Some(GAME.game_id));

        let game = history_game();
        let timeline = timeline();
        let finalised = session
            .finalise(
                GameOutcome {
                    game: Some(&game),
                    timeline: Some(&timeline),
                },
                u64::MAX,
                root.path(),
            )
            .unwrap()
            .unwrap();

        let meta = finalised.meta;
        assert_eq!(meta.status, RecordingStatus::Ready);
        assert_eq!(meta.champion_id, Some(103));
        assert_eq!(meta.win, Some(true));
        assert_eq!(meta.highlights_source, Some(HighlightSource::Timeline));
        let kinds: Vec<_> = meta.highlights.iter().map(|h| h.kind).collect();
        assert_eq!(kinds, vec![HighlightKind::Kill, HighlightKind::Death]);
        // The kill at 1:35 of game time is 20 s further into the video.
        let anchor = meta.anchor.unwrap();
        assert!((anchor.video_ms(95_000) - 115_003).abs() <= 1);
        assert_eq!(
            RecordingDir::new(path).read_meta().unwrap().status,
            RecordingStatus::Ready
        );
        assert_eq!(session.awaiting(), None);
    }

    #[test]
    fn without_a_timeline_the_live_highlights_stay() {
        let root = tempfile::tempdir().unwrap();
        let mut session = Session::new(FakeCapture::default());
        play(&mut session, root.path());

        let game = history_game();
        let meta = session
            .finalise(
                GameOutcome {
                    game: Some(&game),
                    timeline: None,
                },
                u64::MAX,
                root.path(),
            )
            .unwrap()
            .unwrap()
            .meta;
        assert_eq!(meta.highlights_source, Some(HighlightSource::Live));
        assert_eq!(meta.highlights.len(), 2);
        assert_eq!(meta.champion_id, Some(103));
    }

    #[test]
    fn records_at_the_chosen_quality_into_the_chosen_folder() {
        let root = tempfile::tempdir().unwrap();
        let chosen = tempfile::tempdir().unwrap();
        let capture = FakeCapture::default();
        let mut session = Session::new(capture.clone());
        let settings = RecordingSettings {
            folder: Some(chosen.path().to_path_buf()),
            ..settings()
        };
        session
            .on_phase(
                GameflowPhase::InProgress,
                Some(GAME),
                &settings,
                root.path(),
                0,
            )
            .unwrap();

        let started = capture.started.lock().unwrap();
        assert!(started[0].0.starts_with(chosen.path()));
        assert_eq!(started[0].1, settings.quality);
    }

    #[test]
    fn records_nothing_when_off_or_for_a_queue_left_out() {
        let root = tempfile::tempdir().unwrap();
        let mut session = Session::new(FakeCapture::default());
        let off = RecordingSettings {
            enabled: false,
            ..settings()
        };
        let ranked_only = RecordingSettings {
            queues: Queues::Ranked,
            ..settings()
        };
        let aram = GameInfo {
            queue_id: 450,
            ..GAME
        };

        for (settings, game) in [
            (&off, Some(GAME)),
            (&ranked_only, Some(aram)),
            (&settings(), None),
        ] {
            let change = session
                .on_phase(GameflowPhase::InProgress, game, settings, root.path(), 0)
                .unwrap();
            assert_eq!(change, Change::None);
        }
        assert!(!session.is_recording());
    }

    #[test]
    fn a_reconnect_keeps_recording() {
        let root = tempfile::tempdir().unwrap();
        let mut session = Session::new(FakeCapture::default());
        session
            .on_phase(
                GameflowPhase::InProgress,
                Some(GAME),
                &settings(),
                root.path(),
                0,
            )
            .unwrap();
        let change = session
            .on_phase(GameflowPhase::Reconnect, None, &settings(), root.path(), 0)
            .unwrap();
        assert_eq!(change, Change::None);
        assert!(session.is_recording());
    }

    #[test]
    fn a_capture_that_fails_to_start_leaves_nothing_behind() {
        let root = tempfile::tempdir().unwrap();
        let mut session = Session::new(FakeCapture {
            fail_start: true,
            ..FakeCapture::default()
        });
        let result = session.on_phase(
            GameflowPhase::InProgress,
            Some(GAME),
            &settings(),
            root.path(),
            0,
        );

        assert!(matches!(result, Err(SessionError::Capture(_))));
        assert!(!session.is_recording());
        assert!(Store::new(root.path()).list().unwrap().is_empty());
    }

    #[test]
    fn a_new_game_before_finalising_keeps_the_last_one() {
        let root = tempfile::tempdir().unwrap();
        let mut session = Session::new(FakeCapture::default());
        let first = play(&mut session, root.path());

        let next = GameInfo {
            game_id: GAME.game_id + 1,
            ..GAME
        };
        session
            .on_phase(
                GameflowPhase::InProgress,
                Some(next),
                &settings(),
                root.path(),
                3_000,
            )
            .unwrap();

        let meta = RecordingDir::new(first).read_meta().unwrap();
        assert_eq!(meta.status, RecordingStatus::Ready);
        assert_eq!(meta.highlights_source, Some(HighlightSource::Live));
        assert!(session.is_recording());
    }

    #[test]
    fn a_previous_recording_that_fails_to_finalise_does_not_stop_the_next() {
        let root = tempfile::tempdir().unwrap();
        let mut session = Session::new(FakeCapture::default());
        let first = play(&mut session, root.path());
        // The player deleted the folder by hand: its metadata cannot be written.
        fs::remove_dir_all(&first).unwrap();

        let next = GameInfo {
            game_id: GAME.game_id + 1,
            ..GAME
        };
        let change = session
            .on_phase(
                GameflowPhase::InProgress,
                Some(next),
                &settings(),
                root.path(),
                3_000,
            )
            .unwrap();

        assert!(matches!(change, Change::Started(_)));
        assert!(session.is_recording());
    }

    #[test]
    fn finalising_prunes_older_recordings_but_not_this_one() {
        let root = tempfile::tempdir().unwrap();
        let store = Store::new(root.path());
        let older = store
            .create(&RecordingMeta::new(1, 420, settings().quality, 0))
            .unwrap();
        fs::write(older.video_path(), vec![0u8; 4_000]).unwrap();

        let mut session = Session::new(FakeCapture::default());
        play(&mut session, root.path());
        let finalised = session
            .finalise(GameOutcome::default(), 10, root.path())
            .unwrap()
            .unwrap();

        assert_eq!(finalised.pruned.len(), 1);
        assert_eq!(finalised.pruned[0].meta.game_id, 1);
        assert_eq!(store.list().unwrap().len(), 1);
    }

    #[test]
    fn nothing_to_finalise_is_none() {
        let root = tempfile::tempdir().unwrap();
        let mut session = Session::new(FakeCapture::default());
        assert!(session
            .finalise(GameOutcome::default(), u64::MAX, root.path())
            .unwrap()
            .is_none());
    }
}
