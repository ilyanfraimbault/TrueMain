//! Recording the player's games, and finding the moments worth rewatching in
//! them (#1744).
//!
//! Everything here is independent of how the screen is captured — that is the
//! open question of #1745, and it plugs in behind [`session::Capture`]. Like
//! `lcu`, the crate has no Tauri or GUI dependency, so it builds and tests on
//! the Linux CI box.
//!
//! - [`settings`]: the player's few choices — resolution and frame rate, and
//!   the encoder setting derived from them.
//! - [`highlights`]: the player's kills, deaths and assists, from the game's
//!   timeline or its live feed.
//! - [`anchor`]: which second of the video a second of the game is.
//! - [`moments`]: the game's objectives, and every moment placed on the video.
//! - [`store`]: the recordings on disk and the disk budget.
//! - [`clips`]: the clips the player cut from a recording, each its own file.
//! - [`session`]: one game's recording, driven by the gameflow phase.

pub mod anchor;
pub mod clips;
pub mod highlights;
pub mod moments;
pub mod session;
pub mod settings;
pub mod store;

pub use anchor::{Anchor, ClockSample};
pub use clips::{ClipDir, ClipMeta, ClipStore, StoredClip};
pub use highlights::{Highlight, HighlightKind, HighlightSource};
pub use moments::{Moment, MomentKind, Objective, ObjectiveKind};
pub use session::{Capture, CaptureError, Change, GameInfo, GameOutcome, Session, SessionError};
pub use settings::{FrameRate, OutputSpec, Quality, Queues, RecordingSettings, Resolution};
pub use store::{RecordingDir, RecordingMeta, RecordingStatus, Store, StoredRecording};
