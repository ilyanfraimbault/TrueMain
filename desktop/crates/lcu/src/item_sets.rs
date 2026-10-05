//! Pushing a build's items into the client as an item set (#1908).
//!
//! The second place this app writes to the client, after the rune page, and
//! under the same rules — a click only, and never a change to anything the
//! player made. Item sets make the second rule harder to keep: the client has
//! no per-set create or delete, only a `PUT` of the account's **whole** list.
//! An import is therefore a read-modify-write, and a bug in it is a wiped list.
//! Hence:
//!
//! * every set that is not ours goes back as the raw JSON it was read as, in
//!   its place — no typed struct that would drop a field we do not model;
//! * a list that could not be read, or does not decode as one, is never
//!   written back (`decode_item_sets`): the caller aborts;
//! * the app owns **one** set, replaced in place on every import
//!   (`plan_item_set_import`), so a player never ends up with one set per game.

use serde::{Deserialize, Serialize};
use serde_json::Value;

use crate::error::{Error, Result};
use crate::runes::OWNED_PAGE_PREFIX;

/// Marks the one set this app owns. The rune page's prefix, so both objects
/// the app writes read alike in the client.
pub const OWNED_SET_PREFIX: &str = OWNED_PAGE_PREFIX;

/// Summoner's Rift, the only map the set is offered on.
pub const SUMMONERS_RIFT: i64 = 11;

/// The build's items as the build view shows them, by item id. Ids already
/// filtered to the patch's items by the caller; an empty list is a block left
/// out, never padded.
#[derive(Debug, Clone, Default, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct BuildItems {
    #[serde(default)]
    pub starter: Vec<i64>,
    #[serde(default)]
    pub boots: Vec<i64>,
    #[serde(default)]
    pub core: Vec<i64>,
    #[serde(default)]
    pub situational: Vec<i64>,
}

/// One entry of a block. The client knows items by id, as a string.
#[derive(Debug, Clone, PartialEq, Eq, Serialize)]
pub struct ItemSetItem {
    pub id: String,
    pub count: u32,
}

/// One titled row of the set in the shop.
#[derive(Debug, Clone, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ItemSetBlock {
    /// The block's title — the client calls it `type`.
    #[serde(rename = "type")]
    pub title: String,
    pub items: Vec<ItemSetItem>,
    pub show_if_summoner_spell: String,
    pub hide_if_summoner_spell: String,
}

/// The set to write, without the fields the client owns (`uid`, `sortrank`).
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ItemSetDraft {
    pub title: String,
    pub associated_champions: Vec<i64>,
    pub associated_maps: Vec<i64>,
    pub blocks: Vec<ItemSetBlock>,
}

impl ItemSetDraft {
    /// The set for `champion`'s build: Starter, Boots, Core, Situational, each
    /// only when it has items. `None` when the build has none at all — an
    /// empty set in the shop would only say the import failed.
    pub fn from_build(champion: &str, champion_id: i64, build: &BuildItems) -> Option<Self> {
        let blocks: Vec<ItemSetBlock> = [
            ("Starter", &build.starter),
            ("Boots", &build.boots),
            ("Core", &build.core),
            ("Situational", &build.situational),
        ]
        .into_iter()
        .filter_map(|(title, ids)| block(title, ids))
        .collect();

        (champion_id > 0 && !blocks.is_empty()).then(|| Self {
            title: format!("{OWNED_SET_PREFIX}{}", champion.trim()),
            associated_champions: vec![champion_id],
            associated_maps: vec![SUMMONERS_RIFT],
            blocks,
        })
    }

    /// The draft written over `set`: our fields replaced, every other field the
    /// client keeps on it (`uid`, `sortrank`, ones we do not know) left alone.
    fn write_onto(&self, mut set: Value) -> Value {
        let draft = serde_json::to_value(self).expect("an item set draft always serialises");
        if let (Some(target), Value::Object(fields)) = (set.as_object_mut(), draft) {
            target.extend(fields);
            for (key, value) in [("type", "custom"), ("map", "any"), ("mode", "any")] {
                target.insert(key.to_string(), Value::from(value));
            }
        }
        set
    }

    /// A new set carrying the draft, under `uid`.
    fn new_set(&self, uid: &str) -> Value {
        self.write_onto(serde_json::json!({
            "uid": uid,
            "sortrank": 0,
            "startedFrom": "blank",
            "preferredItemSlots": [],
        }))
    }
}

