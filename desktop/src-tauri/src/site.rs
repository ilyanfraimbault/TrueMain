//! Which TrueMain the app belongs to.
//!
//! Every build is tied to one site: the API it reads, the pages it opens in the
//! player's browser and the update feed it polls all live there. The release
//! workflow builds each version twice — once for production, once for preprod
//! (`.github/workflows/desktop-release.yml`) — so a build downloaded from
//! preprod's page reads preprod's data and never production's, and the other
//! way round.
//!
//! The preprod address is not in the repository: the workflow passes it at build
//! time, and a developer sets it in `desktop/.env.local`, which `npm run tauri`
//! loads before starting the CLI.

/// The public site, for a build that names no other.
const PRODUCTION: &str = "https://truemain.lol";

/// Read at build time (the build's site), and again at startup so a developer
/// can repoint a built app without rebuilding it. The runtime value wins.
const SITE_VAR: &str = "TRUEMAIN_SITE_URL";

/// The site's origin, without a trailing slash.
pub fn base() -> String {
    std::env::var(SITE_VAR)
        .ok()
        .filter(|value| !value.trim().is_empty())
        .or_else(|| {
            option_env!("TRUEMAIN_SITE_URL")
                .filter(|value| !value.trim().is_empty())
                .map(str::to_string)
        })
        .unwrap_or_else(|| PRODUCTION.to_string())
        .trim()
        .trim_end_matches('/')
        .to_string()
}

/// The URL of one of the site's pages, or `None` when `path` is not a path on
/// it — the webview only ever names a route, never a host.
pub fn page(base: &str, path: &str) -> Option<String> {
    if !path.starts_with('/') || path.starts_with("//") || path.contains('\\') {
        return None;
    }
    Some(format!("{base}{path}"))
}

#[cfg(test)]
mod tests {
    use super::page;

    #[test]
    fn opens_paths_on_the_site_only() {
        let base = "https://truemain.lol";
        assert_eq!(
            page(base, "/truemains/Faker-KR1").as_deref(),
            Some("https://truemain.lol/truemains/Faker-KR1")
        );
        assert_eq!(page(base, "//evil.example/x"), None);
        assert_eq!(page(base, "https://evil.example"), None);
        assert_eq!(page(base, "/\\evil.example"), None);
        assert_eq!(page(base, ""), None);
    }
}
