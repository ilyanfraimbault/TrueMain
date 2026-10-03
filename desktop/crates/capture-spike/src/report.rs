//! What the spike writes beside the video: a report of what it measured, and a
//! bare player that jumps to the highlights — opened in Safari, it is the same
//! WebKit the app's window renders with, so it also answers whether the file
//! plays and seeks there.
//!
//! Every figure in the report is computed from the run; what only the player
//! can observe (the game's own frame rate, whether a marker lands on its
//! moment) is left as a question to fill in by hand.

use std::fmt::Write as _;

use game_recording::anchor::ClockSample;
use game_recording::{Anchor, Highlight, HighlightKind, OutputSpec, RecordingMeta};
use serde_json::{json, Value};

use capture_helper::{Progress, Stopped};

/// Seconds the player starts before a moment, so the action is seen building.
const LEAD_IN_MS: i64 = 5_000;

/// A live highlight further than this from the timeline's is not the same one.
const MATCH_WINDOW_MS: i64 = 10_000;

/// The same bound the anchor fit applies.
const MAX_ROUND_TRIP_MS: u64 = 250;

pub struct Run<'a> {
    pub system: &'a [(String, String)],
    pub audio: bool,
    pub codec: &'a str,
    pub window: Option<&'a Value>,
    pub output: Option<OutputSpec>,
    pub stopped: Option<Stopped>,
    pub progress: &'a [Progress],
    pub video_bytes: u64,
    pub samples: &'a [ClockSample],
    pub meta: &'a RecordingMeta,
    /// The highlights read from the live feed, kept even when the timeline
    /// replaced them, to measure the fallback against it.
    pub live_highlights: &'a [Highlight],
}

/// How far each usable clock read sits from the fitted anchor, in ms.
pub fn anchor_residuals(anchor: &Anchor, samples: &[ClockSample]) -> Vec<i64> {
    samples
        .iter()
        .filter(|s| s.game_time_s > 0.0)
        .filter(|s| {
            s.video_after_ms >= s.video_before_ms
                && s.video_after_ms - s.video_before_ms <= MAX_ROUND_TRIP_MS
        })
        .map(|s| {
            let game_ms = (s.game_time_s * 1000.0).round() as i64;
            let video_ms = ((s.video_before_ms + s.video_after_ms) / 2) as i64;
            (video_ms - anchor.video_ms(game_ms)).abs()
        })
        .collect()
}

/// For each timeline highlight, how far the nearest live highlight of the same
/// kind is, in ms; unmatched ones are left out.
pub fn live_offsets(timeline: &[Highlight], live: &[Highlight]) -> Vec<i64> {
    timeline
        .iter()
        .filter_map(|t| {
            live.iter()
                .filter(|l| l.kind == t.kind)
                .map(|l| (l.game_time_ms - t.game_time_ms).abs())
                .filter(|gap| *gap <= MATCH_WINDOW_MS)
                .min()
        })
        .collect()
}

/// Median and maximum.
fn spread(values: &[i64]) -> Option<(i64, i64)> {
    let mut sorted = values.to_vec();
    sorted.sort_unstable();
    Some((*sorted.get(sorted.len() / 2)?, *sorted.last()?))
}

fn count(highlights: &[Highlight], kind: HighlightKind) -> usize {
    highlights.iter().filter(|h| h.kind == kind).count()
}

