//! What the app does with a recording once it is written, through Media
//! Foundation like the recording itself: a clip is a passthrough copy — no
//! re-encoding, so it costs no CPU and keeps the game's quality, starting on
//! the keyframe at or before the range (the recorder's one-second keyframes
//! keep that within a second) — and a thumbnail is one decoded frame written
//! as a JPEG through WIC.
//!
//! `access` has nothing to ask on Windows: a desktop app may capture a window
//! without a permission. It reports whether this Windows has
//! Windows.Graphics.Capture at all (Windows 10 1903 or later).

use serde_json::json;
use windows::core::{Result, GUID, HSTRING};
use windows::Graphics::Capture::GraphicsCaptureSession;
use windows::Win32::Foundation::GENERIC_WRITE;
use windows::Win32::Graphics::Imaging::*;
use windows::Win32::Media::MediaFoundation::*;
use windows::Win32::System::Com::StructuredStorage::PROPVARIANT;
use windows::Win32::System::Com::{CoCreateInstance, CLSCTX_INPROC_SERVER};

use crate::args;
use crate::output::{emit, fail};

const MS: i64 = 10_000;
const FIRST_VIDEO: u32 = MF_SOURCE_READER_FIRST_VIDEO_STREAM.0 as u32;
const ANY_STREAM: u32 = MF_SOURCE_READER_ANY_STREAM.0 as u32;
const ALL_STREAMS: u32 = MF_SOURCE_READER_ALL_STREAMS.0 as u32;
const END_OF_STREAM: u32 = MF_SOURCE_READERF_ENDOFSTREAM.0 as u32;

pub fn access() {
    let granted = GraphicsCaptureSession::IsSupported().unwrap_or(false);
    emit("access", json!({ "granted": granted }));
}

fn reader(path: &str, decode: bool) -> Result<IMFSourceReader> {
    // SAFETY: Media Foundation setup on owned objects.
    unsafe {
        let mut attributes = None;
        MFCreateAttributes(&mut attributes, 1)?;
        let attributes = attributes.expect("MFCreateAttributes returned attributes");
        if decode {
            attributes.SetUINT32(&MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING, 1)?;
        }
        MFCreateSourceReaderFromURL(&HSTRING::from(path), &attributes)
    }
}

/// Move the reader to `ms`; it lands on the keyframe at or before it.
fn seek(reader: &IMFSourceReader, ms: u64) -> Result<()> {
    let mut position = PROPVARIANT::default();
    // SAFETY: a VT_I8 PROPVARIANT owns nothing.
    unsafe {
        let inner = &mut *position.Anonymous.Anonymous;
        inner.vt = windows::Win32::System::Variant::VT_I8;
        inner.Anonymous.hVal = ms as i64 * MS;
        reader.SetCurrentPosition(&GUID::zeroed(), &position)
    }
}

struct Read {
    stream: u32,
    end: bool,
    time: i64,
    sample: Option<IMFSample>,
}

fn read(reader: &IMFSourceReader, stream: u32) -> Result<Read> {
    let (mut index, mut flags, mut time, mut sample) = (0u32, 0u32, 0i64, None);
    // SAFETY: out-pointers to locals.
    unsafe {
        reader.ReadSample(
            stream,
            0,
            Some(&mut index),
            Some(&mut flags),
            Some(&mut time),
            Some(&mut sample),
        )?;
    }
    Ok(Read {
        stream: index,
        end: flags & END_OF_STREAM != 0,
        time,
        sample,
    })
}

pub fn clip(arguments: &[String]) {
    let input = args::required("--in", arguments);
    let out = args::required("--out", arguments);
    let start = args::milliseconds("--start-ms", arguments);
    let end = args::milliseconds("--end-ms", arguments);
    if end <= start {
        fail("usage", "--end-ms must be after --start-ms", 1);
    }
    let _ = std::fs::remove_file(out);
    match copy_range(input, out, start, end) {
        Ok(duration) => emit("clipped", json!({ "durationMs": duration / MS })),
        Err(error) => {
            let _ = std::fs::remove_file(out);
            fail("clip", &format!("could not cut the clip: {error}"), 1)
        }
    }
}

