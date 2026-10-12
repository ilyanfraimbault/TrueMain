//! Publishing a clip on YouTube (#1815) without YouTube's API: the app opens
//! YouTube's own upload page in the player's browser, where they are already
//! signed in, and lets them drag the clip's file straight onto it. No Google
//! project, OAuth token or upload quota is involved — the API's quota is shared
//! by every install and its uploads are forced private until an audit
//! (`decisions/desktop.md`).
//!
//! The player gives the video's link back once it is up; the clip keeps it and
//! shows as published.

use game_recording::youtube_video_url;
use tauri::{AppHandle, State, Window};

use crate::telemetry::{self, Feature};

use super::commands::{clip_view, find_clip};
use super::views::ClipView;
use super::{emit_library, SharedRecorder};

/// YouTube's upload page: it opens the channel's upload dialog, or asks the
/// player to sign in first.
const UPLOAD_URL: &str = "https://www.youtube.com/upload";

/// The image under the cursor while a clip is dragged: the app's icon, small
/// enough not to hide the page it is dropped on.
const DRAG_ICON: &[u8] = include_bytes!("../../icons/64x64.png");

fn open(app: &AppHandle, url: &str) -> Result<(), String> {
    use tauri_plugin_shell::ShellExt;

    // As in `open_on_site`: the shell plugin's opener is deprecated in favour
    // of `tauri-plugin-opener`, and the URL is the shell's own, never the
    // webview's.
    #[allow(deprecated)]
    app.shell()
        .open(url, None)
        .map_err(|error| error.to_string())
}

/// Open YouTube's upload page in the player's browser.
#[tauri::command]
pub fn open_youtube_upload(app: AppHandle) -> Result<(), String> {
    open(&app, UPLOAD_URL)?;
    telemetry::feature(&app, Feature::YoutubeUploadOpened);
    Ok(())
}

/// Open the YouTube video a clip was published as.
#[tauri::command]
pub fn clip_open_youtube(
    app: AppHandle,
    recorder: State<'_, SharedRecorder>,
    id: String,
) -> Result<(), String> {
    let url = find_clip(&recorder, &id)?
        .meta
        .youtube_url
        .as_deref()
        .and_then(youtube_video_url)
        .ok_or("this clip has no YouTube link")?;
    open(&app, &url)
}

/// Keep the YouTube video a clip was published as — or forget it, for an
/// empty link. Anything but a link to a YouTube video is refused.
#[tauri::command]
pub fn clip_set_youtube_url(
    app: AppHandle,
    recorder: State<'_, SharedRecorder>,
    id: String,
    url: String,
) -> Result<ClipView, String> {
    let clip = find_clip(&recorder, &id)?;
    let youtube_url = if url.trim().is_empty() {
        None
    } else {
        Some(youtube_video_url(&url).ok_or("this is not a link to a YouTube video")?)
    };
    let newly_published = clip.meta.youtube_url.is_none() && youtube_url.is_some();
    let mut meta = clip.meta;
    meta.youtube_url = youtube_url;
    clip.dir
        .write_meta(&meta)
        .map_err(|error| error.to_string())?;
    if newly_published {
        telemetry::feature(&app, Feature::ClipPublished);
    }
    emit_library(&app);
    clip_view(&recorder, &id)
}

/// Start dragging a clip's video file out of the app, as Finder or Explorer
/// would — onto YouTube's upload page, or anywhere a file is dropped. Called
/// from the press on the drag handle; the path is the shell's, never the
/// webview's. Async so it is not already on the main thread it hops to.
#[tauri::command]
pub async fn clip_start_drag(
    app: AppHandle,
    window: Window,
    recorder: State<'_, SharedRecorder>,
    id: String,
) -> Result<(), String> {
    let video = find_clip(&recorder, &id)?.dir.video_path();
    if !video.is_file() {
        return Err("this clip's video is no longer on disk".into());
    }
    let (sender, receiver) = tokio::sync::oneshot::channel();
    app.run_on_main_thread(move || {
        let started = drag::start_drag(
            &window,
            drag::DragItem::Files(vec![video]),
            drag::Image::Raw(DRAG_ICON.to_vec()),
            |_, _| {},
            drag::Options::default(),
        );
        let _ = sender.send(started.map_err(|error| error.to_string()));
    })
    .map_err(|error| error.to_string())?;
    receiver.await.map_err(|error| error.to_string())?
}
