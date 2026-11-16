//! Recording the player's games from the app (#1744): the capture helper
//! driven through `game-recording`'s session while a game runs, and the
//! recordings and clips on disk offered to the Recordings page and the
//! post-game recap (#1755, #1777).
//!
//! The phase comes from the supervisor like every other part of the app
//! (`follow`, called on each publish); the runner (`runner.rs`) owns the
//! session and does the slow work — starting the helper, following the game
//! clock, waiting for the match history — on its own task. The commands
//! (`commands.rs`) read and change what is on disk; `cut.rs` cuts clips, the
//! recap's and those of the moments the player marked with the mark key
//! (#2079), which the overlay's key watch reports through `mark` and the
//! overlay confirms (#1767);
//! `publish.rs` hands a clip to YouTube's upload page.
//!
//! Recording only happens against a real client: a tape or the simulator
//! stands in for the client's events, not for a game window to capture.

mod commands;
mod cut;
mod embed;
pub mod files;
mod publish;
mod runner;
mod views;

use std::path::{Path, PathBuf};
use std::sync::{Arc, Mutex};

use capture_helper::media::{self, AccessRequest};
use capture_helper::HelperCapture;
use game_recording::{AutoClipKind, ClipStore, MarkLength, RecordingSettings, Session, Store};
use lcu::GameflowPhase;
use serde::Serialize;
use tauri::{AppHandle, Manager};
use tokio::sync::watch;

pub use commands::*;
pub use publish::*;
pub use views::RecordingStatusView;

/// The recording state changed: started, stopped, finalised.
#[derive(Clone, serde::Serialize, specta::Type, tauri_specta::Event)]
#[tauri_specta(event_name = "recording://status")]
pub struct RecordingStatusChanged(pub RecordingStatusView);
/// Something in the library changed: read it again.
#[derive(Clone, serde::Serialize, specta::Type, tauri_specta::Event)]
#[tauri_specta(event_name = "recording://library")]
pub struct LibraryChanged;
/// The game just played stopped recording: open its recap.
#[derive(Clone, serde::Serialize, specta::Type, tauri_specta::Event)]
#[tauri_specta(event_name = "recording://recap")]
pub struct RecapReady {
    pub id: String,
}

/// Use this helper binary instead of the bundled one.
const HELPER_VAR: &str = "TRUEMAIN_CAPTURE_HELPER";
const SETTINGS_FILE: &str = "recording-settings.json";
/// Present once the app has shown macOS's Screen Recording prompt, which
/// macOS shows once per app: after that, asking again opens System Settings.
/// Windows has no such prompt (the helper answers granted).
const ASKED_FILE: &str = "screen-recording-asked";

/// The game being recorded, shared between the runner and the mark key.
pub(crate) type SharedSession = Arc<Mutex<Session<HelperCapture>>>;

/// What the runner is busy with (#2472): one state, read by the mark and
/// Flash keys, the status the sidebar shows and the library alike, so none of
/// them can say a game records while another says it is over. Mirrored as
/// `RecordingActivity` in `desktop/app/app/types/recordings.ts`.
#[derive(Clone, Default, Serialize, specta::Type)]
#[serde(tag = "kind", rename_all = "camelCase")]
pub enum Activity {
    #[default]
    Idle,
    /// A game records: the mark and Flash keys write into its session.
    Recording {
        #[serde(rename = "gameId")]
        game_id: i64,
        /// The id of the recording being written.
        #[serde(skip)]
        folder: String,
        #[serde(skip)]
        session: SharedSession,
    },
    /// The game is over: its recording is being stopped, cut and finalised
    /// once the match history has it (highlights not final yet).
    Processing {
        #[serde(rename = "gameId")]
        game_id: i64,
        #[serde(skip)]
        folder: String,
    },
}

impl Activity {
    /// The id of the recording being written or finalised.
    pub fn folder(&self) -> Option<&str> {
        match self {
            Self::Idle => None,
            Self::Recording { folder, .. } | Self::Processing { folder, .. } => Some(folder),
        }
    }

    /// Whether the recording `id` is the one being written right now.
    pub fn records(&self, id: &str) -> bool {
        matches!(self, Self::Recording { folder, .. } if folder == id)
    }

