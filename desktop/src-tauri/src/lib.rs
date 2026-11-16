//! The TrueMain companion app.
//!
//! The Rust side owns the connection to the League client and derives the
//! app's state from it; the frontend renders that state and never talks to the
//! client itself. That split is what keeps the navigation rule — which screen
//! belongs to which phase — in one place.

mod api;
mod champselect;
mod channels;
mod contract;
mod game;
mod itemsets;
mod kits;
mod loading;
mod menu;
mod overlay;
#[cfg(test)]
mod pinned_settings;
mod record;
mod recording;
mod runes;
#[cfg(debug_assertions)]
mod sim;
mod site;
mod supervisor;
mod telemetry;
mod window_state;

use std::sync::{Arc, Mutex};

use tauri::Manager;

use api::{ApiClient, ReplacedReads, SharedReads};
use game::SharedGame;
use record::{GameCache, SharedClient};
use shell_state::{AppState, AppView};
use supervisor::SharedState;

/// The state the frontend reads on mount.
///
/// Needed because the frontend can finish mounting *after* the supervisor has
/// already published; without this it would sit on a default state until the
/// player's next action changed something.
#[tauri::command]
#[specta::specta]
fn current_state(shared: tauri::State<'_, SharedState>) -> AppView {
    shared.lock().expect("state mutex poisoned").clone().into()
}

/// Open one of the site's pages in the player's browser — the site this build
/// belongs to (`site.rs`), so a preprod build never sends a player to
/// production. The webview names the path only; the host is the shell's.
#[tauri::command]
#[specta::specta]
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

/// Open a known player's channel in the player's browser — Twitch, YouTube or X,
/// nothing else (`channels.rs`).
#[tauri::command]
#[specta::specta]
fn open_channel(app: tauri::AppHandle, url: String) -> Result<(), String> {
    use tauri_plugin_shell::ShellExt;

    let channel = channels::channel_url(&url)
        .ok_or_else(|| format!("{url} is not a channel the app opens"))?;
    // Deprecated opener, as in `open_on_site`: the host rule above is the guard.
    #[allow(deprecated)]
    app.shell()
        .open(channel, None)
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
        .manage(SharedReads::default())
        .manage(ReplacedReads::default())
        .manage(client.clone())
        .manage(champselect::ChampSelectWriter::new(
            &supervisor::Origin::from_env(),
            client.clone(),
        ))
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
        .invoke_handler(contract::builder().invoke_handler())
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
            app.manage(kits::Kits::new(&handle));
            overlay::setup(&handle)?;
            window_state::restore(&handle);
            tauri::async_runtime::spawn(supervisor::run(handle, shared, client));
            Ok(())
        })
        .build(tauri::generate_context!())
        .expect("failed to start the TrueMain companion app")
        .run(|app, event| {
            if let tauri::RunEvent::Exit = event {
                window_state::save(app);
                telemetry::send_at_exit(app);
            }
        });
}
