//! Anonymous usage counts (#1805): how many installs run the app, for how
//! long, and what they use — so the admin portal can say whether the app is
//! used at all, and which of its parts.
//!
//! What leaves the machine is a handful of counters under a random id drawn on
//! the first launch (`telemetry.json` in the app's config folder): launches,
//! minutes open, page views and a few features, with the app's version and
//! operating system. Nothing read from the League client — Riot ID, PUUID,
//! rank, games — is ever part of it.
//!
//! The counters are kept here, in the shell, and sent every few minutes and at
//! exit to the site's `POST /api/desktop/telemetry`. A send that fails keeps
//! its counts for the next one. "Share Anonymous Usage Data" in the app menu
//! (macOS) or the tray menu (Windows) turns it off; a debug build never sends
//! unless `TRUEMAIN_TELEMETRY_DEV` asks it to.

use std::collections::BTreeMap;
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};
use std::time::Duration;

use serde::{Deserialize, Serialize};
use tauri::{AppHandle, Manager};

use crate::api::ApiClient;

const SETTINGS_FILE: &str = "telemetry.json";
const PATH: &str = "/desktop/telemetry";
/// The first send comes soon after launch, so a short session still counts.
const FIRST_SEND: Duration = Duration::from_secs(60);
const SEND_EVERY: Duration = Duration::from_secs(5 * 60);
/// How long the exit waits for the last send before letting the app close.
const EXIT_SEND_TIMEOUT: Duration = Duration::from_secs(2);
const DEV_VAR: &str = "TRUEMAIN_TELEMETRY_DEV";

/// What the API accepts in one batch (`DesktopTelemetryRequest`): counts kept
/// across failed sends stop growing there.
const MAX_LAUNCHES: u32 = 20;
const MAX_OPEN_MINUTES: u32 = 1_440;

/// The app's pages, as the API's catalog lists them
/// (`backend/Api/Services/Desktop/DesktopTelemetryCatalog.cs`). The webview
/// names the page; anything else is ignored here.
const PAGES: &[&str] = &[
    "dashboard",
    "champions",
    "champion",
    "tierlist",
    "matchup",
    "truemains",
    "favorites",
    "draft",
    "game",
    "recordings",
    "recording",
    "clip",
];

/// What the app did for the player, beyond showing a page.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Feature {
    /// A champion select the app followed.
    ChampSelect,
    /// A game the app followed live.
    LiveGame,
    /// A game in which the overlay was drawn over League — once per game.
    OverlayShown,
    /// A game the app recorded.
    GameRecorded,
    /// A page of the site opened in the browser.
    SiteOpened,
    /// A pick hovered from the draft screen, on the player's click (#1909).
    ChampSelectHover,
    /// A pick locked in from the draft screen.
    ChampSelectLock,
    /// A ban made from the draft screen.
    ChampSelectBan,
}

