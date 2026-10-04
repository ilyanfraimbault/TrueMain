//! The platform capture helper (`truemain-capture`), driven over its
//! JSON-lines protocol: presented to `game-recording` as a [`Capture`] while a
//! game records, and asked for clips, thumbnails and the Screen Recording
//! permission afterwards ([`media`]). Shared by the app's shell and the
//! capture spike, so both drive the helper the same way.
//!
//! The video clock starts when the helper reports `started` — the moment its
//! first frame went to the encoder, which is video time zero.

pub mod media;

use std::io::{BufRead, BufReader, Write};
use std::path::{Path, PathBuf};
use std::process::{Child, ChildStdin, Command, Stdio};
use std::sync::mpsc::{self, Receiver, RecvTimeoutError};
use std::sync::{Arc, Mutex};
use std::thread;
use std::time::{Duration, Instant};

use game_recording::settings::OutputSpec;
use game_recording::{Capture, CaptureError, Quality};
use serde_json::Value;

const START_TIMEOUT: Duration = Duration::from_secs(30);
const STOP_TIMEOUT: Duration = Duration::from_secs(60);

/// One `progress` event.
#[derive(Debug, Clone, PartialEq)]
pub struct Progress {
    pub elapsed_ms: u64,
    pub frames: u64,
    pub dropped: u64,
    pub cpu_percent: f64,
    /// Frames ScreenCaptureKit sent, by status (`complete 290, idle 10`) —
    /// what tells a window never drawn from one never captured.
    pub statuses: String,
}

/// What the helper said once it stopped.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct Stopped {
    pub duration_ms: u64,
    pub frames: u64,
    pub dropped: u64,
}

/// The encoder the helper is asked for. HEVC needs about half H.264's
/// bitrate for the same picture.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub enum Codec {
    #[default]
    H264,
    Hevc,
}

impl Codec {
    pub fn parse(value: &str) -> Option<Self> {
        match value {
            "h264" => Some(Self::H264),
            "hevc" => Some(Self::Hevc),
            _ => None,
        }
    }

    pub fn as_arg(self) -> &'static str {
        match self {
            Self::H264 => "h264",
            Self::Hevc => "hevc",
        }
    }

    pub fn label(self) -> &'static str {
        match self {
            Self::H264 => "H.264",
            Self::Hevc => "HEVC",
        }
    }
}

pub struct HelperCapture {
    binary: PathBuf,
    window_id: Option<u32>,
    source: String,
    audio: bool,
    codec: Codec,
    /// Replaces the bitrate `Quality::output_for` derives, to compare
    /// encodings on the same game.
    bitrate_bps: Option<u64>,
    child: Option<Child>,
    stdin: Option<ChildStdin>,
    events: Option<Receiver<(Instant, Value)>>,
    started_at: Option<Instant>,
    /// Every `progress` event, for the report.
    pub progress: Arc<Mutex<Vec<Progress>>>,
    /// The first error the helper reported once recording, if any.
    pub failure: Arc<Mutex<Option<String>>>,
    /// The window the helper recorded, as it described it.
    pub window: Option<Value>,
    /// Print each `progress` event on stderr, as the spike does.
    pub log_progress: bool,
    pub output: Option<OutputSpec>,
    pub stopped: Option<Stopped>,
}

impl HelperCapture {
    /// `source` is `window` (the game's window alone) or `display` (its
    /// display, with only the game's windows drawn).
    pub fn new(binary: PathBuf, window_id: Option<u32>, source: String, audio: bool) -> Self {
        Self {
            binary,
            window_id,
            source,
            audio,
            codec: Codec::default(),
            bitrate_bps: None,
            child: None,
            stdin: None,
            events: None,
            started_at: None,
            progress: Arc::default(),
            failure: Arc::default(),
            window: None,
            log_progress: false,
            output: None,
            stopped: None,
        }
    }

    pub fn with_encoding(mut self, codec: Codec, bitrate_bps: Option<u64>) -> Self {
        self.codec = codec;
        self.bitrate_bps = bitrate_bps;
        self
    }

    pub fn logging_progress(mut self) -> Self {
        self.log_progress = true;
        self
    }

    pub fn binary(&self) -> &Path {
        &self.binary
    }

    pub fn codec(&self) -> Codec {
        self.codec
    }

    fn window_args(&self) -> Vec<String> {
        let mut args = vec!["--source".to_string(), self.source.clone()];
        if let Some(id) = self.window_id {
            args.extend(["--window-id".to_string(), id.to_string()]);
        }
        args
    }

