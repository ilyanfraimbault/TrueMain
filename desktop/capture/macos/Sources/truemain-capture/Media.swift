// What the app does with a recording once it is written, and the Screen
// Recording permission it needs before writing one (#1744, #1777).
//
// Cutting a clip and taking a thumbnail go through AVFoundation, the same
// media stack that wrote the file: a clip is a passthrough export — no
// re-encoding, so it costs no CPU and keeps the game's quality — and the
// recorder's one-second keyframes keep its cut within a second of the range.

import AppKit
import AVFoundation
import CoreGraphics
import Foundation
import ImageIO
import UniformTypeIdentifiers

/// A number of milliseconds, zero allowed.
func milliseconds(_ name: String, in arguments: [String]) -> Int64 {
    guard let raw = value(name, in: arguments), let number = Int64(raw), number >= 0 else {
        fail("usage", "\(name) needs a number of milliseconds")
    }
    return number
}

func time(_ ms: Int64) -> CMTime {
    CMTime(value: ms, timescale: 1000)
}

/// `access` reports whether Screen Recording is allowed, without asking.
/// `--request` asks: macOS shows its prompt once per app; `--open-settings`
/// opens the pane where a refusal is undone.
func access(_ arguments: [String]) {
    var granted = CGPreflightScreenCaptureAccess()
    if !granted && arguments.contains("--request") {
        granted = CGRequestScreenCaptureAccess()
    }
    if !granted && arguments.contains("--open-settings"),
       let pane = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture") {
        NSWorkspace.shared.open(pane)
    }
    emit("access", ["granted": granted])
}

/// `clip --in FILE --out FILE --start-ms N --end-ms N`: the range as its own
/// file, copied rather than re-encoded.
func clip(_ arguments: [String]) async {
    guard let input = value("--in", in: arguments), let out = value("--out", in: arguments) else {
        fail("usage", "--in and --out are required")
    }
    let start = milliseconds("--start-ms", in: arguments)
    let end = milliseconds("--end-ms", in: arguments)
    guard end > start else { fail("usage", "--end-ms must be after --start-ms") }

    let asset = AVURLAsset(url: URL(fileURLWithPath: input))
    guard let session = AVAssetExportSession(asset: asset, presetName: AVAssetExportPresetPassthrough) else {
        fail("clip", "this video cannot be exported")
    }
    let output = URL(fileURLWithPath: out)
    try? FileManager.default.removeItem(at: output)
    session.outputURL = output
    session.outputFileType = .mp4
    session.shouldOptimizeForNetworkUse = true
    session.timeRange = CMTimeRange(start: time(start), end: time(end))
    await session.export()
    guard session.status == .completed else {
        try? FileManager.default.removeItem(at: output)
        fail("clip", "could not cut the clip: \(session.error?.localizedDescription ?? "export \(session.status.rawValue)")")
    }
    let duration = (try? await AVURLAsset(url: output).load(.duration)) ?? .zero
    emit("clipped", ["durationMs": Int(CMTimeGetSeconds(duration) * 1000)])
}

/// `thumbnail --in FILE --out FILE.jpg --at-ms N [--width W]`: one frame as a
/// JPEG, for the Recordings page's cards.
func thumbnail(_ arguments: [String]) async {
    guard let input = value("--in", in: arguments), let out = value("--out", in: arguments) else {
        fail("usage", "--in and --out are required")
    }
    let at = milliseconds("--at-ms", in: arguments)
    let width = value("--width", in: arguments).flatMap { Int($0) } ?? 640

    let generator = AVAssetImageGenerator(asset: AVURLAsset(url: URL(fileURLWithPath: input)))
    generator.appliesPreferredTrackTransform = true
    generator.maximumSize = CGSize(width: width, height: 0)
    // The nearest keyframe is close enough for a card and much faster.
    generator.requestedTimeToleranceBefore = time(1000)
    generator.requestedTimeToleranceAfter = time(1000)

    let image: CGImage
    do {
        image = try await generator.image(at: time(at)).image
    } catch {
        fail("thumbnail", "could not read a frame: \(error.localizedDescription)")
    }
    let output = URL(fileURLWithPath: out)
    guard let destination = CGImageDestinationCreateWithURL(output as CFURL, UTType.jpeg.identifier as CFString, 1, nil) else {
        fail("thumbnail", "could not write \(out)")
    }
    CGImageDestinationAddImage(destination, image, [kCGImageDestinationLossyCompressionQuality: 0.8] as CFDictionary)
    guard CGImageDestinationFinalize(destination) else { fail("thumbnail", "could not write \(out)") }
    emit("thumbnail", ["width": image.width, "height": image.height])
}
