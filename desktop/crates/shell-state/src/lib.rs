//! The app's state, and the rule that maps it onto a screen.
//!
//! Split out of the Tauri shell on purpose: this is the most decision-heavy
//! code in the client and the shell cannot be compiled on a Linux CI box (it
//! needs a platform webview). Here, the navigation rule is covered by tests
//! that run anywhere.

pub mod keys;
pub mod overlay;
pub mod screens;
pub mod window;

use lcu::{
    ChampSelectSession, ChampionMastery, CurrentSummoner, DraftState, GameflowPhase, LcuEvent,
};
use serde::{Deserialize, Serialize};

/// The app's whole view of the world, pushed to the frontend on every change.
///
/// One payload rather than several events: the frontend renders a *state*, and
/// three separately-arriving events would let it paint a champ select with no
/// draft in it for a frame.
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AppState {
    /// False means no client is running — the normal state, not an error.
    pub connected: bool,
    pub phase: GameflowPhase,
    /// `Name#TAG` of the logged-in player, absent until the client is up.
    pub riot_id: Option<String>,
    /// The player's icon and level, for the dashboard's profile card. Set and
    /// cleared together with the Riot ID.
    pub profile_icon_id: Option<i64>,
    pub summoner_level: Option<i64>,
    /// The player's own pool: every champion they have mastery points on, most
    /// points first. The draft ranks it rather than asking the player what they
    /// play. Empty until read, and emptied whenever the Riot ID changes, so a
    /// relog never shows the previous account's pool.
    #[serde(default)]
    pub champion_pool: Vec<i64>,
    /// Present only in champ select.
    pub draft: Option<DraftState>,
}

impl Default for AppState {
    fn default() -> Self {
        Self {
            connected: false,
            phase: GameflowPhase::None,
            riot_id: None,
            profile_icon_id: None,
            summoner_level: None,
            champion_pool: Vec::new(),
            draft: None,
        }
    }
}

impl AppState {
    /// Apply one change the client pushed. Returns whether the state moved.
    ///
    /// The one place an event becomes state, shared by the live client and by
    /// a tape replay — two implementations would let a tape prove something the
    /// real app does not do. It lives here rather than in the shell so the
    /// Linux CI box, which cannot build the shell, still runs its tests.
    pub fn apply(&mut self, event: &LcuEvent) -> bool {
        match event.uri.as_str() {
            lcu::uri::GAMEFLOW_PHASE => {
                let phase = GameflowPhase::from_client_value(&event.data.to_string());
                let changed = phase != self.phase;
                self.phase = phase;
                // Leaving champ select must clear the draft, or the panel keeps
                // showing the draft of a game that already started.
                if phase != GameflowPhase::ChampSelect {
                    self.draft = None;
                }
                changed
            }
            lcu::uri::CHAMP_SELECT_SESSION => {
                self.draft = serde_json::from_value::<ChampSelectSession>(event.data.clone())
                    .ok()
                    .map(|session| session.draft_state());
                true
            }
            lcu::uri::CURRENT_SUMMONER => {
                // The reading taken at attach is empty whenever the app started
                // before the player logged in — the usual order — so the Riot ID
                // arrives through this event, not through that reading.
                let summoner = serde_json::from_value::<CurrentSummoner>(event.data.clone())
                    .ok()
                    .filter(|summoner| !summoner.game_name.is_empty());
                let before = self.identity();
                self.set_summoner(summoner.as_ref());
                before != self.identity()
            }
            _ => false,
        }
    }

    /// Take the player's identity from a summoner reading, or clear it.
    ///
    /// The pool belongs to the Riot ID, so it goes when the Riot ID changes —
    /// here, the one place that can change it. A level or an icon changing is
    /// the same player and keeps it.
    pub fn set_summoner(&mut self, summoner: Option<&CurrentSummoner>) {
        let riot_id = summoner.map(CurrentSummoner::riot_id);
        if riot_id != self.riot_id {
            self.champion_pool.clear();
        }
        self.riot_id = riot_id;
        self.profile_icon_id = summoner.map(|s| s.profile_icon_id);
        self.summoner_level = summoner.map(|s| s.summoner_level);
    }

    /// Take the player's pool from a mastery reading.
    ///
    /// Ties go to the lower champion id, so two readings of the same account
    /// always rank the same way. A champion with no points was never played
    /// and is not part of the pool.
    pub fn set_mastery(&mut self, mastery: &[ChampionMastery]) {
        let mut played: Vec<&ChampionMastery> = mastery
            .iter()
            .filter(|entry| entry.champion_points > 0)
            .collect();
        played.sort_by(|a, b| {
            b.champion_points
                .cmp(&a.champion_points)
                .then(a.champion_id.cmp(&b.champion_id))
        });
        self.champion_pool = played.iter().map(|entry| entry.champion_id).collect();
    }

