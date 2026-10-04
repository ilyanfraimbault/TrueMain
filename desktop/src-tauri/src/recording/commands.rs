//! The Recordings page's and the recap's commands. Each id the webview sends
//! is checked to name a folder of ours (`game_recording::store::is_id`), and
//! the files they touch are only those the store wrote.

use std::path::{Path, PathBuf};
use std::time::{SystemTime, UNIX_EPOCH};

use capture_helper::media;
use game_recording::clips::clean_title;
use game_recording::{ClipMeta, RecordingSettings, StoredClip, StoredRecording};
use tauri::{AppHandle, State};

use super::views::{ClipView, GameView, LibraryView, SettingsView};
use super::{emit_library, emit_status, RecordingStatusView, SharedRecorder};

/// The thumbnail of a clip is taken this far into it — past the lead-in.
const CLIP_THUMBNAIL_AT_MS: u64 = 3_000;
const THUMBNAIL_WIDTH: u32 = 640;
/// A clip shorter than this is a misclick.
const MIN_CLIP_MS: u64 = 1_000;

fn now_ms() -> i64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_millis() as i64)
        .unwrap_or_default()
}

fn text(error: impl std::fmt::Display) -> String {
    error.to_string()
}

#[tauri::command]
pub fn recording_settings(recorder: State<'_, SharedRecorder>) -> SettingsView {
    SettingsView::new(recorder.settings(), recorder.folder())
}

/// Replace the settings. Read leniently, like the file: a value out of range
/// falls back alone. A smaller budget applies at once.
#[tauri::command]
pub fn set_recording_settings(
    app: AppHandle,
    recorder: State<'_, SharedRecorder>,
    settings: serde_json::Value,
) -> Result<SettingsView, String> {
    let settings = RecordingSettings::from_json(&settings.to_string());
    recorder.replace_settings(settings).map_err(text)?;
    let protect = recorder
        .activity()
        .folder
        .map(|id| recorder.folder().join(id));
    let pruned = recorder
        .store()
        .prune(recorder.games_budget(), protect.as_deref())
        .map_err(text)?;
    if !pruned.is_empty() {
        tracing::info!(
            count = pruned.len(),
            "recordings deleted to fit the new budget"
        );
    }
    emit_library(&app);
    Ok(SettingsView::new(recorder.settings(), recorder.folder()))
}

#[tauri::command]
pub async fn recording_status(
    recorder: State<'_, SharedRecorder>,
) -> Result<RecordingStatusView, String> {
    let recorder = recorder.inner().clone();
    tokio::task::spawn_blocking(move || recorder.status())
        .await
        .map_err(text)
}

#[tauri::command]
pub async fn request_capture_permission(
    app: AppHandle,
    recorder: State<'_, SharedRecorder>,
) -> Result<RecordingStatusView, String> {
    let recorder = recorder.inner().clone();
    let status = tokio::task::spawn_blocking(move || {
        recorder.request_permission()?;
        Ok::<_, String>((recorder.status(), recorder))
    })
    .await
    .map_err(text)??;
    emit_status(&app, &status.1);
    Ok(status.0)
}

fn recordings(recorder: &SharedRecorder) -> Result<Vec<StoredRecording>, String> {
    recorder.store().list().map_err(text)
}

fn library(recorder: &SharedRecorder) -> Result<LibraryView, String> {
    LibraryView::new(
        &recordings(recorder)?,
        &recorder.clips(),
        &recorder.activity(),
        recorder.settings().budget_bytes,
        recorder.folder(),
    )
    .map_err(text)
}

#[tauri::command]
pub async fn recording_library(recorder: State<'_, SharedRecorder>) -> Result<LibraryView, String> {
    let recorder = recorder.inner().clone();
    tokio::task::spawn_blocking(move || library(&recorder))
        .await
        .map_err(text)?
}

fn find(recorder: &SharedRecorder, id: &str) -> Result<StoredRecording, String> {
    recordings(recorder)?
        .into_iter()
        .find(|recording| recording.dir.id() == id)
        .ok_or_else(|| "this recording is no longer on disk".to_string())
}

#[tauri::command]
pub fn recording_get(recorder: State<'_, SharedRecorder>, id: String) -> Result<GameView, String> {
    let recording = find(&recorder, &id)?;
    Ok(GameView::new(&recording, &recorder.activity()))
}

/// Keep the full game — exempt from the budget — or let the budget have it.
#[tauri::command]
pub fn recording_set_kept(
    app: AppHandle,
    recorder: State<'_, SharedRecorder>,
    id: String,
    kept: bool,
) -> Result<GameView, String> {
    let recording = find(&recorder, &id)?;
    let mut meta = recording.dir.read_meta().map_err(text)?;
    meta.pinned = kept;
    recording.dir.write_meta(&meta).map_err(text)?;
    emit_library(&app);
    let recording = find(&recorder, &id)?;
    Ok(GameView::new(&recording, &recorder.activity()))
}

/// Delete the full game. Its clips are their own files and stay.
#[tauri::command]
pub fn recording_delete(
    app: AppHandle,
    recorder: State<'_, SharedRecorder>,
    id: String,
) -> Result<(), String> {
    if recorder.activity().folder.as_deref() == Some(id.as_str())
        && recorder.activity().recording.is_some()
    {
        return Err("this game is still being recorded".into());
    }
    let recording = find(&recorder, &id)?;
    recording.dir.delete().map_err(text)?;
    emit_library(&app);
    Ok(())
}