    /// Ask the helper which window it would record, and its size in pixels.
    pub fn probe(&self) -> Result<Value, CaptureError> {
        let output = command(&self.binary)
            .arg("probe")
            .args(self.window_args())
            .stderr(Stdio::inherit())
            .output()
            .map_err(|e| CaptureError(format!("could not run {}: {e}", self.binary.display())))?;
        let last = parse_events(&output.stdout).pop();
        match last {
            Some(event) if event["event"] == "window" => Ok(event),
            Some(event) => Err(error_of(&event)),
            None => Err(CaptureError(format!(
                "the helper answered nothing (exit {})",
                output.status
            ))),
        }
    }

    /// Wait for one event. `error` events end the wait unless `through_errors`
    /// — when stopping, a capture that failed on its own still closes its file
    /// and reports `stopped` after the error.
    fn wait_for(
        &self,
        name: &str,
        timeout: Duration,
        through_errors: bool,
    ) -> Result<(Instant, Value), CaptureError> {
        let events = self
            .events
            .as_ref()
            .ok_or_else(|| CaptureError("the helper is not running".into()))?;
        let deadline = Instant::now() + timeout;
        loop {
            let left = deadline.saturating_duration_since(Instant::now());
            match events.recv_timeout(left) {
                Ok((at, event)) if event["event"] == name => return Ok((at, event)),
                Ok((_, event)) if event["event"] == "error" && !through_errors => {
                    return Err(error_of(&event))
                }
                Ok(_) => continue,
                Err(RecvTimeoutError::Timeout) => {
                    return Err(CaptureError(format!(
                        "no `{name}` from the helper in {timeout:?}"
                    )))
                }
                Err(RecvTimeoutError::Disconnected) => {
                    return Err(CaptureError(format!("the helper exited before `{name}`")))
                }
            }
        }
    }

    fn kill(&mut self) {
        if let Some(mut child) = self.child.take() {
            let _ = child.kill();
            let _ = child.wait();
        }
        self.stdin = None;
        self.events = None;
    }
}

/// Never leave a recording helper behind: dropped mid-recording (the app
/// quitting, a panic in the runner), the capture would outlive the app.
impl Drop for HelperCapture {
    fn drop(&mut self) {
        self.kill();
    }
}

impl Capture for HelperCapture {
    fn start(&mut self, video_path: &Path, quality: Quality) -> Result<(), CaptureError> {
        // Fresh slots for every attempt: an error from a start that failed
        // must not end the one that follows, and the reader thread of a
        // killed helper keeps writing only into the slots it was given.
        self.progress = Arc::default();
        self.failure = Arc::default();
        let window = self.probe()?;
        let width = window["width"].as_u64().unwrap_or(0) as u32;
        let height = window["height"].as_u64().unwrap_or(0) as u32;
        if width == 0 || height == 0 {
            return Err(CaptureError(format!(
                "the helper gave no window size: {window}"
            )));
        }
        let mut output = quality.output_for(width, height);
        if let Some(bitrate_bps) = self.bitrate_bps {
            output.bitrate_bps = bitrate_bps;
        }

        let mut command = command(&self.binary);
        command
            .arg("record")
            .arg("--out")
            .arg(video_path)
            .args(["--width", &output.width.to_string()])
            .args(["--height", &output.height.to_string()])
            .args(["--fps", &output.frame_rate.to_string()])
            .args(["--codec", self.codec.as_arg()])
            .args(["--bitrate", &output.bitrate_bps.to_string()])
            .args([
                "--keyframe-interval",
                &output.keyframe_interval_frames.to_string(),
            ])
            .args(self.window_args());
        if !self.audio {
            command.arg("--no-audio");
        }
        let mut child = command
            .stdin(Stdio::piped())
            .stdout(Stdio::piped())
            .stderr(Stdio::inherit())
            .spawn()
            .map_err(|e| CaptureError(format!("could not run {}: {e}", self.binary.display())))?;

        let stdout = child.stdout.take().expect("stdout is piped");
        let (sender, receiver) = mpsc::channel();
        let progress = self.progress.clone();
        let failure = self.failure.clone();
        let log_progress = self.log_progress;
        thread::spawn(move || {
            for line in BufReader::new(stdout).lines().map_while(Result::ok) {
                let at = Instant::now();
                let Ok(event) = serde_json::from_str::<Value>(&line) else {
                    continue;
                };
                match event["event"].as_str() {
                    Some("progress") => {
                        let entry = Progress {
                            elapsed_ms: event["elapsedMs"].as_u64().unwrap_or(0),
                            frames: event["frames"].as_u64().unwrap_or(0),
                            dropped: event["dropped"].as_u64().unwrap_or(0),
                            cpu_percent: event["cpuPercent"].as_f64().unwrap_or(0.0),
                            statuses: statuses_of(&event["statuses"]),
                        };
                        if log_progress {
                            eprintln!(
                            "  recording {:>5}s · {} frames · {} dropped · helper CPU {:.1}% · sent: {}",
                            entry.elapsed_ms / 1000,
                            entry.frames,
                            entry.dropped,
                            entry.cpu_percent,
                            entry.statuses
                        );
                        }
                        progress.lock().unwrap().push(entry);
                        continue;
                    }
                    Some("error") => {
                        let mut slot = failure.lock().unwrap();
                        if slot.is_none() {
                            *slot = Some(error_of(&event).0);
                        }
                    }
                    _ => {}
                }
                if sender.send((at, event)).is_err() {
                    break;
                }
            }
        });

        self.stdin = child.stdin.take();
        self.child = Some(child);
        self.events = Some(receiver);
        self.window = Some(window);
        self.output = Some(output);

        match self.wait_for("started", START_TIMEOUT, false) {
            Ok((at, _)) => {
                self.started_at = Some(at);
                Ok(())
            }
            Err(error) => {
                self.kill();
                Err(error)
            }
        }
    }

