//! The recordings on disk: one folder per game, a video and its metadata, and
//! the disk budget that decides which ones go.
//!
//! The folder can be one the player chose, next to files that are not ours. So
//! the store only ever lists a folder holding a `recording.json` it can read,
//! and only ever deletes the files it writes itself — never a folder's whole
//! contents.

use std::fs;
use std::io;
use std::path::{Path, PathBuf};

use serde::{Deserialize, Serialize};

use crate::anchor::Anchor;
use crate::highlights::{Highlight, HighlightSource};
use crate::moments::{self, Moment, Objective};
use crate::settings::Quality;
use crate::win_probability::{WIN_PROBABILITY_FILE, WIN_PROBABILITY_TEMPORARY};

pub const VIDEO_FILE: &str = "video.mp4";
pub const METADATA_FILE: &str = "recording.json";
/// One frame of the game, for the Recordings page.
pub const THUMBNAIL_FILE: &str = "thumbnail.jpg";
const METADATA_TEMPORARY: &str = "recording.json.tmp";

/// The metadata format version. A folder written by a later format is left
/// alone rather than misread.
pub const METADATA_VERSION: u32 = 1;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum RecordingStatus {
    /// Being written — or, for a folder no session owns, cut short by a crash:
    /// the video is fragmented, so what was written still plays.
    Recording,
    /// The game is over and the video closed; the highlights are not resolved
    /// yet.
    Processing,
    Ready,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct RecordingMeta {
    pub version: u32,
    pub game_id: i64,
    pub queue_id: i64,
    /// Epoch milliseconds.
    pub started_at_ms: i64,
    pub quality: Quality,
    pub status: RecordingStatus,
    /// Known once the video is closed.
    pub duration_ms: Option<u64>,
    /// From the match history, once the game is in it.
    pub champion_id: Option<i64>,
    pub win: Option<bool>,
    /// The player's line in the game, from the match history.
    #[serde(default)]
    pub kills: Option<i64>,
    #[serde(default)]
    pub deaths: Option<i64>,
    #[serde(default)]
    pub assists: Option<i64>,
    pub anchor: Option<Anchor>,
    /// In game time; the anchor maps them onto the video.
    pub highlights: Vec<Highlight>,
    pub highlights_source: Option<HighlightSource>,
    /// The game's epic monsters and buildings, from its timeline, in game
    /// time.
    #[serde(default)]
    pub objectives: Vec<Objective>,
    /// Exempt from the disk budget — the player kept the full game.
    pub pinned: bool,
}

impl RecordingMeta {
    pub fn new(game_id: i64, queue_id: i64, quality: Quality, started_at_ms: i64) -> Self {
        Self {
            version: METADATA_VERSION,
            game_id,
            queue_id,
            started_at_ms,
            quality,
            status: RecordingStatus::Recording,
            duration_ms: None,
            champion_id: None,
            win: None,
            kills: None,
            deaths: None,
            assists: None,
            anchor: None,
            highlights: Vec::new(),
            highlights_source: None,
            objectives: Vec::new(),
            pinned: false,
        }
    }

    /// The highlights and objectives on the video.
    pub fn moments(&self) -> Vec<Moment> {
        moments::on_video(
            self.anchor.as_ref(),
            &self.highlights,
            &self.objectives,
            self.duration_ms,
        )
    }
}

/// One recording's folder.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct RecordingDir {
    path: PathBuf,
}

impl RecordingDir {
    pub fn new(path: PathBuf) -> Self {
        Self { path }
    }

    pub fn path(&self) -> &Path {
        &self.path
    }

    pub fn video_path(&self) -> PathBuf {
        self.path.join(VIDEO_FILE)
    }

    pub fn thumbnail_path(&self) -> PathBuf {
        self.path.join(THUMBNAIL_FILE)
    }

    /// The folder's name, which the app uses as the recording's id.
    pub fn id(&self) -> String {
        self.path
            .file_name()
            .map(|name| name.to_string_lossy().into_owned())
            .unwrap_or_default()
    }

    pub fn read_meta(&self) -> io::Result<RecordingMeta> {
        let body = fs::read_to_string(self.path.join(METADATA_FILE))?;
        let meta: RecordingMeta = serde_json::from_str(&body).map_err(io::Error::other)?;
        if meta.version != METADATA_VERSION {
            return Err(io::Error::other(format!(
                "metadata version {} is not {METADATA_VERSION}",
                meta.version
            )));
        }
        Ok(meta)
    }

    /// Written through a temporary file, so a crash never leaves a truncated
    /// metadata file that would make the recording invisible.
    pub fn write_meta(&self, meta: &RecordingMeta) -> io::Result<()> {
        let body = serde_json::to_string_pretty(meta).map_err(io::Error::other)?;
        let temporary = self.path.join(METADATA_TEMPORARY);
        fs::write(&temporary, body)?;
        fs::rename(&temporary, self.path.join(METADATA_FILE))
    }