    fn session(&self) -> Option<&SharedSession> {
        match self {
            Self::Recording { session, .. } => Some(session),
            _ => None,
        }
    }

    /// The game ended: what was recording is now being processed.
    fn finished(self) -> Self {
        match self {
            Self::Recording {
                game_id, folder, ..
            } => Self::Processing { game_id, folder },
            other => other,
        }
    }
}

impl std::fmt::Debug for Activity {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            Self::Idle => f.write_str("Idle"),
            Self::Recording {
                game_id, folder, ..
            } => f
                .debug_struct("Recording")
                .field("game_id", game_id)
                .field("folder", folder)
                .finish_non_exhaustive(),
            Self::Processing { game_id, folder } => f
                .debug_struct("Processing")
                .field("game_id", game_id)
                .field("folder", folder)
                .finish(),
        }
    }
}

/// Whether the app can record. Mirrored as `CaptureAvailability`.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, specta::Type)]
#[serde(rename_all = "camelCase")]
pub enum Availability {
    /// The helper is there and Screen Recording is allowed.
    Ready,
    /// Screen Recording is not allowed for the app (macOS).
    Permission,
    /// No capture helper on this platform (Linux), or a Windows too old for
    /// window capture.
    Unsupported,
    /// The helper should be there and is not (a broken install).
    Missing,
}

/// The recorder, managed by Tauri.
pub struct Recorder {
    settings_path: PathBuf,
    config_dir: PathBuf,
    default_folder: PathBuf,
    helper: Option<PathBuf>,
    settings: Mutex<RecordingSettings>,
    activity: Mutex<Activity>,
    phase: watch::Sender<GameflowPhase>,
}

pub type SharedRecorder = Arc<Recorder>;

impl Recorder {
    /// Set up from the app's folders. The phase receiver goes to the runner.
    pub fn new(app: &AppHandle) -> (SharedRecorder, watch::Receiver<GameflowPhase>) {
        let config_dir = app
            .path()
            .app_config_dir()
            .unwrap_or_else(|_| std::env::temp_dir().join("truemain"));
        let default_folder = app
            .path()
            .video_dir()
            .map(|videos| videos.join("TrueMain"))
            .or_else(|_| {
                app.path()
                    .app_data_dir()
                    .map(|data| data.join("recordings"))
            })
            .unwrap_or_else(|_| config_dir.join("recordings"));
        let settings_path = config_dir.join(SETTINGS_FILE);
        let settings = RecordingSettings::load(&settings_path);
        let (phase, receiver) = watch::channel(GameflowPhase::None);
        let recorder = Arc::new(Self {
            settings_path,
            config_dir,
            default_folder,
            helper: helper_path(),
            settings: Mutex::new(settings),
            activity: Mutex::new(Activity::default()),
            phase,
        });
        (recorder, receiver)
    }

    pub fn settings(&self) -> RecordingSettings {
        self.settings.lock().expect("settings poisoned").clone()
    }

    fn replace_settings(&self, settings: RecordingSettings) -> std::io::Result<()> {
        settings.save(&self.settings_path)?;
        *self.settings.lock().expect("settings poisoned") = settings;
        Ok(())
    }

    /// The folder recordings go to: the chosen one, else the default.
    pub fn folder(&self) -> PathBuf {
        self.settings()
            .folder
            .unwrap_or_else(|| self.default_folder.clone())
    }

    pub fn store(&self) -> Store {
        Store::new(self.folder())
    }

    pub fn clips(&self) -> ClipStore {
        ClipStore::new(&self.folder())
    }

    pub fn helper(&self) -> Option<&Path> {
        self.helper.as_deref()
    }

    pub fn activity(&self) -> Activity {
        self.activity.lock().expect("activity poisoned").clone()
    }

    fn set_activity(&self, activity: Activity) {
        *self.activity.lock().expect("activity poisoned") = activity;
    }

    /// The game recording stopped: in one step, so nothing reads it as still
    /// recording once the mark key is off.
    fn finish_recording(&self) {
        let mut activity = self.activity.lock().expect("activity poisoned");
        *activity = std::mem::take(&mut *activity).finished();
    }

