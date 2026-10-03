//! The running game, read through its own **Live Client Data API** (#1748).
//!
//! While a game runs, the game process — not the League client — serves
//! `https://127.0.0.1:2999/liveclientdata/allgamedata`: every player's champion,
//! team, lane, level, items, K/D/A and death timer, the active player's own
//! numbers, the event feed and the game clock. Riot documents it for
//! third-party use; it needs no credentials and reads no memory.
//!
//! - [`model`]: that payload, as the game sends it.
//! - [`game`]: the in-game state the app renders, derived from one payload,
//!   and the changes between two of them.
//! - [`objectives`]: what each team has taken on the map, from the event feed.
//! - [`pace`]: the player's creep score and gold earned, minute by minute.
//! - [`feed`]: what reaches the frontend — a snapshot when a game is first
//!   read, then only the changes, never the payload on every poll.
//! - [`poll`]: how often the game is read. The request itself goes through
//!   `lcu::LiveClient`, the app's one client for the game's API.
//!
//! Like `lcu`, no Tauri or GUI dependency, so all of it builds and tests on the
//! Linux CI box. What the API is known to reveal about enemies, and what is
//! still to be confirmed in a live game, is in `desktop/README.md`.

pub mod feed;
pub mod game;
pub mod model;
pub mod objectives;
pub mod pace;
pub mod poll;

pub use feed::{Emission, GameFeed, GameUpdate};
pub use game::{GameChange, GameItem, GamePlayer, GameSpell, GameState, Team};
pub use model::AllGameData;
pub use objectives::{Objectives, TeamObjectives};
pub use pace::{Pace, PaceSample};
pub use poll::{next_poll, POLL};