impl Feature {
    fn key(self) -> &'static str {
        match self {
            Feature::ChampSelect => "champSelect",
            Feature::LiveGame => "liveGame",
            Feature::OverlayShown => "overlayShown",
            Feature::GameRecorded => "gameRecorded",
            Feature::SiteOpened => "siteOpened",
            Feature::ChampSelectHover => "champSelectHover",
            Feature::ChampSelectLock => "champSelectLock",
            Feature::ChampSelectBan => "champSelectBan",
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
struct Settings {
    install_id: String,
    #[serde(default = "enabled_by_default")]
    enabled: bool,
}

fn enabled_by_default() -> bool {
    true
}

#[derive(Debug, Default, Clone, PartialEq)]
struct Counters {
    launches: u32,
    open_minutes: u32,
    pages: BTreeMap<&'static str, u32>,
    features: BTreeMap<&'static str, u32>,
}

impl Counters {
    fn is_empty(&self) -> bool {
        self.launches == 0
            && self.open_minutes == 0
            && self.pages.is_empty()
            && self.features.is_empty()
    }

    /// Put back the counts of a send that failed, within one batch's bounds.
    fn absorb(&mut self, other: Counters) {
        self.launches = (self.launches + other.launches).min(MAX_LAUNCHES);
        self.open_minutes = (self.open_minutes + other.open_minutes).min(MAX_OPEN_MINUTES);
        for (key, count) in other.pages {
            *self.pages.entry(key).or_default() += count;
        }
        for (key, count) in other.features {
            *self.features.entry(key).or_default() += count;
        }
    }
}

/// One batch, as `POST /desktop/telemetry` reads it.
#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct Batch<'a> {
    install_id: &'a str,
    app_version: &'a str,
    os: &'static str,
    first_launch: bool,
    launches: u32,
    open_minutes: u32,
    page_views: &'a BTreeMap<&'static str, u32>,
    features: &'a BTreeMap<&'static str, u32>,
}

pub struct Telemetry {
    path: PathBuf,
    install_id: String,
    /// True for the whole run that drew the id: the install's first launch.
    first_launch: bool,
    app_version: String,
    enabled: AtomicBool,
    counters: Mutex<Counters>,
    /// Whether the overlay already counted for the game running now.
    overlay_this_game: AtomicBool,
}

pub type SharedTelemetry = Arc<Telemetry>;

impl Telemetry {
    pub fn new(app: &AppHandle) -> SharedTelemetry {
        let path = app
            .path()
            .app_config_dir()
            .unwrap_or_else(|_| std::env::temp_dir().join("truemain"))
            .join(SETTINGS_FILE);
        let (settings, first_launch) = match load(&path) {
            Some(settings) => (settings, false),
            None => {
                let settings = Settings {
                    install_id: uuid::Uuid::new_v4().to_string(),
                    enabled: true,
                };
                if let Err(error) = save(&path, &settings) {
                    tracing::warn!(%error, "could not save the telemetry settings");
                }
                (settings, true)
            }
        };
        Arc::new(Self {
            path,
            install_id: settings.install_id,
            first_launch,
            app_version: app.package_info().version.to_string(),
            enabled: AtomicBool::new(settings.enabled),
            counters: Mutex::new(Counters {
                launches: 1,
                ..Counters::default()
            }),
            overlay_this_game: AtomicBool::new(false),
        })
    }

    pub fn enabled(&self) -> bool {
        self.enabled.load(Ordering::SeqCst)
    }

    /// The menu item's toggle. The choice is kept for the next launches.
    pub fn set_enabled(&self, enabled: bool) {
        self.enabled.store(enabled, Ordering::SeqCst);
        if !enabled {
            *self.counters.lock().expect("telemetry poisoned") = Counters::default();
        }
        let settings = Settings {
            install_id: self.install_id.clone(),
            enabled,
        };
        if let Err(error) = save(&self.path, &settings) {
            tracing::warn!(%error, "could not save the telemetry settings");
        }
    }

    pub fn page(&self, page: &str) {
        let Some(key) = PAGES.iter().find(|known| **known == page) else {
            return;
        };
        self.count(|counters| *counters.pages.entry(key).or_default() += 1);
    }

    pub fn feature(&self, feature: Feature) {
        match feature {
            // A new game: the overlay counts again in it.
            Feature::LiveGame => self.overlay_this_game.store(false, Ordering::SeqCst),
            Feature::OverlayShown => {
                if self.overlay_this_game.swap(true, Ordering::SeqCst) {
                    return;
                }
            }
            _ => {}
        }
        self.count(|counters| *counters.features.entry(feature.key()).or_default() += 1);
    }

    fn minute(&self) {
        self.count(|counters| {
            counters.open_minutes = (counters.open_minutes + 1).min(MAX_OPEN_MINUTES);
        });
    }

    fn count(&self, change: impl FnOnce(&mut Counters)) {
        if self.enabled() {
            change(&mut self.counters.lock().expect("telemetry poisoned"));
        }
    }

