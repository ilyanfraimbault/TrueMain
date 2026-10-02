//! The game's own sound: WASAPI process loopback on the game's process tree —
//! never the microphone, and not the rest of the system either (a voice chat,
//! music). Process loopback needs Windows 10 2004 or later; where it is not
//! there the recording goes on without sound.
//!
//! Packets go to `deliver` as 16-bit stereo PCM at 48 kHz (Windows converts),
//! and an empty delivery every `IDLE` while the game is silent, so the
//! recorder can fill the gap.

use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::mpsc;
use std::sync::Arc;
use std::thread::JoinHandle;
use std::time::Duration;

use windows::core::{implement, IUnknown, Interface, Ref, Result, HRESULT};
use windows::Win32::Foundation::{CloseHandle, WAIT_OBJECT_0};
use windows::Win32::Media::Audio::*;
use windows::Win32::System::Com::StructuredStorage::PROPVARIANT;
use windows::Win32::System::Com::{
    CoInitializeEx, IAgileObject, IAgileObject_Impl, BLOB, COINIT_MULTITHREADED,
};
use windows::Win32::System::Threading::{CreateEventW, WaitForSingleObject};
use windows::Win32::System::Variant::VT_BLOB;

use crate::output::log;
use crate::writer::{AUDIO_BLOCK, AUDIO_CHANNELS, AUDIO_RATE};

const IDLE: Duration = Duration::from_millis(100);
const ACTIVATION_TIMEOUT: Duration = Duration::from_secs(5);

pub struct Loopback {
    stop: Arc<AtomicBool>,
    thread: Option<JoinHandle<()>>,
}

impl Loopback {
    /// Start reading the sound of `pid` and everything it started. `None`
    /// when this Windows cannot (the recording then has no sound).
    pub fn start(pid: u32, deliver: impl FnMut(&[u8]) + Send + 'static) -> Option<Self> {
        let stop = Arc::new(AtomicBool::new(false));
        let (ready, started) = mpsc::channel();
        let flag = stop.clone();
        let thread = std::thread::spawn(move || {
            // SAFETY: this thread's own COM initialisation.
            let _ = unsafe { CoInitializeEx(None, COINIT_MULTITHREADED) };
            match open(pid) {
                Ok((client, capture)) => {
                    let _ = ready.send(true);
                    if let Err(error) = read(&client, &capture, &flag, deliver) {
                        log(&format!("the game's sound stopped: {error}"));
                    }
                    // SAFETY: the client is stopped once, after its last read.
                    let _ = unsafe { client.Stop() };
                }
                Err(error) => {
                    log(&format!("recording without sound: {error}"));
                    let _ = ready.send(false);
                }
            }
        });
        if started.recv().unwrap_or(false) {
            Some(Self {
                stop,
                thread: Some(thread),
            })
        } else {
            let _ = thread.join();
            None
        }
    }

    pub fn stop(mut self) {
        self.stop.store(true, Ordering::SeqCst);
        if let Some(thread) = self.thread.take() {
            let _ = thread.join();
        }
    }
}

#[implement(IActivateAudioInterfaceCompletionHandler, IAgileObject)]
struct Activated(mpsc::SyncSender<()>);

impl IActivateAudioInterfaceCompletionHandler_Impl for Activated_Impl {
    fn ActivateCompleted(&self, _: Ref<IActivateAudioInterfaceAsyncOperation>) -> Result<()> {
        let _ = self.0.send(());
        Ok(())
    }
}

impl IAgileObject_Impl for Activated_Impl {}

