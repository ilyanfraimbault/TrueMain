//! A known player's public channels, opened in the player's browser (#2441).
//!
//! The webview cannot open a link itself and has no shell permission: it names
//! the URL here, and only the platforms the known player's page shows get
//! through. The links come from the API, which takes them from public sources,
//! so nothing else may reach the system opener from a page's anchor.

/// The hosts of the channels the page shows: Twitch, YouTube and X.
const CHANNEL_HOSTS: &[&str] = &[
    "twitch.tv",
    "www.twitch.tv",
    "youtube.com",
    "www.youtube.com",
    "x.com",
    "www.x.com",
];

/// The URL to open for `raw`, or `None` when it is not an `https` link to one of
/// the channel hosts, on the default port and without credentials.
pub fn channel_url(raw: &str) -> Option<String> {
    let url = tauri::Url::parse(raw).ok()?;
    let allowed = url.scheme() == "https"
        && url.username().is_empty()
        && url.password().is_none()
        && url.port().is_none()
        && CHANNEL_HOSTS.contains(&url.host_str()?);
    allowed.then(|| url.into())
}

#[cfg(test)]
mod tests {
    use super::channel_url;

    #[test]
    fn opens_the_channels_the_page_shows() {
        for url in [
            "https://www.twitch.tv/faker",
            "https://www.youtube.com/@faker",
            "https://www.youtube.com/channel/UC123",
            "https://x.com/faker",
        ] {
            assert_eq!(channel_url(url).as_deref(), Some(url));
        }
    }

    #[test]
    fn refuses_anything_else() {
        for url in [
            "http://www.twitch.tv/faker",
            "file:///etc/passwd",
            "javascript:alert(1)",
            "https://evil.example/www.twitch.tv",
            "https://www.twitch.tv.evil.example/faker",
            "https://www.twitch.tv@evil.example/faker",
            "https://user:pass@www.twitch.tv/faker",
            "https://www.twitch.tv:8443/faker",
            "https://lol.fandom.com/wiki/Faker",
            "/players/faker",
            "",
        ] {
            assert_eq!(channel_url(url), None, "{url}");
        }
    }
}