/// A block of `ids` in build order, duplicates folded into `count` (two
/// potions are one entry of two). `None` for a block with nothing in it.
fn block(title: &str, ids: &[i64]) -> Option<ItemSetBlock> {
    let mut items: Vec<ItemSetItem> = Vec::new();
    for id in ids.iter().filter(|id| **id > 0).map(i64::to_string) {
        match items.iter_mut().find(|item| item.id == id) {
            Some(item) => item.count += 1,
            None => items.push(ItemSetItem { id, count: 1 }),
        }
    }
    (!items.is_empty()).then(|| ItemSetBlock {
        title: title.to_string(),
        items,
        show_if_summoner_spell: String::new(),
        hide_if_summoner_spell: String::new(),
    })
}

/// Whether this app wrote `set`.
///
/// Name-based, like the rune page, because the client gives us nothing else to
/// mark a set with: a player who renames one of their own sets to our prefix
/// hands it over, which is a deliberate act and the only false positive.
pub fn is_ours(set: &Value) -> bool {
    set.get("title")
        .and_then(Value::as_str)
        .is_some_and(|title| title.starts_with(OWNED_SET_PREFIX))
}

/// The account's item-set document, and its list of sets.
///
/// Fails rather than guessing on anything that is not an object carrying an
/// `itemSets` array: the list is written back whole, so a list we failed to
/// read would be written back as an empty one.
pub fn decode_item_sets(body: &str) -> Result<(Value, Vec<Value>)> {
    let document: Value = serde_json::from_str(body).map_err(|e| Error::Decode(e.to_string()))?;
    let sets = document
        .get("itemSets")
        .and_then(Value::as_array)
        .cloned()
        .ok_or_else(|| Error::Decode("the item-set list has no `itemSets` array".to_string()))?;
    Ok((document, sets))
}

/// The list to write back: `existing` with our set replaced by `draft`.
///
/// Separated from the requests so the rule that protects the player's sets is
/// covered by tests rather than by a comment:
///
/// * every set that is not ours stays, in its position, exactly as read;
/// * our set is rewritten in place, keeping its `uid` so the client keeps its
///   place; with none, one is appended under `fresh_uid`;
/// * further sets carrying our prefix are dropped — the app owns one.
pub fn plan_item_set_import(
    existing: &[Value],
    draft: &ItemSetDraft,
    fresh_uid: &str,
) -> Vec<Value> {
    let mut planned = Vec::with_capacity(existing.len() + 1);
    let mut placed = false;
    for set in existing {
        if !is_ours(set) {
            planned.push(set.clone());
        } else if !placed {
            planned.push(draft.write_onto(set.clone()));
            placed = true;
        }
    }
    if !placed {
        planned.push(draft.new_set(fresh_uid));
    }
    planned
}

