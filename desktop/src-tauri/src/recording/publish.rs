//! Publishing a clip on YouTube (#1815) without YouTube's API: the app opens
//! YouTube's own upload page in the player's browser, where they are already
//! signed in, and lets them drag the clip's file straight onto it. No Google
//! project, OAuth token or upload quota is involved — the API's quota is shared
//! by every install and its uploads are forced private until an audit
//! (`decisions/desktop.md`).
//!
//! The player gives the video's link back once it is up; the clip keeps it and
//! shows as published. From then on its video file can go (#2005): the clip
//! stays listed, with its thumbnail, and plays from YouTube. So a link is only
//! kept once YouTube's oEmbed endpoint — public, no key — answers for it with
//! the clip's own title: it is the clip's video, and it can be played here.

use std::time::Duration;

use game_recording::{same_youtube_title, youtube_video_id, youtube_video_url};
use serde::Deserialize;
use tauri::{AppHandle, State, Window};

use crate::telemetry::{self, Feature};

use super::commands::{clip_view, find_clip};
use super::embed;
use super::views::ClipView;
use super::{emit_library, SharedRecorder};

/// YouTube's upload page: it opens the channel's upload dialog, or asks the
/// player to sign in first.
const UPLOAD_URL: &str = "https://www.youtube.com/upload";

/// YouTube's oEmbed endpoint: a video's title and channel, for a public or
/// unlisted video only — a private one answers 401.
const OEMBED_URL: &str = "https://www.youtube.com/oembed";
const OEMBED_TIMEOUT: Duration = Duration::from_secs(10);

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

/// The page the clip player frames to play a published clip from YouTube
/// (`embed.rs`).
#[tauri::command]
pub fn clip_youtube_embed(
    recorder: State<'_, SharedRecorder>,
    id: String,
) -> Result<String, String> {
    let clip = find_clip(&recorder, &id)?;
    let video = clip
        .meta
        .youtube_url
        .as_deref()
        .and_then(youtube_video_id)
        .ok_or("this clip has no YouTube link")?;
    embed::page_url(video)
}

#[derive(Deserialize)]
struct OEmbed {
    title: String,
}

/// Check with YouTube that `url` is the clip's video: one it shows to anyone
/// with the link, under the clip's own title.
async fn check_video(url: &str, clip_title: &str) -> Result<(), String> {
    let response = reqwest::Client::builder()
        .timeout(OEMBED_TIMEOUT)
        .build()
        .map_err(|error| error.to_string())?
        .get(OEMBED_URL)
        .query(&[("url", url), ("format", "json")])
        .send()
        .await
        .map_err(|_| "YouTube could not be reached to check the video".to_string())?;
    match response.status().as_u16() {
        200 => {}
        401 | 403 => {
            return Err("YouTube does not show this video to others: set it to Unlisted or Public so TrueMain can check it and play it here".into())
        }
        _ => return Err("there is no YouTube video at this link".into()),
    }
    let video: OEmbed = response
        .json()
        .await
        .map_err(|_| "YouTube's answer about this video could not be read".to_string())?;
    if !same_youtube_title(&video.title, clip_title) {
        return Err(format!(
            "this video is titled \"{}\" on YouTube, not \"{clip_title}\": paste the link of the video you published for this clip, under the clip's title",
            video.title
        ));
    }
    Ok(())
}

/// Keep the YouTube video a clip was published as — once YouTube confirms it
/// is the clip's (`check_video`) — or forget it, for an empty link.
#[tauri::command]
pub async fn clip_set_youtube_url(
    app: AppHandle,
    recorder: State<'_, SharedRecorder>,
    id: String,
    url: String,
) -> Result<ClipView, String> {
    let clip = find_clip(&recorder, &id)?;
    let youtube_url = if url.trim().is_empty() {
        if !clip.dir.video_path().is_file() {
            return Err(
                "this clip is only on YouTube now: its link is all that is left of it".into(),
            );
        }
        None
    } else {
        let url = youtube_video_url(&url).ok_or("this is not a link to a YouTube video")?;
        check_video(&url, &clip.meta.title).await?;
        Some(url)
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

/// Delete a published clip's video from disk, keeping the clip: it stays in the
/// library with its thumbnail and plays from YouTube. A clip without a YouTube
/// link keeps its file — this would leave it pointing at nothing.
#[tauri::command]
pub fn clip_delete_video(
    app: AppHandle,
    recorder: State<'_, SharedRecorder>,
    id: String,
) -> Result<ClipView, String> {
    let clip = find_clip(&recorder, &id)?;
    if clip.meta.youtube_url.is_none() {
        return Err("only a clip published on YouTube can leave this PC".into());
    }
    clip.dir.delete_video().map_err(|error| error.to_string())?;
    telemetry::feature(&app, Feature::ClipVideoDeleted);
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
