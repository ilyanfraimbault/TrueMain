//! The TrueMain companion app.
//!
//! The Rust side owns the connection to the League client and derives the
//! app's state from it; the frontend renders that state and never talks to the
//! client itself. That split is what keeps the navigation rule — which screen
//! belongs to which phase — in one place.

mod api;
mod game;
mod loading;
mod menu;
mod overlay;
mod record;
mod recording;
mod runes;
#[cfg(debug_assertions)]
mod sim;
mod site;
mod supervisor;
mod telemetry;

use std::sync::{Arc, Mutex};

use tauri::Manager;

use api::ApiClient;
use game::SharedGame;
use record::{GameCache, SharedClient};
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
        client
            .post_within(&path, &request, COMPOSITION_TIMEOUT)
            .await
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
    client
        .get(&format!("/champions/{champion_id}"), &query)
        .await
}

/// The reads the pages outside the draft make — the tier list, the champion
/// directory, the true mains leaderboard and its search. Listed rather than
/// open: the shell forwards these and nothing else, so the webview cannot turn
/// it into a general proxy onto the API.
const READABLE_PATHS: &[&str] = &[
    "/champions/tierlist",
    "/champions/directory",
    "/truemains",
    "/truemains/search",
];

/// A path the app may read: one of `READABLE_PATHS`, or one true main's
/// reads under `/truemains/{nameTag}` — their build on a champion
/// (`/champions/{championId}`), their profile, their match list and one of
/// their matches (`/matches/{matchId}`), which the favorites page shows. The
/// Riot ID is percent-encoded into one segment, the champion a number and the
/// match a Riot match id (`EUW1_7123456789`).
fn readable(path: &str) -> bool {
    if READABLE_PATHS.contains(&path) {
        return true;
    }
    if let Some(rest) = path.strip_prefix("/champions/") {
        let segments: Vec<&str> = rest.split('/').collect();
        // A lane's matchup, for the Game page's lane chance (#1863).
        return matches!(
            segments.as_slice(),
            [champion_id, "item-context" | "matchups"] if is_id(champion_id)
        );
    }
    let Some(rest) = path.strip_prefix("/truemains/") else {
        return false;
    };
    let segments: Vec<&str> = rest.split('/').collect();
    let Some((name_tag, tail)) = segments.split_first() else {
        return false;
    };
    let name_tag_ok = !name_tag.is_empty()
        && *name_tag != "."
        && *name_tag != ".."
        && name_tag
            .bytes()
            .all(|b| b.is_ascii_alphanumeric() || b"-_.~%".contains(&b));
    let tail_ok = match tail {
        ["profile"] | ["matches"] => true,
        ["champions", champion_id] => {
            !champion_id.is_empty() && champion_id.bytes().all(|b| b.is_ascii_digit())
        }
        ["matches", match_id] => {
            !match_id.is_empty()
                && match_id
                    .bytes()
                    .all(|b| b.is_ascii_alphanumeric() || b == b'_')
        }
        _ => false,
    };
    name_tag_ok && tail_ok
}

/// A champion id: a non-empty run of digits.
fn is_id(segment: &str) -> bool {
    !segment.is_empty() && segment.bytes().all(|b| b.is_ascii_digit())
}

/// The POSTs the app makes: a champion's composition build against a draft and
/// the games behind it (the shared matchup page, #1732), and the next item to
/// complete in a live game (#1751). Reads that take a game state as a body,
/// listed like `READABLE_PATHS`.
fn postable(path: &str) -> bool {
    let Some(rest) = path.strip_prefix("/champions/") else {
        return false;
    };
    let segments: Vec<&str> = rest.split('/').collect();
    match segments.as_slice() {
        [champion_id, "composition-build"]
        | [champion_id, "composition-build", "games"]
        | [champion_id, "next-item"] => is_id(champion_id),
        _ => false,
    }
}

