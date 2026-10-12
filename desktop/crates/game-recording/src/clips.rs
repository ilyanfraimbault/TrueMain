//! The clips the player cut from a recording in its recap (#1777): each one
//! its own video file with its metadata and a thumbnail, in `clips/` under the
//! recordings folder.
//!
//! A clip is the player's deliberate choice, so it outlives the full game it
//! was cut from — deleted by hand or by the budget — and the budget never
//! deletes it: it only counts the space it takes. Like the store, this only
//! lists folders holding a `clip.json` it reads and only deletes the files it
//! writes.

use std::fs;
use std::io;
use std::path::{Path, PathBuf};

use serde::{Deserialize, Serialize};

use crate::moments::Moment;
use crate::store::RecordingMeta;

pub const CLIPS_DIR: &str = "clips";
pub const CLIP_VIDEO_FILE: &str = "clip.mp4";
pub const CLIP_METADATA_FILE: &str = "clip.json";
pub const CLIP_THUMBNAIL_FILE: &str = "thumbnail.jpg";
const CLIP_METADATA_TEMPORARY: &str = "clip.json.tmp";
pub const CLIP_METADATA_VERSION: u32 = 1;

/// The longest title kept, in characters.
pub const MAX_TITLE_CHARS: usize = 80;

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ClipMeta {
    pub version: u32,
    pub title: String,
    pub game_id: i64,
    pub queue_id: i64,
    /// The recording it was cut from (its folder's name), which may since
    /// have been deleted.
    pub recording_id: Option<String>,
    pub champion_id: Option<i64>,
    pub win: Option<bool>,
    pub kills: Option<i64>,
    pub deaths: Option<i64>,
    pub assists: Option<i64>,
    pub game_started_at_ms: i64,
    pub created_at_ms: i64,
    /// Where in the recording it was cut from.
    pub start_ms: u64,
    pub end_ms: u64,
    /// The clip's own length, as the cut came out.
    pub duration_ms: u64,
    pub favorite: bool,
    /// The recording's moments inside the range, relative to the clip.
    pub moments: Vec<Moment>,
    /// The YouTube video the player published it as, once they gave its link
    /// back to the app (#1815). Absent from a clip saved before then.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub youtube_url: Option<String>,
}

impl ClipMeta {
    /// A clip of `start_ms..end_ms` of a recording, before the cut sets its
    /// real duration.
    pub fn cut_from(
        recording: &RecordingMeta,
        recording_id: &str,
        start_ms: u64,
        end_ms: u64,
        title: &str,
        now_ms: i64,
    ) -> Self {
        let (start, end) = (start_ms as i64, end_ms as i64);
        let moments = recording
            .moments()
            .into_iter()
            .filter(|moment| moment.video_ms >= start && moment.video_ms <= end)
            .map(|moment| moment.shifted(start))
            .collect();
        Self {
            version: CLIP_METADATA_VERSION,
            title: clean_title(title),
            game_id: recording.game_id,
            queue_id: recording.queue_id,
            recording_id: Some(recording_id.to_string()),
            champion_id: recording.champion_id,
            win: recording.win,
            kills: recording.kills,
            deaths: recording.deaths,
            assists: recording.assists,
            game_started_at_ms: recording.started_at_ms,
            created_at_ms: now_ms,
            start_ms,
            end_ms,
            duration_ms: end_ms.saturating_sub(start_ms),
            favorite: false,
            moments,
            youtube_url: None,
        }
    }
}

/// A title as the player typed it, trimmed, never empty and never longer than
/// [`MAX_TITLE_CHARS`].
pub fn clean_title(title: &str) -> String {
    let title: String = title.trim().chars().take(MAX_TITLE_CHARS).collect();
    if title.trim().is_empty() {
        "Clip".into()
    } else {
        title.trim_end().to_string()
    }
}

/// A YouTube video's link, as the player pasted it, in the one form the app
/// keeps (`https://www.youtube.com/watch?v=<id>`) — or `None` when it is not a
/// link to a YouTube video. Accepts the watch page, the short `youtu.be` link
/// YouTube's upload form hands out, Shorts and the video's YouTube Studio page.
pub fn youtube_video_url(link: &str) -> Option<String> {
    youtube_video_id(link).map(|id| format!("https://www.youtube.com/watch?v={id}"))
}