/// Copy `start..end` (ms) of `input` into `out` as it is encoded. Returns the
/// clip's duration in 100 ns.
fn copy_range(input: &str, out: &str, start: u64, end: u64) -> Result<i64> {
    let reader = reader(input, false)?;
    // SAFETY: Media Foundation calls on owned objects; samples are written
    // before the next read.
    unsafe {
        let writer = MFCreateSinkWriterFromURL(&HSTRING::from(out), None, None)?;
        reader.SetStreamSelection(ALL_STREAMS, false)?;
        // Source stream index → sink stream index, for the first video and the
        // first audio stream.
        let mut streams: Vec<(u32, u32, GUID)> = Vec::new();
        let mut index = 0;
        while let Ok(native) = reader.GetNativeMediaType(index, 0) {
            let major = native.GetMajorType()?;
            let wanted = (major == MFMediaType_Video || major == MFMediaType_Audio)
                && !streams.iter().any(|(_, _, kind)| *kind == major);
            if wanted {
                reader.SetStreamSelection(index, true)?;
                let sink = writer.AddStream(&native)?;
                writer.SetInputMediaType(sink, &native, None)?;
                streams.push((index, sink, major));
            }
            index += 1;
        }
        let Some(&(video, _, _)) = streams
            .iter()
            .find(|(_, _, kind)| *kind == MFMediaType_Video)
        else {
            return Err(windows::core::Error::new(
                MF_E_INVALIDSTREAMNUMBER,
                "no video stream",
            ));
        };
        seek(&reader, start)?;
        writer.BeginWriting()?;

        // The keyframe the seek landed on is the clip's time zero.
        let mut first = read(&reader, video)?;
        while first.sample.is_none() && !first.end {
            first = read(&reader, video)?;
        }
        let base = first.time;
        let end = end as i64 * MS;
        let mut done: Vec<u32> = Vec::new();
        let mut duration = 0i64;
        let mut next = Some(first);
        loop {
            let current = match next.take() {
                Some(current) => current,
                None => read(&reader, ANY_STREAM)?,
            };
            if current.end || current.time >= end {
                if !done.contains(&current.stream) {
                    done.push(current.stream);
                    let _ = reader.SetStreamSelection(current.stream, false);
                }
                if done.len() >= streams.len() {
                    break;
                }
                continue;
            }
            let (Some(sample), Some(&(_, sink, _))) = (
                current.sample,
                streams
                    .iter()
                    .find(|(source, _, _)| *source == current.stream),
            ) else {
                continue;
            };
            if current.time < base {
                continue;
            }
            sample.SetSampleTime(current.time - base)?;
            writer.WriteSample(sink, &sample)?;
            if current.stream == video {
                let length = sample.GetSampleDuration().unwrap_or(0);
                duration = duration.max(current.time - base + length);
            }
        }
        writer.Finalize()?;
        Ok(duration)
    }
}

pub fn thumbnail(arguments: &[String]) {
    let input = args::required("--in", arguments);
    let out = args::required("--out", arguments);
    let at = args::milliseconds("--at-ms", arguments);
    let width = args::value("--width", arguments)
        .and_then(|raw| raw.parse().ok())
        .unwrap_or(640u32);
    match frame_at(input, at).and_then(|frame| write_jpeg(&frame, out, width)) {
        Ok((width, height)) => emit("thumbnail", json!({ "width": width, "height": height })),
        Err(error) => fail("thumbnail", &format!("could not read a frame: {error}"), 1),
    }
}

/// One decoded frame, top-down BGRX.
struct Frame {
    width: u32,
    height: u32,
    pixels: Vec<u8>,
}