/// The uids of the player's own sets, in order — what must read the same
/// before and after an import.
pub fn players_set_uids(sets: &[Value]) -> Vec<Option<String>> {
    sets.iter()
        .filter(|set| !is_ours(set))
        .map(|set| set.get("uid").and_then(Value::as_str).map(str::to_string))
        .collect()
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    fn build() -> BuildItems {
        BuildItems {
            starter: vec![1056, 2003, 2003],
            boots: vec![3020],
            core: vec![6655, 4645, 3089],
            situational: vec![],
        }
    }

    fn draft() -> ItemSetDraft {
        ItemSetDraft::from_build("Ahri", 103, &build()).expect("a build with items")
    }

    fn players_set(uid: &str, title: &str) -> Value {
        json!({
            "uid": uid,
            "title": title,
            "type": "custom",
            "map": "any",
            "mode": "any",
            "sortrank": 3,
            "startedFrom": "blank",
            "associatedChampions": [266],
            "associatedMaps": [11, 12],
            "preferredItemSlots": [{ "id": "3031", "preferredItemSlot": 2 }],
            "someFieldWeDoNotModel": { "nested": [1, 2, 3] },
            "blocks": [{ "type": "Mine", "items": [{ "id": "1055", "count": 1 }] }],
        })
    }

    #[test]
    fn folds_duplicate_starter_items_into_a_count() {
        let starter = &draft().blocks[0];
        assert_eq!(starter.title, "Starter");
        assert_eq!(
            starter.items,
            vec![
                ItemSetItem {
                    id: "1056".into(),
                    count: 1
                },
                ItemSetItem {
                    id: "2003".into(),
                    count: 2
                },
            ]
        );
    }

    #[test]
    fn writes_only_the_blocks_that_have_items_in_build_order() {
        let draft = draft();
        let titles: Vec<&str> = draft.blocks.iter().map(|b| b.title.as_str()).collect();
        assert_eq!(titles, vec!["Starter", "Boots", "Core"]);
        assert_eq!(draft.title, "TrueMain: Ahri");
        assert_eq!(draft.associated_champions, vec![103]);
        assert_eq!(draft.associated_maps, vec![SUMMONERS_RIFT]);
        let core: Vec<&str> = draft.blocks[2]
            .items
            .iter()
            .map(|i| i.id.as_str())
            .collect();
        assert_eq!(core, vec!["6655", "4645", "3089"]);
    }

    #[test]
    fn refuses_a_build_with_no_item() {
        assert!(ItemSetDraft::from_build("Ahri", 103, &BuildItems::default()).is_none());
        let zeros = BuildItems {
            core: vec![0, 0],
            ..BuildItems::default()
        };
        assert!(ItemSetDraft::from_build("Ahri", 103, &zeros).is_none());
    }

    #[test]
    fn creates_our_set_when_there_is_none() {
        let existing = vec![players_set("a", "My Teemo")];
        let planned = plan_item_set_import(&existing, &draft(), "fresh");
        assert_eq!(planned.len(), 2);
        assert_eq!(planned[0], existing[0]);
        let ours = &planned[1];
        assert_eq!(ours["uid"], "fresh");
        assert_eq!(ours["title"], "TrueMain: Ahri");
        assert_eq!(ours["type"], "custom");
        assert_eq!(ours["associatedChampions"], json!([103]));
        assert_eq!(ours["associatedMaps"], json!([11]));
        assert_eq!(ours["blocks"][0]["type"], "Starter");
        assert_eq!(
            ours["blocks"][0]["items"][1],
            json!({ "id": "2003", "count": 2 })
        );
    }

    #[test]
    fn replaces_our_set_in_place_and_keeps_its_uid() {
        let existing = vec![
            players_set("a", "My Teemo"),
            json!({ "uid": "ours", "title": "TrueMain: Teemo", "sortrank": 7, "associatedChampions": [17], "blocks": [] }),
            players_set("b", "Jungle"),
        ];
        let planned = plan_item_set_import(&existing, &draft(), "fresh");
        assert_eq!(planned.len(), 3);
        assert_eq!(planned[0], existing[0]);
        assert_eq!(planned[2], existing[2]);
        assert_eq!(planned[1]["uid"], "ours");
        assert_eq!(planned[1]["sortrank"], 7);
        assert_eq!(planned[1]["title"], "TrueMain: Ahri");
        assert_eq!(planned[1]["associatedChampions"], json!([103]));
        assert_eq!(planned[1]["blocks"].as_array().map(Vec::len), Some(3));
    }

    #[test]
    fn collapses_duplicate_sets_of_ours_into_one() {
        let existing = vec![
            json!({ "uid": "first", "title": "TrueMain: Ahri" }),
            players_set("a", "My Teemo"),
            json!({ "uid": "second", "title": "TrueMain: Zed" }),
        ];
        let planned = plan_item_set_import(&existing, &draft(), "fresh");
        assert_eq!(planned.len(), 2);
        assert_eq!(planned[0]["uid"], "first");
        assert_eq!(planned[1], existing[1]);
        assert_eq!(planned.iter().filter(|set| is_ours(set)).count(), 1);
    }

    #[test]
    fn keeps_every_set_the_player_made_verbatim() {
        // Fields we do not model must survive: the list goes back whole.
        let existing = vec![
            players_set("a", "My Teemo"),
            players_set("b", "Jungle"),
            json!({ "uid": "c", "title": "Untyped", "future": true }),
            json!({ "uid": "d" }),
        ];
        let planned = plan_item_set_import(&existing, &draft(), "fresh");
        assert_eq!(&planned[..4], &existing[..]);
        assert_eq!(players_set_uids(&planned), players_set_uids(&existing));
    }

    #[test]
    fn recognises_only_our_own_prefix() {
        assert!(is_ours(&json!({ "title": "TrueMain: Ahri" })));
        assert!(!is_ours(&json!({ "title": "truemain ahri" })));
        assert!(!is_ours(&json!({ "title": "My TrueMain set" })));
        assert!(!is_ours(&json!({ "uid": "no title" })));
    }

    #[test]
    fn a_list_that_does_not_decode_is_an_error_not_an_empty_list() {
        assert!(decode_item_sets("not json").is_err());
        assert!(decode_item_sets(r#"{ "accountId": 1 }"#).is_err());
        assert!(decode_item_sets(r#"{ "itemSets": null }"#).is_err());
        assert!(decode_item_sets("[]").is_err());

        let (document, sets) =
            decode_item_sets(r#"{ "accountId": 1, "itemSets": [{ "uid": "a" }], "timestamp": 5 }"#)
                .expect("a well-formed list");
        assert_eq!(sets.len(), 1);
        assert_eq!(document["timestamp"], 5);
    }
}
