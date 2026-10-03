//! Pushing a rune page into the client.
//!
//! The only place this app **writes** to the client, so it carries its own
//! boundaries:
//!
//! * it runs on an explicit user click, never on a pick or a phase change.
//!   Riot's player-facing policy forbids an application "taking actions on your
//!   behalf", and the distance between importing runes on click and
//!   auto-accepting a queue is the distance between a tool and a bannable one;
//! * it never deletes a page it did not create. Overwriting somebody's own rune
//!   pages because we ran out of slots would be an unforced disaster, so a full
//!   page list with no page of ours is reported back, not resolved by deleting
//!   one of theirs.

use serde::{Deserialize, Serialize};

/// Marks the one page this app owns, so it can be reused across games instead
/// of accumulating a new page every draft.
pub const OWNED_PAGE_PREFIX: &str = "TrueMain: ";

#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct RunePage {
    pub id: i64,
    #[serde(default)]
    pub name: String,
    /// False for the pages the client generates itself, which cannot be removed.
    #[serde(default)]
    pub is_deletable: bool,
}

impl RunePage {
    /// Whether this app created the page.
    ///
    /// Name-based because the client gives us nothing else to mark a page with;
    /// a player who renames one of their own pages to match takes it over, which
    /// is a deliberate act and the only false positive available.
    pub fn is_ours(&self) -> bool {
        self.name.starts_with(OWNED_PAGE_PREFIX)
    }
}

/// The page to send to the client.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct RunePageDraft {
    pub name: String,
    pub primary_style_id: i64,
    pub sub_style_id: i64,
    /// Perk ids, in the client's slot order. The client knows perks only by id;
    /// there is no name it would accept.
    pub selected_perk_ids: Vec<i64>,
}

/// What to do before creating the new page.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum RuneImportPlan {
    /// Room to spare: create the page outright.
    Create,
    /// Our previous page exists; delete it and create the new one in its place.
    ReplaceOwn { page_id: i64 },
    /// The list is full and none of it is ours. The user has to free a slot —
    /// we will not choose one for them.
    NoSlotAvailable,
}

/// Decide what the import has to do, given the pages that exist.
///
/// Separated from the requests so the rule that protects the player's own pages
/// is covered by tests rather than by a comment.
pub fn plan_import(pages: &[RunePage], max_pages: usize) -> RuneImportPlan {
    if let Some(ours) = pages
        .iter()
        .find(|page| page.is_ours() && page.is_deletable)
    {
        return RuneImportPlan::ReplaceOwn { page_id: ours.id };
    }

    // Only editable pages count against the limit; the client's own generated
    // pages sit in the list without occupying a slot the player can free.
    let used = pages.iter().filter(|page| page.is_deletable).count();
    if used < max_pages {
        RuneImportPlan::Create
    } else {
        RuneImportPlan::NoSlotAvailable
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn page(id: i64, name: &str, deletable: bool) -> RunePage {
        RunePage {
            id,
            name: name.to_string(),
            is_deletable: deletable,
        }
    }

    #[test]
    fn creates_a_page_when_there_is_room() {
        let pages = vec![page(1, "My Ahri", true)];
        assert_eq!(plan_import(&pages, 5), RuneImportPlan::Create);
    }

    #[test]
    fn reuses_our_own_page_instead_of_accumulating_one_per_game() {
        let pages = vec![page(1, "My Ahri", true), page(2, "TrueMain: Ahri", true)];
        assert_eq!(
            plan_import(&pages, 5),
            RuneImportPlan::ReplaceOwn { page_id: 2 }
        );
    }

    #[test]
    fn reuses_our_page_even_when_the_list_is_full() {
        let pages = vec![
            page(1, "My Ahri", true),
            page(2, "TrueMain: Ahri", true),
            page(3, "Jungle", true),
        ];
        assert_eq!(
            plan_import(&pages, 3),
            RuneImportPlan::ReplaceOwn { page_id: 2 }
        );
    }

    #[test]
    fn never_deletes_a_page_the_player_made() {
        // The whole point: a full list with nothing of ours must come back as a
        // refusal, not as a freed slot.
        let pages = vec![page(1, "My Ahri", true), page(2, "Jungle", true)];
        assert_eq!(plan_import(&pages, 2), RuneImportPlan::NoSlotAvailable);
    }

    #[test]
    fn the_clients_own_undeletable_pages_do_not_use_up_the_budget() {
        // Champion-recommended pages appear in the list but are not the
        // player's to free; counting them would make us refuse with slots left.
        let pages = vec![page(1, "Recommended", false), page(2, "My Ahri", true)];
        assert_eq!(plan_import(&pages, 2), RuneImportPlan::Create);
    }

    #[test]
    fn a_page_of_ours_that_cannot_be_deleted_is_not_treated_as_reusable() {
        let pages = vec![page(1, "TrueMain: Ahri", false)];
        assert_eq!(plan_import(&pages, 1), RuneImportPlan::Create);
    }

    #[test]
    fn recognises_only_our_own_prefix() {
        assert!(page(1, "TrueMain: Ahri", true).is_ours());
        assert!(!page(1, "truemain ahri", true).is_ours());
        assert!(!page(1, "My TrueMain page", true).is_ours());
    }
}
