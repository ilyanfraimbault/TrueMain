//! A client for the League Client Update (LCU) local API.
//!
//! Deliberately free of any Tauri or GUI dependency: it builds and tests on any
//! platform, including the Linux CI box, while the app shell it serves only
//! builds on macOS and Windows.
//!
//! Read-only by design. Nothing here injects, reads process memory, or takes an
//! action on the player's behalf — the app talks to the same local HTTP API the
//! client serves to its own web UI.

pub mod client;
pub mod credentials;
pub mod error;
pub mod events;
pub mod model;
pub mod runes;
pub mod tls;

pub use client::LcuClient;
pub use credentials::Credentials;
pub use error::{Error, Result};
pub use events::{stream_events, LcuEvent};
pub use model::{ChampSelectSession, CurrentSummoner, DraftState, GameflowPhase};
pub use runes::{plan_import, RuneImportPlan, RunePage, RunePageDraft};

/// Endpoints the app subscribes to, named once so the Rust and the shell agree.
pub mod uri {
    pub const GAMEFLOW_PHASE: &str = "/lol-gameflow/v1/gameflow-phase";
    pub const CHAMP_SELECT_SESSION: &str = "/lol-champ-select/v1/session";
}