    fn elapsed_ms(&self) -> Option<u64> {
        self.started_at
            .map(|at| u64::try_from(at.elapsed().as_millis()).unwrap_or(u64::MAX))
    }

    fn stop(&mut self) -> Result<u64, CaptureError> {
        self.started_at = None;
        if let Some(stdin) = self.stdin.as_mut() {
            // A helper that already died has closed its end; the wait below
            // reports that.
            let _ = writeln!(stdin, "stop").and_then(|()| stdin.flush());
        }
        let result = self.wait_for("stopped", STOP_TIMEOUT, true);
        // A helper that never said `stopped` may be stuck in ScreenCaptureKit
        // with the capture still running — and macOS still showing it
        // recording. Kill it rather than wait on it forever.
        if result.is_err() {
            self.kill();
        } else if let Some(mut child) = self.child.take() {
            let _ = child.wait();
        }
        self.stdin = None;
        self.events = None;
        let (_, event) = result?;
        let stopped = Stopped {
            duration_ms: event["durationMs"].as_u64().unwrap_or(0),
            frames: event["frames"].as_u64().unwrap_or(0),
            dropped: event["dropped"].as_u64().unwrap_or(0),
        };
        self.stopped = Some(stopped);
        Ok(stopped.duration_ms)
    }
}

/// The helper as a child process. On Windows it is a console program started
/// from a windowed app, which would otherwise open a console window over the
/// game.
pub(crate) fn command(binary: &Path) -> Command {
    #[allow(unused_mut)]
    let mut command = Command::new(binary);
    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        const CREATE_NO_WINDOW: u32 = 0x0800_0000;
        command.creation_flags(CREATE_NO_WINDOW);
    }
    command
}

pub(crate) fn parse_events(stdout: &[u8]) -> Vec<Value> {
    String::from_utf8_lossy(stdout)
        .lines()
        .filter_map(|line| serde_json::from_str(line).ok())
        .collect()
}

/// `{"complete": 290, "idle": 10}` as `complete 290, idle 10`, most first.
fn statuses_of(value: &Value) -> String {
    let mut counts: Vec<(&String, u64)> = value
        .as_object()
        .map(|map| {
            map.iter()
                .map(|(k, v)| (k, v.as_u64().unwrap_or(0)))
                .collect()
        })
        .unwrap_or_default();
    if counts.is_empty() {
        return "nothing".into();
    }
    counts.sort_by(|a, b| b.1.cmp(&a.1).then(a.0.cmp(b.0)));
    counts
        .iter()
        .map(|(name, count)| format!("{name} {count}"))
        .collect::<Vec<_>>()
        .join(", ")
}

