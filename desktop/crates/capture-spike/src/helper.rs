//! The platform capture helper, driven over its JSON-lines protocol and
//! presented to `game-recording` as a [`Capture`].
//!
//! The video clock starts when the helper reports `started` — the moment its
//! first frame went to the encoder, which is video time zero.

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

pub struct HelperCapture {
    binary: PathBuf,
    window_id: Option<u32>,
    source: String,
    audio: bool,
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
            child: None,
            stdin: None,
            events: None,
            started_at: None,
            progress: Arc::default(),
            failure: Arc::default(),
            window: None,
            output: None,
            stopped: None,
        }
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
        let output = Command::new(&self.binary)
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

impl Capture for HelperCapture {
    fn start(&mut self, video_path: &Path, quality: Quality) -> Result<(), CaptureError> {
        let window = self.probe()?;
        let width = window["width"].as_u64().unwrap_or(0) as u32;
        let height = window["height"].as_u64().unwrap_or(0) as u32;
        if width == 0 || height == 0 {
            return Err(CaptureError(format!(
                "the helper gave no window size: {window}"
            )));
        }
        let output = quality.output_for(width, height);

        let mut command = Command::new(&self.binary);
        command
            .arg("record")
            .arg("--out")
            .arg(video_path)
            .args(["--width", &output.width.to_string()])
            .args(["--height", &output.height.to_string()])
            .args(["--fps", &output.frame_rate.to_string()])
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
                        eprintln!(
                            "  recording {:>5}s · {} frames · {} dropped · helper CPU {:.1}% · sent: {}",
                            entry.elapsed_ms / 1000,
                            entry.frames,
                            entry.dropped,
                            entry.cpu_percent,
                            entry.statuses
                        );
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
        if let Some(mut child) = self.child.take() {
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

fn parse_events(stdout: &[u8]) -> Vec<Value> {
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

fn error_of(event: &Value) -> CaptureError {
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
}
