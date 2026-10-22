//! The game's own local API, the Live Client Data API, served by the game
//! process on port 2999 while a game runs — not by the League client.
//!
//! Read for the game recording (#1744): the game clock, to tie a second of the
//! game to a second of the video, and the game's events, the fallback for the
//! highlights when the match history never delivers the game's timeline. And
//! for the game page (#1748): the whole game in one reading (`allgamedata`),
//! which `live-client` derives the in-game state from. It serves the same
//! Riot-rooted certificate as the client, so it goes through the same pinned
//! TLS, and it needs no credentials.

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
/// recording reads — champion kills, and the plays the game announces itself
/// (a steal, an ace, first blood, #1766); the others (multikill
/// announcements, turrets) are skipped.
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
    /// An epic monster's kill: whether the killer's side stole it. The game
    /// sends `"True"` / `"False"`; kept as it came so a boolean cannot fail
    /// the whole feed.
    pub stolen: serde_json::Value,
    /// `Ace`: the side that aced, `ORDER` (blue side) or `CHAOS`.
    pub acing_team: String,
    /// `FirstBlood`: who drew it.
    pub recipient: String,
    /// `DragonKill`: `Fire`, `Water`, `Earth`, `Air`, `Hextech`, `Chemtech`,
    /// `Elder`.
    pub dragon_type: String,
}

impl LiveEvent {
    pub const CHAMPION_KILL: &'static str = "ChampionKill";
    pub const FIRST_BLOOD: &'static str = "FirstBlood";
    pub const ACE: &'static str = "Ace";
    /// The epic monsters' kills, which may be steals.
    pub const MONSTER_KILLS: [&'static str; 5] = [
        "DragonKill",
        "BaronKill",
        "HeraldKill",
        "HordeKill",
        "AtakhanKill",
    ];

    /// Whether the feed marks this kill as stolen.
    pub fn is_stolen(&self) -> bool {
        match &self.stolen {
            serde_json::Value::Bool(stolen) => *stolen,
            serde_json::Value::String(stolen) => stolen.eq_ignore_ascii_case("true"),
            _ => false,
        }
    }
}

/// One player of `GET /liveclientdata/playerlist`: who plays which champion
/// on which side, for the plays the feed names players in.
#[derive(Debug, Clone, Default, PartialEq, Eq, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct LivePlayer {
    /// The champion's display name, in the game's language.
    pub champion_name: String,
    pub riot_id: String,
    pub riot_id_game_name: String,
    pub summoner_name: String,
    /// `ORDER` (blue side) or `CHAOS` (red side).
    pub team: String,
}

impl LivePlayer {
    /// Whether an event's player name is this player's, under any of their
    /// names — the Riot ID's game name alone included.
    pub fn is_named(&self, name: &str) -> bool {
        let name = name.trim();
        let game_name = self.riot_id.split('#').next().unwrap_or_default();
        !name.is_empty()
            && [
                self.riot_id.as_str(),
                &self.riot_id_game_name,
                &self.summoner_name,
                game_name,
            ]
            .iter()
            .any(|own| !own.is_empty() && own.eq_ignore_ascii_case(name))
    }
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

    /// Every player of the game, both sides.
    pub async fn player_list(&self) -> Result<Vec<LivePlayer>> {
        self.get_json("/playerlist").await
    }

    /// Every event of the game so far, oldest first.
    pub async fn events(&self) -> Result<Vec<LiveEvent>> {
        Ok(self.get_json::<EventData>("/eventdata").await?.events)
    }

    /// `GET /allgamedata` — every player, the active player, the events and
    /// the clock in one reading — as the raw body, so the caller records it as
    /// it came (`tape::Reading::Game`) before deriving anything from it
    /// (`live-client`). An error is the normal answer until the game has
    /// loaded: the port refuses connections until the game process listens.
    pub async fn all_game_data(&self) -> Result<serde_json::Value> {
        self.get_json("/allgamedata").await
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
    fn reads_the_plays_the_game_announces() {
        let data: EventData = serde_json::from_str(
            r#"{ "Events": [
              { "EventID": 3, "EventName": "FirstBlood", "EventTime": 190.2, "Recipient": "Me" },
              { "EventID": 9, "EventName": "BaronKill", "EventTime": 1500.0, "Stolen": "True", "KillerName": "Me" },
              { "EventID": 10, "EventName": "DragonKill", "EventTime": 1600.0, "DragonType": "Elder", "Stolen": "False", "KillerName": "Foe" },
              { "EventID": 11, "EventName": "HeraldKill", "EventTime": 800.0, "Stolen": true },
              { "EventID": 12, "EventName": "Ace", "EventTime": 1610.0, "Acer": "Foe", "AcingTeam": "CHAOS" }
            ] }"#,
        )
        .unwrap();

        let stolen: Vec<bool> = data.events.iter().map(LiveEvent::is_stolen).collect();
        assert_eq!(stolen, vec![false, true, false, true, false]);
        assert_eq!(data.events[0].recipient, "Me");
        assert_eq!(data.events[2].dragon_type, "Elder");
        assert_eq!(data.events[4].acing_team, "CHAOS");
    }

    #[test]
    fn a_player_is_named_by_any_of_their_names() {
        let players: Vec<LivePlayer> = serde_json::from_str(
            r#"[{ "championName": "Ahri", "riotId": "Sheiden#1234", "riotIdGameName": "", "summonerName": "", "team": "ORDER", "level": 3 }]"#,
        )
        .unwrap();

        assert_eq!(players[0].champion_name, "Ahri");
        assert!(players[0].is_named("Sheiden"));
        assert!(players[0].is_named("sheiden#1234"));
        assert!(!players[0].is_named("Sheide"));
        assert!(!players[0].is_named(" "));
    }

    #[test]
    fn reads_the_clock() {
        let stats: GameStats =
            serde_json::from_str(r#"{ "gameMode": "CLASSIC", "gameTime": 93.218 }"#).unwrap();
        assert_eq!(stats.game_time, 93.218);
    }
}
