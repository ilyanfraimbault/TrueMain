// Finding the game's window. Which process and title the macOS game uses is
// one of the things the spike establishes, so the guess is loose — any window
// of an app whose name or bundle id says League, the launcher and the client's
// own UI excluded, the largest first — and `list` prints every window so a
// wrong guess can be corrected with `--window-id`.

import AppKit
import CoreGraphics
import Foundation
import ScreenCaptureKit

private let excludedApps = ["riot client", "leagueclientux", "leagueclient"]

func shareableContent() async -> SCShareableContent {
    if !CGPreflightScreenCaptureAccess() {
        // Shows the system prompt the first time; afterwards the player has to
        // allow it in System Settings and restart the helper.
        CGRequestScreenCaptureAccess()
        fail("permission", "Screen Recording is not allowed for this app (System Settings → Privacy & Security → Screen Recording)", code: 3)
    }
    do {
        return try await SCShareableContent.excludingDesktopWindows(true, onScreenWindowsOnly: false)
    } catch {
        fail("permission", "could not list windows: \(error.localizedDescription)", code: 3)
    }
}

func describe(_ window: SCWindow) -> [String: Any] {
    [
        "windowId": Int(window.windowID),
        "title": window.title ?? "",
        "app": window.owningApplication?.applicationName ?? "",
        "bundleId": window.owningApplication?.bundleIdentifier ?? "",
        "pid": Int(window.owningApplication?.processID ?? 0),
        "onScreen": window.isOnScreen,
        "widthPoints": Int(window.frame.width),
        "heightPoints": Int(window.frame.height),
    ]
}

func isGameWindow(_ window: SCWindow) -> Bool {
    let app = (window.owningApplication?.applicationName ?? "").lowercased()
    let bundle = (window.owningApplication?.bundleIdentifier ?? "").lowercased()
    let title = (window.title ?? "").lowercased()
    if excludedApps.contains(where: { app.contains($0) || bundle.contains($0.replacingOccurrences(of: " ", with: "")) }) {
        return false
    }
    let mentionsLeague = [app, bundle, title].contains { $0.contains("league") }
    return mentionsLeague && window.frame.width >= 640 && window.frame.height >= 360
}

func findWindow(in content: SCShareableContent, id: UInt32?) -> SCWindow? {
    if let id {
        return content.windows.first { $0.windowID == id }
    }
    return content.windows
        .filter(isGameWindow)
        .max { $0.frame.width * $0.frame.height < $1.frame.width * $1.frame.height }
}

/// The window's size in pixels, which is what the encoder works in.
func pixelSize(of window: SCWindow, filter: SCContentFilter) -> (Int, Int) {
    var scale: CGFloat = NSScreen.main?.backingScaleFactor ?? 2
    if #available(macOS 14.0, *) {
        scale = CGFloat(SCShareableContent.info(for: filter).pointPixelScale)
    }
    return (Int(window.frame.width * scale), Int(window.frame.height * scale))
}
