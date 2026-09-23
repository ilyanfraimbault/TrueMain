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

/// The composition build request currently in flight, if any.
#[derive(Default)]
struct BuildInFlight(Mutex<Option<tokio::task::AbortHandle>>);

/// The build for one champion against the draft as it stands — both teams'
/// locked picks, each on its lane — from the endpoint behind the site's
/// matchup page.
///
/// Champion select moves faster than the API answers: a lock landing while a
/// request is out makes that request's answer obsolete. So each call aborts the
/// one before it rather than queueing behind it, and the superseded call
/// returns an error the frontend recognises and drops.
#[tauri::command]
async fn composition_build(
    client: tauri::State<'_, ApiClient>,
    in_flight: tauri::State<'_, BuildInFlight>,
    champion_id: i64,
    request: serde_json::Value,
) -> Result<serde_json::Value, String> {
    let client = client.inner().clone();
    let path = format!("/champions/{champion_id}/composition-build");
    let task = tokio::spawn(async move {
        client.post_within(&path, &request, COMPOSITION_TIMEOUT).await
    });

    let previous = in_flight
        .0
        .lock()
        .expect("in-flight mutex poisoned")
        .replace(task.abort_handle());
    if let Some(previous) = previous {
        previous.abort();
    }

    match task.await {
        Ok(answer) => answer,
        Err(error) if error.is_cancelled() => Err(SUPERSEDED.to_string()),
        Err(error) => Err(format!("the build request failed: {error}")),
    }
}

/// A full ten-pick composition is the slowest query the API runs, and an
/// uncached one can take well past the client-wide eight seconds. Failing it
/// would drop the very drafts that matter most; a newer lock aborts a slow
/// request anyway, so a long deadline never leaves the panel waiting on a
/// draft that no longer exists.
const COMPOSITION_TIMEOUT: std::time::Duration = std::time::Duration::from_secs(30);

/// What a request replaced by a newer one answers. Matched by the frontend.
const SUPERSEDED: &str = "superseded";

/// The build, runes and summoner spells for one champion on one lane —
/// narrowed to the lane opponent when there is one.
///
/// Answered by the same endpoint as the site's champion page, so the desktop
/// shows the build the site shows.
#[tauri::command]
async fn champion_build(
    client: tauri::State<'_, ApiClient>,
    champion_id: i64,
    position: String,
    opponent_champion_id: Option<i64>,
) -> Result<serde_json::Value, String> {
    let mut query = vec![("position", position)];
    if let Some(opponent) = opponent_champion_id {
        query.push(("opponentChampionId", opponent.to_string()));
    }
    client.get(&format!("/champions/{champion_id}"), &query).await
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
        .manage(BuildInFlight::default())
        .invoke_handler(tauri::generate_handler![
            current_state,
            current_screen,
            draft_recommendation,
            champion_build,
            composition_build
        ])
        .setup(move |app| {
            let handle = app.handle().clone();
            tauri::async_runtime::spawn(supervisor::run(handle, shared));
            Ok(())
        })
        .run(tauri::generate_context!())
        .expect("failed to start the TrueMain companion app");
}
