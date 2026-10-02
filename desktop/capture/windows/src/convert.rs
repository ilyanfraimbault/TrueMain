//! The GPU half of a recording: the D3D11 device the capture and the encoder
//! share, and the video processor that turns each captured BGRA frame into
//! the NV12 picture the hardware encoder takes, scaled to the output size —
//! one blit, no copy through system memory.
//!
//! The NV12 surfaces come from Media Foundation's sample allocator, which
//! takes each one back once the encoder has released it.

use std::collections::HashMap;

use windows::core::{Interface, Result};
use windows::Graphics::DirectX::Direct3D11::IDirect3DDevice;
use windows::Win32::Foundation::{HMODULE, RECT};
use windows::Win32::Graphics::Direct3D::{D3D_DRIVER_TYPE_HARDWARE, D3D_FEATURE_LEVEL_11_0};
use windows::Win32::Graphics::Direct3D11::*;
use windows::Win32::Graphics::Dxgi::Common::{
    DXGI_FORMAT_B8G8R8A8_UNORM, DXGI_RATIONAL, DXGI_SAMPLE_DESC,
};
use windows::Win32::Graphics::Dxgi::IDXGIDevice;
use windows::Win32::Media::MediaFoundation::*;
use windows::Win32::System::WinRT::Direct3D11::CreateDirect3D11DeviceFromDXGIDevice;

/// `D3D11_VIDEO_PROCESSOR_COLOR_SPACE` bits: BT.709 matrix, studio range —
/// what browsers assume for HD video.
const OUTPUT_COLOR_SPACE: u32 = (1 << 2) | (1 << 4);

pub struct Gpu {
    pub device: ID3D11Device,
    pub context: ID3D11DeviceContext,
    pub winrt: IDirect3DDevice,
    pub manager: IMFDXGIDeviceManager,
}

impl Gpu {
    pub fn new() -> Result<Self> {
        let mut device = None;
        let mut context = None;
        // SAFETY: out-pointers to locals; the device is shared with Media
        // Foundation's threads, hence multithread protection.
        unsafe {
            D3D11CreateDevice(
                None,
                D3D_DRIVER_TYPE_HARDWARE,
                HMODULE::default(),
                D3D11_CREATE_DEVICE_BGRA_SUPPORT | D3D11_CREATE_DEVICE_VIDEO_SUPPORT,
                Some(&[D3D_FEATURE_LEVEL_11_0]),
                D3D11_SDK_VERSION,
                Some(&mut device),
                None,
                Some(&mut context),
            )?;
            let device: ID3D11Device = device.expect("D3D11CreateDevice returned a device");
            let context = context.expect("D3D11CreateDevice returned a context");
            let _ = device
                .cast::<ID3D11Multithread>()?
                .SetMultithreadProtected(true);

            let winrt: IDirect3DDevice =
                CreateDirect3D11DeviceFromDXGIDevice(&device.cast::<IDXGIDevice>()?)?.cast()?;

            let mut token = 0;
            let mut manager = None;
            MFCreateDXGIDeviceManager(&mut token, &mut manager)?;
            let manager = manager.expect("MFCreateDXGIDeviceManager returned a manager");
            manager.ResetDevice(&device, token)?;
            Ok(Self {
                device,
                context,
                winrt,
                manager,
            })
        }
    }
}

/// Hands out GPU-backed NV12 samples of the output size.
pub struct Samples {
    allocator: IMFVideoSampleAllocatorEx,
}

impl Samples {
    pub fn new(gpu: &Gpu, width: u32, height: u32) -> Result<Self> {
        // SAFETY: plain Media Foundation setup on owned objects.
        unsafe {
            let mut raw = std::ptr::null_mut();
            MFCreateVideoSampleAllocatorEx(&IMFVideoSampleAllocatorEx::IID, &mut raw)?;
            let allocator = IMFVideoSampleAllocatorEx::from_raw(raw);
            allocator.SetDirectXManager(&gpu.manager)?;

            let mut attributes = None;
            MFCreateAttributes(&mut attributes, 2)?;
            let attributes = attributes.expect("MFCreateAttributes returned attributes");
            attributes.SetUINT32(&MF_SA_D3D11_BINDFLAGS, D3D11_BIND_RENDER_TARGET.0 as u32)?;
            attributes.SetUINT32(&MF_SA_D3D11_USAGE, D3D11_USAGE_DEFAULT.0 as u32)?;

            let format = crate::writer::video_type(&MFVideoFormat_NV12, width, height, 1)?;
            allocator.InitializeSampleAllocatorEx(4, 16, &attributes, &format)?;
            Ok(Self { allocator })
        }
    }

    /// A free sample, or `None` when the encoder still holds all of them —
    /// a frame dropped.
    pub fn next(&self) -> Option<IMFSample> {
        // SAFETY: the allocator was initialised in `new`.
        unsafe { self.allocator.AllocateSample().ok() }
    }
}

