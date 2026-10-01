// Records one window to an H.264 + AAC MP4: ScreenCaptureKit delivers the
// frames, AVAssetWriter hands them to the hardware encoder (VideoToolbox).
//
// The file is written in one-second movie fragments, so a helper killed
// mid-game still leaves a video that plays up to the last second. Video time
// zero is the first frame captured; the `started` event is emitted the moment
// that frame is handed to the writer, which is what the shell's video clock
// counts from.

import AVFoundation
import CoreMedia
import Foundation
import ScreenCaptureKit

struct RecordOptions {
    var output: URL
    var width: Int
    var height: Int
    var fps: Int
    var bitrate: Int
    var keyframeInterval: Int
    var audio: Bool
}

final class Recorder: NSObject, SCStreamOutput, SCStreamDelegate {
    private let options: RecordOptions
    private let writer: AVAssetWriter
    private let videoInput: AVAssetWriterInput
    private let audioInput: AVAssetWriterInput?
    private let queue = DispatchQueue(label: "lol.truemain.capture.samples")
    private var stream: SCStream?
    private var sessionStarted = false
    private var stopping = false
    private var firstPts = CMTime.invalid
    private var lastPts = CMTime.invalid
    private(set) var frames = 0
    private(set) var dropped = 0
    /// Every frame ScreenCaptureKit sent, by its status — what tells a window
    /// that is captured but never drawn (all `idle`) from one never captured.
    private var statuses: [String: Int] = [:]
    private let stopLock = NSLock()
    private var stopTask: Task<Void, Never>?
    /// Called once the file is closed, whoever asked for the stop — the
    /// shell, or the capture failing on its own.
    var onStopped: (() -> Void)?

    init(options: RecordOptions) throws {
        self.options = options
        try? FileManager.default.removeItem(at: options.output)
        writer = try AVAssetWriter(outputURL: options.output, fileType: .mp4)
        writer.movieFragmentInterval = CMTime(seconds: 1, preferredTimescale: 600)
        writer.shouldOptimizeForNetworkUse = false

        videoInput = AVAssetWriterInput(mediaType: .video, outputSettings: [
            AVVideoCodecKey: AVVideoCodecType.h264,
            AVVideoWidthKey: options.width,
            AVVideoHeightKey: options.height,
            AVVideoScalingModeKey: AVVideoScalingModeResizeAspect,
            AVVideoCompressionPropertiesKey: [
                AVVideoAverageBitRateKey: options.bitrate,
                AVVideoMaxKeyFrameIntervalKey: options.keyframeInterval,
                AVVideoExpectedSourceFrameRateKey: options.fps,
                AVVideoProfileLevelKey: AVVideoProfileLevelH264HighAutoLevel,
                AVVideoAllowFrameReorderingKey: false,
            ],
        ])
        videoInput.expectsMediaDataInRealTime = true
        guard writer.canAdd(videoInput) else {
            throw NSError(domain: "truemain-capture", code: 1, userInfo: [NSLocalizedDescriptionKey: "the writer refused the video settings"])
        }
        writer.add(videoInput)

        if options.audio {
            let input = AVAssetWriterInput(mediaType: .audio, outputSettings: [
                AVFormatIDKey: kAudioFormatMPEG4AAC,
                AVSampleRateKey: 48_000,
                AVNumberOfChannelsKey: 2,
                AVEncoderBitRateKey: 160_000,
            ])
            input.expectsMediaDataInRealTime = true
            if writer.canAdd(input) {
                writer.add(input)
                audioInput = input
            } else {
                log("the writer refused the audio settings; recording without sound")
                audioInput = nil
            }
        } else {
            audioInput = nil
        }
        super.init()
    }

    func start(filter: SCContentFilter) async throws {
        let configuration = SCStreamConfiguration()
        configuration.width = options.width
        configuration.height = options.height
        configuration.minimumFrameInterval = CMTime(value: 1, timescale: CMTimeScale(options.fps))
        configuration.pixelFormat = kCVPixelFormatType_420YpCbCr8BiPlanarVideoRange
        configuration.queueDepth = 6
        configuration.showsCursor = true
        if options.audio {
            configuration.capturesAudio = true
            configuration.sampleRate = 48_000
            configuration.channelCount = 2
            configuration.excludesCurrentProcessAudio = true
        }

        let stream = SCStream(filter: filter, configuration: configuration, delegate: self)
        try stream.addStreamOutput(self, type: .screen, sampleHandlerQueue: queue)
        if audioInput != nil {
            try stream.addStreamOutput(self, type: .audio, sampleHandlerQueue: queue)
        }
        guard writer.startWriting() else {
            throw writer.error ?? NSError(domain: "truemain-capture", code: 2)
        }
        self.stream = stream
        try await stream.startCapture()
    }

