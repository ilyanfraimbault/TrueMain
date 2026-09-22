//! The app's state, and the rule that maps it onto a screen.
//!
//! Split out of the Tauri shell on purpose: this is the most decision-heavy
//! code in the client and the shell cannot be compiled on a Linux CI box (it
//! needs a platform webview). Here, the navigation rule is covered by tests
//! that run anywhere.

use lcu::{DraftState, GameflowPhase};
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

    #[test]
    fn an_unknown_phase_falls_back_to_the_dashboard_rather_than_a_blank_screen() {
        assert_eq!(
            connected(GameflowPhase::Unknown).screen(),
            Screen::Dashboard
        );
    }
}