/// The texture and the subresource behind a sample from `Samples`.
pub fn sample_texture(sample: &IMFSample) -> Result<(ID3D11Texture2D, u32)> {
    // SAFETY: the sample's one buffer is a DXGI buffer (`Samples` made it).
    unsafe {
        let buffer: IMFDXGIBuffer = sample.GetBufferByIndex(0)?.cast()?;
        let mut raw = std::ptr::null_mut();
        buffer.GetResource(&ID3D11Texture2D::IID, &mut raw)?;
        Ok((
            ID3D11Texture2D::from_raw(raw),
            buffer.GetSubresourceIndex()?,
        ))
    }
}

pub struct Converter {
    video_device: ID3D11VideoDevice,
    video_context: ID3D11VideoContext,
    enumerator: ID3D11VideoProcessorEnumerator,
    processor: ID3D11VideoProcessor,
    /// The captured content, copied out of the capture's surface (which can
    /// be larger than its content, and is handed back to the frame pool).
    input: Option<(ID3D11Texture2D, ID3D11VideoProcessorInputView, (u32, u32))>,
    outputs: HashMap<(usize, u32), ID3D11VideoProcessorOutputView>,
    output_size: (u32, u32),
}

// SAFETY: the converter is only ever used under the recorder's lock, one
// thread at a time; the device is multithread-protected.
unsafe impl Send for Converter {}

impl Converter {
    pub fn new(gpu: &Gpu, input: (u32, u32), output: (u32, u32), fps: u32) -> Result<Self> {
        // SAFETY: plain D3D11 video setup on owned objects.
        unsafe {
            let video_device: ID3D11VideoDevice = gpu.device.cast()?;
            let video_context: ID3D11VideoContext = gpu.context.cast()?;
            let rate = DXGI_RATIONAL {
                Numerator: fps,
                Denominator: 1,
            };
            let enumerator = video_device.CreateVideoProcessorEnumerator(
                &D3D11_VIDEO_PROCESSOR_CONTENT_DESC {
                    InputFrameFormat: D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE,
                    InputFrameRate: rate,
                    InputWidth: input.0,
                    InputHeight: input.1,
                    OutputFrameRate: rate,
                    OutputWidth: output.0,
                    OutputHeight: output.1,
                    Usage: D3D11_VIDEO_USAGE_OPTIMAL_SPEED,
                },
            )?;
            let processor = video_device.CreateVideoProcessor(&enumerator, 0)?;
            video_context.VideoProcessorSetStreamFrameFormat(
                &processor,
                0,
                D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE,
            );
            video_context.VideoProcessorSetStreamAutoProcessingMode(&processor, 0, false);
            video_context.VideoProcessorSetStreamColorSpace(
                &processor,
                0,
                &D3D11_VIDEO_PROCESSOR_COLOR_SPACE { _bitfield: 0 },
            );
            video_context.VideoProcessorSetOutputColorSpace(
                &processor,
                &D3D11_VIDEO_PROCESSOR_COLOR_SPACE {
                    _bitfield: OUTPUT_COLOR_SPACE,
                },
            );
            let black = D3D11_VIDEO_COLOR {
                Anonymous: D3D11_VIDEO_COLOR_0 {
                    RGBA: D3D11_VIDEO_COLOR_RGBA {
                        R: 0.0,
                        G: 0.0,
                        B: 0.0,
                        A: 1.0,
                    },
                },
            };
            video_context.VideoProcessorSetOutputBackgroundColor(&processor, false, &black);
            Ok(Self {
                video_device,
                video_context,
                enumerator,
                processor,
                input: None,
                outputs: HashMap::new(),
                output_size: output,
            })
        }
    }

    /// Draw `content` (the top-left `size` of `frame`) into `target`, scaled
    /// to fit and centred, black around it.
    pub fn convert(
        &mut self,
        gpu: &Gpu,
        frame: &ID3D11Texture2D,
        size: (u32, u32),
        target: &ID3D11Texture2D,
        subresource: u32,
    ) -> Result<()> {
        let (input, input_view) = self.input_for(gpu, size)?;
        let output_view = self.output_view(target, subresource)?;
        let (source, dest) = (
            RECT {
                left: 0,
                top: 0,
                right: size.0 as i32,
                bottom: size.1 as i32,
            },
            fit(size, self.output_size),
        );
        // SAFETY: every view and texture is alive for the calls; the stream
        // struct's input view is borrowed and released before returning.
        unsafe {
            gpu.context.CopySubresourceRegion(
                &input,
                0,
                0,
                0,
                0,
                frame,
                0,
                Some(&D3D11_BOX {
                    left: 0,
                    top: 0,
                    front: 0,
                    right: size.0,
                    bottom: size.1,
                    back: 1,
                }),
            );
            self.video_context.VideoProcessorSetStreamSourceRect(
                &self.processor,
                0,
                true,
                Some(&source),
            );
            self.video_context.VideoProcessorSetStreamDestRect(
                &self.processor,
                0,
                true,
                Some(&dest),
            );
            let mut stream = D3D11_VIDEO_PROCESSOR_STREAM {
                Enable: true.into(),
                pInputSurface: std::mem::ManuallyDrop::new(Some(input_view)),
                ..Default::default()
            };
            let result = self.video_context.VideoProcessorBlt(
                &self.processor,
                &output_view,
                0,
                std::slice::from_ref(&stream),
            );
            std::mem::ManuallyDrop::drop(&mut stream.pInputSurface);
            result
        }
    }