    /// Send what was counted since the last send. A failure puts the counts
    /// back for the next one; an opted-out or debug app sends nothing.
    async fn send(&self, client: &ApiClient) {
        if !self.enabled() || !sends_from_this_build() {
            return;
        }
        let counters = std::mem::take(&mut *self.counters.lock().expect("telemetry poisoned"));
        if counters.is_empty() {
            return;
        }
        let batch = Batch {
            install_id: &self.install_id,
            app_version: &self.app_version,
            os: os(),
            first_launch: self.first_launch,
            launches: counters.launches,
            open_minutes: counters.open_minutes,
            page_views: &counters.pages,
            features: &counters.features,
        };
        if let Err(error) = client.send(PATH, &batch).await {
            tracing::debug!(%error, "usage counts not sent, kept for the next send");
            self.counters
                .lock()
                .expect("telemetry poisoned")
                .absorb(counters);
        }
    }
}

/// A debug build is a developer's: its counts would land in whichever site it
/// points at, production included.
fn sends_from_this_build() -> bool {
    !cfg!(debug_assertions) || std::env::var_os(DEV_VAR).is_some()
}

/// The API's names for the operating systems.
fn os() -> &'static str {
    match std::env::consts::OS {
        "macos" => "macos",
        "windows" => "windows",
        _ => "linux",
    }
}

fn load(path: &Path) -> Option<Settings> {
    let text = std::fs::read_to_string(path).ok()?;
    let settings: Settings = serde_json::from_str(&text).ok()?;
    uuid::Uuid::parse_str(&settings.install_id).ok()?;
    Some(settings)
}

fn save(path: &Path, settings: &Settings) -> std::io::Result<()> {
    if let Some(dir) = path.parent() {
        std::fs::create_dir_all(dir)?;
    }
    let text = serde_json::to_string_pretty(settings).map_err(std::io::Error::other)?;
    std::fs::write(path, text)
}

/// Count the minutes the app is open and send on a timer, for the app's life.
pub fn start(app: &AppHandle, telemetry: SharedTelemetry) {
    let client = app.state::<ApiClient>().inner().clone();
    tauri::async_runtime::spawn(async move {
        let mut minutes = tokio::time::interval(Duration::from_secs(60));
        minutes.tick().await;
        let mut sends =
            tokio::time::interval_at(tokio::time::Instant::now() + FIRST_SEND, SEND_EVERY);
        loop {
            tokio::select! {
                _ = minutes.tick() => telemetry.minute(),
                _ = sends.tick() => telemetry.send(&client).await,
            }
        }
    });
}

/// The last send, as the app exits — bounded, so a slow network never holds
/// the app open.
pub fn send_at_exit(app: &AppHandle) {
    let (Some(telemetry), Some(client)) = (
        app.try_state::<SharedTelemetry>(),
        app.try_state::<ApiClient>(),
    ) else {
        return;
    };
    let (telemetry, client) = (telemetry.inner().clone(), client.inner().clone());
    tauri::async_runtime::block_on(async move {
        let _ = tokio::time::timeout(EXIT_SEND_TIMEOUT, telemetry.send(&client)).await;
    });
}

/// Count a page the webview opened. The webview names it by its key.
#[tauri::command]
pub fn telemetry_page(telemetry: tauri::State<'_, SharedTelemetry>, page: String) {
    telemetry.page(&page);
}

