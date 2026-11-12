//! The TrueMain reads the webview may ask the shell for, and nothing else: the
//! shell forwards these and refuses the rest, so the webview cannot turn it
//! into a general proxy onto the API.
//!
//! The list is data (`api-routes.json`), each path named as the API's OpenAPI
//! document names it. Two tests hold it to the code around it: every route
//! exists in that document (`every_route_is_one_the_api_serves`), and every
//! read the app and the shared pages make is a route
//! (`desktop/app/tests/api-routes.test.ts`).

use std::sync::LazyLock;
use std::time::Duration;

use serde::Deserialize;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Deserialize)]
#[serde(rename_all = "UPPERCASE")]
pub enum Method {
    Get,
    Post,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct Route {
    pub method: Method,
    pub path: String,
    /// For a read the API computes on its first ask after a start (the tier
    /// list, #1783) or the slowest queries it runs (a ten-pick composition):
    /// past the client-wide deadline, failing them would empty the very panel
    /// that needs them.
    #[serde(default)]
    deadline_seconds: Option<u64>,
}

impl Route {
    pub fn deadline(&self) -> Option<Duration> {
        self.deadline_seconds.map(Duration::from_secs)
    }

    fn matches(&self, path: &str) -> bool {
        let mut asked = path.split('/');
        let mut pattern = self.path.split('/');
        loop {
            match (pattern.next(), asked.next()) {
                (None, None) => return true,
                (Some(expected), Some(segment)) => {
                    let fits = match expected
                        .strip_prefix('{')
                        .and_then(|name| name.strip_suffix('}'))
                    {
                        Some(param) => Param::named(param).accepts(segment),
                        None => expected == segment,
                    };
                    if !fits {
                        return false;
                    }
                }
                _ => return false,
            }
        }
    }
}

/// What each `{name}` of a route may hold: one path segment of its own shape,
/// so a value cannot reach another route by carrying a `/` or a `..`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum Param {
    /// A champion id: digits.
    ChampionId,
    /// A Riot ID percent-encoded into one segment (`Faker-KR1`).
    NameTag,
    /// A Riot match id (`EUW1_7123456789`).
    MatchId,
    /// A known player's slug, as the API writes them: up to 64 characters of
    /// lowercase kebab case.
    Slug,
    /// A platform (`EUW1`).
    PlatformId,
}

impl Param {
    fn named(name: &str) -> Self {
        match name {
            "championId" => Self::ChampionId,
            "nameTag" => Self::NameTag,
            "matchId" => Self::MatchId,
            "slug" => Self::Slug,
            "platformId" => Self::PlatformId,
            other => panic!("api-routes.json names a parameter the shell does not check: {other}"),
        }
    }

    fn accepts(self, segment: &str) -> bool {
        let all = |test: fn(u8) -> bool| !segment.is_empty() && segment.bytes().all(test);
        match self {
            Self::ChampionId => all(|b| b.is_ascii_digit()),
            Self::NameTag => {
                segment != "."
                    && segment != ".."
                    && all(|b| b.is_ascii_alphanumeric() || b"-_.~%".contains(&b))
            }
            Self::MatchId => all(|b| b.is_ascii_alphanumeric() || b == b'_'),
            Self::Slug => {
                segment.len() <= 64
                    && segment.split('-').all(|part| {
                        !part.is_empty()
                            && part
                                .bytes()
                                .all(|b| b.is_ascii_lowercase() || b.is_ascii_digit())
                    })
            }
            Self::PlatformId => {
                segment.len() <= 8 && all(|b| b.is_ascii_uppercase() || b.is_ascii_digit())
            }
        }
    }
}

static ROUTES: LazyLock<Vec<Route>> = LazyLock::new(|| {
    serde_json::from_str(include_str!("../../api-routes.json"))
        .expect("api-routes.json is a list of routes")
});

/// The route `method` on `path` asks for, if the app may ask it.
pub fn route(method: Method, path: &str) -> Option<&'static Route> {
    ROUTES
        .iter()
        .find(|route| route.method == method && route.matches(path))
}

#[cfg(test)]
mod tests {
    use super::{route, Method, Param, ROUTES};

    fn readable(path: &str) -> bool {
        route(Method::Get, path).is_some()
    }

    fn postable(path: &str) -> bool {
        route(Method::Post, path).is_some()
    }

