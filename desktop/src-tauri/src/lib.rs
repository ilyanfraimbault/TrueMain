//! The TrueMain companion app.
//!
//! The Rust side owns the connection to the League client and derives the
//! app's state from it; the frontend renders that state and never talks to the
//! client itself. That split is what keeps the navigation rule — which screen
//! belongs to which phase — in one place.

mod api;
mod supervisor;

use std::sync::{Arc, Mutex};

use api::ApiClient;
use shell_state::{AppState, Screen};
use supervisor::SharedState;

/// The state the frontend reads on mount.
///
/// Needed because the frontend can finish mounting *after* the supervisor has
/// already published; without this it would sit on a default state until the
/// player's next action changed something.
#[tauri::command]
fn current_state(shared: tauri::State<'_, SharedState>) -> AppState {
    shared.lock().expect("state mutex poisoned").clone()
}

/// Which screen the current state calls for. Derived in Rust so the rule has
/// exactly one implementation.
#[tauri::command]
fn current_screen(shared: tauri::State<'_, SharedState>) -> Screen {
    shared.lock().expect("state mutex poisoned").screen()
}

/// Ask TrueMain to read one champion-select state.
///
/// Proxied through Rust rather than fetched from the webview: see `api.rs` for
/// why (CORS against an origin the site never expected, and a CSP that would
/// have to be opened to a remote host).
#[tauri::command]
async fn draft_recommendation(
    client: tauri::State<'_, ApiClient>,
    request: serde_json::Value,
) -> Result<serde_json::Value, String> {
    client.post("/champions/draft", &request).await
}

pub fn run() {
    tracing_subscriber::fmt()
        .with_env_filter(
            tracing_subscriber::EnvFilter::try_from_default_env()
                .unwrap_or_else(|_| "truemain_desktop_lib=info,lcu=info".into()),
        )
        .init();

    let shared: SharedState = Arc::new(Mutex::new(AppState::default()));

    tauri::Builder::default()
        .plugin(tauri_plugin_shell::init())
        .manage(shared.clone())
        .manage(ApiClient::new())
        .invoke_handler(tauri::generate_handler![current_state, current_screen, draft_recommendation])
        .setup(move |app| {
            let handle = app.handle().clone();
            tauri::async_runtime::spawn(supervisor::run(handle, shared));
            Ok(())
        })
        .run(tauri::generate_context!())
        .expect("failed to start the TrueMain companion app");
}