    /// The game recording now, for the mark and Flash keys.
    fn session(&self) -> Option<SharedSession> {
        self.activity
            .lock()
            .expect("activity poisoned")
            .session()
            .cloned()
    }

    /// The chord that marks a moment, while a game records: the key watch
    /// reads it then and only then.
    pub fn mark_key(&self) -> Option<shell_state::keys::Chord> {
        let recording = self.session().is_some();
        recording.then(|| self.settings().mark_key).flatten()
    }

    /// Whether the key watch reads the Flash key: while a game records and the
    /// Flash plays are among the automatic clips chosen (#2382).
    pub fn reads_flash(&self) -> bool {
        let recording = self.session().is_some();
        recording
            && self
                .settings()
                .auto_clips
                .contains(&AutoClipKind::FlashKill)
    }

    /// The player pressed their Flash key: note this second of the game being
    /// recorded, for the Flash plays cut once it is finalised.
    pub fn flash(&self) {
        let Some(session) = self.session() else {
            return;
        };
        let noted = session.lock().expect("session poisoned").flash();
        match noted {
            Ok(Some(video_ms)) => tracing::info!(video_ms, "flash noted"),
            Ok(None) => {}
            Err(error) => tracing::warn!(%error, "the flash could not be written"),
        }
    }

    /// The player pressed the mark key: mark this second of the game being
    /// recorded, keeping the length chosen in the settings before it. Its
    /// clip is cut once the video is closed (`cut::cut_marks`). The length
    /// kept, for the overlay's confirmation (#1767); `None` when nothing was
    /// marked.
    pub fn mark(&self) -> Option<MarkLength> {
        let session = self.session()?;
        let length = self.settings().mark_length;
        let marked = session
            .lock()
            .expect("session poisoned")
            .mark(length.millis());
        match marked {
            Ok(Some(video_ms)) => {
                tracing::info!(video_ms, seconds = length.seconds(), "moment marked");
                Some(length)
            }
            Ok(None) => None,
            Err(error) => {
                tracing::warn!(%error, "the marked moment could not be written");
                None
            }
        }
    }

    /// The budget left for full games once the clips — never deleted by the
    /// budget — are counted.
    pub fn games_budget(&self) -> u64 {
        let clips = self.clips().size_bytes().unwrap_or(0);
        self.settings().budget_bytes.saturating_sub(clips)
    }

    /// Whether the app can record, and if not why.
    pub fn status(&self) -> RecordingStatusView {
        let (availability, message) = match self.helper() {
            None => (Availability::Unsupported, None),
            Some(helper) if !helper.is_file() => (
                Availability::Missing,
                Some(format!(
                    "the capture helper is missing ({})",
                    helper.display()
                )),
            ),
            Some(helper) => match media::access(helper, AccessRequest::Check) {
                Ok(true) => (Availability::Ready, None),
                // Windows asks no permission: a refusal means this Windows
                // has no window capture at all.
                Ok(false) if cfg!(windows) => (
                    Availability::Unsupported,
                    Some("Recording needs Windows 10 version 1903 or later.".into()),
                ),
                Ok(false) => (Availability::Permission, None),
                Err(error) => (Availability::Missing, Some(error.0)),
            },
        };
        RecordingStatusView {
            availability,
            message,
            activity: self.activity(),
        }
    }

    /// Ask for Screen Recording: macOS's prompt the first time, System
    /// Settings after that.
    fn request_permission(&self) -> Result<(), String> {
        let helper = self
            .helper()
            .ok_or("recording is not available on this platform yet")?;
        let asked = self.config_dir.join(ASKED_FILE);
        let request = if asked.exists() {
            AccessRequest::OpenSettings
        } else {
            AccessRequest::Prompt
        };
        media::access(helper, request).map_err(|e| e.0)?;
        let _ = std::fs::create_dir_all(&self.config_dir);
        let _ = std::fs::write(&asked, b"");
        Ok(())
    }
}

/// Follow the phase the app publishes. Cheap: the runner wakes on a change.
pub fn follow(app: &AppHandle, phase: GameflowPhase) {
    if let Some(recorder) = app.try_state::<SharedRecorder>() {
        recorder.phase.send_if_modified(|current| {
            let changed = *current != phase;
            *current = phase;
            changed
        });
    }
}