    #[test]
    fn every_route_is_one_the_api_serves() {
        let document: serde_json::Value =
            serde_json::from_str(include_str!("../../../../web/shared/contract/openapi.json"))
                .expect("the API's OpenAPI document parses");
        for route in ROUTES.iter() {
            let method = match route.method {
                Method::Get => "get",
                Method::Post => "post",
            };
            assert!(
                document["paths"][&route.path][method].is_object(),
                "the API serves no {method} {}",
                route.path
            );
            for segment in route.path.split('/') {
                if let Some(param) = segment.strip_prefix('{').and_then(|s| s.strip_suffix('}')) {
                    Param::named(param);
                }
            }
        }
    }

    #[test]
    fn reads_the_listed_paths_and_a_true_mains_reads() {
        assert!(readable("/truemains"));
        assert!(readable("/truemains/lookup"));
        assert!(readable("/champions/tierlist"));
        assert!(readable("/champions/directory"));
        assert!(readable("/champions/damage-profiles"));
        assert!(readable("/items/classes"));
        assert!(readable("/benchmarks/pace"));
        assert!(readable("/benchmarks/form"));
        assert!(readable("/champions/8"));
        assert!(readable("/truemains/ttv%20ronaldoo-back/champions/8"));
        assert!(readable("/truemains/Faker-KR1/champions/7"));
        assert!(readable("/truemains/Faker-KR1/profile"));
        assert!(readable("/truemains/Faker-KR1/matches"));
        assert!(readable("/truemains/Faker-KR1/rank-history"));
        assert!(readable("/truemains/Faker-KR1/activity"));
        assert!(readable("/truemains/Faker-KR1/matches/KR_7123456789"));
        assert!(readable("/champions/103/item-context"));
        assert!(readable("/champions/234/matchups"));
        assert!(readable("/champions/266/trend"));
        assert!(readable("/ladder/EUW1/apex-cutoffs"));
        assert!(readable("/players/faker"));
        assert!(readable("/players/t1-faker-2"));
        assert!(readable(&format!("/players/{}", "a".repeat(64))));
    }

    #[test]
    fn posts_the_draft_reads_and_the_builds_against_it() {
        assert!(postable("/champions/draft"));
        assert!(postable("/champions/draft/bans"));
        assert!(postable("/champions/103/composition-build"));
        assert!(postable("/champions/103/composition-build/games"));
        assert!(postable("/champions/103/next-item"));
        assert!(!postable("/champions/x/next-item"));
        assert!(!postable("/champions/x/composition-build"));
        assert!(!postable("/champions//composition-build"));
        assert!(!postable("/champions/103/composition-build/games/1"));
        assert!(!postable("/truemains/Faker-KR1/profile"));
        assert!(!readable("/champions/draft"));
    }

    #[test]
    fn refuses_anything_else() {
        assert!(!readable("/players"));
        assert!(!readable("/players/"));
        assert!(!readable("/players/faker/accounts"));
        assert!(!readable("/players/Faker"));
        assert!(!readable("/players/fa_ker"));
        assert!(!readable("/players/fa%6Ber"));
        assert!(!readable("/players/fa.ker"));
        assert!(!readable("/players/.."));
        assert!(!readable("/players/-faker"));
        assert!(!readable("/players/faker-"));
        assert!(!readable("/players/fa--ker"));
        assert!(!readable("/players/faker?x=1"));
        assert!(!readable(&format!("/players/{}", "a".repeat(65))));
        assert!(!readable("/champions/x"));
        assert!(!readable("/champions/x/item-context"));
        assert!(!readable("/champions/x/matchups"));
        assert!(!readable("/champions/234/matchups/1"));
        assert!(!readable("/champions/8/item-context/1"));
        assert!(!readable("/ladder/euw1/apex-cutoffs"));
        assert!(!readable("/ladder/../apex-cutoffs"));
        assert!(!readable("/truemains/Faker-KR1/activity/day"));
        assert!(!readable("/truemains/lookup/1"));
        assert!(!readable("/truemains/Faker-KR1/matches/KR_1/timeline"));
        assert!(!readable("/truemains/Faker-KR1/matches/KR-1"));
        assert!(!readable("/truemains/Faker-KR1/matches/"));
        assert!(!readable("/truemains/Faker-KR1/champions/7/matchups"));
        assert!(!readable("/truemains/../champions/7"));
        assert!(!readable("/truemains/a/b/champions/7"));
        assert!(!readable("/truemains/Faker?x=1/champions/7"));
        assert!(!readable("/truemains/Faker-KR1/champions/7x"));
        assert!(!readable("/truemains//champions/7"));
        assert!(!readable("/ops/logs"));
    }
}
