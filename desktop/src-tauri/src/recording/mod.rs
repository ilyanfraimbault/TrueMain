//! Recording the player's games from the app (#1744): the capture helper
//! driven through `game-recording`'s session while a game runs, and the
//! recordings and clips on disk offered to the Recordings page and the
//! post-game recap (#1755, #1777).
//!
//! The phase comes from the supervisor like every other part of the app
//! (`follow`, called on each publish); the runner (`runner.rs`) owns the
//! session and does the slow work — starting the helper, following the game
//! clock, waiting for the match history — on its own task. The commands
//! (`commands.rs`) read and change what is on disk.
//!
//! Recording only happens against a real client: a tape or the simulator
//! stands in for the client's events, not for a game window to capture.

mod commands;
pub mod files;
mod runner;
mod views;

use std::path::{Path, PathBuf};
use std::sync::{Arc, Mutex};

use capture_helper::media::{self, AccessRequest};
use game_recording::{ClipStore, RecordingSettings, Store};
use lcu::GameflowPhase;
use serde::Serialize;
use tauri::{AppHandle, Emitter, Manager};
use tokio::sync::watch;

pub use commands::*;
pub use views::RecordingStatusView;

/// The recording state changed: started, stopped, finalised.
pub const STATUS_EVENT: &str = "recording://status";
/// Something in the library changed: read it again.
pub const LIBRARY_EVENT: &str = "recording://library";
/// The game just played stopped recording: open its recap.
pub const RECAP_EVENT: &str = "recording://recap";

/// Use this helper binary instead of the bundled one.
const HELPER_VAR: &str = "TRUEMAIN_CAPTURE_HELPER";
const SETTINGS_FILE: &str = "recording-settings.json";
/// Present once the app has shown macOS's Screen Recording prompt, which
/// macOS shows once per app: after that, asking again opens System Settings.
/// Windows has no such prompt (the helper answers granted).
const ASKED_FILE: &str = "screen-recording-asked";

/// The game the runner is busy with, for the status the page shows.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct Activity {
    pub recording: Option<i64>,
    pub processing: Option<i64>,
    /// The id of the recording being written or finalised.
    pub folder: Option<String>,
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

    /// The budget left for full games once the clips — never deleted by the
    /// budget — are counted.
    pub fn games_budget(&self) -> u64 {
        let clips = self.clips().size_bytes().unwrap_or(0);
        self.settings().budget_bytes.saturating_sub(clips)
    }

    /// Whether the app can record, and if not why.
    pub fn status(&self) -> RecordingStatusView {
        let (availability, message) = match self.helper() {
            None => ("unsupported", None),
            Some(helper) if !helper.is_file() => (
                "missing",
                Some(format!(
                    "the capture helper is missing ({})",
                    helper.display()
                )),
            ),
            Some(helper) => match media::access(helper, AccessRequest::Check) {
                Ok(true) => ("ready", None),
                // Windows asks no permission: a refusal means this Windows
                // has no window capture at all.
                Ok(false) if cfg!(windows) => (
                    "unsupported",
                    Some("Recording needs Windows 10 version 1903 or later.".into()),
                ),
                Ok(false) => ("permission", None),
                Err(error) => ("missing", Some(error.0)),
            },
        };
        let activity = self.activity();
        RecordingStatusView {
            availability,
            message,
            recording_game_id: activity.recording,
            processing_game_id: activity.processing,
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
    let _ = app.emit(STATUS_EVENT, recorder.status());
}

pub(crate) fn emit_library(app: &AppHandle) {
    let _ = app.emit(LIBRARY_EVENT, ());
}

#[derive(Clone, Serialize)]
struct Recap {
    id: String,
}

pub(crate) fn emit_recap(app: &AppHandle, id: String) {
    let _ = app.emit(RECAP_EVENT, Recap { id });
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
