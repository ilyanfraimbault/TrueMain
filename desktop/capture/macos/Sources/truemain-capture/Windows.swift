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
    // The app, or the game's own render-window title: any window's title
    // could mention League — a browser tab on a build page — and one picked
    // before the game's window exists records that page instead of the game.
    let isLeague = app.contains("league") || bundle.contains("league") || title.contains("(tm) client")
    return isLeague && window.frame.width >= 640 && window.frame.height >= 360
}

/// The game's own render window is titled "League of Legends (TM) Client";
/// the game process also owns untitled windows of the same size that never
/// receive a frame (the first run of the spike picked one). So the title wins,
/// then a window on screen, then the largest.
func findWindow(in content: SCShareableContent, id: UInt32?) -> SCWindow? {
    if let id {
        return content.windows.first { $0.windowID == id }
    }
    func rank(_ window: SCWindow) -> (Int, Int, CGFloat) {
        let titled = (window.title ?? "").lowercased().contains("(tm) client") ? 1 : 0
        return (titled, window.isOnScreen ? 1 : 0, window.frame.width * window.frame.height)
    }
    return content.windows
        .filter(isGameWindow)
        .max { rank($0) < rank($1) }
}

/// What to capture: the game's window alone, or the display it is on with
/// only the game's windows drawn — what game recorders usually capture, and
/// the safer bet when the game is in its own full-screen Space.
enum Source: String {
    case window
    case display
}

/// The capture filter and the size it covers, in points.
func contentFilter(for window: SCWindow, in content: SCShareableContent, source: Source) -> (SCContentFilter, CGSize) {
    if source == .display, let app = window.owningApplication {
        let display = content.displays.first { $0.frame.intersects(window.frame) } ?? content.displays.first
        if let display {
            let filter = SCContentFilter(display: display, including: [app], exceptingWindows: [])
            return (filter, display.frame.size)
        }
    }
    return (SCContentFilter(desktopIndependentWindow: window), window.frame.size)
}

/// A size in points, in pixels — what the encoder works in.
func pixelSize(of size: CGSize, filter: SCContentFilter) -> (Int, Int) {
    var scale: CGFloat = NSScreen.main?.backingScaleFactor ?? 2
    if #available(macOS 14.0, *) {
        scale = CGFloat(SCShareableContent.info(for: filter).pointPixelScale)
    }
    return (Int(size.width * scale), Int(size.height * scale))
}
