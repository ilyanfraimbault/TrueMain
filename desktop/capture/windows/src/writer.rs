//! The file: Media Foundation's sink writer, H.264 (or HEVC) + AAC in
//! fragmented MP4, so a helper killed mid-game still leaves a video that
//! plays up to its last fragment. Hardware transforms are enabled and the
//! writer shares the capture's D3D11 device, so the GPU's encoder (NVENC,
//! AMF, QuickSync) takes the NV12 surfaces where they are.

use windows::core::{Result, GUID, HSTRING};
use windows::Win32::Media::MediaFoundation::*;
use windows::Win32::System::Com::CoTaskMemFree;

use crate::args::Codec;

pub const AUDIO_RATE: u32 = 48_000;
pub const AUDIO_CHANNELS: u32 = 2;
/// 16-bit PCM in, which is what the AAC encoder takes.
pub const AUDIO_BLOCK: u32 = AUDIO_CHANNELS * 2;
const AUDIO_BITRATE: u32 = 160_000;

pub struct VideoOptions {
    pub width: u32,
    pub height: u32,
    pub fps: u32,
    pub codec: Codec,
    pub bitrate: u32,
    pub keyframe_interval: u32,
}

fn subtype(codec: Codec) -> GUID {
    match codec {
        Codec::H264 => MFVideoFormat_H264,
        Codec::Hevc => MFVideoFormat_HEVC,
    }
}

fn pack(high: u32, low: u32) -> u64 {
    (u64::from(high) << 32) | u64::from(low)
}

fn attributes(count: u32) -> Result<IMFAttributes> {
    let mut attributes = None;
    // SAFETY: out-pointer to a local.
    unsafe { MFCreateAttributes(&mut attributes, count)? };
    Ok(attributes.expect("MFCreateAttributes returned attributes"))
}

/// An uncompressed or compressed video type of this size and rate.
pub fn video_type(format: &GUID, width: u32, height: u32, fps: u32) -> Result<IMFMediaType> {
    // SAFETY: setting attributes on a fresh media type.
    unsafe {
        let media = MFCreateMediaType()?;
        media.SetGUID(&MF_MT_MAJOR_TYPE, &MFMediaType_Video)?;
        media.SetGUID(&MF_MT_SUBTYPE, format)?;
        media.SetUINT64(&MF_MT_FRAME_SIZE, pack(width, height))?;
        media.SetUINT64(&MF_MT_FRAME_RATE, pack(fps, 1))?;
        media.SetUINT64(&MF_MT_PIXEL_ASPECT_RATIO, pack(1, 1))?;
        media.SetUINT32(&MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive.0 as u32)?;
        media.SetUINT32(&MF_MT_YUV_MATRIX, MFVideoTransferMatrix_BT709.0 as u32)?;
        media.SetUINT32(&MF_MT_VIDEO_NOMINAL_RANGE, MFNominalRange_16_235.0 as u32)?;
        Ok(media)
    }
}

fn audio_type(format: &GUID) -> Result<IMFMediaType> {
    // SAFETY: setting attributes on a fresh media type.
    unsafe {
        let media = MFCreateMediaType()?;
        media.SetGUID(&MF_MT_MAJOR_TYPE, &MFMediaType_Audio)?;
        media.SetGUID(&MF_MT_SUBTYPE, format)?;
        media.SetUINT32(&MF_MT_AUDIO_SAMPLES_PER_SECOND, AUDIO_RATE)?;
        media.SetUINT32(&MF_MT_AUDIO_NUM_CHANNELS, AUDIO_CHANNELS)?;
        media.SetUINT32(&MF_MT_AUDIO_BITS_PER_SAMPLE, 16)?;
        if *format == MFAudioFormat_PCM {
            media.SetUINT32(&MF_MT_AUDIO_BLOCK_ALIGNMENT, AUDIO_BLOCK)?;
            media.SetUINT32(&MF_MT_AUDIO_AVG_BYTES_PER_SECOND, AUDIO_RATE * AUDIO_BLOCK)?;
        } else {
            media.SetUINT32(&MF_MT_AUDIO_AVG_BYTES_PER_SECOND, AUDIO_BITRATE / 8)?;
        }
        Ok(media)
    }
}

/// Whether the GPU has an encoder for this codec. Without one Media
/// Foundation falls back to its software encoder, which the game pays for.
pub fn hardware_encoder(codec: Codec) -> bool {
    let output = MFT_REGISTER_TYPE_INFO {
        guidMajorType: MFMediaType_Video,
        guidSubtype: subtype(codec),
    };
    let mut found: *mut Option<IMFActivate> = std::ptr::null_mut();
    let mut count = 0u32;
    // SAFETY: the array MFTEnumEx allocates is released, each entry dropped.
    unsafe {
        if MFTEnumEx(
            MFT_CATEGORY_VIDEO_ENCODER,
            MFT_ENUM_FLAG_HARDWARE | MFT_ENUM_FLAG_SORTANDFILTER,
            None,
            Some(&output),
            &mut found,
            &mut count,
        )
        .is_err()
        {
            return false;
        }
        for index in 0..count as usize {
            std::ptr::drop_in_place(found.add(index));
        }
        CoTaskMemFree(Some(found as *const _));
    }
    count > 0
}