/// The id of the YouTube video a link points to, from the links
/// [`youtube_video_url`] accepts.
pub fn youtube_video_id(link: &str) -> Option<&str> {
    let link = link.trim();
    let rest = ["https://", "http://"]
        .iter()
        .find(|scheme| {
            link.get(..scheme.len())
                .is_some_and(|start| start.eq_ignore_ascii_case(scheme))
        })
        .map_or(link, |scheme| &link[scheme.len()..]);
    let (host, path) = rest.split_once('/').unwrap_or((rest, ""));
    let host = host.to_ascii_lowercase();
    let host = host.strip_prefix("www.").unwrap_or(&host);
    let (path, query) = path.split_once('?').unwrap_or((path, ""));
    let path = path.split('#').next().unwrap_or_default();
    let id = match host {
        "youtu.be" => path.split('/').next(),
        "youtube.com" | "m.youtube.com" | "music.youtube.com" => {
            match path.split('/').collect::<Vec<_>>()[..] {
                ["watch"] => query
                    .split('#')
                    .next()
                    .unwrap_or_default()
                    .split('&')
                    .find_map(|pair| pair.strip_prefix("v=")),
                ["shorts" | "live" | "embed", id, ..] => Some(id),
                _ => None,
            }
        }
        "studio.youtube.com" => match path.split('/').collect::<Vec<_>>()[..] {
            ["video", id, ..] => Some(id),
            _ => None,
        },
        _ => None,
    }?;
    is_video_id(id).then_some(id)
}

/// Whether a YouTube video's title is the clip's own, as the publish dialog
/// hands it over: case, runs of spaces and the `<` `>` YouTube refuses aside.
/// It is how a pasted link is checked to be the clip's video (#2005).
pub fn same_youtube_title(youtube: &str, clip: &str) -> bool {
    let normal = |title: &str| {
        title
            .replace(['<', '>'], "")
            .split_whitespace()
            .collect::<Vec<_>>()
            .join(" ")
            .to_lowercase()
    };
    normal(youtube) == normal(clip)
}

/// YouTube's video ids: eleven characters of its URL-safe base64.
pub fn is_video_id(id: &str) -> bool {
    id.len() == 11
        && id
            .bytes()
            .all(|byte| byte.is_ascii_alphanumeric() || byte == b'-' || byte == b'_')
}

/// One clip's folder.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ClipDir {
    path: PathBuf,
}

impl ClipDir {
    pub fn new(path: PathBuf) -> Self {
        Self { path }
    }

    pub fn path(&self) -> &Path {
        &self.path
    }

    pub fn id(&self) -> String {
        self.path
            .file_name()
            .map(|name| name.to_string_lossy().into_owned())
            .unwrap_or_default()
    }

    pub fn video_path(&self) -> PathBuf {
        self.path.join(CLIP_VIDEO_FILE)
    }

    pub fn thumbnail_path(&self) -> PathBuf {
        self.path.join(CLIP_THUMBNAIL_FILE)
    }

    pub fn read_meta(&self) -> io::Result<ClipMeta> {
        let body = fs::read_to_string(self.path.join(CLIP_METADATA_FILE))?;
        let meta: ClipMeta = serde_json::from_str(&body).map_err(io::Error::other)?;
        if meta.version != CLIP_METADATA_VERSION {
            return Err(io::Error::other(format!(
                "clip metadata version {} is not {CLIP_METADATA_VERSION}",
                meta.version
            )));
        }
        Ok(meta)
    }

    pub fn write_meta(&self, meta: &ClipMeta) -> io::Result<()> {
        let body = serde_json::to_string_pretty(meta).map_err(io::Error::other)?;
        let temporary = self.path.join(CLIP_METADATA_TEMPORARY);
        fs::write(&temporary, body)?;
        fs::rename(&temporary, self.path.join(CLIP_METADATA_FILE))
    }

    pub fn size_bytes(&self) -> u64 {
        [CLIP_VIDEO_FILE, CLIP_METADATA_FILE, CLIP_THUMBNAIL_FILE]
            .iter()
            .filter_map(|name| fs::metadata(self.path.join(name)).ok())
            .map(|metadata| metadata.len())
            .sum()
    }

    /// Delete the clip's video only, keeping its metadata and thumbnail: a
    /// clip published on YouTube stays listed and plays from there (#2005).
    /// Deleting a video already gone is not an error.
    pub fn delete_video(&self) -> io::Result<()> {
        match fs::remove_file(self.video_path()) {
            Err(error) if error.kind() != io::ErrorKind::NotFound => Err(error),
            _ => Ok(()),
        }
    }

