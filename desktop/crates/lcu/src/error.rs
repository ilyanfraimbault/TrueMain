use std::io;

pub type Result<T> = std::result::Result<T, Error>;

#[derive(Debug, thiserror::Error)]
pub enum Error {
    /// No League client is running. The normal state, not a failure: the app
    /// spends most of its life here and has a screen for it.
    #[error("the League client is not running")]
    ClientNotRunning,

    #[error("the client's lockfile is malformed: {0}")]
    MalformedLockfile(String),

    #[error("the client rejected our credentials — it probably restarted on a new port")]
    Unauthorized,

    #[error("the client answered {status} for {path}")]
    UnexpectedStatus { status: u16, path: String },

    #[error("could not reach the client: {0}")]
    Transport(String),

    #[error("could not read the client's answer: {0}")]
    Decode(String),

    #[error("the pinned Riot root certificate is unusable: {0}")]
    Certificate(String),

    /// Every rune page slot is taken and none of them is ours. Deliberately its
    /// own error: the caller must tell the player to free a slot, never resolve
    /// it by deleting a page they made.
    #[error("every rune page is in use and none of them is ours")]
    NoRunePageSlot,

    /// The account's item sets could not be read, or did not decode as a list.
    /// Nothing was written: the list goes back whole, so writing one built from
    /// a failed read would wipe the player's sets.
    #[error("could not read the client's item sets, nothing was written: {0}")]
    ItemSetsUnreadable(String),

    /// The item set was written, but reading the list back no longer shows
    /// every set the player had. The caller must say so, not swallow it.
    #[error("the item set was written, but the player's own sets no longer read the same")]
    PlayerItemSetsChanged,

    /// A recorded session that cannot be read back. Dev tooling, but an error
    /// rather than a skipped line: a replay that drops a pick is a fixture
    /// that lies.
    #[error("the tape is malformed: {0}")]
    MalformedTape(String),

    #[error(transparent)]
    Io(#[from] io::Error),
}

impl From<reqwest::Error> for Error {
    fn from(error: reqwest::Error) -> Self {
        if error.is_decode() {
            Error::Decode(error.to_string())
        } else {
            Error::Transport(error.to_string())
        }
    }
}