    /// Whether the pool should be read now: a player is logged in, their pool
    /// is empty, and it was not already read for them. `read_for` is the Riot
    /// ID of the last read, kept by the caller.
    ///
    /// Once per Riot ID, not until it succeeds: a new account has no mastery at
    /// all, and re-reading on every event would ask the client again on each
    /// hover of a champion select. A relog to another account, or back, is a
    /// new Riot ID and reads again. Shared by the live path and a replay, so a
    /// tape hands out its readings exactly when the client was asked.
    pub fn wants_mastery(&self, read_for: Option<&str>) -> bool {
        self.riot_id.is_some()
            && self.champion_pool.is_empty()
            && self.riot_id.as_deref() != read_for
    }

    fn identity(&self) -> (Option<String>, Option<i64>, Option<i64>) {
        (
            self.riot_id.clone(),
            self.profile_icon_id,
            self.summoner_level,
        )
    }

    /// Which screen the shell should show.
    ///
    /// Derived here, in one place, rather than in the frontend: the navigation
    /// rule is a product decision, and two implementations of it would drift.
    pub fn screen(&self) -> Screen {
        if !self.connected {
            return Screen::NoClient;
        }
        match self.phase {
            GameflowPhase::ChampSelect => Screen::Draft,
            GameflowPhase::InProgress => Screen::InGame,
            _ => Screen::Dashboard,
        }
    }
}

/// The screens the shell can show. `InGame` is the game page (`/game`, #1748),
/// and the only screen during which the running game is read: the shell's game
/// feed follows this rule rather than a second test of the phase.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "kebab-case")]
pub enum Screen {
    NoClient,
    Dashboard,
    Draft,
    InGame,
}

#[cfg(test)]
mod tests {
    use super::*;

    fn connected(phase: GameflowPhase) -> AppState {
        AppState {
            connected: true,
            phase,
            ..Default::default()
        }
    }

    #[test]
    fn no_client_wins_over_any_stale_phase() {
        let state = AppState {
            connected: false,
            phase: GameflowPhase::ChampSelect,
            ..Default::default()
        };
        assert_eq!(state.screen(), Screen::NoClient);
    }

    #[test]
    fn champ_select_opens_the_draft() {
        assert_eq!(
            connected(GameflowPhase::ChampSelect).screen(),
            Screen::Draft
        );
    }

    #[test]
    fn a_running_game_opens_the_game_and_its_end_leaves_it() {
        assert_eq!(
            connected(GameflowPhase::InProgress).screen(),
            Screen::InGame
        );
        // A crashed game the client offers to rejoin is not one to read: the
        // game's API is down until the player reconnects, which is `InProgress`
        // again.
        for phase in [
            GameflowPhase::Reconnect,
            GameflowPhase::WaitingForStats,
            GameflowPhase::PreEndOfGame,
        ] {
            assert_eq!(connected(phase).screen(), Screen::Dashboard, "{phase:?}");
        }
        assert_eq!(
            serde_json::to_value(Screen::InGame).unwrap(),
            serde_json::json!("in-game")
        );
    }

    #[test]
    fn the_lobby_and_every_idle_phase_show_the_dashboard() {
        for phase in [
            GameflowPhase::None,
            GameflowPhase::Lobby,
            GameflowPhase::Matchmaking,
            GameflowPhase::EndOfGame,
        ] {
            assert_eq!(connected(phase).screen(), Screen::Dashboard, "{phase:?}");
        }
    }

    fn event(uri: &str, data: serde_json::Value) -> LcuEvent {
        LcuEvent {
            uri: uri.to_string(),
            event_type: "Update".to_string(),
            data,
        }
    }

    #[test]
    fn the_riot_id_arrives_after_attach_when_the_player_logs_in_later() {
        // The app attaches at the login screen, so the reading taken then is
        // empty; the login itself is what fills the name in.
        let mut state = connected(GameflowPhase::None);
        assert!(state.riot_id.is_none());

        let changed = state.apply(&event(
            lcu::uri::CURRENT_SUMMONER,
            serde_json::json!({
                "gameName": "Phantasm",
                "tagLine": "EUW",
                "summonerLevel": 412,
                "profileIconId": 29
            }),
        ));
        assert!(changed);
        assert_eq!(state.riot_id.as_deref(), Some("Phantasm#EUW"));
        assert_eq!(state.profile_icon_id, Some(29));
        assert_eq!(state.summoner_level, Some(412));
    }

    fn mastery(champion_id: i64, champion_points: i64) -> ChampionMastery {
        ChampionMastery {
            champion_id,
            champion_points,
        }
    }

    fn logged_in(game_name: &str) -> AppState {
        let mut state = connected(GameflowPhase::Lobby);
        state.apply(&event(
            lcu::uri::CURRENT_SUMMONER,
            serde_json::json!({"gameName": game_name, "tagLine": "EUW"}),
        ));
        state
    }