/// One POST read of a `postable` path. The composition build is the slowest
/// query the API runs, hence its deadline rather than the client-wide one.
#[tauri::command]
async fn api_post(
    client: tauri::State<'_, ApiClient>,
    path: String,
    query: Vec<(String, String)>,
    request: serde_json::Value,
) -> Result<serde_json::Value, String> {
    if !postable(&path) {
        return Err(format!("{path} is not readable from the app"));
    }
    client
        .post_query_within(&path, &query, &request, COMPOSITION_TIMEOUT)
        .await
}

/// The tier list is computed on the first read after the API starts — about
/// ten seconds, past the client-wide deadline (#1783). Abandoning it there
/// left the draft's pick panel empty for the whole champion select.
const TIER_LIST_TIMEOUT: std::time::Duration = std::time::Duration::from_secs(30);

/// One read-only GET of a `readable` path, with its query as key/value pairs.
#[tauri::command]
async fn api_get(
    client: tauri::State<'_, ApiClient>,
    path: String,
    query: Vec<(String, String)>,
) -> Result<serde_json::Value, String> {
    if !readable(&path) {
        return Err(format!("{path} is not readable from the app"));
    }
    if path == "/champions/tierlist" {
        return client.get_within(&path, &query, TIER_LIST_TIMEOUT).await;
    }
    client.get(&path, &query).await
}

/// Open one of the site's pages in the player's browser — the site this build
/// belongs to (`site.rs`), so a preprod build never sends a player to
/// production. The webview names the path only; the host is the shell's.
#[tauri::command]
fn open_on_site(app: tauri::AppHandle, path: String) -> Result<(), String> {
    use tauri_plugin_shell::ShellExt;

    let url = site::page(&site::base(), &path)
        .ok_or_else(|| format!("{path} is not a page of the site"))?;
    telemetry::feature(&app, telemetry::Feature::SiteOpened);
    // The shell plugin's opener is deprecated in favour of `tauri-plugin-opener`,
    // which this one call does not justify adding; it has no scope to check
    // from Rust, so the path rule above is the whole guard.
    #[allow(deprecated)]
    app.shell()
        .open(url, None)
        .map_err(|error| error.to_string())
}

pub fn run() {
    tracing_subscriber::fmt()
        .with_env_filter(
            tracing_subscriber::EnvFilter::try_from_default_env()
                .unwrap_or_else(|_| "truemain_desktop_lib=info,lcu=info".into()),
        )
        .init();

    let shared: SharedState = Arc::new(Mutex::new(AppState::default()));
    let client = SharedClient::default();

    let builder = tauri::Builder::default();
    #[cfg(target_os = "macos")]
    let builder = builder.plugin(tauri_nspanel::init());

    builder
        .plugin(tauri_plugin_shell::init())
        .plugin(tauri_plugin_updater::Builder::new().build())
        .plugin(tauri_plugin_process::init())
        .manage(shared.clone())
        .manage(ApiClient::new())
        .manage(BuildInFlight::default())
        .manage(client.clone())
        .manage(GameCache::default())
        .manage(SharedGame::default())
        .manage(loading::SharedLoading::default())
        .register_asynchronous_uri_scheme_protocol(
            recording::files::SCHEME,
            |context, request, responder| {
                // The recorder is managed in `setup`; a request before then
                // gets an answer, not a panic.
                let Some(recorder) = context
                    .app_handle()
                    .try_state::<recording::SharedRecorder>()
                else {
                    responder.respond(recording::files::unavailable());
                    return;
                };
                let folder = recorder.folder();
                tauri::async_runtime::spawn_blocking(move || {
                    responder.respond(recording::files::serve(&request, &folder));
                });
            },
        )
        .invoke_handler(tauri::generate_handler![
            current_state,
            current_screen,
            game::current_game,
            draft_recommendation,
            champion_build,
            composition_build,
            api_get,
            api_post,
            open_on_site,
            record::player_record,
            record::player_history,
            record::player_game,
            recording::recording_settings,
            recording::set_recording_settings,
            recording::recording_status,
            recording::request_capture_permission,
            recording::recording_library,
            recording::recording_get,
            recording::recording_set_kept,
            recording::recording_delete,
            recording::clip_save,
            recording::clip_update,
            recording::clip_delete,
            recording::reveal_recording_file,
            overlay::overlay_view,
            overlay::set_overlay_settings,
            overlay::overlay_preview,
            overlay::overlay_fit,
            loading::loading_players,
            runes::import_runes,
            telemetry::telemetry_page
        ])
        // The overlay's panel is a window too: without this, closing the
        // app's window would leave the app running with no window to show.
        .on_window_event(|window, event| {
            if window.label() == "main" && matches!(event, tauri::WindowEvent::Destroyed) {
                window.app_handle().exit(0);
            }
        })
        .setup(move |app| {
            let handle = app.handle().clone();
            // Before the menu, which shows whether it is on.
            let usage = telemetry::Telemetry::new(&handle);
            app.manage(usage.clone());
            telemetry::start(&handle, usage);
            menu::install(app)?;
            let (recorder, phases) = recording::Recorder::new(&handle);
            app.manage(recorder.clone());
            recording::start(&handle, recorder, phases);
            overlay::setup(&handle)?;
            tauri::async_runtime::spawn(supervisor::run(handle, shared, client));
            Ok(())
        })
        .build(tauri::generate_context!())
        .expect("failed to start the TrueMain companion app")
        .run(|app, event| {
            if let tauri::RunEvent::Exit = event {
                telemetry::send_at_exit(app);
            }
        });
}

