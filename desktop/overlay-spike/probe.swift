// Spike #1673: what the window server sees while the overlay sits over a game.
//
// Once a second, prints the frontmost application (the one taking keyboard
// input) and every on-screen window of League and of TrueMain, front to back,
// with its layer and bounds, plus every screen's frame. Run it in a terminal
// before starting the game and read the log afterwards:
//
//   swift desktop/overlay-spike/probe.swift 900 | tee overlay-probe.log
//
// The argument is how many seconds to run (default 600). Needs no permission:
// window owners, layers and bounds are readable without Screen Recording; only
// window titles are not, and none are read.

import AppKit
import CoreGraphics

let duration = Double(CommandLine.arguments.dropFirst().first ?? "") ?? 600
let watched = ["League", "TrueMain", "truemain"]
let start = Date()
var lastLine = ""

func describe(_ rect: CGRect) -> String {
    "\(Int(rect.origin.x)),\(Int(rect.origin.y)) \(Int(rect.width))x\(Int(rect.height))"
}

while Date().timeIntervalSince(start) < duration {
    // Running the main run loop, not sleeping, is what lets NSWorkspace update
    // its frontmost application from the activation notifications.
    RunLoop.main.run(until: Date().addingTimeInterval(1))

    let front = NSWorkspace.shared.frontmostApplication
    let frontName = front?.localizedName ?? "?"
    let windows = (CGWindowListCopyWindowInfo([.optionOnScreenOnly], kCGNullWindowID) as? [[String: Any]]) ?? []

    var rows: [String] = []
    for (order, window) in windows.enumerated() {
        let owner = window[kCGWindowOwnerName as String] as? String ?? "?"
        guard watched.contains(where: { owner.contains($0) }) else { continue }
        let layer = window[kCGWindowLayer as String] as? Int ?? 0
        let alpha = window[kCGWindowAlpha as String] as? Double ?? 1
        var bounds = CGRect.zero
        if let dict = window[kCGWindowBounds as String] as? NSDictionary {
            CGRectMakeWithDictionaryRepresentation(dict, &bounds)
        }
        rows.append("  #\(order) \(owner) layer=\(layer) alpha=\(String(format: "%.2f", alpha)) \(describe(bounds))")
    }
    let screens = NSScreen.screens.map { describe($0.frame) }.joined(separator: " | ")

    let line = "front=\(frontName) screens=[\(screens)] onscreen=\(windows.count)\n" + rows.joined(separator: "\n")
    // Only print when something changed, so a long game stays readable.
    if line != lastLine {
        let stamp = ISO8601DateFormatter().string(from: Date())
        print("[\(stamp)] " + line)
        fflush(stdout)
        lastLine = line
    }
}
