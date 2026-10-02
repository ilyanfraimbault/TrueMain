//! `record`: the game's window to a file until told to stop.
//!
//! Windows.Graphics.Capture hands each frame over as a D3D11 surface on a
//! free-threaded frame pool; frames come at the display's pace and are paced
//! down to the asked rate, converted to NV12 on the GPU (`convert`) and
//! written by the hardware encoder (`writer`). Video time zero is the first
//! frame written; the `started` event is emitted the moment it is, which is
//! what the shell's video clock counts from. The game's sound (`audio`) is
//! placed on the same clock (`timeline`).

use std::io::BufRead;
use std::sync::mpsc;
use std::sync::{Arc, Mutex, MutexGuard};
use std::time::{Duration, Instant};

use serde_json::json;
use windows::core::{IInspectable, Interface, Result};
use windows::Foundation::TypedEventHandler;
use windows::Graphics::Capture::{
    Direct3D11CaptureFramePool, GraphicsCaptureItem, GraphicsCaptureSession,
};
use windows::Graphics::DirectX::DirectXPixelFormat;
use windows::Graphics::SizeInt32;
use windows::Win32::Foundation::FILETIME;
use windows::Win32::Graphics::Direct3D11::ID3D11Texture2D;
use windows::Win32::System::Performance::{QueryPerformanceCounter, QueryPerformanceFrequency};
use windows::Win32::System::Threading::{GetCurrentProcess, GetProcessTimes};
use windows::Win32::System::WinRT::Direct3D11::IDirect3DDxgiInterfaceAccess;

use crate::args::{self, Codec, Source};
use crate::audio::Loopback;
use crate::convert::{Gpu, Pipeline};
use crate::output::{emit, fail, log};
use crate::timeline::{AudioTimeline, SECOND};
use crate::window;
use crate::writer::{self, VideoOptions, Writer, AUDIO_BLOCK, AUDIO_RATE};

const PROGRESS_EVERY: Duration = Duration::from_secs(5);
const FORMAT: DirectXPixelFormat = DirectXPixelFormat::B8G8R8A8UIntNormalized;

#[derive(Debug)]
enum Stop {
    Asked,
    WindowClosed,
    Failed,
}

struct State {
    gpu: Gpu,
    pipeline: Pipeline,
    writer: Option<Writer>,
    pool_size: SizeInt32,
    interval: i64,
    /// Capture time of video time zero, in 100 ns on the QPC clock.
    first: Option<i64>,
    next_due: i64,
    last: i64,
    frames: u64,
    dropped: u64,
    delivered: u64,
    paced: u64,
    timeline: AudioTimeline,
    stopping: bool,
    stop: mpsc::Sender<Stop>,
    options: VideoOptions,
    hardware: bool,
}

// SAFETY: the D3D11 and Media Foundation objects inside are only touched
// under the mutex, by one thread at a time; the device is
// multithread-protected.
unsafe impl Send for State {}

fn lock(state: &Mutex<State>) -> MutexGuard<'_, State> {
    state
        .lock()
        .unwrap_or_else(|poisoned| poisoned.into_inner())
}

/// Now, in 100 ns on the clock frames are stamped with.
fn now() -> i64 {
    let (mut counter, mut frequency) = (0i64, 0i64);
    // SAFETY: out-pointers to locals.
    unsafe {
        let _ = QueryPerformanceCounter(&mut counter);
        let _ = QueryPerformanceFrequency(&mut frequency);
    }
    (i128::from(counter) * i128::from(SECOND) / i128::from(frequency.max(1))) as i64
}