#[cfg(test)]
mod tests {
    use super::{postable, readable};

    #[test]
    fn reads_the_listed_paths_and_a_true_mains_build() {
        assert!(readable("/truemains"));
        assert!(readable("/champions/tierlist"));
        assert!(readable("/champions/directory"));
        assert!(readable("/truemains/ttv%20ronaldoo-back/champions/8"));
        assert!(readable("/truemains/Faker-KR1/champions/7"));
        assert!(readable("/truemains/Faker-KR1/profile"));
        assert!(readable("/truemains/Faker-KR1/matches"));
        assert!(readable("/truemains/Faker-KR1/matches/KR_7123456789"));
        assert!(readable("/champions/103/item-context"));
        assert!(readable("/champions/234/matchups"));
    }

    #[test]
    fn posts_only_the_composition_build_and_its_games() {
        assert!(postable("/champions/103/composition-build"));
        assert!(postable("/champions/103/composition-build/games"));
        assert!(postable("/champions/103/next-item"));
        assert!(!postable("/champions/x/next-item"));
        assert!(!postable("/champions/draft"));
        assert!(!postable("/champions/x/composition-build"));
        assert!(!postable("/champions//composition-build"));
        assert!(!postable("/champions/103/composition-build/games/1"));
        assert!(!postable("/truemains/Faker-KR1/profile"));
    }

    #[test]
    fn refuses_anything_else() {
        assert!(!readable("/champions/8"));
        assert!(!readable("/champions/x/item-context"));
        assert!(!readable("/champions/x/matchups"));
        assert!(!readable("/champions/234/matchups/1"));
        assert!(!readable("/champions/8/item-context/1"));
        assert!(!readable("/truemains/Faker-KR1/activity"));
        assert!(!readable("/truemains/Faker-KR1/matches/KR_1/timeline"));
        assert!(!readable("/truemains/Faker-KR1/matches/KR-1"));
        assert!(!readable("/truemains/Faker-KR1/matches/"));
        assert!(!readable("/truemains/Faker-KR1/champions/7/matchups"));
        assert!(!readable("/truemains/../champions/7"));
        assert!(!readable("/truemains/a/b/champions/7"));
        assert!(!readable("/truemains/Faker?x=1/champions/7"));
        assert!(!readable("/truemains/Faker-KR1/champions/7x"));
        assert!(!readable("/truemains//champions/7"));
    }
}
