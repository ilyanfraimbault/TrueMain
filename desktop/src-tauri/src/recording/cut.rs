//! Cutting a range of a recording into a clip of its own: the recap's
//! "Save" (`clip_save`); once a game's video is closed, the moments the
//! player marked with the mark key (#2079) — each the length chosen before
//! the press (#1767), saved like a clip cut by hand, so the disk budget never
//! deletes it;
//! and once the game is finalised, the plays the player chose to clip
//! automatically (#1766, `game_recording::auto_clips`).

use std::path::Path;
use std::time::{SystemTime, UNIX_EPOCH};

use capture_helper::media;
use game_recording::{auto_clips, AutoClipKind, ClipMeta, RecordingDir, RecordingMeta};

use super::Recorder;

/// The thumbnail of a clip is taken this far into it — past the lead-in.
const CLIP_THUMBNAIL_AT_MS: u64 = 3_000;
const THUMBNAIL_WIDTH: u32 = 640;
/// A clip shorter than this is a misclick.
const MIN_CLIP_MS: u64 = 1_000;

pub(super) fn now_ms() -> i64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_millis() as i64)
        .unwrap_or_default()
}

/// What a clip is besides its range and title.
#[derive(Default)]
struct Cut<'a> {
    /// Where its thumbnail is taken, from its start; just past the lead-in
    /// (or the middle of a short clip) when `None`.
    thumbnail_at_ms: Option<u64>,
    /// The plays an automatic clip holds.
    automatic: &'a [AutoClipKind],
}

/// Cut `start_ms..end_ms` of the recording in `dir` into its own file, with a
/// thumbnail; the new clip's id. Blocking: it runs the helper.
pub(super) fn cut_clip(
    recorder: &Recorder,
    helper: &Path,
    dir: &RecordingDir,
    recording: &RecordingMeta,
    range: (u64, u64),
    title: &str,
) -> Result<String, String> {
    cut(
        recorder,
        helper,
        dir,
        recording,
        range,
        title,
        Cut::default(),
    )
}

fn cut(
    recorder: &Recorder,
    helper: &Path,
    dir: &RecordingDir,
    recording: &RecordingMeta,
    range: (u64, u64),
    title: &str,
    options: Cut<'_>,
) -> Result<String, String> {
    let (start_ms, end_ms) = range;
    let end_ms = end_ms.min(recording.duration_ms.unwrap_or(u64::MAX));
    if end_ms < start_ms.saturating_add(MIN_CLIP_MS) {
        return Err("a clip lasts at least a second".to_string());
    }
    let mut meta = ClipMeta::cut_from(recording, &dir.id(), start_ms, end_ms, title, now_ms());
    meta.automatic = options.automatic.to_vec();
    let clip = recorder.clips().create(&meta).map_err(|e| e.to_string())?;
    match media::clip(
        helper,
        &dir.video_path(),
        &clip.video_path(),
        start_ms,
        end_ms,
    ) {
        Ok(duration_ms) => meta.duration_ms = duration_ms,
        Err(error) => {
            let _ = clip.delete();
            return Err(error.0);
        }
    }
    let at = match options.thumbnail_at_ms {
        // The play itself, inside the cut as it came out.
        Some(at) => at.min(meta.duration_ms.saturating_sub(MIN_CLIP_MS / 2)),
        None => CLIP_THUMBNAIL_AT_MS.min(meta.duration_ms / 2),
    };
    if let Err(error) = media::thumbnail(
        helper,
        &clip.video_path(),
        &clip.thumbnail_path(),
        at,
        THUMBNAIL_WIDTH,
    ) {
        tracing::warn!(%error, "no thumbnail for the clip");
    }
    if let Err(error) = clip.write_meta(&meta) {
        let _ = clip.delete();
        return Err(error.to_string());
    }
    Ok(clip.id())
}

/// The clips of the moments the player marked in the game recorded in `dir`,
/// once its video is closed: each mark's window (`Mark::window`), titled with
/// the champion and the game clock at the press. A cut that fails costs that mark alone. The
/// number of clips saved. Blocking.
pub(super) fn cut_marks(recorder: &Recorder, helper: &Path, dir: &Path) -> usize {
    let dir = RecordingDir::new(dir.to_path_buf());
    let Ok(meta) = dir.read_meta() else {
        return 0;
    };
    let mut saved = 0;
    for mark in &meta.marks {
        let Some(range) = mark.window(meta.duration_ms) else {
            continue;
        };
        let title = mark.title(meta.anchor.as_ref(), meta.champion_name.as_deref());
        match cut_clip(recorder, helper, &dir, &meta, range, &title) {
            Ok(_) => saved += 1,
            Err(error) => {
                tracing::warn!(%error, video_ms = mark.video_ms, "a marked moment was not cut")
            }
        }
    }
    saved
}

/// The clips of the plays the player chose (`RecordingSettings::auto_clips`)
/// in the game finalised in `dir`, each thumbnailed at its play. A cut that
/// fails costs that clip alone. The number of clips saved. Blocking.
pub(super) fn cut_auto_clips(
    recorder: &Recorder,
    helper: &Path,
    dir: &Path,
    kinds: &[AutoClipKind],
) -> usize {
    let dir = RecordingDir::new(dir.to_path_buf());
    let Ok(meta) = dir.read_meta() else {
        return 0;
    };
    let mut saved = 0;
    for clip in auto_clips(&meta, kinds) {
        let options = Cut {
            thumbnail_at_ms: Some(clip.moment_ms - clip.start_ms),
            automatic: &clip.kinds,
        };
        let range = (clip.start_ms, clip.end_ms);
        match cut(recorder, helper, &dir, &meta, range, &clip.title, options) {
            Ok(_) => saved += 1,
            Err(error) => {
                tracing::warn!(%error, title = %clip.title, "an automatic clip was not cut")
            }
        }
    }
    saved
}