    func stream(_ stream: SCStream, didOutputSampleBuffer buffer: CMSampleBuffer, of type: SCStreamOutputType) {
        guard !stopping, buffer.isValid else { return }
        switch type {
        case .screen:
            appendVideo(buffer)
        case .audio:
            appendAudio(buffer)
        default:
            break
        }
    }

    private func appendVideo(_ buffer: CMSampleBuffer) {
        // ScreenCaptureKit also sends idle and blank frames; only complete
        // ones carry an image.
        let attachments = CMSampleBufferGetSampleAttachmentsArray(buffer, createIfNecessary: false) as? [[SCStreamFrameInfo: Any]]
        let status = (attachments?.first?[.status] as? Int).flatMap(SCFrameStatus.init(rawValue:))
        statuses[name(of: status), default: 0] += 1
        guard status == .complete else { return }

        let pts = CMSampleBufferGetPresentationTimeStamp(buffer)
        if !sessionStarted {
            writer.startSession(atSourceTime: pts)
            sessionStarted = true
            firstPts = pts
        }
        guard videoInput.isReadyForMoreMediaData else {
            dropped += 1
            return
        }
        if videoInput.append(buffer) {
            if frames == 0 {
                emit("started", ["width": options.width, "height": options.height, "fps": options.fps])
            }
            frames += 1
            lastPts = pts
        } else {
            dropped += 1
        }
    }

    private func appendAudio(_ buffer: CMSampleBuffer) {
        guard sessionStarted, let audioInput, audioInput.isReadyForMoreMediaData else { return }
        // Sound from before the first frame would start the file before video
        // time zero.
        guard CMTimeCompare(CMSampleBufferGetPresentationTimeStamp(buffer), firstPts) >= 0 else { return }
        audioInput.append(buffer)
    }

    func stream(_ stream: SCStream, didStopWithError error: Error) {
        emit("error", ["kind": "capture", "message": error.localizedDescription])
        log("capture stopped: \(error.localizedDescription)")
        Task { await self.stop() }
    }

    /// Stop capturing and close the file. Safe to call more than once and from
    /// anywhere: every caller waits for the same close.
    func stop() async {
        stopLock.lock()
        let task = stopTask ?? Task { await self.close() }
        stopTask = task
        stopLock.unlock()
        await task.value
    }

    private func close() async {
        queue.sync { stopping = true }
        try? await stream?.stopCapture()

        let (sessionStarted, firstPts, lastPts, frames, dropped) = queue.sync {
            (self.sessionStarted, self.firstPts, self.lastPts, self.frames, self.dropped)
        }
        defer { onStopped?() }
        guard sessionStarted, frames > 0 else {
            writer.cancelWriting()
            try? FileManager.default.removeItem(at: options.output)
            emit("stopped", ["durationMs": 0, "frames": 0, "dropped": dropped])
            return
        }
        videoInput.markAsFinished()
        audioInput?.markAsFinished()
        writer.endSession(atSourceTime: lastPts)
        await writer.finishWriting()
        if writer.status == .failed {
            emit("error", ["kind": "writer", "message": writer.error?.localizedDescription ?? "unknown"])
        }
        let duration = CMTimeGetSeconds(CMTimeSubtract(lastPts, firstPts))
        emit("stopped", [
            "durationMs": Int((duration * 1000).rounded()),
            "frames": frames,
            "dropped": dropped,
        ])
    }

    /// Frames, drops and frame statuses so far, read on the sample queue.
    func counters() -> (frames: Int, dropped: Int, statuses: [String: Int]) {
        queue.sync { (frames, dropped, statuses) }
    }
}

private func name(of status: SCFrameStatus?) -> String {
    switch status {
    case .complete: return "complete"
    case .idle: return "idle"
    case .blank: return "blank"
    case .suspended: return "suspended"
    case .started: return "started"
    case .stopped: return "stopped"
    default: return "unknown"
    }
}
