// The helper's only channel back to the shell: one JSON object per line on
// stdout. Everything else (diagnostics) goes to stderr.

import Foundation

private let outputLock = NSLock()

func emit(_ event: String, _ fields: [String: Any] = [:]) {
    var object = fields
    object["event"] = event
    guard let data = try? JSONSerialization.data(withJSONObject: object, options: [.sortedKeys]),
          let line = String(data: data, encoding: .utf8)
    else { return }
    outputLock.lock()
    defer { outputLock.unlock() }
    FileHandle.standardOutput.write((line + "\n").data(using: .utf8)!)
}

func log(_ message: String) {
    FileHandle.standardError.write(("truemain-capture: " + message + "\n").data(using: .utf8)!)
}

/// Exit with an error event the shell can act on. `kind` is machine-readable:
/// `permission`, `no-window`, `usage`, `capture`, `writer`, `clip`, `thumbnail`.
func fail(_ kind: String, _ message: String, code: Int32 = 1) -> Never {
    emit("error", ["kind": kind, "message": message])
    log(message)
    exit(code)
}

/// CPU time this process has used, in seconds — the helper's own cost, which
/// the spike reports. The hardware encoder runs in a system service and is not
/// counted here.
func processCpuSeconds() -> Double {
    var usage = rusage()
    getrusage(RUSAGE_SELF, &usage)
    let user = Double(usage.ru_utime.tv_sec) + Double(usage.ru_utime.tv_usec) / 1_000_000
    let system = Double(usage.ru_stime.tv_sec) + Double(usage.ru_stime.tv_usec) / 1_000_000
    return user + system
}