    /// Delete the clip's own files, then its folder if nothing else is in it.
    pub fn delete(&self) -> io::Result<()> {
        for name in [
            CLIP_VIDEO_FILE,
            CLIP_METADATA_FILE,
            CLIP_METADATA_TEMPORARY,
            CLIP_THUMBNAIL_FILE,
        ] {
            match fs::remove_file(self.path.join(name)) {
                Ok(()) => {}
                Err(error) if error.kind() == io::ErrorKind::NotFound => {}
                Err(error) => return Err(error),
            }
        }
        let _ = fs::remove_dir(&self.path);
        Ok(())
    }
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct StoredClip {
    pub dir: ClipDir,
    pub meta: ClipMeta,
    pub size_bytes: u64,
}

/// The clips of one recordings folder.
#[derive(Debug, Clone)]
pub struct ClipStore {
    dir: PathBuf,
}

impl ClipStore {
    /// The clips of the recordings folder `root`.
    pub fn new(root: &Path) -> Self {
        Self {
            dir: root.join(CLIPS_DIR),
        }
    }

    /// A new folder for a clip of `meta.game_id`, its metadata not written
    /// yet: the clip is listed only once its video is cut.
    pub fn create(&self, meta: &ClipMeta) -> io::Result<ClipDir> {
        fs::create_dir_all(&self.dir)?;
        let mut attempt = 1;
        loop {
            let name = match attempt {
                1 => format!("{}-{}", meta.game_id, meta.created_at_ms),
                n => format!("{}-{}-{n}", meta.game_id, meta.created_at_ms),
            };
            let path = self.dir.join(name);
            match fs::create_dir(&path) {
                Ok(()) => return Ok(ClipDir::new(path)),
                Err(error) if error.kind() == io::ErrorKind::AlreadyExists => attempt += 1,
                Err(error) => return Err(error),
            }
        }
    }

    /// The clip with this id, if it exists.
    pub fn get(&self, id: &str) -> Option<ClipDir> {
        if !crate::store::is_id(id) {
            return None;
        }
        let dir = ClipDir::new(self.dir.join(id));
        dir.path().join(CLIP_METADATA_FILE).is_file().then_some(dir)
    }

    /// Every clip, newest first.
    pub fn list(&self) -> io::Result<Vec<StoredClip>> {
        let entries = match fs::read_dir(&self.dir) {
            Ok(entries) => entries,
            Err(error) if error.kind() == io::ErrorKind::NotFound => return Ok(Vec::new()),
            Err(error) => return Err(error),
        };
        let mut clips: Vec<StoredClip> = entries
            .filter_map(Result::ok)
            .filter(|entry| entry.file_type().is_ok_and(|kind| kind.is_dir()))
            .filter_map(|entry| {
                let dir = ClipDir::new(entry.path());
                let meta = dir.read_meta().ok()?;
                let size_bytes = dir.size_bytes();
                Some(StoredClip {
                    dir,
                    meta,
                    size_bytes,
                })
            })
            .collect();
        clips.sort_by_key(|clip| std::cmp::Reverse(clip.meta.created_at_ms));
        Ok(clips)
    }

