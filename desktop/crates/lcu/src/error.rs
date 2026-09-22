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