/// Start the runner. Called once, from the app's setup.
pub fn start(app: &AppHandle, recorder: SharedRecorder, phases: watch::Receiver<GameflowPhase>) {
    tauri::async_runtime::spawn(runner::run(app.clone(), recorder, phases));
}

pub(crate) fn emit_status(app: &AppHandle, recorder: &Recorder) {
    crate::contract::emit(app, RecordingStatusChanged(recorder.status()));
}

pub(crate) fn emit_library(app: &AppHandle) {
    crate::contract::emit(app, LibraryChanged);
}

pub(crate) fn emit_recap(app: &AppHandle, id: String) {
    crate::contract::emit(app, RecapReady { id });
}

/// Where the helper is: an override, the one bundled beside the app's binary
/// (`Contents/MacOS` on macOS, the install folder on Windows), or in a
/// development build the one built from the source tree — `swift build`'s on
/// macOS, `cargo build -p truemain-capture`'s on Windows. `None` where no
/// helper exists (Linux).
fn helper_path() -> Option<PathBuf> {
    if let Some(path) = std::env::var_os(HELPER_VAR) {
        return Some(PathBuf::from(path));
    }
    let name = if cfg!(windows) {
        "truemain-capture.exe"
    } else if cfg!(target_os = "macos") {
        "truemain-capture"
    } else {
        return None;
    };
    let bundled = std::env::current_exe()
        .ok()
        .and_then(|exe| exe.parent().map(|dir| dir.join(name)))
        .unwrap_or_else(|| PathBuf::from(name));
    #[cfg(debug_assertions)]
    if !bundled.is_file() {
        let root = Path::new(env!("CARGO_MANIFEST_DIR")).join("..");
        let candidates: &[&str] = if cfg!(windows) {
            &["target/release", "target/debug"]
        } else {
            &[
                "capture/macos/.build/release",
                "capture/macos/.build/debug",
                "capture/macos/.build/apple/Products/Release",
            ]
        };
        for build in candidates {
            let candidate = root.join(build).join(name);
            if candidate.is_file() {
                return Some(candidate);
            }
        }
    }
    Some(bundled)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn session() -> SharedSession {
        Arc::new(Mutex::new(Session::new(HelperCapture::new(
            PathBuf::from("helper"),
            None,
            "window".into(),
            false,
        ))))
    }

    fn recording() -> Activity {
        Activity::Recording {
            game_id: 7,
            folder: "game-7".into(),
            session: session(),
        }
    }

    #[test]
    fn only_a_recording_game_has_a_session_for_the_mark_key() {
        assert!(Activity::Idle.session().is_none());
        assert!(recording().session().is_some());
        assert!(recording().finished().session().is_none());
    }

    #[test]
    fn a_finished_recording_is_processed_under_the_same_game_and_folder() {
        let processing = recording().finished();
        assert!(matches!(
            &processing,
            Activity::Processing { game_id: 7, folder } if folder == "game-7"
        ));
        assert_eq!(processing.folder(), Some("game-7"));
        assert!(!processing.records("game-7"), "no longer being written");
        assert!(recording().records("game-7"));
        assert!(!recording().records("game-8"));
    }

    #[test]
    fn finishing_anything_but_a_recording_changes_nothing() {
        assert!(matches!(Activity::Idle.finished(), Activity::Idle));
        assert!(matches!(
            recording().finished().finished(),
            Activity::Processing { game_id: 7, .. }
        ));
        assert_eq!(Activity::Idle.folder(), None);
    }

    #[test]
    fn the_wire_shape_is_the_apps_union() {
        let json = |activity: Activity| serde_json::to_value(activity).expect("serialises");
        assert_eq!(json(Activity::Idle), serde_json::json!({ "kind": "idle" }));
        assert_eq!(
            json(recording()),
            serde_json::json!({ "kind": "recording", "gameId": 7 })
        );
        assert_eq!(
            json(recording().finished()),
            serde_json::json!({ "kind": "processing", "gameId": 7 })
        );
        assert_eq!(
            serde_json::to_value(Availability::Permission).expect("serialises"),
            serde_json::json!("permission")
        );
    }
}
