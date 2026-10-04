// The recording's sound, kept on the capture clock (#1832).
//
// AVAssetWriter's AAC encoder lays the samples it is given end to end,
// whatever their timestamps: a hole in what ScreenCaptureKit delivers — or a
// buffer dropped because the writer was busy — pulls all the sound after it
// earlier than its picture, and over a game the holes add up to seconds. So
// the position the writer has really reached is counted in samples written,
// every hole past it is filled with silence before the next buffer goes in,
// and a buffer that arrives behind it is dropped.

import AVFoundation
import CoreMedia

final class AudioTrack {
    /// A hole or an overlap shorter than this is clock jitter, left alone.
    static let tolerance = 0.010
    /// Silence goes in by at most this much at a time.
    private static let maxSilenceSeconds = 1.0

    private let input: AVAssetWriterInput
    /// The timestamp of the first sample written, and how many have been
    /// written since: where the writer's sound really is.
    private var origin = CMTime.invalid
    private var written: Int64 = 0
    private(set) var silenceSeconds = 0.0
    private(set) var droppedBuffers = 0

    init(input: AVAssetWriterInput) {
        self.input = input
    }

    func append(_ buffer: CMSampleBuffer) {
        let count = CMSampleBufferGetNumSamples(buffer)
        guard count > 0,
              let format = CMSampleBufferGetFormatDescription(buffer),
              let description = CMAudioFormatDescriptionGetStreamBasicDescription(format)?.pointee,
              description.mSampleRate > 0
        else { return }
        let rate = description.mSampleRate
        let pts = CMSampleBufferGetPresentationTimeStamp(buffer)

        if origin.isValid {
            let gap = CMTimeGetSeconds(CMTimeSubtract(pts, position(rate: rate)))
            if gap < -Self.tolerance {
                droppedBuffers += 1
                return
            }
            if gap > Self.tolerance {
                // A hole not filled yet keeps this buffer out: the next one
                // fills the rest of it.
                guard fillSilence(frames: Int64((gap * rate).rounded()), format: format, description: description) else {
                    droppedBuffers += 1
                    return
                }
            }
        }
        guard input.isReadyForMoreMediaData else {
            droppedBuffers += 1
            return
        }
        if input.append(buffer) {
            if !origin.isValid { origin = pts }
            written += Int64(count)
        }
    }

    private func position(rate: Double) -> CMTime {
        CMTimeAdd(origin, CMTime(value: written, timescale: CMTimeScale(rate)))
    }

    /// Write `frames` of silence at the writer's position. False when the
    /// writer could not take all of it now.
    private func fillSilence(frames: Int64, format: CMAudioFormatDescription, description: AudioStreamBasicDescription) -> Bool {
        let rate = description.mSampleRate
        var left = frames
        while left > 0 {
            guard input.isReadyForMoreMediaData else { return false }
            let chunk = min(left, Int64(rate * Self.maxSilenceSeconds))
            guard let silence = Self.silence(frames: Int(chunk), at: position(rate: rate), format: format, description: description),
                  input.append(silence)
            else { return false }
            written += chunk
            left -= chunk
            silenceSeconds += Double(chunk) / rate
        }
        return true
    }

    /// Zeroed linear PCM in the capture's own format: silence for float and
    /// signed integer samples alike.
    static func silence(frames: Int, at pts: CMTime, format: CMAudioFormatDescription, description: AudioStreamBasicDescription) -> CMSampleBuffer? {
        guard description.mFormatID == kAudioFormatLinearPCM, description.mBytesPerFrame > 0 else { return nil }
        let planes = description.mFormatFlags & kAudioFormatFlagIsNonInterleaved != 0 ? Int(description.mChannelsPerFrame) : 1
        let bytes = frames * Int(description.mBytesPerFrame) * planes
        var block: CMBlockBuffer?
        guard CMBlockBufferCreateWithMemoryBlock(allocator: nil, memoryBlock: nil, blockLength: bytes, blockAllocator: nil, customBlockSource: nil, offsetToData: 0, dataLength: bytes, flags: kCMBlockBufferAssureMemoryNowFlag, blockBufferOut: &block) == noErr,
              let block,
              CMBlockBufferFillDataBytes(with: 0, blockBuffer: block, offsetIntoDestination: 0, dataLength: bytes) == noErr
        else { return nil }
        var buffer: CMSampleBuffer?
        guard CMAudioSampleBufferCreateReadyWithPacketDescriptions(allocator: nil, dataBuffer: block, formatDescription: format, sampleCount: frames, presentationTimeStamp: pts, packetDescriptions: nil, sampleBufferOut: &buffer) == noErr
        else { return nil }
        return buffer
    }
}