/// Count a feature from anywhere in the shell.
pub fn feature(app: &AppHandle, feature: Feature) {
    if let Some(telemetry) = app.try_state::<SharedTelemetry>() {
        telemetry.feature(feature);
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn telemetry(enabled: bool) -> Telemetry {
        Telemetry {
            path: std::env::temp_dir().join("truemain-telemetry-test.json"),
            install_id: "3f2b8c1e-5a4d-4e8f-9b7a-1c2d3e4f5a6b".into(),
            first_launch: false,
            app_version: "0.3.1".into(),
            enabled: AtomicBool::new(enabled),
            counters: Mutex::new(Counters::default()),
            overlay_this_game: AtomicBool::new(false),
        }
    }

    #[test]
    fn counts_listed_pages_only() {
        let telemetry = telemetry(true);
        telemetry.page("draft");
        telemetry.page("draft");
        telemetry.page("admin");
        let counters = telemetry.counters.lock().unwrap().clone();
        assert_eq!(counters.pages.get("draft"), Some(&2));
        assert_eq!(counters.pages.len(), 1);
    }

    #[test]
    fn counts_the_overlay_once_per_game() {
        let telemetry = telemetry(true);
        telemetry.feature(Feature::LiveGame);
        telemetry.feature(Feature::OverlayShown);
        telemetry.feature(Feature::OverlayShown);
        telemetry.feature(Feature::LiveGame);
        telemetry.feature(Feature::OverlayShown);
        let counters = telemetry.counters.lock().unwrap().clone();
        assert_eq!(counters.features.get("overlayShown"), Some(&2));
        assert_eq!(counters.features.get("liveGame"), Some(&2));
    }

    #[test]
    fn counts_nothing_once_turned_off() {
        let telemetry = telemetry(false);
        telemetry.page("draft");
        telemetry.feature(Feature::ChampSelect);
        telemetry.minute();
        assert!(telemetry.counters.lock().unwrap().is_empty());
    }

    #[test]
    fn a_failed_send_gives_its_counts_back_within_a_batch() {
        let mut kept = Counters {
            launches: 15,
            open_minutes: 1_400,
            ..Counters::default()
        };
        let mut failed = Counters {
            launches: 10,
            open_minutes: 100,
            ..Counters::default()
        };
        failed.pages.insert("game", 3);
        kept.absorb(failed);
        assert_eq!(kept.launches, MAX_LAUNCHES);
        assert_eq!(kept.open_minutes, MAX_OPEN_MINUTES);
        assert_eq!(kept.pages.get("game"), Some(&3));
    }

    #[test]
    fn the_batch_reads_as_the_api_expects() {
        let mut pages = BTreeMap::new();
        pages.insert("draft", 2);
        let features = BTreeMap::new();
        let batch = Batch {
            install_id: "3f2b8c1e-5a4d-4e8f-9b7a-1c2d3e4f5a6b",
            app_version: "0.3.1",
            os: "macos",
            first_launch: true,
            launches: 1,
            open_minutes: 5,
            page_views: &pages,
            features: &features,
        };
        let json = serde_json::to_value(&batch).unwrap();
        assert_eq!(json["installId"], "3f2b8c1e-5a4d-4e8f-9b7a-1c2d3e4f5a6b");
        assert_eq!(json["appVersion"], "0.3.1");
        assert_eq!(json["firstLaunch"], true);
        assert_eq!(json["openMinutes"], 5);
        assert_eq!(json["pageViews"]["draft"], 2);
    }

    #[test]
    fn keeps_a_valid_id_and_the_choice_across_launches() {
        let dir = std::env::temp_dir().join(format!("truemain-telemetry-{}", uuid::Uuid::new_v4()));
        let path = dir.join(SETTINGS_FILE);
        assert!(load(&path).is_none());
        let settings = Settings {
            install_id: uuid::Uuid::new_v4().to_string(),
            enabled: false,
        };
        save(&path, &settings).unwrap();
        let loaded = load(&path).unwrap();
        assert_eq!(loaded.install_id, settings.install_id);
        assert!(!loaded.enabled);
        std::fs::write(&path, r#"{"installId":"not-a-uuid"}"#).unwrap();
        assert!(load(&path).is_none(), "a damaged file draws a new id");
        let _ = std::fs::remove_dir_all(dir);
    }
}