pub fn record(arguments: &[String]) {
    let out = args::required("--out", arguments).to_string();
    let options = VideoOptions {
        width: args::integer("--width", arguments) & !1,
        height: args::integer("--height", arguments) & !1,
        fps: args::integer("--fps", arguments),
        codec: Codec::parse(arguments),
        bitrate: args::integer("--bitrate", arguments),
        keyframe_interval: args::integer("--keyframe-interval", arguments),
    };
    let with_audio = !args::has("--no-audio", arguments);
    let target = window::find_or_fail(arguments);
    let item = window::capture_item(&target, Source::parse(arguments)).unwrap_or_else(|error| {
        fail(
            "capture",
            &format!("this window cannot be captured: {error}"),
            5,
        )
    });

    let _ = std::fs::remove_file(&out);
    let (stop, stopped) = mpsc::channel();
    let (state, pool, session) = match start(&item, &out, options, with_audio, stop.clone()) {
        Ok(started) => started,
        Err(error) => fail("capture", &format!("could not start ({error})"), 5),
    };

    let loopback = with_audio
        .then(|| {
            let state = state.clone();
            Loopback::start(target.pid, move |pcm| write_audio(&state, pcm))
        })
        .flatten();

    // `stop`, or the shell closing stdin, ends the recording.
    let asked = stop.clone();
    std::thread::spawn(move || {
        for line in std::io::stdin().lock().lines() {
            match line {
                Ok(line) if line.trim() == "stop" => break,
                Ok(_) => continue,
                Err(_) => break,
            }
        }
        let _ = asked.send(Stop::Asked);
    });
    let closed = stop.clone();
    let _ = item.Closed(
        &TypedEventHandler::<GraphicsCaptureItem, IInspectable>::new(move |_, _| {
            let _ = closed.send(Stop::WindowClosed);
            Ok(())
        }),
    );

    let progress = state.clone();
    std::thread::spawn(move || {
        let meter = CpuMeter::new();
        loop {
            std::thread::sleep(PROGRESS_EVERY);
            let state = lock(&progress);
            if state.stopping {
                return;
            }
            let (elapsed_ms, cpu_percent) = meter.sample();
            emit(
                "progress",
                json!({
                    "elapsedMs": elapsed_ms,
                    "frames": state.frames,
                    "dropped": state.dropped,
                    "cpuPercent": cpu_percent,
                    "statuses": { "complete": state.frames, "paced": state.paced, "dropped": state.dropped },
                }),
            );
        }
    });

    let reason = stopped.recv().unwrap_or(Stop::Asked);
    if !matches!(reason, Stop::Asked) {
        log(&format!("stopping on its own: {reason:?}"));
    }
    lock(&state).stopping = true;
    let _ = session.Close();
    let _ = pool.Close();
    if let Some(loopback) = loopback {
        loopback.stop();
    }
    close(&state, &out);
}

fn start(
    item: &GraphicsCaptureItem,
    out: &str,
    options: VideoOptions,
    with_audio: bool,
    stop: mpsc::Sender<Stop>,
) -> std::result::Result<
    (
        Arc<Mutex<State>>,
        Direct3D11CaptureFramePool,
        GraphicsCaptureSession,
    ),
    String,
> {
    let step = |what: &'static str| move |error: windows::core::Error| format!("{what}: {error}");
    let gpu = Gpu::new().map_err(step("the D3D11 device"))?;
    let size = item.Size().map_err(step("the window's size"))?;
    let pipeline = Pipeline::new(
        &gpu,
        (size.Width.max(2) as u32, size.Height.max(2) as u32),
        (options.width, options.height),
        options.fps,
    );
    let hardware = writer::hardware_encoder(options.codec);
    if !hardware {
        log("no hardware encoder for this codec: Media Foundation encodes in software");
    }
    let writer =
        Writer::new(out, &options, &gpu.manager, with_audio).map_err(step("the video writer"))?;
    let pool = Direct3D11CaptureFramePool::CreateFreeThreaded(&gpu.winrt, FORMAT, 2, size)
        .map_err(step("the capture's frame pool"))?;
    let session = pool
        .CreateCaptureSession(item)
        .map_err(step("the capture session"))?;
    let _ = session.SetIsCursorCaptureEnabled(true);
    // Windows 11 only: without it a yellow border marks the captured window.
    let _ = session.SetIsBorderRequired(false);

    let state = Arc::new(Mutex::new(State {
        gpu,
        pipeline,
        writer: Some(writer),
        pool_size: size,
        interval: SECOND / i64::from(options.fps),
        first: None,
        next_due: 0,
        last: 0,
        frames: 0,
        dropped: 0,
        delivered: 0,
        paced: 0,
        timeline: AudioTimeline::new(AUDIO_RATE),
        stopping: false,
        stop,
        options,
        hardware,
    }));
    let frames = state.clone();
    pool.FrameArrived(
        &TypedEventHandler::<Direct3D11CaptureFramePool, IInspectable>::new(move |pool, _| {
            if let Some(pool) = pool.as_ref() {
                on_frame(&frames, pool);
            }
            Ok(())
        }),
    )
    .map_err(step("the frame handler"))?;
    session.StartCapture().map_err(step("the capture"))?;
    Ok((state, pool, session))
}

fn on_frame(shared: &Mutex<State>, pool: &Direct3D11CaptureFramePool) {
    let Ok(frame) = pool.TryGetNextFrame() else {
        return;
    };
    let mut state = lock(shared);
    if state.stopping {
        return;
    }
    state.delivered += 1;
    let (Ok(time), Ok(size)) = (frame.SystemRelativeTime(), frame.ContentSize()) else {
        return;
    };
    if size.Width <= 0 || size.Height <= 0 {
        return;
    }
    if size != state.pool_size {
        // The window was resized: the pool's surfaces follow.
        if pool.Recreate(&state.gpu.winrt, FORMAT, 2, size).is_ok() {
            state.pool_size = size;
        }
    }

    let time = time.Duration;
    let video_time = state.first.map_or(0, |first| time - first);
    if state.first.is_some() && video_time < state.next_due - state.interval / 4 {
        state.paced += 1;
        return;
    }
    let written = write_frame(
        &mut state,
        &frame,
        (size.Width as u32, size.Height as u32),
        video_time,
    );
    if matches!(written, Ok(false)) {
        state.dropped += 1;
        return;
    }
    if let Err(error) = written {
        state.dropped += 1;
        if state.frames > 0 {
            emit(
                "error",
                json!({ "kind": "writer", "message": error.to_string() }),
            );
            let _ = state.stop.send(Stop::Failed);
        } else {
            log(&format!("frame not written: {error}"));
        }
        return;
    }
    if state.first.is_none() {
        state.first = Some(time);
        emit(
            "started",
            json!({
                "width": state.options.width,
                "height": state.options.height,
                "fps": state.options.fps,
                "hardware": state.hardware,
                "converter": state.pipeline.name(),
            }),
        );
    }
    state.frames += 1;
    state.last = video_time;
    state.next_due += state.interval;
    if state.next_due < video_time {
        state.next_due = video_time + state.interval;
    }
}

