//! The app's state, and the rule that maps it onto a screen.
//!
//! Split out of the Tauri shell on purpose: this is the most decision-heavy
//! code in the client and the shell cannot be compiled on a Linux CI box (it
//! needs a platform webview). Here, the navigation rule is covered by tests
//! that run anywhere.

use lcu::{ChampSelectSession, CurrentSummoner, DraftState, GameflowPhase, LcuEvent};
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
    /// Present only in champ select.
    pub draft: Option<DraftState>,
}

impl Default for AppState {
    fn default() -> Self {
        Self {
            connected: false,
            phase: GameflowPhase::None,
            riot_id: None,
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
                let riot_id = serde_json::from_value::<CurrentSummoner>(event.data.clone())
                    .ok()
                    .filter(|summoner| !summoner.game_name.is_empty())
                    .map(|summoner| summoner.riot_id());
                let changed = riot_id != self.riot_id;
                self.riot_id = riot_id;
                changed
            }
            _ => false,
        }
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

/// The screens the shell can show. `InGame` is reserved for the overlay work
/// and currently renders the dashboard; naming it now keeps the state machine
/// honest instead of making the overlay a refactor later.
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
            serde_json::json!({"gameName": "Phantasm", "tagLine": "EUW"}),
        ));
        assert!(changed);
        assert_eq!(state.riot_id.as_deref(), Some("Phantasm#EUW"));
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

        state.apply(&event(lcu::uri::GAMEFLOW_PHASE, serde_json::json!("InProgress")));
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
