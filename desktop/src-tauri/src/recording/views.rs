//! What the webview receives about recordings: the shapes of
//! `desktop/app/app/types/recordings.ts`, built from what is on disk.

use std::path::PathBuf;

use game_recording::anchor::Segment;
use game_recording::{
    ClipStore, FrameRate, Moment, ParticipantChampion, Quality, RecordedWinProbability,
    RecordingSettings, RecordingStatus, Resolution, StoredClip, StoredRecording,
};
use lcu::WinProbabilityTimeline;
use serde::Serialize;

use super::Activity;

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct RecordingStatusView {
    pub availability: &'static str,
    pub message: Option<String>,
    pub recording_game_id: Option<i64>,
    pub processing_game_id: Option<i64>,
}

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Estimate {
    resolution: Resolution,
    frame_rate: FrameRate,
    bytes_per_hour: Option<u64>,
}

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct SettingsView {
    settings: RecordingSettings,
    folder: PathBuf,
    min_budget_bytes: u64,
    estimates: Vec<Estimate>,
}

impl SettingsView {
    pub fn new(settings: RecordingSettings, folder: PathBuf) -> Self {
        let mut estimates = Vec::new();
        for resolution in [
            Resolution::Native,
            Resolution::P1440,
            Resolution::P1080,
            Resolution::P720,
        ] {
            for frame_rate in [FrameRate::Fps30, FrameRate::Fps60] {
                let quality = Quality {
                    resolution,
                    frame_rate,
                };
                estimates.push(Estimate {
                    resolution,
                    frame_rate,
                    bytes_per_hour: quality.nominal_bitrate_bps().map(|bps| bps / 8 * 3600),
                });
            }
        }
        Self {
            settings,
            folder,
            min_budget_bytes: game_recording::settings::MIN_BUDGET_BYTES,
            estimates,
        }
    }
}

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct GameView {
    id: String,
    game_id: i64,
    queue_id: i64,
    started_at_ms: i64,
    status: RecordingStatus,
    duration_ms: Option<u64>,
    champion_id: Option<i64>,
    win: Option<bool>,
    kills: Option<i64>,
    deaths: Option<i64>,
    assists: Option<i64>,
    kept: bool,
    size_bytes: u64,
    video_path: PathBuf,
    thumbnail_path: Option<PathBuf>,
    moments: Vec<Moment>,
    highlights_source: Option<game_recording::HighlightSource>,
}

impl GameView {
    /// A folder still marked as being written that the runner is not busy
    /// with was cut short by a crash or a quit: its video plays up to where it
    /// stopped, so it is shown as ready.
    pub fn new(recording: &StoredRecording, activity: &Activity) -> Self {
        let meta = &recording.meta;
        let id = recording.dir.id();
        let busy = activity.folder.as_deref() == Some(id.as_str());
        let status = match meta.status {
            RecordingStatus::Ready => RecordingStatus::Ready,
            other if busy => other,
            _ => RecordingStatus::Ready,
        };
        let thumbnail = recording.dir.thumbnail_path();
        Self {
            id,
            game_id: meta.game_id,
            queue_id: meta.queue_id,
            started_at_ms: meta.started_at_ms,
            status,
            duration_ms: meta.duration_ms,
            champion_id: meta.champion_id,
            win: meta.win,
            kills: meta.kills,
            deaths: meta.deaths,
            assists: meta.assists,
            kept: meta.pinned,
            size_bytes: recording.size_bytes,
            video_path: recording.dir.video_path(),
            thumbnail_path: thumbnail.is_file().then_some(thumbnail),
            moments: meta.moments(),
            highlights_source: meta.highlights_source,
        }
    }
}

/// What the recap draws its win-probability curve from (#1911): the game's
/// reduced timeline, read for the player's side, and the anchor's segments
/// that place its game time on the video the way the moments are placed
/// (`Anchor::video_ms`, clamped to the video's length).
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct WinProbabilityView {
    team_id: i64,
    champions: Vec<ParticipantChampion>,
    timeline: WinProbabilityTimeline,
    clock: Vec<Segment>,
    duration_ms: Option<u64>,
}

impl WinProbabilityView {
    /// `None` without the kept timeline, or without an anchor: a curve placed
    /// on the wrong second of the video is worse than none, as for moments.
    pub fn new(recording: &StoredRecording) -> Option<Self> {
        let anchor = recording.meta.anchor.as_ref()?;
        let RecordedWinProbability {
            team_id,
            champions,
            timeline,
        } = recording.dir.read_win_probability()?;
        Some(Self {
            team_id,
            champions,
            timeline,
            clock: anchor.segments.clone(),
            duration_ms: recording.meta.duration_ms,
        })
    }
}

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ClipView {
    id: String,
    title: String,
    game_id: i64,
    recording_id: Option<String>,
    queue_id: i64,
    champion_id: Option<i64>,
    win: Option<bool>,
    kills: Option<i64>,
    deaths: Option<i64>,
    assists: Option<i64>,
    game_started_at_ms: i64,
    created_at_ms: i64,
    start_ms: u64,
    end_ms: u64,
    duration_ms: u64,
    favorite: bool,
    size_bytes: u64,
    video_path: PathBuf,
    thumbnail_path: Option<PathBuf>,
    moments: Vec<Moment>,
}

impl ClipView {
    /// `recordings` are the full games still on disk: the clip names its
    /// source only while it exists.
    pub fn new(clip: &StoredClip, recordings: &[StoredRecording]) -> Self {
        let meta = &clip.meta;
        let thumbnail = clip.dir.thumbnail_path();
        let recording_id = meta
            .recording_id
            .clone()
            .filter(|id| recordings.iter().any(|r| r.dir.id() == *id));
        Self {
            id: clip.dir.id(),
            title: meta.title.clone(),
            game_id: meta.game_id,
            recording_id,
            queue_id: meta.queue_id,
            champion_id: meta.champion_id,
            win: meta.win,
            kills: meta.kills,
            deaths: meta.deaths,
            assists: meta.assists,
            game_started_at_ms: meta.game_started_at_ms,
            created_at_ms: meta.created_at_ms,
            start_ms: meta.start_ms,
            end_ms: meta.end_ms,
            duration_ms: meta.duration_ms,
            favorite: meta.favorite,
            size_bytes: clip.size_bytes,
            video_path: clip.dir.video_path(),
            thumbnail_path: thumbnail.is_file().then_some(thumbnail),
            moments: meta.moments.clone(),
        }
    }
}

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct LibraryView {
    games: Vec<GameView>,
    clips: Vec<ClipView>,
    used_bytes: u64,
    budget_bytes: u64,
    folder: PathBuf,
}

impl LibraryView {
    pub fn new(
        recordings: &[StoredRecording],
        clips: &ClipStore,
        activity: &Activity,
        budget_bytes: u64,
        folder: PathBuf,
    ) -> std::io::Result<Self> {
        let clips = clips.list()?;
        let used_bytes = recordings.iter().map(|r| r.size_bytes).sum::<u64>()
            + clips.iter().map(|c| c.size_bytes).sum::<u64>();
        Ok(Self {
            games: recordings
                .iter()
                .map(|r| GameView::new(r, activity))
                .collect(),
            clips: clips.iter().map(|c| ClipView::new(c, recordings)).collect(),
            used_bytes,
            budget_bytes,
            folder,
        })
    }
}
