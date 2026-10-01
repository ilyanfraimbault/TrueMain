//! `truemain-capture-spike` — records one League game the way the app will,
//! and reports what it measured (spike #1745).
//!
//! Runs outside the app on purpose: the spike has to settle the capture stack
//! before any of it is wired into the shell. It drives the platform helper
//! (`desktop/capture/macos` on a Mac) through `game-recording`'s session — the
//! same state machine, anchor, highlights and storage the app will use — so
//! what it measures is what the app would do.
//!
//! Start it, then start a game. It waits for the game, records it, follows the
//! game clock and live events, and once the game is over waits for the match
//! history to resolve the highlights. It leaves `video.mp4`,
//! `recording.json`, `report.md` and `player.html` in one folder per game.

mod helper;
mod report;

use std::path::{Path, PathBuf};
use std::process::Command;
use std::time::{Duration, SystemTime, UNIX_EPOCH};

use game_recording::anchor::ClockSample;
use game_recording::{
    Change, FrameRate, GameInfo, GameOutcome, Quality, Queues, RecordingDir, RecordingSettings,
    Resolution, Session,
};
use helper::HelperCapture;
use lcu::live::LiveClient;
use lcu::{GameflowPhase, LcuClient};

const USAGE: &str =
    "usage: truemain-capture-spike [--resolution native|1440p|1080p|720p] [--fps 30|60]
                              [--source window|display] [--no-audio] [--window-id N]
                              [--out DIR] [--helper PATH]";

const CLOCK_EVERY: u32 = 5;
const EVENTS_EVERY: u32 = 2;
/// Without the League client to say the game ended, a game API this long
/// silent means the game is gone.
const GAME_GONE_AFTER: u32 = 15;
const HISTORY_WAIT: Duration = Duration::from_secs(300);
/// The capture can fail to start on the loading screen; it is tried again
/// while the game runs rather than giving up on the game.
const START_ATTEMPTS: u32 = 6;

struct Options {
    quality: Quality,
    audio: bool,
    window_id: Option<u32>,
    source: String,
    out: PathBuf,
    helper: PathBuf,
}

fn parse_options() -> Result<Options, String> {
    let args: Vec<String> = std::env::args().skip(1).collect();
    let value = |name: &str| {
        args.iter()
            .position(|a| a == name)
            .and_then(|i| args.get(i + 1))
            .cloned()
    };
    if args.iter().any(|a| a == "--help" || a == "-h") {
        return Err(USAGE.into());
    }
    let resolution = match value("--resolution").as_deref().unwrap_or("1080p") {
        "native" => Resolution::Native,
        "1440p" => Resolution::P1440,
        "1080p" => Resolution::P1080,
        "720p" => Resolution::P720,
        other => return Err(format!("unknown resolution {other}\n{USAGE}")),
    };
    let frame_rate = match value("--fps").as_deref().unwrap_or("30") {
        "30" => FrameRate::Fps30,
        "60" => FrameRate::Fps60,
        other => return Err(format!("{other} fps is not offered\n{USAGE}")),
    };
    let window_id = value("--window-id")
        .map(|v| v.parse().map_err(|_| format!("bad window id {v}")))
        .transpose()?;
    let source = value("--source").unwrap_or_else(|| "window".into());
    if source != "window" && source != "display" {
        return Err(format!("unknown source {source}\n{USAGE}"));
    }
    let out = PathBuf::from(value("--out").unwrap_or_else(|| "capture-spike".into()));
    let helper = value("--helper").map(PathBuf::from).unwrap_or_else(|| {
        std::env::current_exe()
            .ok()
            .and_then(|exe| exe.parent().map(|dir| dir.join("truemain-capture")))
            .unwrap_or_else(|| PathBuf::from("truemain-capture"))
    });
    Ok(Options {
        quality: Quality {
            resolution,
            frame_rate,
        },
        audio: !args.iter().any(|a| a == "--no-audio"),
        window_id,
        source,
        out,
        helper,
    })
}

fn now_ms() -> i64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_millis() as i64)
        .unwrap_or_default()
}

fn command_output(program: &str, args: &[&str]) -> Option<String> {
    let output = Command::new(program).args(args).output().ok()?;
    let text = String::from_utf8_lossy(&output.stdout).trim().to_string();
    (!text.is_empty()).then_some(text)
}

