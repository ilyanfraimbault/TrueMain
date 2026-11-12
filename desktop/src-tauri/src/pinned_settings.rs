//! Values the shell's config files and the site's code hold in their own
//! formats — a JSON config cannot import a Rust constant — checked here
//! against the one place each belongs to, so a change on one side fails until
//! the other follows.

use std::collections::BTreeSet;

const CONF: &str = include_str!("../tauri.conf.json");
const IPX: &str = include_str!("../../../web/shared/utils/ipx.ts");
const PLAYER_LINKS: &str =
    include_str!("../../../web/layers/common/app/components/players/PlayerLinks.vue");
const NUXT_CONFIG: &str = include_str!("../../app/nuxt.config.ts");
const OVERLAY_SMOKE: &str = include_str!("../../tools/overlay-smoke-windows.ps1");

fn conf() -> serde_json::Value {
    serde_json::from_str(CONF).expect("tauri.conf.json parses")
}

/// The sources of one CSP directive.
fn csp(directive: &str) -> Vec<String> {
    let conf = conf();
    let policy = conf["app"]["security"]["csp"].as_str().expect("a CSP");
    policy
        .split(';')
        .map(str::trim)
        .find_map(|part| part.strip_prefix(directive)?.strip_prefix(' '))
        .unwrap_or_default()
        .split_whitespace()
        .map(str::to_string)
        .collect()
}

/// The quoted strings of the TS declaration that starts at `marker`, up to its
/// closing bracket.
fn quoted_after(source: &str, marker: &str, close: char) -> Vec<String> {
    let start = source
        .find(marker)
        .unwrap_or_else(|| panic!("{marker} not found"));
    let body = &source[start..];
    let body = &body[..body.find(close).expect("a closing bracket")];
    body.split('\'')
        .skip(1)
        .step_by(2)
        .map(str::to_string)
        .collect()
}

#[test]
fn the_window_minimum_is_the_one_the_shell_places_by() {
    let window = &conf()["app"]["windows"][0];
    let (width, height) = shell_state::window::MIN_SIZE;
    assert_eq!(window["minWidth"].as_f64(), Some(width));
    assert_eq!(window["minHeight"].as_f64(), Some(height));
}

#[test]
fn the_csp_lets_the_recording_scheme_through() {
    let scheme = crate::recording::files::SCHEME;
    for directive in ["img-src", "media-src"] {
        let sources = csp(directive);
        assert!(sources.contains(&format!("{scheme}:")), "{directive}");
        assert!(
            sources.contains(&format!("http://{scheme}.localhost")),
            "{directive}"
        );
    }
}

#[test]
fn the_csp_shows_the_pictures_the_site_serves() {
    let ipx: BTreeSet<String> = quoted_after(IPX, "IPX_SOURCE_DOMAINS", ']')
        .into_iter()
        .collect();
    let csp: BTreeSet<String> = csp("img-src")
        .into_iter()
        .filter_map(|source| source.strip_prefix("https://").map(str::to_string))
        .collect();
    assert_eq!(csp, ipx);
}

#[test]
fn the_shell_opens_the_channels_the_page_shows() {
    let shown: BTreeSet<String> = PLAYER_LINKS
        .lines()
        .skip_while(|line| !line.starts_with("const SHOWN"))
        .skip(1)
        .take_while(|line| !line.starts_with('}'))
        .filter_map(|line| line.trim().split(':').next().map(str::to_string))
        .collect();
    let opened: BTreeSet<String> = crate::channels::CHANNELS
        .iter()
        .map(|(kind, _)| kind.to_string())
        .collect();
    assert_eq!(opened, shown);
}

#[test]
fn every_copy_of_the_production_site_is_the_shells() {
    let production = crate::site::PRODUCTION;
    let updater = conf()["plugins"]["updater"]["endpoints"][0]
        .as_str()
        .expect("an updater endpoint")
        .to_string();
    assert!(updater.starts_with(&format!("{production}/")), "{updater}");
    assert!(
        NUXT_CONFIG.contains(&format!("|| '{production}'")),
        "nuxt.config.ts's default site"
    );
}

#[test]
fn the_overlay_smoke_test_writes_the_shells_current_files() {
    let identifier = conf()["identifier"]
        .as_str()
        .expect("an app identifier")
        .to_string();
    assert!(
        OVERLAY_SMOKE.contains(&format!("Join-Path $env:APPDATA \"{identifier}\"")),
        "the smoke test's config folder"
    );
    let version = OVERLAY_SMOKE
        .lines()
        .find_map(|line| line.trim().strip_prefix("version"))
        .and_then(|rest| rest.trim().strip_prefix('='))
        .and_then(|value| value.trim().parse::<u32>().ok());
    assert_eq!(
        version,
        Some(shell_state::overlay::SETTINGS_VERSION),
        "overlay-settings.json's version in tools/overlay-smoke-windows.ps1"
    );
}