fn find_clip(recorder: &SharedRecorder, id: &str) -> Result<StoredClip, String> {
    let dir = recorder
        .clips()
        .get(id)
        .ok_or_else(|| "this clip is no longer on disk".to_string())?;
    let meta = dir.read_meta().map_err(text)?;
    let size_bytes = dir.size_bytes();
    Ok(StoredClip {
        dir,
        meta,
        size_bytes,
    })
}

fn clip_view(recorder: &SharedRecorder, id: &str) -> Result<ClipView, String> {
    Ok(ClipView::new(
        &find_clip(recorder, id)?,
        &recordings(recorder)?,
    ))
}

/// Cut `start_ms..end_ms` of a recording into its own file, with a thumbnail.
#[tauri::command]
pub async fn clip_save(
    app: AppHandle,
    recorder: State<'_, SharedRecorder>,
    recording_id: String,
    start_ms: u64,
    end_ms: u64,
    title: String,
) -> Result<ClipView, String> {
    let recorder = recorder.inner().clone();
    let helper = recorder
        .helper()
        .map(Path::to_path_buf)
        .ok_or("clips are not available on this platform yet")?;
    let view = tokio::task::spawn_blocking(move || {
        let recording = find(&recorder, &recording_id)?;
        let duration = recording.meta.duration_ms.unwrap_or(u64::MAX);
        let end_ms = end_ms.min(duration);
        if end_ms < start_ms.saturating_add(MIN_CLIP_MS) {
            return Err("a clip lasts at least a second".to_string());
        }
        let mut meta = ClipMeta::cut_from(
            &recording.meta,
            &recording_id,
            start_ms,
            end_ms,
            &title,
            now_ms(),
        );
        let clips = recorder.clips();
        let dir = clips.create(&meta).map_err(text)?;
        let cut = media::clip(
            &helper,
            &recording.dir.video_path(),
            &dir.video_path(),
            start_ms,
            end_ms,
        );
        match cut {
            Ok(duration_ms) => meta.duration_ms = duration_ms,
            Err(error) => {
                let _ = dir.delete();
                return Err(error.0);
            }
        }
        let at = CLIP_THUMBNAIL_AT_MS.min(meta.duration_ms / 2);
        if let Err(error) = media::thumbnail(
            &helper,
            &dir.video_path(),
            &dir.thumbnail_path(),
            at,
            THUMBNAIL_WIDTH,
        ) {
            tracing::warn!(%error, "no thumbnail for the clip");
        }
        dir.write_meta(&meta).map_err(text)?;
        clip_view(&recorder, &dir.id())
    })
    .await
    .map_err(text)??;
    emit_library(&app);
    Ok(view)
}

#[tauri::command]
pub fn clip_update(
    app: AppHandle,
    recorder: State<'_, SharedRecorder>,
    id: String,
    title: Option<String>,
    favorite: Option<bool>,
) -> Result<ClipView, String> {
    let clip = find_clip(&recorder, &id)?;
    let mut meta = clip.meta;
    if let Some(title) = title {
        meta.title = clean_title(&title);
    }
    if let Some(favorite) = favorite {
        meta.favorite = favorite;
    }
    clip.dir.write_meta(&meta).map_err(text)?;
    emit_library(&app);
    clip_view(&recorder, &id)
}

#[tauri::command]
pub fn clip_delete(
    app: AppHandle,
    recorder: State<'_, SharedRecorder>,
    id: String,
) -> Result<(), String> {
    find_clip(&recorder, &id)?.dir.delete().map_err(text)?;
    emit_library(&app);
    Ok(())
}

/// Show a file of the recordings folder in Finder / Explorer.
#[tauri::command]
pub fn reveal_recording_file(
    recorder: State<'_, SharedRecorder>,
    path: PathBuf,
) -> Result<(), String> {
    // The folder exists once a game was recorded; before that, showing it
    // creates it rather than failing.
    let folder = recorder.folder();
    std::fs::create_dir_all(&folder).map_err(text)?;
    let folder = folder.canonicalize().map_err(text)?;
    let path = path.canonicalize().map_err(text)?;
    if !path.starts_with(&folder) {
        return Err("only the recordings folder can be shown".into());
    }
    reveal(&path).map_err(text)
}

#[cfg(target_os = "macos")]
fn reveal(path: &Path) -> std::io::Result<()> {
    std::process::Command::new("open")
        .arg("-R")
        .arg(path)
        .spawn()
        .map(|_| ())
}

#[cfg(target_os = "windows")]
fn reveal(path: &Path) -> std::io::Result<()> {
    let mut select = std::ffi::OsString::from("/select,");
    select.push(path);
    std::process::Command::new("explorer")
        .arg(select)
        .spawn()
        .map(|_| ())
}

#[cfg(not(any(target_os = "macos", target_os = "windows")))]
fn reveal(path: &Path) -> std::io::Result<()> {
    let folder = if path.is_dir() {
        path
    } else {
        path.parent().unwrap_or(path)
    };
    std::process::Command::new("xdg-open")
        .arg(folder)
        .spawn()
        .map(|_| ())
}
