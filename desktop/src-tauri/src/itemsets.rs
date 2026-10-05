//! The item set import button (#1908): the build's items pushed into the
//! client as the one TrueMain item set, on the player's click.
//!
//! The rules that keep this from touching the player's own sets — the whole
//! list read right before it is written, every set not ours written back as
//! read, nothing written after a failed read — live in `lcu::item_sets` and
//! `LcuClient::import_item_set`; this is the command the button calls.

use lcu::{BuildItems, Error, ItemSetDraft};

use crate::record::SharedClient;

/// What the command answers when the client's list could not be read, so
/// nothing was written. Matched by the frontend.
const READ_FAILED: &str = "read-failed";

/// What the command answers when the set was written but the player's own
/// sets no longer read the same. Matched by the frontend.
const SETS_CHANGED: &str = "sets-changed";

/// What the command answers for a build with no item to write.
const EMPTY_BUILD: &str = "empty-build";

/// Write `build` into the client as the TrueMain item set, offered on
/// `champion_id` on Summoner's Rift. Only ever called from the button.
#[tauri::command]
pub async fn import_item_set(
    client: tauri::State<'_, SharedClient>,
    champion_id: i64,
    champion: String,
    build: BuildItems,
) -> Result<(), String> {
    let draft = ItemSetDraft::from_build(&champion, champion_id, &build)
        .ok_or_else(|| EMPTY_BUILD.to_string())?;
    let client = client
        .read()
        .expect("client lock poisoned")
        .clone()
        .ok_or_else(|| "the League client is not running".to_string())?;

    match client.import_item_set(&draft).await {
        Ok(()) => Ok(()),
        Err(Error::ItemSetsUnreadable(error)) => {
            tracing::warn!(%error, "item set import aborted: the list could not be read");
            Err(READ_FAILED.to_string())
        }
        Err(Error::PlayerItemSetsChanged) => Err(SETS_CHANGED.to_string()),
        Err(error) => {
            tracing::warn!(%error, "item set import failed");
            Err(error.to_string())
        }
    }
}