    /// The space the recording's own files take.
    pub fn size_bytes(&self) -> u64 {
        [
            VIDEO_FILE,
            METADATA_FILE,
            THUMBNAIL_FILE,
            WIN_PROBABILITY_FILE,
        ]
        .iter()
        .filter_map(|name| fs::metadata(self.path.join(name)).ok())
        .map(|metadata| metadata.len())
        .sum()
    }

    /// Delete the recording's own files, then its folder if nothing else is
    /// in it.
    pub fn delete(&self) -> io::Result<()> {
        for name in [
            VIDEO_FILE,
            METADATA_FILE,
            METADATA_TEMPORARY,
            THUMBNAIL_FILE,
            WIN_PROBABILITY_FILE,
            WIN_PROBABILITY_TEMPORARY,
        ] {
            match fs::remove_file(self.path.join(name)) {
                Ok(()) => {}
                Err(error) if error.kind() == io::ErrorKind::NotFound => {}
                Err(error) => return Err(error),
            }
        }
        // Fails on a folder something else was put into; that folder stays.
        let _ = fs::remove_dir(&self.path);
        Ok(())
    }
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct StoredRecording {
    pub dir: RecordingDir,
    pub meta: RecordingMeta,
    pub size_bytes: u64,
}

/// The recordings folder.
#[derive(Debug, Clone)]
pub struct Store {
    root: PathBuf,
}

impl Store {
    pub fn new(root: impl Into<PathBuf>) -> Self {
        Self { root: root.into() }
    }

    pub fn root(&self) -> &Path {
        &self.root
    }

    /// A new folder for a game, its metadata written at once so a recording
    /// cut short by a crash is still listed. A game recorded twice (the app
    /// restarted mid-game) gets a second folder rather than overwriting the
    /// first.
    pub fn create(&self, meta: &RecordingMeta) -> io::Result<RecordingDir> {
        fs::create_dir_all(&self.root)?;
        let mut attempt = 1;
        let path = loop {
            let name = match attempt {
                1 => meta.game_id.to_string(),
                n => format!("{}-{n}", meta.game_id),
            };
            let path = self.root.join(name);
            match fs::create_dir(&path) {
                Ok(()) => break path,
                Err(error) if error.kind() == io::ErrorKind::AlreadyExists => attempt += 1,
                Err(error) => return Err(error),
            }
        };
        let dir = RecordingDir::new(path);
        dir.write_meta(meta)?;
        Ok(dir)
    }

    /// The recording with this id (its folder's name), if it exists.
    pub fn get(&self, id: &str) -> Option<RecordingDir> {
        if !is_id(id) {
            return None;
        }
        let dir = RecordingDir::new(self.root.join(id));
        dir.path().join(METADATA_FILE).is_file().then_some(dir)
    }

    /// Every recording in the folder, newest first. Folders without metadata
    /// this build reads are not ours, or not ours to judge, and are skipped.
    pub fn list(&self) -> io::Result<Vec<StoredRecording>> {
        let entries = match fs::read_dir(&self.root) {
            Ok(entries) => entries,
            Err(error) if error.kind() == io::ErrorKind::NotFound => return Ok(Vec::new()),
            Err(error) => return Err(error),
        };
        let mut recordings: Vec<StoredRecording> = entries
            .filter_map(Result::ok)
            .filter(|entry| entry.file_type().is_ok_and(|kind| kind.is_dir()))
            .filter_map(|entry| {
                let dir = RecordingDir::new(entry.path());
                let meta = dir.read_meta().ok()?;
                let size_bytes = dir.size_bytes();
                Some(StoredRecording {
                    dir,
                    meta,
                    size_bytes,
                })
            })
            .collect();
        recordings.sort_by_key(|recording| std::cmp::Reverse(recording.meta.started_at_ms));
        Ok(recordings)
    }

