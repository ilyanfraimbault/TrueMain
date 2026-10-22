//! The Recordings page's and the recap's commands. Each id the webview sends
//! is checked to name a folder of ours (`game_recording::store::is_id`), and
//! the files they touch are only those the store wrote.

use std::path::{Path, PathBuf};

use game_recording::clips::clean_title;
use game_recording::{RecordingSettings, StoredClip, StoredRecording};
use shell_state::keys::Chord;
use tauri::{AppHandle, State};

use crate::overlay::{SharedOverlay, TriggerCheck};
use crate::telemetry::{self, Feature};

use super::cut::cut_clip;
use super::views::{ClipView, GameView, LibraryView, SettingsView, WinProbabilityView};
use super::{emit_library, emit_status, RecordingStatusView, SharedRecorder};

fn text(error: impl std::fmt::Display) -> String {
    error.to_string()
}

fn settings_view(recorder: &SharedRecorder, overlay: &SharedOverlay) -> SettingsView {
    let settings = recorder.settings();
    let mark_key = settings.mark_key.map(|chord| overlay.chord_view(&chord));
    SettingsView::new(settings, recorder.folder(), mark_key)
}

#[tauri::command]
pub fn recording_settings(
    recorder: State<'_, SharedRecorder>,
    overlay: State<'_, SharedOverlay>,
) -> SettingsView {
    settings_view(&recorder, &overlay)
}

/// Why `chord` cannot be the mark key: refused on its own, like a panel's
/// (`Chord::refusal`), or a panel's chord already.
fn mark_key_refusal(chord: &Chord, overlay: &SharedOverlay) -> Option<String> {
    if let Some(reason) = chord.refusal(false) {
        return Some(reason.to_string());
    }
    overlay
        .settings()
        .panel_on(chord)
        .map(|panel| format!("Already the shortcut of {}.", panel.name()))
}

/// Whether `chord` can be the mark key, and how it reads, before the page
/// saves it — the overlay's `overlay_check_trigger` for the mark key (#2079).
#[tauri::command]
pub fn recording_check_mark_key(
    overlay: State<'_, SharedOverlay>,
    chord: serde_json::Value,
) -> Result<TriggerCheck, String> {
    let chord: Chord =
        serde_json::from_value(chord).map_err(|_| "a key this app does not know".to_string())?;
    Ok(TriggerCheck {
        refusal: mark_key_refusal(&chord, &overlay),
        chord: Some(overlay.chord_view(&chord)),
    })
}

/// Replace the settings. Read leniently, like the file: a value out of range
/// falls back alone. A smaller budget applies at once. A mark key that is a
/// panel's chord is refused with the reason, as the page's check words it.
#[tauri::command]
pub fn set_recording_settings(
    app: AppHandle,
    recorder: State<'_, SharedRecorder>,
    overlay: State<'_, SharedOverlay>,
    settings: serde_json::Value,
) -> Result<SettingsView, String> {
    let settings = RecordingSettings::from_json(&settings.to_string());
    if let Some(reason) = settings
        .mark_key
        .and_then(|chord| mark_key_refusal(&chord, &overlay))
    {
        return Err(reason);
    }
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
    Ok(settings_view(&recorder, &overlay))
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

/// The recap's win-probability curve data, read apart from the library since
/// only the recap needs it. `None` for a recording that has none.
#[tauri::command]
pub fn recording_win_probability(
    recorder: State<'_, SharedRecorder>,
    id: String,
) -> Result<Option<WinProbabilityView>, String> {
    let recording = find(&recorder, &id)?;
    Ok(WinProbabilityView::new(&recording))
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
    let newly_kept = kept && !meta.pinned;
    meta.pinned = kept;
    recording.dir.write_meta(&meta).map_err(text)?;
    if newly_kept {
        telemetry::feature(&app, Feature::GameKept);
    }
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
    telemetry::feature(&app, Feature::RecordingDeleted);
    emit_library(&app);
    Ok(())
}

pub(super) fn find_clip(recorder: &SharedRecorder, id: &str) -> Result<StoredClip, String> {
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

pub(super) fn clip_view(recorder: &SharedRecorder, id: &str) -> Result<ClipView, String> {
    Ok(ClipView::new(
        &find_clip(recorder, id)?,
        &recordings(recorder)?,
    ))
}

/// Cut `start_ms..end_ms` of a recording into its own file, with a thumbnail.
/// With `replaces`, the range is a saved clip's, adjusted in the recap (a
/// marked moment's, most often): the new cut takes its favourite and the old
/// clip goes once the new one is written. A clip published on YouTube is not
/// replaced — its link is the video's, not the new cut's.
#[tauri::command]
pub async fn clip_save(
    app: AppHandle,
    recorder: State<'_, SharedRecorder>,
    recording_id: String,
    start_ms: u64,
    end_ms: u64,
    title: String,
    replaces: Option<String>,
) -> Result<ClipView, String> {
    let recorder = recorder.inner().clone();
    let helper = recorder
        .helper()
        .map(Path::to_path_buf)
        .ok_or("clips are not available on this platform yet")?;
    let view = tokio::task::spawn_blocking(move || {
        let recording = find(&recorder, &recording_id)?;
        let replaced = replaces
            .map(|id| find_clip(&recorder, &id))
            .transpose()?
            .filter(|old| old.meta.recording_id.as_deref() == Some(recording_id.as_str()));
        if replaced
            .as_ref()
            .is_some_and(|old| old.meta.youtube_url.is_some())
        {
            return Err("a clip published on YouTube keeps its range".to_string());
        }
        let id = cut_clip(
            &recorder,
            &helper,
            &recording.dir,
            &recording.meta,
            (start_ms, end_ms),
            &title,
        )?;
        if let Some(old) = replaced {
            if old.meta.favorite {
                let clip = find_clip(&recorder, &id)?;
                let mut meta = clip.meta;
                meta.favorite = true;
                clip.dir.write_meta(&meta).map_err(text)?;
            }
            if let Err(error) = old.dir.delete() {
                tracing::warn!(%error, "the adjusted clip's previous cut stays");
            }
        }
        clip_view(&recorder, &id)
    })
    .await
    .map_err(text)??;
    telemetry::feature(&app, Feature::ClipSaved);
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
