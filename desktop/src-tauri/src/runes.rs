//! The rune import button (#1678): the build's rune page pushed into the
//! client, on the player's click.
//!
//! The rules that keep this a tool rather than an automation — a click only,
//! never a page of the player's deleted — live in `lcu::runes` and
//! `LcuClient::import_rune_page`; this is the command the button calls.

use lcu::runes::OWNED_PAGE_PREFIX;
use lcu::{Error, RunePageDraft};
use serde::Deserialize;

use crate::record::SharedClient;

/// What the command answers when every page is the player's own. Matched by
/// the frontend, which tells the player to free one.
const NO_SLOT: &str = "no-slot";

/// A build's rune page as the API serves it (`BuildRunePage`): every perk by
/// id, in the order the client expects them.
#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct BuildRunePage {
    primary_style_id: i64,
    primary_keystone_id: i64,
    primary_perk1_id: i64,
    primary_perk2_id: i64,
    primary_perk3_id: i64,
    secondary_style_id: i64,
    secondary_perk1_id: i64,
    secondary_perk2_id: i64,
    stat_offense: i64,
    stat_flex: i64,
    stat_defense: i64,
}

impl BuildRunePage {
    /// The page to send, named so it is recognised as ours on the next
    /// import. `None` when a slot is empty: the client would take an
    /// incomplete page and leave the player to find the hole in it.
    fn draft(&self, champion: &str) -> Option<RunePageDraft> {
        let selected_perk_ids = vec![
            self.primary_keystone_id,
            self.primary_perk1_id,
            self.primary_perk2_id,
            self.primary_perk3_id,
            self.secondary_perk1_id,
            self.secondary_perk2_id,
            self.stat_offense,
            self.stat_flex,
            self.stat_defense,
        ];
        let complete = self.primary_style_id > 0
            && self.secondary_style_id > 0
            && selected_perk_ids.iter().all(|id| *id > 0);
        complete.then(|| RunePageDraft {
            name: format!("{OWNED_PAGE_PREFIX}{}", champion.trim()),
            primary_style_id: self.primary_style_id,
            sub_style_id: self.secondary_style_id,
            selected_perk_ids,
        })
    }
}

/// Push `page` into the client as the TrueMain page and select it. Only ever
/// called from the button.
#[tauri::command]
pub async fn import_runes(
    client: tauri::State<'_, SharedClient>,
    champion: String,
    page: BuildRunePage,
) -> Result<(), String> {
    let draft = page
        .draft(&champion)
        .ok_or_else(|| "this rune page is incomplete".to_string())?;
    let client = client
        .read()
        .expect("client lock poisoned")
        .clone()
        .ok_or_else(|| "the League client is not running".to_string())?;

    match client.import_rune_page(&draft).await {
        Ok(_) => Ok(()),
        Err(Error::NoRunePageSlot) => Err(NO_SLOT.to_string()),
        Err(error) => {
            tracing::warn!(%error, "rune import failed");
            Err(error.to_string())
        }
    }
}

#[cfg(test)]
mod tests {
    use super::BuildRunePage;

    fn page() -> BuildRunePage {
        BuildRunePage {
            primary_style_id: 8100,
            primary_keystone_id: 8112,
            primary_perk1_id: 8139,
            primary_perk2_id: 8138,
            primary_perk3_id: 8135,
            secondary_style_id: 8200,
            secondary_perk1_id: 8226,
            secondary_perk2_id: 8210,
            stat_offense: 5008,
            stat_flex: 5008,
            stat_defense: 5011,
        }
    }

    #[test]
    fn sends_every_perk_in_the_clients_order_under_our_name() {
        let draft = page().draft("Ahri").expect("a complete page");
        assert_eq!(draft.name, "TrueMain: Ahri");
        assert_eq!(draft.primary_style_id, 8100);
        assert_eq!(draft.sub_style_id, 8200);
        assert_eq!(
            draft.selected_perk_ids,
            vec![8112, 8139, 8138, 8135, 8226, 8210, 5008, 5008, 5011]
        );
    }

    #[test]
    fn refuses_a_page_with_an_empty_slot() {
        let mut incomplete = page();
        incomplete.stat_defense = 0;
        assert!(incomplete.draft("Ahri").is_none());
    }
}