    /// The space every clip takes.
    pub fn size_bytes(&self) -> io::Result<u64> {
        Ok(self.list()?.iter().map(|clip| clip.size_bytes).sum())
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::anchor::{Anchor, Segment};
    use crate::highlights::{Highlight, HighlightKind};
    use crate::settings::{FrameRate, Quality, Resolution};
    use crate::store::Store;

    fn recording() -> RecordingMeta {
        let mut meta = RecordingMeta::new(
            7_100_000_001,
            420,
            Quality {
                resolution: Resolution::P1080,
                frame_rate: FrameRate::Fps30,
            },
            1_000,
        );
        meta.duration_ms = Some(1_800_000);
        meta.anchor = Some(Anchor {
            segments: vec![Segment {
                from_game_ms: 0,
                offset_ms: 20_000,
            }],
        });
        meta.kills = Some(7);
        for (kind, at) in [
            (HighlightKind::Kill, 100_000),
            (HighlightKind::Death, 400_000),
        ] {
            meta.highlights.push(Highlight {
                kind,
                game_time_ms: at,
                end_game_time_ms: at,
                kills: u8::from(kind == HighlightKind::Kill),
                killer_id: None,
                victim_ids: vec![],
            });
        }
        meta
    }

    #[test]
    fn a_clip_keeps_the_moments_inside_its_range_relative_to_its_start() {
        let clip = ClipMeta::cut_from(
            &recording(),
            "7100000001",
            110_000,
            130_000,
            "  Solo kill  ",
            5,
        );
        assert_eq!(clip.title, "Solo kill");
        assert_eq!(clip.kills, Some(7));
        assert_eq!(clip.moments.len(), 1);
        // Game 100 s is video 120 s, 10 s into a clip starting at 110 s.
        assert_eq!(clip.moments[0].video_ms, 10_000);
        assert_eq!(clip.duration_ms, 20_000);
    }

    #[test]
    fn titles_are_trimmed_and_never_empty() {
        assert_eq!(clean_title("   "), "Clip");
        assert_eq!(
            clean_title(&"a".repeat(200)).chars().count(),
            MAX_TITLE_CHARS
        );
    }

    #[test]
    fn youtube_links_are_kept_in_one_form() {
        let kept = Some("https://www.youtube.com/watch?v=dQw4w9WgXcQ".to_string());
        for link in [
            "https://youtu.be/dQw4w9WgXcQ",
            "  https://youtu.be/dQw4w9WgXcQ?si=abc  ",
            "youtu.be/dQw4w9WgXcQ",
            "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
            "https://youtube.com/watch?feature=share&v=dQw4w9WgXcQ#t=3",
            "http://m.youtube.com/watch?v=dQw4w9WgXcQ",
            "https://www.youtube.com/shorts/dQw4w9WgXcQ",
            "https://studio.youtube.com/video/dQw4w9WgXcQ/edit",
            "HTTPS://WWW.YOUTUBE.COM/watch?v=dQw4w9WgXcQ",
        ] {
            assert_eq!(youtube_video_url(link), kept, "{link}");
            assert_eq!(youtube_video_id(link), Some("dQw4w9WgXcQ"), "{link}");
        }
        for link in [
            "",
            "https://www.youtube.com/",
            "https://www.youtube.com/watch?v=short",
            "https://www.youtube.com/@channel",
            "https://evil.example/watch?v=dQw4w9WgXcQ",
            "https://youtube.com.evil.example/watch?v=dQw4w9WgXcQ",
            "https://youtu.be/dQw4w9WgXc<",
            "javascript:alert(1)",
        ] {
            assert_eq!(youtube_video_url(link), None, "{link}");
        }
    }

    #[test]
    fn a_video_matches_its_clip_by_title() {
        assert!(same_youtube_title(
            "Triple kill on Ahri",
            "Triple kill on Ahri"
        ));
        assert!(same_youtube_title(
            "  triple  KILL on ahri ",
            "Triple kill on Ahri"
        ));
        assert!(same_youtube_title("Kill  Dragon", "Kill <> Dragon"));
        assert!(!same_youtube_title(
            "Triple kill on Ahri (2)",
            "Triple kill on Ahri"
        ));
        assert!(!same_youtube_title("Me at the zoo", "Triple kill on Ahri"));
    }

    #[test]
    fn a_clip_saved_before_youtube_links_still_reads() {
        let mut meta = ClipMeta::cut_from(&recording(), "7100000001", 0, 5_000, "Old", 10);
        let mut body = serde_json::to_value(&meta).unwrap();
        assert!(body.get("youtubeUrl").is_none());
        body.as_object_mut().unwrap().remove("youtubeUrl");
        let read: ClipMeta = serde_json::from_value(body).unwrap();
        assert_eq!(read, meta);

        meta.youtube_url = youtube_video_url("https://youtu.be/dQw4w9WgXcQ");
        let read: ClipMeta = serde_json::from_value(serde_json::to_value(&meta).unwrap()).unwrap();
        assert_eq!(read.youtube_url, meta.youtube_url);
    }

    #[test]
    fn clips_are_listed_once_written_and_survive_their_recording() {
        let root = tempfile::tempdir().unwrap();
        let store = Store::new(root.path());
        let recording_dir = store.create(&recording()).unwrap();
        let clips = ClipStore::new(root.path());

        let meta = ClipMeta::cut_from(&recording(), &recording_dir.id(), 0, 5_000, "First", 10);
        let dir = clips.create(&meta).unwrap();
        // Not listed until its metadata is written.
        assert!(clips.list().unwrap().is_empty());
        fs::write(dir.video_path(), vec![0u8; 300]).unwrap();
        dir.write_meta(&meta).unwrap();

        recording_dir.delete().unwrap();
        let listed = clips.list().unwrap();
        let with_video = listed[0].size_bytes;
        assert_eq!(listed.len(), 1);
        assert!(listed[0].size_bytes >= 300);
        // The clips folder is not a recording.
        assert!(store.list().unwrap().is_empty());
        assert!(clips.get(&dir.id()).is_some());
        assert!(clips.get("../x").is_none());

        // Without its video, the clip is still listed, at its remaining size.
        dir.delete_video().unwrap();
        dir.delete_video().unwrap();
        let listed = clips.list().unwrap();
        assert_eq!(listed.len(), 1);
        assert_eq!(listed[0].size_bytes, with_video - 300);

        dir.delete().unwrap();
        assert!(clips.list().unwrap().is_empty());
    }
}