pub fn render_report(run: &Run<'_>) -> String {
    let meta = run.meta;
    let mut out = String::new();
    let _ = writeln!(out, "# Capture spike — game {}\n", meta.game_id);

    let _ = writeln!(out, "## Machine\n");
    for (name, value) in run.system {
        let _ = writeln!(out, "- {name}: {value}");
    }

    let _ = writeln!(out, "\n## Settings\n");
    let _ = writeln!(
        out,
        "- Asked: {} at {} fps, audio {}",
        serde_json::to_value(meta.quality.resolution)
            .ok()
            .and_then(|v| v.as_str().map(str::to_string))
            .unwrap_or_default(),
        meta.quality.frame_rate.per_second(),
        if run.audio { "on" } else { "off" }
    );
    if let Some(window) = run.window {
        let _ = writeln!(
            out,
            "- Window: {} \"{}\" of {} ({}), {}×{} pt, {}×{} px captured from the {}, on screen: {}",
            window["windowId"],
            window["title"].as_str().unwrap_or(""),
            window["app"].as_str().unwrap_or(""),
            window["bundleId"].as_str().unwrap_or(""),
            window["widthPoints"],
            window["heightPoints"],
            window["width"],
            window["height"],
            window["source"].as_str().unwrap_or("window"),
            window["onScreen"]
        );
    }
    if let Some(output) = run.output {
        let _ = writeln!(
            out,
            "- Encoded: {} {}×{} at {} fps, {:.1} Mbit/s, a keyframe every {} frames",
            run.codec,
            output.width,
            output.height,
            output.frame_rate,
            output.bitrate_bps as f64 / 1e6,
            output.keyframe_interval_frames
        );
    }

    let _ = writeln!(out, "\n## Measured\n");
    match run.stopped {
        Some(stopped) if stopped.duration_ms > 0 => {
            let seconds = stopped.duration_ms as f64 / 1000.0;
            let _ = writeln!(
                out,
                "- Video: {:.0} s, {} frames ({:.1} fps delivered), {} dropped",
                seconds,
                stopped.frames,
                stopped.frames as f64 / seconds,
                stopped.dropped
            );
            let _ = writeln!(
                out,
                "- File: {:.1} MB, {:.1} MB per minute",
                run.video_bytes as f64 / 1e6,
                run.video_bytes as f64 / 1e6 / (seconds / 60.0)
            );
        }
        _ => {
            let _ = writeln!(out, "- Video: none written");
        }
    }
    if let Some(last) = run.progress.last() {
        let _ = writeln!(out, "- Frames sent by ScreenCaptureKit: {}", last.statuses);
    }
    let cpu: Vec<f64> = run.progress.iter().map(|p| p.cpu_percent).collect();
    if !cpu.is_empty() {
        let mean = cpu.iter().sum::<f64>() / cpu.len() as f64;
        let max = cpu.iter().cloned().fold(f64::MIN, f64::max);
        let _ = writeln!(
            out,
            "- Helper CPU (one core = 100%, encoder service not included): mean {mean:.1}%, max {max:.1}%"
        );
    }
    match &meta.anchor {
        Some(anchor) => {
            let residuals = anchor_residuals(anchor, run.samples);
            let _ = writeln!(
                out,
                "- Clock anchor: {} segment(s) from {} reads ({} usable)",
                anchor.segments.len(),
                run.samples.len(),
                residuals.len()
            );
            if let Some((median, max)) = spread(&residuals) {
                let _ = writeln!(
                    out,
                    "- Clock reads vs the anchor: median {median} ms, max {max} ms"
                );
            }
        }
        None => {
            let _ = writeln!(
                out,
                "- Clock anchor: none ({} reads, none usable)",
                run.samples.len()
            );
        }
    }
    let source = meta
        .highlights_source
        .and_then(|s| serde_json::to_value(s).ok())
        .and_then(|v| v.as_str().map(str::to_string))
        .unwrap_or_else(|| "none".into());
    let _ = writeln!(
        out,
        "- Highlights ({source}): {} kills, {} deaths, {} assists",
        count(&meta.highlights, HighlightKind::Kill),
        count(&meta.highlights, HighlightKind::Death),
        count(&meta.highlights, HighlightKind::Assist)
    );
    if meta.highlights_source == Some(game_recording::HighlightSource::Timeline) {
        let offsets = live_offsets(&meta.highlights, run.live_highlights);
        match spread(&offsets) {
            Some((median, max)) => {
                let _ = writeln!(
                    out,
                    "- Live feed vs timeline: {} of {} matched, median {median} ms apart, max {max} ms",
                    offsets.len(),
                    meta.highlights.len()
                );
            }
            None => {
                let _ = writeln!(out, "- Live feed vs timeline: nothing to compare");
            }
        }
    }

    let _ = writeln!(
        out,
        "\n## To fill in by hand\n\n\
         - League display mode (Windowed / Borderless / Full Screen):\n\
         - Does the video show the game the whole way through (no black, no frozen frame)?\n\
         - In-game FPS (Ctrl+F) on the same scene, without recording / while recording:\n\
         - In `player.html` (Safari): does the video play and seek? Does each marker land a few seconds before its moment?\n\
         - Sound: game audio present? Anything else in it (Discord, music)?\n\
         - Screen Recording permission: asked when? Needed a restart of the terminal?\n\
         - Anything else (alt-tab, cmd-tab, a Space switch during the game):"
    );
    out
}