    /// Bring the folder within `budget_bytes`, oldest unpinned recording first,
    /// never touching `protect` (the one just written, or being written).
    /// Returns what was deleted, oldest first.
    pub fn prune(
        &self,
        budget_bytes: u64,
        protect: Option<&Path>,
    ) -> io::Result<Vec<StoredRecording>> {
        let recordings = self.list()?;
        let doomed = plan_prune(&recordings, budget_bytes, protect);
        let mut deleted = Vec::new();
        for path in doomed {
            if let Some(recording) = recordings.iter().find(|r| r.dir.path() == path) {
                recording.dir.delete()?;
                deleted.push(recording.clone());
            }
        }
        Ok(deleted)
    }
}

/// Whether `id` can name a folder of ours: digits, letters and dashes, so an
/// id from the webview can never reach outside the recordings folder.
pub fn is_id(id: &str) -> bool {
    !id.is_empty() && id.len() <= 64 && id.bytes().all(|b| b.is_ascii_alphanumeric() || b == b'-')
}

/// Which recordings to delete to fit `budget_bytes`: the oldest first, pinned
/// ones and `protect` never. When those alone exceed the budget, everything
/// else goes and the folder stays over it — the player pinned it.
pub fn plan_prune(
    recordings: &[StoredRecording],
    budget_bytes: u64,
    protect: Option<&Path>,
) -> Vec<PathBuf> {
    let mut total: u64 = recordings.iter().map(|r| r.size_bytes).sum();
    let mut oldest_first: Vec<&StoredRecording> = recordings
        .iter()
        .filter(|r| !r.meta.pinned && Some(r.dir.path()) != protect)
        .collect();
    oldest_first.sort_by_key(|r| r.meta.started_at_ms);

    let mut doomed = Vec::new();
    for recording in oldest_first {
        if total <= budget_bytes {
            break;
        }
        total -= recording.size_bytes;
        doomed.push(recording.dir.path().to_path_buf());
    }
    doomed
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::settings::{FrameRate, Resolution};

    const QUALITY: Quality = Quality {
        resolution: Resolution::P1080,
        frame_rate: FrameRate::Fps30,
    };

    fn record(store: &Store, game_id: i64, started_at_ms: i64, video_bytes: usize) -> RecordingDir {
        let dir = store
            .create(&RecordingMeta::new(game_id, 420, QUALITY, started_at_ms))
            .unwrap();
        fs::write(dir.video_path(), vec![0u8; video_bytes]).unwrap();
        dir
    }

    fn pin(dir: &RecordingDir) {
        let mut meta = dir.read_meta().unwrap();
        meta.pinned = true;
        dir.write_meta(&meta).unwrap();
    }

    fn game_ids(recordings: &[StoredRecording]) -> Vec<i64> {
        recordings.iter().map(|r| r.meta.game_id).collect()
    }

    #[test]
    fn lists_newest_first_and_skips_folders_that_are_not_ours() {
        let root = tempfile::tempdir().unwrap();
        let store = Store::new(root.path());
        record(&store, 1, 1_000, 10);
        record(&store, 2, 3_000, 10);
        record(&store, 3, 2_000, 10);
        fs::create_dir(root.path().join("holiday photos")).unwrap();
        fs::write(root.path().join("holiday photos").join(METADATA_FILE), "{}").unwrap();
        fs::write(root.path().join("notes.txt"), "not a folder").unwrap();

        assert_eq!(game_ids(&store.list().unwrap()), vec![2, 3, 1]);
    }

    #[test]
    fn a_missing_folder_is_an_empty_list() {
        let root = tempfile::tempdir().unwrap();
        assert!(Store::new(root.path().join("absent"))
            .list()
            .unwrap()
            .is_empty());
    }

    #[test]
    fn a_game_recorded_twice_gets_a_second_folder() {
        let root = tempfile::tempdir().unwrap();
        let store = Store::new(root.path());
        let first = record(&store, 7, 1_000, 10);
        let second = record(&store, 7, 2_000, 10);
        assert_ne!(first.path(), second.path());
        assert!(second.path().ends_with("7-2"));
        assert_eq!(store.list().unwrap().len(), 2);
    }

    #[test]
    fn prunes_the_oldest_unpinned_until_within_budget() {
        let root = tempfile::tempdir().unwrap();
        let store = Store::new(root.path());
        let oldest = record(&store, 1, 1_000, 1_000);
        pin(&oldest);
        record(&store, 2, 2_000, 1_000);
        record(&store, 3, 3_000, 1_000);
        let newest = record(&store, 4, 4_000, 1_000);

        // Four videos of 1000 bytes plus their metadata; room for about two.
        let deleted = store.prune(2_900, Some(newest.path())).unwrap();

        assert_eq!(game_ids(&deleted), vec![2, 3]);
        assert_eq!(game_ids(&store.list().unwrap()), vec![4, 1]);
        assert!(!root.path().join("2").exists());
    }

    #[test]
    fn never_deletes_the_protected_recording_even_over_budget() {
        let root = tempfile::tempdir().unwrap();
        let store = Store::new(root.path());
        let only = record(&store, 1, 1_000, 5_000);
        assert!(store.prune(100, Some(only.path())).unwrap().is_empty());
        assert!(only.video_path().exists());
    }

    #[test]
    fn within_budget_deletes_nothing() {
        let root = tempfile::tempdir().unwrap();
        let store = Store::new(root.path());
        record(&store, 1, 1_000, 100);
        assert!(store.prune(1_000_000, None).unwrap().is_empty());
    }

    #[test]
    fn deleting_leaves_files_that_are_not_ours() {
        let root = tempfile::tempdir().unwrap();
        let store = Store::new(root.path());
        let dir = record(&store, 1, 1_000, 100);
        fs::write(dir.path().join("my-edit.mp4"), "the player's").unwrap();

        dir.delete().unwrap();

        assert!(!dir.video_path().exists());
        assert!(dir.path().join("my-edit.mp4").exists());
    }

    #[test]
    fn metadata_of_another_version_is_not_read() {
        let root = tempfile::tempdir().unwrap();
        let store = Store::new(root.path());
        let dir = record(&store, 1, 1_000, 10);
        let mut meta = dir.read_meta().unwrap();
        meta.version = METADATA_VERSION + 1;
        dir.write_meta(&meta).unwrap();
        assert!(store.list().unwrap().is_empty());
    }
}