pub struct Writer {
    sink: IMFSinkWriter,
    pub video: u32,
    pub audio: Option<u32>,
}

// SAFETY: the sink writer is free-threaded; the recorder serialises its
// calls anyway.
unsafe impl Send for Writer {}

impl Writer {
    pub fn new(
        path: &str,
        options: &VideoOptions,
        manager: &IMFDXGIDeviceManager,
        audio: bool,
    ) -> Result<Self> {
        // SAFETY: Media Foundation setup on owned objects.
        unsafe {
            let settings = attributes(4)?;
            settings.SetUINT32(&MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, 1)?;
            settings.SetUnknown(&MF_SINK_WRITER_D3D_MANAGER, manager)?;
            settings.SetGUID(
                &MF_TRANSCODE_CONTAINERTYPE,
                &MFTranscodeContainerType_FMPEG4,
            )?;
            settings.SetUINT32(&MF_LOW_LATENCY, 1)?;
            let sink = MFCreateSinkWriterFromURL(&HSTRING::from(path), None, &settings)?;

            let encoded = video_type(
                &subtype(options.codec),
                options.width,
                options.height,
                options.fps,
            )?;
            encoded.SetUINT32(&MF_MT_AVG_BITRATE, options.bitrate)?;
            let profile = match options.codec {
                Codec::H264 => eAVEncH264VProfile_High.0 as u32,
                Codec::Hevc => eAVEncH265VProfile_Main_420_8.0 as u32,
            };
            encoded.SetUINT32(&MF_MT_MPEG2_PROFILE, profile)?;
            let video = sink.AddStream(&encoded)?;

            let encoding = attributes(5)?;
            encoding.SetUINT32(&CODECAPI_AVEncMPVGOPSize, options.keyframe_interval)?;
            encoding.SetUINT32(&CODECAPI_AVEncMPVDefaultBPictureCount, 0)?;
            encoding.SetUINT32(
                &CODECAPI_AVEncCommonRateControlMode,
                eAVEncCommonRateControlMode_UnconstrainedVBR.0 as u32,
            )?;
            encoding.SetUINT32(&CODECAPI_AVEncCommonMeanBitRate, options.bitrate)?;
            encoding.SetUINT32(&CODECAPI_AVLowLatencyMode, 1)?;
            let raw = video_type(
                &MFVideoFormat_NV12,
                options.width,
                options.height,
                options.fps,
            )?;
            sink.SetInputMediaType(video, &raw, &encoding)?;

            let audio = if audio {
                let stream = sink.AddStream(&audio_type(&MFAudioFormat_AAC)?)?;
                sink.SetInputMediaType(stream, &audio_type(&MFAudioFormat_PCM)?, None)?;
                Some(stream)
            } else {
                None
            };
            sink.BeginWriting()?;
            Ok(Self { sink, video, audio })
        }
    }

    /// A video sample, at `time` from video time zero, in 100 ns units.
    pub fn write_video(&self, sample: &IMFSample, time: i64, duration: i64) -> Result<()> {
        // SAFETY: the sample is alive for the call.
        unsafe {
            sample.SetSampleTime(time)?;
            sample.SetSampleDuration(duration)?;
            self.sink.WriteSample(self.video, sample)
        }
    }

    /// Interleaved 16-bit PCM at `time`.
    pub fn write_audio(&self, pcm: &[u8], time: i64) -> Result<()> {
        let Some(stream) = self.audio else {
            return Ok(());
        };
        if pcm.is_empty() {
            return Ok(());
        }
        let frames = (pcm.len() / AUDIO_BLOCK as usize) as i64;
        // SAFETY: the buffer is locked while it is written, and sized for it.
        unsafe {
            let buffer = MFCreateMemoryBuffer(pcm.len() as u32)?;
            let mut data = std::ptr::null_mut();
            buffer.Lock(&mut data, None, None)?;
            std::ptr::copy_nonoverlapping(pcm.as_ptr(), data, pcm.len());
            buffer.Unlock()?;
            buffer.SetCurrentLength(pcm.len() as u32)?;
            let sample = MFCreateSample()?;
            sample.AddBuffer(&buffer)?;
            sample.SetSampleTime(time)?;
            sample.SetSampleDuration(frames * 10_000_000 / i64::from(AUDIO_RATE))?;
            self.sink.WriteSample(stream, &sample)
        }
    }

    /// Close the file. Nothing written after this.
    pub fn finish(&self) -> Result<()> {
        // SAFETY: the writer is not used after finalising.
        unsafe { self.sink.Finalize() }
    }
}