pub fn render_player(meta: &RecordingMeta) -> String {
    let moments: Vec<Value> = meta
        .anchor
        .as_ref()
        .map(|anchor| {
            meta.highlights
                .iter()
                .map(|h| {
                    json!({
                        "kind": h.kind,
                        "kills": h.kills,
                        "gameMs": h.game_time_ms,
                        "videoMs": anchor.video_ms(h.game_time_ms),
                    })
                })
                .collect()
        })
        .unwrap_or_default();
    let data = serde_json::to_string(&moments)
        .unwrap_or_else(|_| "[]".into())
        .replace("</", "<\\/");

    PLAYER_TEMPLATE
        .replace("{{GAME_ID}}", &meta.game_id.to_string())
        .replace("{{LEAD_IN_MS}}", &LEAD_IN_MS.to_string())
        .replace("{{MOMENTS}}", &data)
}

const PLAYER_TEMPLATE: &str = r#"<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Game {{GAME_ID}}</title>
<style>
  :root { color-scheme: dark; --bg: #101012; --surface: #1b1b1f; --line: #2c2c33; --text: #ececf1; --muted: #9a9aa6;
          --kill: #4fc38a; --death: #ef5f5f; --assist: #6aa1f2; }
  * { box-sizing: border-box; }
  body { margin: 0; background: var(--bg); color: var(--text); font: 14px/1.4 -apple-system, system-ui, sans-serif; }
  main { max-width: 1100px; margin: 24px auto; padding: 0 16px; }
  h1 { font-size: 16px; font-weight: 600; margin: 0 0 12px; }
  video { width: 100%; display: block; background: #000; border-radius: 8px; }
  .bar { position: relative; height: 16px; margin: 12px 0; background: var(--surface); border: 1px solid var(--line); border-radius: 8px; }
  .mark { position: absolute; top: 2px; width: 4px; height: 10px; border-radius: 2px; transform: translateX(-2px); cursor: pointer; }
  .kill { background: var(--kill); } .death { background: var(--death); } .assist { background: var(--assist); }
  .controls { display: flex; gap: 8px; margin-bottom: 12px; }
  button { font: inherit; color: var(--text); background: var(--surface); border: 1px solid var(--line); border-radius: 6px; padding: 6px 10px; cursor: pointer; }
  button:hover { border-color: var(--muted); }
  ol { list-style: none; margin: 0; padding: 0; display: flex; flex-wrap: wrap; gap: 6px; }
  ol button { display: flex; align-items: center; gap: 6px; }
  .dot { width: 8px; height: 8px; border-radius: 50%; }
  .empty { color: var(--muted); }
</style>
</head>
<body>
<main>
  <h1>Game {{GAME_ID}}</h1>
  <video id="video" src="video.mp4" controls preload="metadata"></video>
  <div class="bar" id="bar"></div>
  <div class="controls">
    <button id="previous">← Previous moment</button>
    <button id="next">Next moment →</button>
  </div>
  <ol id="moments"></ol>
</main>
<script>
  const LEAD_IN_MS = {{LEAD_IN_MS}};
  const moments = {{MOMENTS}};
  const video = document.getElementById("video");
  const bar = document.getElementById("bar");
  const list = document.getElementById("moments");
  const clock = (ms) => { const s = Math.floor(ms / 1000); return Math.floor(s / 60) + ":" + String(s % 60).padStart(2, "0"); };
  const label = (m) => m.kind === "kill" ? (["", "Kill", "Double kill", "Triple kill", "Quadra kill", "Penta kill"][m.kills] || "Kill") : m.kind === "death" ? "Death" : "Assist";
  const jump = (m) => { video.currentTime = Math.max(0, m.videoMs - LEAD_IN_MS) / 1000; video.play(); };

  if (moments.length === 0) {
    list.innerHTML = '<li class="empty">No highlight to show.</li>';
  }
  for (const m of moments) {
    const item = document.createElement("li");
    const button = document.createElement("button");
    button.innerHTML = '<span class="dot ' + m.kind + '"></span>' + clock(m.gameMs) + " · " + label(m);
    button.addEventListener("click", () => jump(m));
    item.appendChild(button);
    list.appendChild(item);
  }
  video.addEventListener("loadedmetadata", () => {
    for (const m of moments) {
      const mark = document.createElement("div");
      mark.className = "mark " + m.kind;
      mark.style.left = (m.videoMs / 1000 / video.duration * 100) + "%";
      mark.title = clock(m.gameMs) + " · " + label(m);
      mark.addEventListener("click", () => jump(m));
      bar.appendChild(mark);
    }
  });
  const now = () => video.currentTime * 1000 + LEAD_IN_MS;
  document.getElementById("next").addEventListener("click", () => {
    const m = moments.find((m) => m.videoMs > now() + 500);
    if (m) jump(m);
  });
  document.getElementById("previous").addEventListener("click", () => {
    const m = [...moments].reverse().find((m) => m.videoMs < now() - 1500);
    if (m) jump(m);
  });
</script>
</body>
</html>
"#;

#[cfg(test)]
mod tests {
    use super::*;
    use game_recording::anchor::Segment;
    use game_recording::{FrameRate, HighlightSource, Quality, Resolution};

    fn highlight(kind: HighlightKind, game_time_ms: i64) -> Highlight {
        Highlight {
            kind,
            game_time_ms,
            end_game_time_ms: game_time_ms,
            kills: u8::from(kind == HighlightKind::Kill),
            killer_id: None,
            victim_ids: Vec::new(),
        }
    }

    fn meta() -> RecordingMeta {
        let mut meta = RecordingMeta::new(
            42,
            420,
            Quality {
                resolution: Resolution::P1080,
                frame_rate: FrameRate::Fps60,
            },
            0,
        );
        meta.anchor = Some(Anchor {
            segments: vec![Segment {
                from_game_ms: 0,
                offset_ms: 20_000,
            }],
        });
        meta.highlights = vec![
            highlight(HighlightKind::Kill, 95_000),
            highlight(HighlightKind::Death, 400_000),
        ];
        meta.highlights_source = Some(HighlightSource::Timeline);
        meta
    }

    #[test]
    fn residuals_measure_reads_against_the_anchor() {
        let anchor = meta().anchor.unwrap();
        let samples = [
            ClockSample {
                video_before_ms: 50_000,
                video_after_ms: 50_010,
                game_time_s: 30.0,
            },
            // Slow: left out, as the fit leaves it out.
            ClockSample {
                video_before_ms: 60_000,
                video_after_ms: 61_000,
                game_time_s: 40.0,
            },
        ];
        assert_eq!(anchor_residuals(&anchor, &samples), vec![5]);
    }

    #[test]
    fn the_live_feed_is_matched_by_kind_and_nearness() {
        let timeline = [
            highlight(HighlightKind::Kill, 95_000),
            highlight(HighlightKind::Death, 400_000),
        ];
        let live = [
            highlight(HighlightKind::Kill, 95_400),
            // Same time, wrong kind: no match for the death.
            highlight(HighlightKind::Assist, 400_000),
        ];
        assert_eq!(live_offsets(&timeline, &live), vec![400]);
    }

    #[test]
    fn the_report_states_what_was_measured() {
        let meta = meta();
        let progress = [Progress {
            elapsed_ms: 5_000,
            frames: 300,
            dropped: 0,
            cpu_percent: 12.5,
            statuses: "complete 300".into(),
        }];
        let report = render_report(&Run {
            system: &[("macOS".into(), "15.6".into())],
            audio: true,
            codec: "HEVC",
            window: None,
            output: Some(OutputSpec {
                width: 2560,
                height: 1440,
                frame_rate: 60,
                bitrate_bps: 12_000_000,
                keyframe_interval_frames: 60,
            }),
            stopped: Some(Stopped {
                duration_ms: 120_000,
                frames: 7_200,
                dropped: 3,
            }),
            progress: &progress,
            video_bytes: 90_000_000,
            samples: &[],
            meta: &meta,
            live_highlights: &[highlight(HighlightKind::Kill, 95_300)],
        });

        assert!(report.contains("- macOS: 15.6"), "{report}");
        assert!(
            report.contains("- Encoded: HEVC 2560×1440 at 60 fps, 12.0 Mbit/s"),
            "{report}"
        );
        assert!(
            report.contains("7200 frames (60.0 fps delivered), 3 dropped"),
            "{report}"
        );
        assert!(report.contains("45.0 MB per minute"), "{report}");
        assert!(report.contains("mean 12.5%"), "{report}");
        assert!(report.contains("1 of 2 matched, median 300 ms"), "{report}");
        assert!(report.contains("## To fill in by hand"), "{report}");
    }

    #[test]
    fn the_player_places_moments_on_the_video() {
        let player = render_player(&meta());
        assert!(player.contains(r#""videoMs":115000"#), "{player}");
        assert!(player.contains(r#""kind":"death""#), "{player}");
        assert!(player.contains("const LEAD_IN_MS = 5000;"));
        assert!(!player.contains("{{"));
    }

    #[test]
    fn no_anchor_no_markers() {
        let mut meta = meta();
        meta.anchor = None;
        assert!(render_player(&meta).contains("const moments = [];"));
    }
}