fn system_description() -> Vec<(String, String)> {
    [
        ("OS", command_output("sw_vers", &["-productVersion"])),
        ("Model", command_output("sysctl", &["-n", "hw.model"])),
        (
            "CPU",
            command_output("sysctl", &["-n", "machdep.cpu.brand_string"]),
        ),
        (
            "Memory (bytes)",
            command_output("sysctl", &["-n", "hw.memsize"]),
        ),
    ]
    .into_iter()
    .filter_map(|(name, value)| value.map(|v| (name.to_string(), v)))
    .collect()
}

#[tokio::main(flavor = "current_thread")]
async fn main() {
    let options = match parse_options() {
        Ok(options) => options,
        Err(message) => {
            eprintln!("{message}");
            std::process::exit(2);
        }
    };
    if let Err(message) = run(options).await {
        eprintln!("error: {message}");
        std::process::exit(1);
    }
}

async fn run(options: Options) -> Result<(), String> {
    std::fs::create_dir_all(&options.out).map_err(|e| e.to_string())?;
    let out = options
        .out
        .canonicalize()
        .map_err(|e| format!("{}: {e}", options.out.display()))?;
    let settings = RecordingSettings {
        enabled: true,
        queues: Queues::All,
        quality: options.quality,
        budget_bytes: u64::MAX,
        folder: Some(out.clone()),
    };

    let mut session = Session::new(HelperCapture::new(
        options.helper.clone(),
        options.window_id,
        options.source.clone(),
        options.audio,
    ));
    // Fail now on a missing helper or a missing permission, not once a game
    // has started.
    match session.capture().probe() {
        Ok(window) => eprintln!(
            "helper ok — a League window is already open: {}",
            window["title"]
        ),
        Err(error) if error.0.contains("no-window") => {
            eprintln!("helper ok, screen recording allowed")
        }
        Err(error) => return Err(error.to_string()),
    }

    let live = LiveClient::new().map_err(|e| e.to_string())?;
    eprintln!("waiting for a game — start one now (Ctrl+C to quit)");
    loop {
        tokio::select! {
            _ = tokio::signal::ctrl_c() => return Ok(()),
            _ = tokio::time::sleep(Duration::from_secs(2)) => {}
        }
        if live.game_stats().await.is_ok() {
            break;
        }
    }

    let lcu = LcuClient::connect().await.ok();
    let gameflow = match &lcu {
        Some(client) => client.gameflow_session().await.ok().flatten(),
        None => None,
    };
    let game = GameInfo {
        game_id: gameflow
            .as_ref()
            .map(|s| s.game_data.game_id)
            .filter(|id| *id > 0)
            .unwrap_or_else(|| now_ms() / 1000),
        queue_id: gameflow
            .as_ref()
            .map(|s| s.game_data.queue.id)
            .unwrap_or_default(),
    };
    if lcu.is_none() {
        eprintln!(
            "the League client did not answer: the highlights will come from the live feed only"
        );
    }

    let mut attempt = 0;
    let dir = loop {
        attempt += 1;
        match session.on_phase(
            GameflowPhase::InProgress,
            Some(game),
            &settings,
            &out,
            now_ms(),
        ) {
            Ok(Change::Started(dir)) => break dir,
            Ok(change) => return Err(format!("the recording did not start: {change:?}")),
            Err(error) => {
                eprintln!(
                    "the capture did not start (attempt {attempt}/{START_ATTEMPTS}): {error}"
                );
                if attempt >= START_ATTEMPTS || live.game_stats().await.is_err() {
                    return Err("giving up: the capture never started".into());
                }
                tokio::time::sleep(Duration::from_secs(5)).await;
            }
        }
    };
    if let Some(window) = &session.capture().window {
        eprintln!(
            "capturing window {} \"{}\" of {}, {}×{} px, from the {}",
            window["windowId"],
            window["title"].as_str().unwrap_or(""),
            window["app"].as_str().unwrap_or(""),
            window["width"],
            window["height"],
            window["source"].as_str().unwrap_or("window")
        );
    }
    eprintln!("recording game {} into {}", game.game_id, dir.display());
    if let Ok(player) = live.active_player().await {
        session.note_player(player);
    }

    let mut samples: Vec<ClockSample> = Vec::new();
    let mut tick: u32 = 0;
    let mut silent: u32 = 0;
    loop {
        tokio::select! {
            _ = tokio::signal::ctrl_c() => {
                eprintln!("stopping on Ctrl+C");
                break;
            }
            _ = tokio::time::sleep(Duration::from_secs(1)) => {}
        }
        tick += 1;
        if let Some(failure) = session.capture().failure.lock().unwrap().clone() {
            eprintln!("the helper failed: {failure}");
            break;
        }

        if tick.is_multiple_of(CLOCK_EVERY) {
            let before = session.video_clock();
            let stats = live.game_stats().await;
            let after = session.video_clock();
            match (before, stats, after) {
                (Some(before), Ok(stats), Some(after)) => {
                    silent = 0;
                    let sample = ClockSample {
                        video_before_ms: before,
                        video_after_ms: after,
                        game_time_s: stats.game_time,
                    };
                    samples.push(sample);
                    session.note_clock(sample);
                }
                (_, Err(_), _) => silent += CLOCK_EVERY,
                _ => {}
            }
        }
        if tick.is_multiple_of(EVENTS_EVERY) {
            if let Ok(events) = live.events().await {
                session.note_events(events);
            }
        }

        let phase = match &lcu {
            Some(client) => client.gameflow_phase().await.ok(),
            None => None,
        };
        let over = match phase {
            Some(GameflowPhase::InProgress | GameflowPhase::Reconnect) => false,
            Some(_) => true,
            None => silent >= GAME_GONE_AFTER,
        };
        if over {
            eprintln!("the game is over");
            break;
        }
    }

    // A stop that went wrong still leaves what was written; report on it.
    if let Err(error) = session.on_phase(GameflowPhase::EndOfGame, None, &settings, &out, now_ms())
    {
        eprintln!("stopping went wrong: {error}");
    }
    let live_highlights = RecordingDir::new(dir.clone())
        .read_meta()
        .map(|meta| meta.highlights)
        .unwrap_or_default();

    let (history_game, timeline) = match &lcu {
        Some(client) => wait_for_history(client, game.game_id).await,
        None => (None, None),
    };
    let finalised = session
        .finalise(
            GameOutcome {
                game: history_game.as_ref(),
                timeline: timeline.as_ref(),
            },
            u64::MAX,
            &out,
        )
        .map_err(|e| e.to_string())?
        .ok_or("nothing to finalise")?;

    let capture = session.capture();
    let progress = capture.progress.lock().unwrap().clone();
    let video_bytes = std::fs::metadata(finalised.dir.video_path())
        .map(|m| m.len())
        .unwrap_or(0);
    let system = system_description();
    let report = report::render_report(&report::Run {
        system: &system,
        audio: options.audio,
        window: capture.window.as_ref(),
        output: capture.output,
        stopped: capture.stopped,
        progress: &progress,
        video_bytes,
        samples: &samples,
        meta: &finalised.meta,
        live_highlights: &live_highlights,
    });
    write(&dir.join("report.md"), &report)?;
    write(
        &dir.join("player.html"),
        &report::render_player(&finalised.meta),
    )?;

    println!("{report}");
    eprintln!(
        "\nDone. Open {} in Safari, then fill in the end of {} and send it back.",
        dir.join("player.html").display(),
        dir.join("report.md").display()
    );
    Ok(())
}

/// The game's line in the player's history and its timeline, once the
/// history has them — a few seconds to a few minutes after the game.
async fn wait_for_history(
    client: &LcuClient,
    game_id: i64,
) -> (
    Option<lcu::record::HistoryGame>,
    Option<lcu::detail::GameTimeline>,
) {
    eprintln!("waiting for the match history to list the game (up to {HISTORY_WAIT:?})");
    let deadline = tokio::time::Instant::now() + HISTORY_WAIT;
    while tokio::time::Instant::now() < deadline {
        if let Ok(history) = client.match_history(0, 5).await {
            if let Some(game) = history
                .games
                .games
                .into_iter()
                .find(|g| g.game_id == game_id)
            {
                let timeline = client.game_timeline(game_id).await.ok();
                if timeline.is_none() {
                    eprintln!("the history has the game but no timeline: live highlights kept");
                }
                return (Some(game), timeline);
            }
        }
        tokio::time::sleep(Duration::from_secs(10)).await;
    }
    eprintln!("the history never listed the game: live highlights kept");
    (None, None)
}

fn write(path: &Path, body: &str) -> Result<(), String> {
    std::fs::write(path, body).map_err(|e| format!("{}: {e}", path.display()))
}
