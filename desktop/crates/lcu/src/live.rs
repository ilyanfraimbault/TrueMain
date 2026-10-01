//! The game's own local API, the Live Client Data API, served by the game
//! process on port 2999 while a game runs — not by the League client.
//!
//! Read for the game recording (#1744): the game clock, to tie a second of the
//! game to a second of the video, and the game's events, the fallback for the
//! highlights when the match history never delivers the game's timeline. It
//! serves the same Riot-rooted certificate as the client, so it goes through
//! the same pinned TLS, and it needs no credentials.

use std::time::Duration;

use serde::de::DeserializeOwned;
use serde::Deserialize;

use crate::error::{Error, Result};
use crate::tls;

const BASE_URL: &str = "https://127.0.0.1:2999/liveclientdata";

/// Short on purpose: a clock reading is only worth anything if it comes back
/// at once, and a slow answer is one the anchor would discard anyway.
const TIMEOUT: Duration = Duration::from_secs(1);

/// `GET /liveclientdata/gamestats`, reduced to the clock.
#[derive(Debug, Clone, Default, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct GameStats {
    /// Seconds since the game started; zero on the loading screen.
    pub game_time: f64,
}

/// `GET /liveclientdata/activeplayer`, reduced to who it is.
///
/// The event feed names players rather than numbering them, and which of these
/// names it uses has moved with the Riot ID migration — so all of them are
/// kept and an event is ours when it names any one of them.
#[derive(Debug, Clone, Default, PartialEq, Eq, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct ActivePlayer {
    /// `Name#TAG`.
    pub riot_id: String,
    pub riot_id_game_name: String,
    pub summoner_name: String,
}

impl ActivePlayer {
    /// Whether an event's player name is this player's.
    pub fn is_named(&self, name: &str) -> bool {
        let name = name.trim();
        !name.is_empty()
            && [&self.riot_id, &self.riot_id_game_name, &self.summoner_name]
                .iter()
                .any(|own| !own.is_empty() && own.eq_ignore_ascii_case(name))
    }
}

/// `GET /liveclientdata/eventdata`.
#[derive(Debug, Clone, Default, Deserialize)]
#[serde(default)]
pub struct EventData {
    #[serde(rename = "Events")]
    pub events: Vec<LiveEvent>,
}

/// One event of the game, in the feed's own PascalCase. Only the fields the
/// recording reads; the others (objectives, multikill announcements) are
/// skipped.
#[derive(Debug, Clone, Default, PartialEq, Deserialize)]
#[serde(rename_all = "PascalCase", default)]
pub struct LiveEvent {
    #[serde(rename = "EventID")]
    pub event_id: i64,
    pub event_name: String,
    /// Seconds of game time.
    pub event_time: f64,
    pub killer_name: String,
    pub victim_name: String,
    pub assisters: Vec<String>,
}

impl LiveEvent {
    pub const CHAMPION_KILL: &'static str = "ChampionKill";
}

/// A connection to the running game's local API.
#[derive(Clone)]
pub struct LiveClient {
    http: reqwest::Client,
}

impl LiveClient {
    pub fn new() -> Result<Self> {
        let http = reqwest::Client::builder()
            .use_preconfigured_tls(tls::client_config()?)
            .timeout(TIMEOUT)
            .build()?;
        Ok(Self { http })
    }

    async fn get_json<T: DeserializeOwned>(&self, path: &str) -> Result<T> {
        let response = self.http.get(format!("{BASE_URL}{path}")).send().await?;
        let status = response.status();
        if !status.is_success() {
            return Err(Error::UnexpectedStatus {
                status: status.as_u16(),
                path: path.to_string(),
            });
        }
        let body = response.text().await?;
        serde_json::from_str(&body).map_err(|e| Error::Decode(e.to_string()))
    }

    pub async fn game_stats(&self) -> Result<GameStats> {
        self.get_json("/gamestats").await
    }

    pub async fn active_player(&self) -> Result<ActivePlayer> {
        self.get_json("/activeplayer").await
    }

    /// Every event of the game so far, oldest first.
    pub async fn events(&self) -> Result<Vec<LiveEvent>> {
        Ok(self.get_json::<EventData>("/eventdata").await?.events)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn reads_the_event_feed_in_its_own_casing() {
        let data: EventData = serde_json::from_str(
            r#"{ "Events": [
              { "EventID": 0, "EventName": "GameStart", "EventTime": 0.04 },
              { "EventID": 7, "EventName": "ChampionKill", "EventTime": 412.5,
                "KillerName": "Me", "VictimName": "Foe", "Assisters": ["Ally"] },
              { "EventID": 8, "EventName": "Multikill", "EventTime": 415.0, "KillerName": "Me", "KillStreak": 2 }
            ] }"#,
        )
        .unwrap();

        assert_eq!(data.events.len(), 3);
        let kill = &data.events[1];
        assert_eq!(kill.event_id, 7);
        assert_eq!(kill.event_name, LiveEvent::CHAMPION_KILL);
        assert_eq!(kill.event_time, 412.5);
        assert_eq!(kill.killer_name, "Me");
        assert_eq!(kill.victim_name, "Foe");
        assert_eq!(kill.assisters, vec!["Ally".to_string()]);
    }

    #[test]
    fn an_event_is_ours_under_any_of_our_names() {
        let me: ActivePlayer = serde_json::from_str(
            r#"{ "riotId": "Sheiden#1234", "riotIdGameName": "Sheiden", "summonerName": "Sheiden#1234" }"#,
        )
        .unwrap();

        assert!(me.is_named("Sheiden"));
        assert!(me.is_named("sheiden#1234"));
        assert!(!me.is_named("Someone"));
        assert!(!me.is_named(""));
        assert!(!ActivePlayer::default().is_named(""));
    }

    #[test]
    fn reads_the_clock() {
        let stats: GameStats =
            serde_json::from_str(r#"{ "gameMode": "CLASSIC", "gameTime": 93.218 }"#).unwrap();
        assert_eq!(stats.game_time, 93.218);
    }
}