    /// The input texture and its view, made again when the content's size
    /// changes (the player resized the window).
    fn input_for(
        &mut self,
        gpu: &Gpu,
        size: (u32, u32),
    ) -> Result<(ID3D11Texture2D, ID3D11VideoProcessorInputView)> {
        if let Some((texture, view, current)) = &self.input {
            if *current == size {
                return Ok((texture.clone(), view.clone()));
            }
        }
        // SAFETY: plain resource creation on the shared device.
        unsafe {
            let mut texture = None;
            gpu.device.CreateTexture2D(
                &D3D11_TEXTURE2D_DESC {
                    Width: size.0,
                    Height: size.1,
                    MipLevels: 1,
                    ArraySize: 1,
                    Format: DXGI_FORMAT_B8G8R8A8_UNORM,
                    SampleDesc: DXGI_SAMPLE_DESC {
                        Count: 1,
                        Quality: 0,
                    },
                    Usage: D3D11_USAGE_DEFAULT,
                    BindFlags: (D3D11_BIND_RENDER_TARGET.0 | D3D11_BIND_SHADER_RESOURCE.0) as u32,
                    CPUAccessFlags: 0,
                    MiscFlags: 0,
                },
                None,
                Some(&mut texture),
            )?;
            let texture = texture.expect("CreateTexture2D returned a texture");
            let mut view = None;
            self.video_device.CreateVideoProcessorInputView(
                &texture,
                &self.enumerator,
                &D3D11_VIDEO_PROCESSOR_INPUT_VIEW_DESC {
                    FourCC: 0,
                    ViewDimension: D3D11_VPIV_DIMENSION_TEXTURE2D,
                    Anonymous: D3D11_VIDEO_PROCESSOR_INPUT_VIEW_DESC_0 {
                        Texture2D: D3D11_TEX2D_VPIV {
                            MipSlice: 0,
                            ArraySlice: 0,
                        },
                    },
                },
                Some(&mut view),
            )?;
            let view = view.expect("CreateVideoProcessorInputView returned a view");
            self.input = Some((texture.clone(), view.clone(), size));
            Ok((texture, view))
        }
    }

    /// The allocator recycles a handful of surfaces: their views are kept.
    fn output_view(
        &mut self,
        target: &ID3D11Texture2D,
        subresource: u32,
    ) -> Result<ID3D11VideoProcessorOutputView> {
        let key = (target.as_raw() as usize, subresource);
        if let Some(view) = self.outputs.get(&key) {
            return Ok(view.clone());
        }
        // SAFETY: plain view creation on a texture the allocator owns.
        let view = unsafe {
            let mut description = D3D11_TEXTURE2D_DESC::default();
            target.GetDesc(&mut description);
            let desc = if description.ArraySize > 1 {
                D3D11_VIDEO_PROCESSOR_OUTPUT_VIEW_DESC {
                    ViewDimension: D3D11_VPOV_DIMENSION_TEXTURE2DARRAY,
                    Anonymous: D3D11_VIDEO_PROCESSOR_OUTPUT_VIEW_DESC_0 {
                        Texture2DArray: D3D11_TEX2D_ARRAY_VPOV {
                            MipSlice: 0,
                            FirstArraySlice: subresource,
                            ArraySize: 1,
                        },
                    },
                }
            } else {
                D3D11_VIDEO_PROCESSOR_OUTPUT_VIEW_DESC {
                    ViewDimension: D3D11_VPOV_DIMENSION_TEXTURE2D,
                    Anonymous: D3D11_VIDEO_PROCESSOR_OUTPUT_VIEW_DESC_0 {
                        Texture2D: D3D11_TEX2D_VPOV { MipSlice: 0 },
                    },
                }
            };
            let mut view = None;
            self.video_device.CreateVideoProcessorOutputView(
                target,
                &self.enumerator,
                &desc,
                Some(&mut view),
            )?;
            view.expect("CreateVideoProcessorOutputView returned a view")
        };
        self.outputs.insert(key, view.clone());
        Ok(view)
    }
}

/// `content` scaled to fit `output`, centred, on even pixels (NV12 halves
/// the chroma).
fn fit(content: (u32, u32), output: (u32, u32)) -> RECT {
    let scale = f64::min(
        f64::from(output.0) / f64::from(content.0.max(1)),
        f64::from(output.1) / f64::from(content.1.max(1)),
    );
    let width = ((f64::from(content.0) * scale) as i32 & !1).min(output.0 as i32);
    let height = ((f64::from(content.1) * scale) as i32 & !1).min(output.1 as i32);
    let left = ((output.0 as i32 - width) / 2) & !1;
    let top = ((output.1 as i32 - height) / 2) & !1;
    RECT {
        left,
        top,
        right: left + width,
        bottom: top + height,
    }
}