fn open(pid: u32) -> Result<(IAudioClient, IAudioCaptureClient)> {
    let mut parameters = AUDIOCLIENT_ACTIVATION_PARAMS {
        ActivationType: AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK,
        Anonymous: AUDIOCLIENT_ACTIVATION_PARAMS_0 {
            ProcessLoopbackParams: AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS {
                TargetProcessId: pid,
                ProcessLoopbackMode: PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE,
            },
        },
    };
    let mut blob = PROPVARIANT::default();
    // SAFETY: the blob points at `parameters`, alive until activation is done;
    // the PROPVARIANT is never cleared (it owns nothing).
    unsafe {
        let inner = &mut *blob.Anonymous.Anonymous;
        inner.vt = VT_BLOB;
        inner.Anonymous.blob = BLOB {
            cbSize: std::mem::size_of::<AUDIOCLIENT_ACTIVATION_PARAMS>() as u32,
            pBlobData: &mut parameters as *mut AUDIOCLIENT_ACTIVATION_PARAMS as *mut u8,
        };
    }

    let (done, completed) = mpsc::sync_channel(1);
    let handler: IActivateAudioInterfaceCompletionHandler = Activated(done).into();
    // SAFETY: every pointer handed over outlives the wait below.
    unsafe {
        let operation = ActivateAudioInterfaceAsync(
            VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK,
            &IAudioClient::IID,
            Some(&blob),
            &handler,
        )?;
        completed.recv_timeout(ACTIVATION_TIMEOUT).map_err(|_| {
            windows::core::Error::new(HRESULT(-1), "process loopback never answered")
        })?;
        let mut result = HRESULT(0);
        let mut activated: Option<IUnknown> = None;
        operation.GetActivateResult(&mut result, &mut activated)?;
        result.ok()?;
        let client: IAudioClient = activated
            .ok_or_else(|| windows::core::Error::new(HRESULT(-1), "no audio client"))?
            .cast()?;

        let format = WAVEFORMATEX {
            wFormatTag: WAVE_FORMAT_PCM as u16,
            nChannels: AUDIO_CHANNELS as u16,
            nSamplesPerSec: AUDIO_RATE,
            nAvgBytesPerSec: AUDIO_RATE * AUDIO_BLOCK,
            nBlockAlign: AUDIO_BLOCK as u16,
            wBitsPerSample: 16,
            cbSize: 0,
        };
        client.Initialize(
            AUDCLNT_SHAREMODE_SHARED,
            AUDCLNT_STREAMFLAGS_LOOPBACK
                | AUDCLNT_STREAMFLAGS_EVENTCALLBACK
                | AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM
                | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY,
            2_000_000,
            0,
            &format,
            None,
        )?;
        let capture: IAudioCaptureClient = client.GetService()?;
        Ok((client, capture))
    }
}

fn read(
    client: &IAudioClient,
    capture: &IAudioCaptureClient,
    stop: &AtomicBool,
    mut deliver: impl FnMut(&[u8]),
) -> Result<()> {
    // SAFETY: the event is closed on the way out; buffers are released after
    // being copied out.
    unsafe {
        let event = CreateEventW(None, false, false, None)?;
        client.SetEventHandle(event)?;
        client.Start()?;
        let mut silence = Vec::new();
        let result = (|| {
            while !stop.load(Ordering::SeqCst) {
                if WaitForSingleObject(event, IDLE.as_millis() as u32) != WAIT_OBJECT_0 {
                    deliver(&[]);
                    continue;
                }
                loop {
                    let size = capture.GetNextPacketSize()?;
                    if size == 0 {
                        break;
                    }
                    let mut data = std::ptr::null_mut();
                    let mut frames = 0;
                    let mut flags = 0;
                    capture.GetBuffer(&mut data, &mut frames, &mut flags, None, None)?;
                    let bytes = frames as usize * AUDIO_BLOCK as usize;
                    if flags & AUDCLNT_BUFFERFLAGS_SILENT.0 as u32 != 0 || data.is_null() {
                        silence.resize(bytes, 0);
                        deliver(&silence);
                    } else {
                        deliver(std::slice::from_raw_parts(data, bytes));
                    }
                    capture.ReleaseBuffer(frames)?;
                }
            }
            Ok(())
        })();
        let _ = CloseHandle(event);
        result
    }
}