    #[test]
    fn the_pool_is_ranked_by_mastery_points_most_first() {
        let mut state = logged_in("Phantasm");
        state.set_mastery(&[
            mastery(61, 40_000),
            mastery(103, 250_000),
            // The client lists champions that were never played, at zero.
            mastery(1, 0),
            mastery(134, 90_000),
            // Tied with Orianna: the lower id goes first, so the order does
            // not depend on how the client happened to list them.
            mastery(7, 40_000),
        ]);
        assert_eq!(state.champion_pool, vec![103, 134, 7, 61]);
    }

    #[test]
    fn the_pool_serialises_under_the_name_the_frontend_reads() {
        let mut state = logged_in("Phantasm");
        state.set_mastery(&[mastery(103, 10)]);
        let payload = serde_json::to_value(&state).unwrap();
        assert_eq!(payload["championPool"], serde_json::json!([103]));
    }

    #[test]
    fn a_relog_to_another_account_drops_the_previous_pool() {
        let mut state = logged_in("Phantasm");
        state.set_mastery(&[mastery(103, 250_000)]);

        let changed = state.apply(&event(
            lcu::uri::CURRENT_SUMMONER,
            serde_json::json!({"gameName": "Smurf", "tagLine": "EUW"}),
        ));
        assert!(changed);
        assert!(state.champion_pool.is_empty());
    }

    #[test]
    fn a_logout_drops_the_pool_with_the_riot_id() {
        let mut state = logged_in("Phantasm");
        state.set_mastery(&[mastery(103, 250_000)]);

        state.apply(&event(
            lcu::uri::CURRENT_SUMMONER,
            serde_json::json!({"gameName": "", "tagLine": ""}),
        ));
        assert!(state.riot_id.is_none());
        assert!(state.champion_pool.is_empty());
    }

    #[test]
    fn a_level_up_is_the_same_player_and_keeps_the_pool() {
        let mut state = logged_in("Phantasm");
        state.set_mastery(&[mastery(103, 250_000)]);

        state.apply(&event(
            lcu::uri::CURRENT_SUMMONER,
            serde_json::json!({"gameName": "Phantasm", "tagLine": "EUW", "summonerLevel": 413}),
        ));
        assert_eq!(state.champion_pool, vec![103]);
    }

    #[test]
    fn mastery_is_wanted_once_per_riot_id() {
        assert!(
            !connected(GameflowPhase::None).wants_mastery(None),
            "nobody is logged in yet"
        );

        let mut state = logged_in("Phantasm");
        assert!(state.wants_mastery(None));
        // Read, and it came back empty: a new account. Asking again on every
        // event would not change that.
        assert!(!state.wants_mastery(Some("Phantasm#EUW")));

        // A relog is a new player, whose pool has not been read.
        state.apply(&event(
            lcu::uri::CURRENT_SUMMONER,
            serde_json::json!({"gameName": "Smurf", "tagLine": "EUW"}),
        ));
        assert!(state.wants_mastery(Some("Phantasm#EUW")));

        state.set_mastery(&[mastery(103, 10)]);
        assert!(!state.wants_mastery(None), "a pool is already there");
    }

    #[test]
    fn a_summoner_event_with_no_name_does_not_invent_a_riot_id() {
        // The client emits the endpoint before the account is resolved, with
        // empty strings; `#` alone is not a player.
        let mut state = connected(GameflowPhase::None);
        state.apply(&event(
            lcu::uri::CURRENT_SUMMONER,
            serde_json::json!({"gameName": "", "tagLine": ""}),
        ));
        assert!(state.riot_id.is_none());
    }

    #[test]
    fn leaving_champ_select_clears_the_draft() {
        let mut state = connected(GameflowPhase::ChampSelect);
        state.apply(&event(
            lcu::uri::CHAMP_SELECT_SESSION,
            serde_json::json!({"myTeam": [], "theirTeam": []}),
        ));
        assert!(state.draft.is_some());

        state.apply(&event(
            lcu::uri::GAMEFLOW_PHASE,
            serde_json::json!("InProgress"),
        ));
        assert_eq!(state.phase, GameflowPhase::InProgress);
        assert!(state.draft.is_none());
    }

    #[test]
    fn an_endpoint_the_app_does_not_follow_changes_nothing() {
        let mut state = connected(GameflowPhase::Lobby);
        assert!(!state.apply(&event("/lol-chat/v1/friends", serde_json::json!([]))));
    }

    #[test]
    fn an_unknown_phase_falls_back_to_the_dashboard_rather_than_a_blank_screen() {
        assert_eq!(
            connected(GameflowPhase::Unknown).screen(),
            Screen::Dashboard
        );
    }
}
