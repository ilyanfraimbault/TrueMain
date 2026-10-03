//! What the player chooses about their recordings — deliberately little.
//!
//! The product owner's call (#1744): the resolution and the frame rate, so the
//! player can pick what their machine runs best, and nothing like OBS's
//! settings. Codec, bitrate and keyframe interval are derived from those two,
//! so no combination the player can pick produces a broken recording.
//!
//! The file is read leniently: a value this build does not know, or one out of
//! range, falls back to its default on its own rather than failing the whole
//! file — a setting must never be the reason a game is not recorded.

use std::fs;
use std::io;
use std::path::{Path, PathBuf};

use serde::de::DeserializeOwned;
use serde::{Deserialize, Serialize};
use serde_json::Value;

/// The settings file's format version, written into it so a later format can
/// migrate rather than guess.
pub const SETTINGS_VERSION: u32 = 1;

/// The queues the client marks as ranked: Solo/Duo and Flex.
const RANKED_QUEUES: [i64; 2] = [420, 440];

/// Budgets are in decimal gigabytes, the unit Finder and the settings page
/// show, so the default reads as a round "50 GB".
const GB: u64 = 1_000_000_000;

/// Below this a budget would not hold a single long game at the lowest preset.
pub const MIN_BUDGET_BYTES: u64 = 2 * GB;

/// H.264 bits per pixel per frame. One constant rather than a table per
/// preset, so every resolution and frame rate gets a consistent quality.
const BITS_PER_PIXEL: f64 = 0.1;

/// One keyframe a second: a jump to a highlight lands on a keyframe within a
/// second, and a fragment is never longer than that if the app dies mid-game.
pub const KEYFRAME_INTERVAL_SECONDS: u32 = 1;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum Resolution {
    /// The game window's own size.
    #[serde(rename = "native")]
    Native,
    #[serde(rename = "1440p")]
    P1440,
    #[serde(rename = "1080p")]
    P1080,
    #[serde(rename = "720p")]
    P720,
}

