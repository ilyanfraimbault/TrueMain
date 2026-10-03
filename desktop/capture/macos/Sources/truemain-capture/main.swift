// truemain-capture — the macOS screen-capture helper for game recording
// (#1744, spike #1745).
//
// A separate process rather than code in the app: a crash in capture or in the
// encoder must not take the app down, and the shell talks to it the same way
// whichever platform's helper it runs. Protocol: arguments in, one JSON object
// per line out on stdout; `stop` on stdin (or SIGINT / SIGTERM) closes the
// file.
//
//   truemain-capture list
//       every window, as `window` events — to find the game's when the guess
//       is wrong
//   truemain-capture probe [--window-id N] [--source window|display]
//       the game window and its size in pixels, as one `window` event
//   truemain-capture record --out FILE --width W --height H --fps F
//                           --bitrate BPS --keyframe-interval FRAMES
//                           [--codec h264|hevc] [--window-id N]
//                           [--source window|display] [--no-audio]
//       records until told to stop; events: `started`, `progress` every five
//       seconds, `stopped`, `error`
//   truemain-capture access [--request] [--open-settings]
//       whether Screen Recording is allowed, as one `access` event
//   truemain-capture clip --in FILE --out FILE --start-ms N --end-ms N
//       the range as its own file, not re-encoded; one `clipped` event
//   truemain-capture thumbnail --in FILE --out FILE.jpg --at-ms N [--width W]
//       one frame as a JPEG; one `thumbnail` event
//
// The output size, bitrate and keyframe interval are computed by the shell
// (`game-recording`'s `Quality::output_for`), so the rule lives in one place.

import AppKit
import CoreGraphics
import Foundation
import ScreenCaptureKit

func value(_ name: String, in arguments: [String]) -> String? {
    guard let index = arguments.firstIndex(of: name), index + 1 < arguments.count else { return nil }
    return arguments[index + 1]
}

func integer(_ name: String, in arguments: [String]) -> Int {
    guard let raw = value(name, in: arguments), let number = Int(raw), number > 0 else {
        fail("usage", "\(name) needs a positive number")
    }
    return number
}

func windowId(in arguments: [String]) -> UInt32? {
    value("--window-id", in: arguments).flatMap { UInt32($0) }
}

func source(in arguments: [String]) -> Source {
    guard let raw = value("--source", in: arguments) else { return .window }
    guard let source = Source(rawValue: raw) else { fail("usage", "--source is window or display") }
    return source
}

func codec(in arguments: [String]) -> Codec {
    guard let raw = value("--codec", in: arguments) else { return .h264 }
    guard let codec = Codec(rawValue: raw) else { fail("usage", "--codec is h264 or hevc") }
    return codec
}

func list() async {
    let content = await shareableContent()
    for window in content.windows {
        emit("window", describe(window).merging(["gameGuess": isGameWindow(window)]) { $1 })
    }
}

func probe(_ arguments: [String]) async {
    let content = await shareableContent()
    guard let window = findWindow(in: content, id: windowId(in: arguments)) else {
        fail("no-window", "no League game window found (run `list` and pass --window-id)", code: 4)
    }
    let captureSource = source(in: arguments)
    let (filter, points) = contentFilter(for: window, in: content, source: captureSource)
    let (width, height) = pixelSize(of: points, filter: filter)
    emit("window", describe(window).merging([
        "width": width, "height": height, "source": captureSource.rawValue,
    ]) { $1 })
}