/// Convert and write one frame. `Ok(false)`: no surface free, dropped.
fn write_frame(
    state: &mut State,
    frame: &windows::Graphics::Capture::Direct3D11CaptureFrame,
    size: (u32, u32),
    video_time: i64,
) -> Result<bool> {
    let surface = frame.Surface()?;
    // SAFETY: the frame's surface is a D3D11 texture, alive with the frame.
    let texture: ID3D11Texture2D = unsafe {
        surface
            .cast::<IDirect3DDxgiInterfaceAccess>()?
            .GetInterface()?
    };
    let State { gpu, pipeline, .. } = state;
    let Some(sample) = pipeline.sample(gpu, &texture, size)? else {
        return Ok(false);
    };
    if let Some(writer) = &state.writer {
        writer.write_video(&sample, video_time, state.interval)?;
    }
    Ok(true)
}

/// One loopback packet, or an empty one when the game was silent.
fn write_audio(shared: &Mutex<State>, pcm: &[u8]) {
    let now = now();
    let mut state = lock(shared);
    let Some(first) = state.first else {
        // Sound from before the first frame would start before video time zero.
        return;
    };
    if state.stopping {
        return;
    }
    let block = AUDIO_BLOCK as usize;
    let placed = state
        .timeline
        .place((pcm.len() / block) as u64, now - first);
    let skip = (placed.skip as usize * block).min(pcm.len());
    let mut data = vec![0u8; placed.pad as usize * block];
    data.extend_from_slice(&pcm[skip..]);
    if let Some(writer) = &state.writer {
        if let Err(error) = writer.write_audio(&data, placed.time) {
            log(&format!("sound not written: {error}"));
        }
    }
}

fn close(shared: &Mutex<State>, out: &str) {
    let mut state = lock(shared);
    let writer = state.writer.take();
    let (frames, dropped) = (state.frames, state.dropped);
    if frames == 0 {
        drop(writer);
        let _ = std::fs::remove_file(out);
        emit(
            "stopped",
            json!({ "durationMs": 0, "frames": 0, "dropped": dropped }),
        );
        return;
    }
    if let Some(writer) = writer {
        if let Err(error) = writer.finish() {
            emit(
                "error",
                json!({ "kind": "writer", "message": error.to_string() }),
            );
        }
    }
    emit(
        "stopped",
        json!({
            "durationMs": state.last / 10_000,
            "frames": frames,
            "dropped": dropped,
        }),
    );
}

/// The helper's CPU use over each interval it is sampled at. The hardware
/// encoder runs on the GPU and is not counted here.
struct CpuMeter {
    started: Instant,
    last: Mutex<(Instant, f64)>,
}

impl CpuMeter {
    fn new() -> Self {
        Self {
            started: Instant::now(),
            last: Mutex::new((Instant::now(), cpu_seconds())),
        }
    }

    fn sample(&self) -> (u64, f64) {
        let mut last = self
            .last
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner());
        let (now, cpu) = (Instant::now(), cpu_seconds());
        let interval = now.duration_since(last.0).as_secs_f64().max(0.001);
        let percent = (cpu - last.1) / interval * 100.0;
        *last = (now, cpu);
        (
            self.started.elapsed().as_millis() as u64,
            (percent * 10.0).round() / 10.0,
        )
    }
}

fn cpu_seconds() -> f64 {
    let (mut created, mut exited, mut kernel, mut user) = Default::default();
    // SAFETY: out-pointers to locals, on this process's own pseudo-handle.
    let read = unsafe {
        GetProcessTimes(
            GetCurrentProcess(),
            &mut created,
            &mut exited,
            &mut kernel,
            &mut user,
        )
    };
    let seconds = |time: FILETIME| {
        (u64::from(time.dwHighDateTime) << 32 | u64::from(time.dwLowDateTime)) as f64 / 1e7
    };
    if read.is_ok() {
        seconds(kernel) + seconds(user)
    } else {
        0.0
    }
}