impl Resolution {
    /// The output height this preset asks for; `None` for the window's own.
    pub fn height(self) -> Option<u32> {
        match self {
            Self::Native => None,
            Self::P1440 => Some(1440),
            Self::P1080 => Some(1080),
            Self::P720 => Some(720),
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(try_from = "u32", into = "u32")]
pub enum FrameRate {
    Fps30,
    Fps60,
}

impl FrameRate {
    pub fn per_second(self) -> u32 {
        match self {
            Self::Fps30 => 30,
            Self::Fps60 => 60,
        }
    }
}

impl TryFrom<u32> for FrameRate {
    type Error = String;

    fn try_from(value: u32) -> Result<Self, Self::Error> {
        match value {
            30 => Ok(Self::Fps30),
            60 => Ok(Self::Fps60),
            other => Err(format!("{other} fps is not offered")),
        }
    }
}

impl From<FrameRate> for u32 {
    fn from(value: FrameRate) -> Self {
        value.per_second()
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum Queues {
    /// Ranked Solo/Duo and Flex only.
    Ranked,
    All,
}

impl Queues {
    pub fn includes(self, queue_id: i64) -> bool {
        match self {
            Self::Ranked => RANKED_QUEUES.contains(&queue_id),
            Self::All => true,
        }
    }
}

/// The two choices the player makes about quality.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Quality {
    pub resolution: Resolution,
    pub frame_rate: FrameRate,
}

/// What the encoder is given, derived from a [`Quality`] and the game window.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct OutputSpec {
    pub width: u32,
    pub height: u32,
    pub frame_rate: u32,
    pub bitrate_bps: u64,
    pub keyframe_interval_frames: u32,
}

impl Quality {
    /// The video to encode from a game window of `window_width` × `window_height`.
    ///
    /// Never upscaled: a preset taller than the window records at the window's
    /// size, since upscaling costs encoder time and disk for no detail. The
    /// aspect ratio is the window's, and both sides are even, which H.264's
    /// 4:2:0 sampling requires.
    pub fn output_for(self, window_width: u32, window_height: u32) -> OutputSpec {
        let (width, height) = match self.resolution.height() {
            Some(target) if target < window_height && window_height > 0 => {
                let scaled = u64::from(window_width) * u64::from(target) / u64::from(window_height);
                (u32::try_from(scaled).unwrap_or(u32::MAX), target)
            }
            _ => (window_width, window_height),
        };
        let (width, height) = (even(width), even(height));
        let frame_rate = self.frame_rate.per_second();
        OutputSpec {
            width,
            height,
            frame_rate,
            bitrate_bps: bitrate(width, height, frame_rate),
            keyframe_interval_frames: frame_rate * KEYFRAME_INTERVAL_SECONDS,
        }
    }

    /// The bitrate at this preset for a 16:9 window at least as tall as it —
    /// what the settings show the size of an hour from. `None` for `Native`,
    /// whose size is only known once a game window exists.
    pub fn nominal_bitrate_bps(self) -> Option<u64> {
        let height = self.resolution.height()?;
        Some(bitrate(
            even(height * 16 / 9),
            height,
            self.frame_rate.per_second(),
        ))
    }
}

/// One field of the settings file, or `None` when absent or invalid.
fn parse<T: DeserializeOwned>(value: Option<&Value>) -> Option<T> {
    serde_json::from_value(value?.clone()).ok()
}

fn even(value: u32) -> u32 {
    value & !1
}

fn bitrate(width: u32, height: u32, frame_rate: u32) -> u64 {
    let pixels_per_second = f64::from(width) * f64::from(height) * f64::from(frame_rate);
    (pixels_per_second * BITS_PER_PIXEL).round() as u64
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct RecordingSettings {
    /// Off until the player turns it on: it costs disk and, on macOS, a
    /// permission.
    pub enabled: bool,
    pub queues: Queues,
    pub quality: Quality,
    /// The most the recordings folder may hold; the oldest unpinned
    /// recordings go first when it is exceeded.
    pub budget_bytes: u64,
    /// `None` is the app's own data folder, decided by the shell.
    pub folder: Option<PathBuf>,
}

impl Default for RecordingSettings {
    fn default() -> Self {
        Self {
            enabled: false,
            queues: Queues::All,
            // The conservative end until #1745 measures what recording costs
            // in game; the player can raise it.
            quality: Quality {
                resolution: Resolution::P1080,
                frame_rate: FrameRate::Fps30,
            },
            budget_bytes: 50 * GB,
            folder: None,
        }
    }
}

impl RecordingSettings {
    pub fn records(&self, queue_id: i64) -> bool {
        self.enabled && self.queues.includes(queue_id)
    }

    /// Read the settings file. A missing or unreadable file is the defaults; a
    /// field that does not parse is that field's default. Never fails.
    pub fn load(path: &Path) -> Self {
        match fs::read_to_string(path) {
            Ok(body) => Self::from_json(&body),
            Err(error) if error.kind() == io::ErrorKind::NotFound => Self::default(),
            Err(error) => {
                tracing::warn!(%error, path = %path.display(), "recording settings unreadable, using defaults");
                Self::default()
            }
        }
    }

    pub fn from_json(body: &str) -> Self {
        let Ok(Value::Object(fields)) = serde_json::from_str::<Value>(body) else {
            tracing::warn!("recording settings are not a JSON object, using defaults");
            return Self::default();
        };
        let defaults = Self::default();
        let quality = fields.get("quality");

        Self {
            enabled: parse(fields.get("enabled")).unwrap_or(defaults.enabled),
            queues: parse(fields.get("queues")).unwrap_or(defaults.queues),
            quality: Quality {
                resolution: parse(quality.and_then(|q| q.get("resolution")))
                    .unwrap_or(defaults.quality.resolution),
                frame_rate: parse(quality.and_then(|q| q.get("frameRate")))
                    .unwrap_or(defaults.quality.frame_rate),
            },
            budget_bytes: parse::<u64>(fields.get("budgetBytes"))
                .filter(|budget| *budget >= MIN_BUDGET_BYTES)
                .unwrap_or(defaults.budget_bytes),
            folder: parse::<PathBuf>(fields.get("folder")).filter(|folder| folder.is_absolute()),
        }
    }

    /// Write the settings, versioned, through a temporary file so a crash
    /// mid-write never leaves half a file behind.
    pub fn save(&self, path: &Path) -> io::Result<()> {
        let mut value = serde_json::to_value(self).map_err(io::Error::other)?;
        value["version"] = SETTINGS_VERSION.into();
        let body = serde_json::to_string_pretty(&value).map_err(io::Error::other)?;
        if let Some(parent) = path.parent() {
            fs::create_dir_all(parent)?;
        }
        let temporary = path.with_extension("json.tmp");
        fs::write(&temporary, body)?;
        fs::rename(&temporary, path)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn quality(resolution: Resolution, frame_rate: FrameRate) -> Quality {
        Quality {
            resolution,
            frame_rate,
        }
    }

    #[test]
    fn a_preset_below_the_window_scales_down_keeping_its_aspect() {
        let out = quality(Resolution::P1080, FrameRate::Fps60).output_for(2560, 1440);
        assert_eq!((out.width, out.height), (1920, 1080));
        assert_eq!(out.frame_rate, 60);
        assert_eq!(out.keyframe_interval_frames, 60);

        // An ultrawide keeps its own aspect, not 16:9.
        let out = quality(Resolution::P1080, FrameRate::Fps30).output_for(3440, 1440);
        assert_eq!((out.width, out.height), (2580, 1080));
    }

    #[test]
    fn a_preset_above_the_window_never_upscales() {
        let out = quality(Resolution::P1440, FrameRate::Fps30).output_for(1920, 1080);
        assert_eq!((out.width, out.height), (1920, 1080));
    }

    #[test]
    fn native_records_the_window_with_even_sides() {
        let out = quality(Resolution::Native, FrameRate::Fps30).output_for(1365, 767);
        assert_eq!((out.width, out.height), (1364, 766));
    }

    #[test]
    fn the_bitrate_follows_the_pixel_rate() {
        let at = |frame_rate| {
            quality(Resolution::P1080, frame_rate)
                .output_for(1920, 1080)
                .bitrate_bps
        };
        assert_eq!(at(FrameRate::Fps30), 6_220_800);
        assert_eq!(at(FrameRate::Fps60), 2 * at(FrameRate::Fps30));
        assert_eq!(
            quality(Resolution::P1080, FrameRate::Fps30).nominal_bitrate_bps(),
            Some(6_220_800)
        );
        assert_eq!(
            quality(Resolution::Native, FrameRate::Fps30).nominal_bitrate_bps(),
            None
        );
    }

    #[test]
    fn only_ranked_queues_when_asked() {
        let mut settings = RecordingSettings {
            enabled: true,
            queues: Queues::Ranked,
            ..RecordingSettings::default()
        };
        assert!(settings.records(420));
        assert!(settings.records(440));
        assert!(!settings.records(450));
        settings.queues = Queues::All;
        assert!(settings.records(450));
        settings.enabled = false;
        assert!(!settings.records(420));
    }

    #[test]
    fn round_trips_through_its_file() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("nested").join("recording.json");
        let settings = RecordingSettings {
            enabled: true,
            queues: Queues::Ranked,
            quality: quality(Resolution::P1440, FrameRate::Fps60),
            budget_bytes: 10 * GB,
            folder: Some(dir.path().join("videos")),
        };
        settings.save(&path).unwrap();

        let body = fs::read_to_string(&path).unwrap();
        assert!(body.contains("\"version\": 1"), "{body}");
        assert!(body.contains("\"frameRate\": 60"), "{body}");
        assert!(body.contains("\"resolution\": \"1440p\""), "{body}");
        assert_eq!(RecordingSettings::load(&path), settings);
    }

    #[test]
    fn a_missing_file_is_the_defaults() {
        let dir = tempfile::tempdir().unwrap();
        assert_eq!(
            RecordingSettings::load(&dir.path().join("absent.json")),
            RecordingSettings::default()
        );
    }

    #[test]
    fn a_bad_field_falls_back_alone() {
        let settings = RecordingSettings::from_json(
            r#"{ "version": 9, "enabled": true, "queues": "aram",
                 "quality": { "resolution": "4k", "frameRate": 60 },
                 "budgetBytes": 1024, "folder": "relative/path" }"#,
        );
        let defaults = RecordingSettings::default();
        assert!(settings.enabled);
        assert_eq!(settings.queues, defaults.queues);
        assert_eq!(settings.quality.resolution, defaults.quality.resolution);
        assert_eq!(settings.quality.frame_rate, FrameRate::Fps60);
        assert_eq!(settings.budget_bytes, defaults.budget_bytes);
        assert_eq!(settings.folder, None);
    }

    #[test]
    fn a_frame_rate_not_offered_is_refused() {
        let settings = RecordingSettings::from_json(r#"{ "quality": { "frameRate": 144 } }"#);
        assert_eq!(settings.quality.frame_rate, FrameRate::Fps30);
    }

    #[test]
    fn garbage_is_the_defaults() {
        assert_eq!(
            RecordingSettings::from_json("not json"),
            RecordingSettings::default()
        );
        assert_eq!(
            RecordingSettings::from_json("[1, 2]"),
            RecordingSettings::default()
        );
    }
}
