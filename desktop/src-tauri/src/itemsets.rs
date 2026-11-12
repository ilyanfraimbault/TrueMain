//! The item set import button (#1908): the build's items pushed into the
//! client as the one TrueMain item set, on the player's click.
//!
//! The rules that keep this from touching the player's own sets — the whole
//! list read right before it is written, every set not ours written back as
//! read, nothing written after a failed read — live in `lcu::item_sets` and
//! `LcuClient::import_item_set`; this is the command the button calls.

use lcu::{BuildItems, Error, ItemSetDraft, NamedBlock};
use serde::Serialize;

use crate::contract::Refused;
use crate::record::SharedClient;

/// Why an import did not go as asked, told to the player by the frontend.
#[derive(Debug, Serialize, specta::Type)]
#[serde(rename_all = "kebab-case")]
pub enum ItemSetRefusal {
    /// The client's list could not be read, so nothing was written.
    ReadFailed,
    /// The set was written, but the player's own sets no longer read the same.
    SetsChanged,
    /// The build has no item to write.
    EmptyBuild,
}

type Answer = Result<(), Refused<ItemSetRefusal>>;

/// Write `build` into the client as the TrueMain item set, offered on
/// `champion_id` on Summoner's Rift. Only ever called from the button.
#[tauri::command]
#[specta::specta]
pub async fn import_item_set(
    client: tauri::State<'_, SharedClient>,
    champion_id: i64,
    champion: String,
    build: BuildItems,
) -> Answer {
    let draft = ItemSetDraft::from_build(&champion, champion_id, &build)
        .ok_or(Refused::Known(ItemSetRefusal::EmptyBuild))?;
    write(&client, &draft).await
}

/// Write a kit's tree into the client as the same TrueMain item set, one
/// block per path after the starter and the boots (#2185). Only ever called
/// from the button, like `import_item_set`.
#[tauri::command]
#[specta::specta]
pub async fn import_kit_item_set(
    client: tauri::State<'_, SharedClient>,
    champion_id: i64,
    champion: String,
    blocks: Vec<NamedBlock>,
) -> Answer {
    let draft = ItemSetDraft::from_blocks(&champion, champion_id, &blocks)
        .ok_or(Refused::Known(ItemSetRefusal::EmptyBuild))?;
    write(&client, &draft).await
}

async fn write(client: &SharedClient, draft: &ItemSetDraft) -> Answer {
    let client = client
        .read()
        .expect("client lock poisoned")
        .clone()
        .ok_or(Refused::from("the League client is not running"))?;

    match client.import_item_set(draft).await {
        Ok(()) => Ok(()),
        Err(Error::ItemSetsUnreadable(error)) => {
            tracing::warn!(%error, "item set import aborted: the list could not be read");
            Err(Refused::Known(ItemSetRefusal::ReadFailed))
        }
        Err(Error::PlayerItemSetsChanged) => Err(Refused::Known(ItemSetRefusal::SetsChanged)),
        Err(error) => {
            tracing::warn!(%error, "item set import failed");
            Err(error.to_string().into())
        }
    }
}