fn frame_at(input: &str, at: u64) -> Result<Frame> {
    let reader = reader(input, true)?;
    // SAFETY: Media Foundation calls on owned objects; the buffer is unlocked
    // after its rows are copied out.
    unsafe {
        reader.SetStreamSelection(ALL_STREAMS, false)?;
        reader.SetStreamSelection(FIRST_VIDEO, true)?;
        let wanted = MFCreateMediaType()?;
        wanted.SetGUID(&MF_MT_MAJOR_TYPE, &MFMediaType_Video)?;
        wanted.SetGUID(&MF_MT_SUBTYPE, &MFVideoFormat_RGB32)?;
        reader.SetCurrentMediaType(FIRST_VIDEO, None, &wanted)?;
        let size = reader
            .GetCurrentMediaType(FIRST_VIDEO)?
            .GetUINT64(&MF_MT_FRAME_SIZE)?;
        let (width, height) = ((size >> 32) as u32, size as u32);
        seek(&reader, at)?;
        // The nearest keyframe is close enough for a card and much faster.
        let sample = loop {
            let read = read(&reader, FIRST_VIDEO)?;
            if let Some(sample) = read.sample {
                break sample;
            }
            if read.end {
                return Err(windows::core::Error::new(
                    MF_E_INVALIDSTREAMNUMBER,
                    "no frame at that time",
                ));
            }
        };
        let buffer = sample.ConvertToContiguousBuffer()?;
        let row = width as usize * 4;
        let mut pixels = vec![0u8; row * height as usize];
        let two_d: Result<IMF2DBuffer> = windows::core::Interface::cast(&buffer);
        if let Ok(two_d) = two_d {
            let (mut scan0, mut pitch) = (std::ptr::null_mut(), 0i32);
            two_d.Lock2D(&mut scan0, &mut pitch)?;
            for y in 0..height as usize {
                let line = scan0.offset(y as isize * pitch as isize);
                std::ptr::copy_nonoverlapping(line, pixels[y * row..].as_mut_ptr(), row);
            }
            two_d.Unlock2D()?;
        } else {
            let (mut data, mut length) = (std::ptr::null_mut(), 0u32);
            buffer.Lock(&mut data, None, Some(&mut length))?;
            let copied = pixels.len().min(length as usize);
            std::ptr::copy_nonoverlapping(data, pixels.as_mut_ptr(), copied);
            buffer.Unlock()?;
        }
        Ok(Frame {
            width,
            height,
            pixels,
        })
    }
}

/// `frame` scaled to `width` (aspect kept) as a JPEG. Returns its size.
fn write_jpeg(frame: &Frame, out: &str, width: u32) -> Result<(u32, u32)> {
    let width = width.clamp(16, frame.width.max(16));
    let height = ((u64::from(frame.height) * u64::from(width) / u64::from(frame.width.max(1)))
        as u32)
        .max(1);
    // SAFETY: WIC calls on owned objects; the pixels outlive the bitmap.
    unsafe {
        let factory: IWICImagingFactory =
            CoCreateInstance(&CLSID_WICImagingFactory, None, CLSCTX_INPROC_SERVER)?;
        let bitmap = factory.CreateBitmapFromMemory(
            frame.width,
            frame.height,
            &GUID_WICPixelFormat32bppBGR,
            frame.width * 4,
            &frame.pixels,
        )?;
        let scaler = factory.CreateBitmapScaler()?;
        scaler.Initialize(&bitmap, width, height, WICBitmapInterpolationModeFant)?;
        let converter = factory.CreateFormatConverter()?;
        converter.Initialize(
            &scaler,
            &GUID_WICPixelFormat24bppBGR,
            WICBitmapDitherTypeNone,
            None,
            0.0,
            WICBitmapPaletteTypeCustom,
        )?;

        let stream = factory.CreateStream()?;
        stream.InitializeFromFilename(&HSTRING::from(out), GENERIC_WRITE.0)?;
        let encoder = factory.CreateEncoder(&GUID_ContainerFormatJpeg, std::ptr::null())?;
        encoder.Initialize(&stream, WICBitmapEncoderNoCache)?;
        let (mut target, mut options) = (None, None);
        encoder.CreateNewFrame(&mut target, &mut options)?;
        let target = target.expect("CreateNewFrame returned a frame");
        target.Initialize(options.as_ref())?;
        target.SetSize(width, height)?;
        let mut format = GUID_WICPixelFormat24bppBGR;
        target.SetPixelFormat(&mut format)?;
        target.WriteSource(&converter, std::ptr::null())?;
        target.Commit()?;
        encoder.Commit()?;
        Ok((width, height))
    }
}
