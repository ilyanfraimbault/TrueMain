//! The helper's only channel back to the shell: one JSON object per line on
//! stdout. Everything else (diagnostics) goes to stderr.

use std::io::Write;
use std::sync::Mutex;

use serde_json::{Map, Value};

static OUTPUT: Mutex<()> = Mutex::new(());

/// One event. `fields` is a JSON object; `event` is added to it.
pub fn emit(event: &str, fields: Value) {
    let mut object = match fields {
        Value::Object(map) => map,
        _ => Map::new(),
    };
    object.insert("event".into(), Value::from(event));
    let line = Value::Object(object).to_string();
    let _guard = OUTPUT
        .lock()
        .unwrap_or_else(|poisoned| poisoned.into_inner());
    let mut stdout = std::io::stdout().lock();
    let _ = writeln!(stdout, "{line}").and_then(|()| stdout.flush());
}

pub fn log(message: &str) {
    eprintln!("truemain-capture: {message}");
}

/// Exit with an error event the shell can act on. `kind` is machine-readable,
/// the macOS helper's: `permission`, `no-window`, `usage`, `capture`,
/// `writer`, `clip`, `thumbnail`.
pub fn fail(kind: &str, message: &str, code: i32) -> ! {
    emit(
        "error",
        serde_json::json!({ "kind": kind, "message": message }),
    );
    log(message);
    std::process::exit(code)
}