func record(_ arguments: [String]) async {
    guard let out = value("--out", in: arguments) else { fail("usage", "--out is required") }
    let options = RecordOptions(
        output: URL(fileURLWithPath: out),
        width: integer("--width", in: arguments),
        height: integer("--height", in: arguments),
        fps: integer("--fps", in: arguments),
        codec: codec(in: arguments),
        bitrate: integer("--bitrate", in: arguments),
        keyframeInterval: integer("--keyframe-interval", in: arguments),
        audio: !arguments.contains("--no-audio")
    )

    let content = await shareableContent()
    guard let window = findWindow(in: content, id: windowId(in: arguments)) else {
        fail("no-window", "no League game window found (run `list` and pass --window-id)", code: 4)
    }

    let recorder: Recorder
    do {
        recorder = try Recorder(options: options)
        let (filter, _) = contentFilter(for: window, in: content, source: source(in: arguments))
        try await recorder.start(filter: filter)
    } catch {
        fail("capture", "could not start: \(error.localizedDescription)", code: 5)
    }

    let stopSignal = StopSignal()
    recorder.onStopped = { stopSignal.fire() }

    // `stop`, or the shell closing stdin, ends the recording.
    Thread.detachNewThread {
        while let line = readLine() {
            if line.trimmingCharacters(in: .whitespaces) == "stop" { break }
        }
        stopSignal.fire()
    }

    var signalSources: [DispatchSourceSignal] = []
    for sig in [SIGINT, SIGTERM] {
        signal(sig, SIG_IGN)
        let source = DispatchSource.makeSignalSource(signal: sig, queue: .global())
        source.setEventHandler { stopSignal.fire() }
        source.resume()
        signalSources.append(source)
    }

    let meter = CpuMeter()
    let timer = DispatchSource.makeTimerSource(queue: .global())
    timer.schedule(deadline: .now() + 5, repeating: 5)
    timer.setEventHandler {
        let counters = recorder.counters()
        let sample = meter.sample()
        emit("progress", [
            "elapsedMs": sample.elapsedMs,
            "frames": counters.frames,
            "dropped": counters.dropped,
            "cpuPercent": sample.cpuPercent,
            "statuses": counters.statuses,
        ])
    }
    timer.resume()

    await stopSignal.wait()
    timer.cancel()
    signalSources.forEach { $0.cancel() }
    await recorder.stop()
}

/// Fired once, by whichever of stdin, a signal or the capture itself gets
/// there first; waited on by the main task.
final class StopSignal {
    private let lock = NSLock()
    private var continuation: CheckedContinuation<Void, Never>?
    private var fired = false

    func fire() {
        lock.lock()
        fired = true
        let waiting = continuation
        continuation = nil
        lock.unlock()
        waiting?.resume()
    }

    func wait() async {
        await withCheckedContinuation { (continuation: CheckedContinuation<Void, Never>) in
            lock.lock()
            if fired {
                lock.unlock()
                continuation.resume()
            } else {
                self.continuation = continuation
                lock.unlock()
            }
        }
    }
}

/// The helper's CPU use over each interval it is sampled at.
final class CpuMeter {
    private let lock = NSLock()
    private let started = Date()
    private var lastCpu = processCpuSeconds()
    private var lastTick = Date()

    func sample() -> (elapsedMs: Int, cpuPercent: Double) {
        lock.lock()
        defer { lock.unlock() }
        let now = Date()
        let cpu = processCpuSeconds()
        let interval = max(now.timeIntervalSince(lastTick), 0.001)
        let percent = (cpu - lastCpu) / interval * 100
        lastCpu = cpu
        lastTick = now
        return (Int(now.timeIntervalSince(started) * 1000), (percent * 10).rounded() / 10)
    }
}

// A command-line tool has no window-server connection until something opens
// one, and ScreenCaptureKit asserts it exists (`CGS_REQUIRE_INIT`) as soon as a
// window filter is built. Opening it here, before any command, is what an
// application bundle gets for free. `.prohibited` keeps the helper out of the
// Dock and the app switcher.
NSApplication.shared.setActivationPolicy(.prohibited)
_ = CGMainDisplayID()

let arguments = Array(CommandLine.arguments.dropFirst())
switch arguments.first {
case "list":
    await list()
case "probe":
    await probe(arguments)
case "record":
    await record(arguments)
case "access":
    access(arguments)
case "clip":
    await clip(arguments)
case "thumbnail":
    await thumbnail(arguments)
default:
    fail("usage", "usage: truemain-capture list | probe [--window-id N] [--source window|display] | record --out FILE --width W --height H --fps F --bitrate BPS --keyframe-interval FRAMES [--codec h264|hevc] [--window-id N] [--source window|display] [--no-audio] | access [--request] [--open-settings] | clip --in FILE --out FILE --start-ms N --end-ms N | thumbnail --in FILE --out FILE --at-ms N [--width W]")
}
exit(0)