pub(crate) fn error_of(event: &Value) -> CaptureError {
    CaptureError(format!(
        "{} ({})",
        event["message"].as_str().unwrap_or("the helper failed"),
        event["kind"].as_str().unwrap_or("unknown")
    ))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn reads_events_line_by_line_and_skips_noise() {
        let events = parse_events(
            b"{\"event\":\"window\",\"width\":2880,\"height\":1800}\nnot json\n{\"event\":\"error\",\"kind\":\"no-window\",\"message\":\"none\"}\n",
        );
        assert_eq!(events.len(), 2);
        assert_eq!(events[0]["width"], 2880);
        assert_eq!(error_of(&events[1]).0, "none (no-window)");
    }

    #[test]
    fn frame_statuses_read_most_first() {
        let statuses = serde_json::json!({ "idle": 12, "complete": 290, "blank": 0 });
        assert_eq!(statuses_of(&statuses), "complete 290, idle 12, blank 0");
        assert_eq!(statuses_of(&Value::Null), "nothing");
    }

    #[test]
    fn codecs_read_from_their_argument() {
        assert_eq!(Codec::parse("hevc"), Some(Codec::Hevc));
        assert_eq!(Codec::parse("h264").map(Codec::as_arg), Some("h264"));
        assert_eq!(Codec::parse("av1"), None);
    }

    #[test]
    fn a_missing_helper_is_a_capture_error() {
        let capture = HelperCapture::new(
            "/nonexistent/truemain-capture".into(),
            None,
            "window".into(),
            true,
        );
        let error = capture.probe().unwrap_err();
        assert!(error.0.contains("could not run"), "{error}");
    }

    /// A stand-in helper whose first `record` fails and whose second starts.
    #[cfg(unix)]
    fn flaky_helper(dir: &Path) -> PathBuf {
        use std::os::unix::fs::PermissionsExt;
        let marker = dir.join("failed-once");
        let script = dir.join("truemain-capture");
        std::fs::write(
            &script,
            format!(
                r#"#!/bin/sh
case "$1" in
probe) echo '{{"event":"window","width":1920,"height":1080}}' ;;
record)
  if [ ! -e "{marker}" ]; then
    touch "{marker}"
    echo '{{"event":"error","kind":"capture","message":"not drawn yet"}}'
    exit 1
  fi
  echo '{{"event":"started"}}'
  read line
  echo '{{"event":"stopped","durationMs":1000,"frames":30,"dropped":0}}'
  ;;
esac
"#,
                marker = marker.display()
            ),
        )
        .unwrap();
        std::fs::set_permissions(&script, std::fs::Permissions::from_mode(0o755)).unwrap();
        script
    }

    #[cfg(unix)]
    #[test]
    fn an_error_from_a_failed_start_does_not_outlive_it() {
        let dir = tempfile::tempdir().unwrap();
        let mut capture =
            HelperCapture::new(flaky_helper(dir.path()), None, "window".into(), false);
        let video = dir.path().join("game.mp4");
        let quality = game_recording::RecordingSettings::default().quality;

        let error = capture.start(&video, quality).unwrap_err();
        assert!(error.0.contains("not drawn yet"), "{error}");

        capture.start(&video, quality).unwrap();
        assert_eq!(*capture.failure.lock().unwrap(), None);
        assert_eq!(capture.stop().unwrap(), 1000);
        assert_eq!(*capture.failure.lock().unwrap(), None);
    }

    /// A stand-in helper that starts recording and never stops, whatever it
    /// is told — the one ScreenCaptureKit left hanging — and leaves its pid.
    #[cfg(unix)]
    fn stuck_helper(dir: &Path) -> PathBuf {
        use std::os::unix::fs::PermissionsExt;
        let script = dir.join("truemain-capture");
        std::fs::write(
            &script,
            format!(
                r#"#!/bin/sh
case "$1" in
probe) echo '{{"event":"window","width":1920,"height":1080}}' ;;
record)
  echo $$ > "{pid}"
  echo '{{"event":"started"}}'
  exec sleep 600
  ;;
esac
"#,
                pid = dir.join("pid").display()
            ),
        )
        .unwrap();
        std::fs::set_permissions(&script, std::fs::Permissions::from_mode(0o755)).unwrap();
        script
    }

    #[cfg(unix)]
    #[test]
    fn a_dropped_capture_does_not_leave_its_helper_running() {
        let dir = tempfile::tempdir().unwrap();
        let mut capture =
            HelperCapture::new(stuck_helper(dir.path()), None, "window".into(), false);
        let quality = game_recording::RecordingSettings::default().quality;
        capture
            .start(&dir.path().join("game.mp4"), quality)
            .unwrap();
        let pid = std::fs::read_to_string(dir.path().join("pid")).unwrap();

        drop(capture);

        let alive = std::process::Command::new("kill")
            .args(["-0", pid.trim()])
            .stderr(Stdio::null())
            .status()
            .unwrap()
            .success();
        assert!(!alive, "the helper {} is still running", pid.trim());
    }
}
