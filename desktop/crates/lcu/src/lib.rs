//! A client for the League Client Update (LCU) local API.
//!
//! Deliberately free of any Tauri or GUI dependency: it builds and tests on any
//! platform, including the Linux CI box, while the app shell it serves only
//! builds on macOS and Windows.
//!
//! Read-mostly by design. Nothing here injects, reads process memory, or takes
//! an action on the player's behalf — the app talks to the same local HTTP API
//! the client serves to its own web UI, and its few writes (a rune page, a
//! champion select hover, lock or ban) are made on the player's click only.

pub mod champ_select;
pub mod client;
pub mod credentials;
pub mod detail;
pub mod error;
pub mod events;
pub mod form;
pub mod laning;
pub mod live;
pub mod model;
pub mod record;
pub mod runes;
pub mod tape;
pub mod tls;

pub use champ_select::{ActionKind, DraftAction, Refusal};
pub use client::LcuClient;
pub use credentials::Credentials;
pub use detail::GameDetail;
pub use error::{Error, Result};
pub use events::{stream_events, LcuEvent};
pub use form::{PlayerForm, PositionGames, RecentGame, FORM_GAMES, RECENT_GAMES};
pub use laning::{
    gaps_at_fifteen, laning_games, LaningForm, LaningGame, LANING_DEPTH, LANING_GAMES,
};
pub use live::LiveClient;
pub use model::{
    ChampSelectSession, ChampionMastery, CurrentSummoner, DraftState, GameflowPhase,
    GameflowPlayer, GameflowSession, TeamSlot,
};
pub use record::{GameParticipant, PlayerGame, PlayerRecord, RankedQueue, Scoreboard};
pub use runes::{plan_import, RuneImportPlan, RunePage, RunePageDraft};
pub use tape::{Played, Reading, Recorder, Tape};

/// Endpoints the app subscribes to, named once so the Rust and the shell agree.
pub mod uri {
    pub const GAMEFLOW_PHASE: &str = "/lol-gameflow/v1/gameflow-phase";
    pub const CHAMP_SELECT_SESSION: &str = "/lol-champ-select/v1/session";
    /// Pushed when the login completes. The app usually attaches while the
    /// client is still at its login screen, so the reading taken at attach is
    /// empty and this event is how the Riot ID actually arrives.
    pub const CURRENT_SUMMONER: &str = "/lol-summoner/v1/current-summoner";

    /// Every endpoint the app acts on — the recorder's allow-list, so a tape
    /// never holds anything else the client's socket carries.
    pub const FOLLOWED: [&str; 3] = [GAMEFLOW_PHASE, CHAMP_SELECT_SESSION, CURRENT_SUMMONER];
}
