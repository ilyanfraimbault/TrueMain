//! The helper's one-shot commands: the Screen Recording permission, a clip
//! cut out of a recording and a thumbnail taken from one.
//!
//! Each runs the helper once and reads the one event it answers with, an
//! `error` event becoming a [`CaptureError`]. They block for as long as the
//! helper runs — a clip is a copy, not an encode, so seconds at most — and
//! are meant for a blocking thread.

use std::path::Path;
use std::process::{Command, Stdio};

use game_recording::CaptureError;
use serde_json::Value;

use crate::{error_of, parse_events};

/// What to do about the Screen Recording permission when it is not granted.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum AccessRequest {
    /// Only read it.
    Check,
    /// Show the system prompt — which macOS shows once per app, ever.
    Prompt,
    /// Open System Settings on the Screen Recording pane, where a refusal is
    /// undone.
    OpenSettings,
}

/// Whether the app may record the screen.
pub fn access(binary: &Path, request: AccessRequest) -> Result<bool, CaptureError> {
    let mut args = vec!["access"];
    match request {
        AccessRequest::Check => {}
        AccessRequest::Prompt => args.push("--request"),
        AccessRequest::OpenSettings => args.push("--open-settings"),
    }
    let event = run(binary, &args, "access")?;
    Ok(event["granted"].as_bool().unwrap_or(false))
}

/// Copy `start_ms..end_ms` of `input` into `output`, without re-encoding.
/// Returns the clip's duration.
pub fn clip(
    binary: &Path,
    input: &Path,
    output: &Path,
    start_ms: u64,
    end_ms: u64,
) -> Result<u64, CaptureError> {
    let (start, end) = (start_ms.to_string(), end_ms.to_string());
    let event = run(
        binary,
        &[
            "clip",
            "--in",
            &input.to_string_lossy(),
            "--out",
            &output.to_string_lossy(),
            "--start-ms",
            &start,
            "--end-ms",
            &end,
        ],
        "clipped",
    )?;
    Ok(event["durationMs"].as_u64().unwrap_or(end_ms - start_ms))
}

/// One frame of `input` at `at_ms`, written as a JPEG `width` pixels wide.
pub fn thumbnail(
    binary: &Path,
    input: &Path,
    output: &Path,
    at_ms: u64,
    width: u32,
) -> Result<(), CaptureError> {
    let (at, width) = (at_ms.to_string(), width.to_string());
    run(
        binary,
        &[
            "thumbnail",
            "--in",
            &input.to_string_lossy(),
            "--out",
            &output.to_string_lossy(),
            "--at-ms",
            &at,
            "--width",
            &width,
        ],
        "thumbnail",
    )
    .map(|_| ())
}

/// Run the helper once and return the `expected` event it answered with.
fn run(binary: &Path, args: &[&str], expected: &str) -> Result<Value, CaptureError> {
    let output = Command::new(binary)
        .args(args)
        .stdin(Stdio::null())
        .stderr(Stdio::null())
        .output()
        .map_err(|e| CaptureError(format!("could not run {}: {e}", binary.display())))?;
    answer(&output.stdout, expected).ok_or_else(|| {
        CaptureError(format!(
            "the helper answered no `{expected}` (exit {})",
            output.status
        ))
    })?
}

/// The `expected` event or the `error` event in the helper's output, if any.
fn answer(stdout: &[u8], expected: &str) -> Option<Result<Value, CaptureError>> {
    parse_events(stdout).into_iter().find_map(|event| {
        if event["event"] == expected {
            Some(Ok(event))
        } else if event["event"] == "error" {
            Some(Err(error_of(&event)))
        } else {
            None
        }
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn the_expected_event_or_the_error_is_the_answer() {
        let ok = answer(b"{\"event\":\"clipped\",\"durationMs\":14700}\n", "clipped");
        assert_eq!(ok.unwrap().unwrap()["durationMs"], 14700);

        let failed = answer(
            b"{\"event\":\"error\",\"kind\":\"clip\",\"message\":\"no video\"}\n",
            "clipped",
        );
        assert_eq!(failed.unwrap().unwrap_err().0, "no video (clip)");

        assert!(answer(b"noise\n", "clipped").is_none());
    }

    #[test]
    fn a_missing_helper_is_an_error() {
        let error = access(
            Path::new("/nonexistent/truemain-capture"),
            AccessRequest::Check,
        )
        .unwrap_err();
        assert!(error.0.contains("could not run"), "{error}");
    }
}
