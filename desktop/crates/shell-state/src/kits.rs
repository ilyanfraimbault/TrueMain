//! The player's kits (#2185): their own builds — a branching item tree, a rune
//! page and two summoner spells per champion and position — and which one is
//! used on each champion and position.
//!
//! Kept by the shell in its own file next to the overlay's settings, read and
//! written whole by the app (`kits_get` / `kits_set`), and read the same
//! lenient way: a kit that does not read is dropped, never the file, so one
//! bad entry cannot cost the player every other kit. The rules a kit must keep
//! to be used anywhere — a path at most six items with the boots, two spells,
//! a complete rune page — are enforced here too, since the overlay and the
//! client writes trust what this file holds.

use std::collections::{BTreeMap, HashSet};
use std::fs;
use std::io;
use std::path::Path;

use serde::de::DeserializeOwned;
use serde::{Deserialize, Serialize};
use serde_json::Value;

/// The file's format version, written into it so a later format can migrate
/// rather than guess.
pub const KITS_VERSION: u32 = 1;

/// The most items a path holds, the boots counted: a full build.
pub const MAX_PATH_ITEMS: usize = 6;

/// The longest name kept, in characters; the app's editor stops at the same.
pub const MAX_NAME_CHARS: usize = 40;

/// Caps that keep a hand-edited or corrupted file from growing without bound.
const MAX_KITS: usize = 500;
const MAX_CHILDREN: usize = 12;
const MAX_STARTER: usize = 6;
/// Boots the player may pick between; the first is the kit's default.
const MAX_BOOTS: usize = 4;

/// A kit's rune page: every perk by id, as the build's page names them.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct KitRunes {
    pub primary_style_id: i64,
    pub primary_keystone_id: i64,
    pub primary_perk1_id: i64,
    pub primary_perk2_id: i64,
    pub primary_perk3_id: i64,
    pub secondary_style_id: i64,
    pub secondary_perk1_id: i64,
    pub secondary_perk2_id: i64,
    pub stat_offense: i64,
    pub stat_flex: i64,
    pub stat_defense: i64,
}

impl KitRunes {
    fn complete(&self) -> bool {
        [
            self.primary_style_id,
            self.primary_keystone_id,
            self.primary_perk1_id,
            self.primary_perk2_id,
            self.primary_perk3_id,
            self.secondary_style_id,
            self.secondary_perk1_id,
            self.secondary_perk2_id,
            self.stat_offense,
            self.stat_flex,
            self.stat_defense,
        ]
        .iter()
        .all(|id| *id > 0)
            && self.primary_style_id != self.secondary_style_id
    }
}

/// One step of the item tree: an item and what can follow it, the first child
/// the default.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct KitNode {
    pub item_id: i64,
    #[serde(default)]
    pub children: Vec<KitNode>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Kit {
    pub id: String,
    pub name: String,
    pub champion_id: i64,
    pub position: String,
    pub runes: KitRunes,
    pub spells: [i64; 2],
    #[serde(default)]
    pub starter: Vec<i64>,
    /// The boots to pick between, the first the default. A file written before
    /// several were allowed holds one id or `null`; both still read.
    #[serde(default, deserialize_with = "boots_list")]
    pub boots: Vec<i64>,
    #[serde(default)]
    pub tree: Vec<KitNode>,
    #[serde(default)]
    pub updated_at: String,
}

impl Kit {
    /// The kit as it may be kept: trimmed to its caps, every path cut to a full
    /// build and stripped of a repeated item, or `None` when it is not a kit
    /// anything could use.
    fn sanitized(mut self) -> Option<Self> {
        self.name = self.name.trim().chars().take(MAX_NAME_CHARS).collect();
        let [first, second] = self.spells;
        let usable = !self.id.is_empty()
            && !self.name.is_empty()
            && self.champion_id > 0
            && !self.position.is_empty()
            && self.runes.complete()
            && first > 0
            && second > 0
            && first != second;
        if !usable {
            return None;
        }
        let mut seen = HashSet::new();
        self.boots.retain(|id| *id > 0 && seen.insert(*id));
        self.boots.truncate(MAX_BOOTS);
        self.starter.retain(|id| *id > 0);
        self.starter.truncate(MAX_STARTER);
        let depth = MAX_PATH_ITEMS - usize::from(!self.boots.is_empty());
        let mut above = self.boots.clone();
        self.tree = prune(std::mem::take(&mut self.tree), depth, &mut above);
        Some(self)
    }

    /// The key this kit is active under.
    pub fn key(&self) -> String {
        key(self.champion_id, &self.position)
    }
}

/// `{championId}:{position}`, the key of the store's `active` map.
pub fn key(champion_id: i64, position: &str) -> String {
    format!("{champion_id}:{position}")
}

/// `nodes` kept to `depth` levels: a node whose item already sits above it on
/// its path, or is not an item, goes with its subtree.
fn prune(nodes: Vec<KitNode>, depth: usize, above: &mut Vec<i64>) -> Vec<KitNode> {
    if depth == 0 {
        return Vec::new();
    }
    let mut kept = Vec::new();
    for node in nodes {
        if node.item_id <= 0 || above.contains(&node.item_id) || kept.len() == MAX_CHILDREN {
            continue;
        }
        above.push(node.item_id);
        let children = prune(node.children, depth - 1, above);
        above.pop();
        kept.push(KitNode {
            item_id: node.item_id,
            children,
        });
    }
    kept
}

/// Every kit, and the one used on each champion and position.
#[derive(Debug, Clone, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct KitStore {
    pub kits: Vec<Kit>,
    pub active: BTreeMap<String, String>,
}

impl KitStore {
    /// Read the file. A missing or unreadable file is no kits.
    pub fn load(path: &Path) -> Self {
        fs::read_to_string(path)
            .map(|body| Self::from_json(&body))
            .unwrap_or_default()
    }

    /// Parse a store leniently, the file's or the app's: each kit that does
    /// not read, or breaks a rule, is dropped alone; a second kit under one id
    /// is dropped; an active entry naming no kit of its own champion and
    /// position is forgotten.
    pub fn from_json(body: &str) -> Self {
        let Ok(Value::Object(fields)) = serde_json::from_str::<Value>(body) else {
            return Self::default();
        };
        let mut seen = HashSet::new();
        let kits: Vec<Kit> = fields
            .get("kits")
            .and_then(Value::as_array)
            .into_iter()
            .flatten()
            .filter_map(|value| parse::<Kit>(Some(value)))
            .filter_map(Kit::sanitized)
            .filter(|kit| seen.insert(kit.id.clone()))
            .take(MAX_KITS)
            .collect();
        let active = parse::<BTreeMap<String, String>>(fields.get("active"))
            .unwrap_or_default()
            .into_iter()
            .filter(|(key, id)| kits.iter().any(|kit| &kit.id == id && &kit.key() == key))
            .collect();
        Self { kits, active }
    }

    /// The kit used on a champion and position, if one is.
    pub fn active_kit(&self, champion_id: i64, position: &str) -> Option<&Kit> {
        let id = self.active.get(&key(champion_id, position))?;
        self.kits.iter().find(|kit| &kit.id == id)
    }

    /// Write the store, versioned, through a temporary file so a crash
    /// mid-write never leaves half a file behind.
    pub fn save(&self, path: &Path) -> io::Result<()> {
        let mut value = serde_json::to_value(self).map_err(io::Error::other)?;
        value["version"] = KITS_VERSION.into();
        let body = serde_json::to_string_pretty(&value).map_err(io::Error::other)?;
        if let Some(parent) = path.parent() {
            fs::create_dir_all(parent)?;
        }
        let temporary = path.with_extension("json.tmp");
        fs::write(&temporary, body)?;
        fs::rename(&temporary, path)
    }
}

/// A kit's boots as a list, from a list, a single id or nothing.
fn boots_list<'de, D: serde::Deserializer<'de>>(deserializer: D) -> Result<Vec<i64>, D::Error> {
    Ok(match Value::deserialize(deserializer)? {
        Value::Array(ids) => ids.iter().filter_map(Value::as_i64).collect(),
        Value::Number(id) => id.as_i64().into_iter().collect(),
        _ => Vec::new(),
    })
}

fn parse<T: DeserializeOwned>(value: Option<&Value>) -> Option<T> {
    value.and_then(|value| serde_json::from_value(value.clone()).ok())
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    fn runes() -> Value {
        json!({
            "primaryStyleId": 8100, "primaryKeystoneId": 8112, "primaryPerk1Id": 8139,
            "primaryPerk2Id": 8138, "primaryPerk3Id": 8135, "secondaryStyleId": 8200,
            "secondaryPerk1Id": 8226, "secondaryPerk2Id": 8210,
            "statOffense": 5008, "statFlex": 5008, "statDefense": 5011
        })
    }

    fn kit(id: &str, tree: Value) -> Value {
        json!({
            "id": id, "name": "Into tanks", "championId": 103, "position": "MIDDLE",
            "runes": runes(), "spells": [4, 14], "starter": [1056, 2003, 2003],
            "boots": [3020], "tree": tree, "updatedAt": "2026-10-20T10:00:00Z"
        })
    }

    fn chain(items: &[i64]) -> Value {
        items.iter().rev().fold(
            json!([]),
            |children, item| json!([{ "itemId": item, "children": children }]),
        )
    }

    fn depth(nodes: &[KitNode]) -> usize {
        nodes
            .iter()
            .map(|node| 1 + depth(&node.children))
            .max()
            .unwrap_or(0)
    }

    #[test]
    fn keeps_a_branching_kit_and_its_active_mark() {
        let tree = json!([{ "itemId": 6655, "children": [
            { "itemId": 4645, "children": [] },
            { "itemId": 3157, "children": [] }
        ]}]);
        let body =
            json!({ "version": 1, "kits": [kit("a", tree)], "active": { "103:MIDDLE": "a" } });
        let store = KitStore::from_json(&body.to_string());
        assert_eq!(store.kits.len(), 1);
        assert_eq!(store.kits[0].tree[0].children.len(), 2);
        assert_eq!(
            store.active_kit(103, "MIDDLE").map(|k| k.id.as_str()),
            Some("a")
        );
        assert!(store.active_kit(103, "TOP").is_none());
    }

    #[test]
    fn cuts_every_path_to_a_full_build_with_the_boots() {
        let body = json!({ "kits": [kit("a", chain(&[1, 2, 3, 4, 5, 6, 7]))] });
        let store = KitStore::from_json(&body.to_string());
        assert_eq!(depth(&store.kits[0].tree), MAX_PATH_ITEMS - 1);

        let mut bootless = kit("b", chain(&[1, 2, 3, 4, 5, 6, 7]));
        bootless["boots"] = json!([]);
        let store = KitStore::from_json(&json!({ "kits": [bootless] }).to_string());
        assert_eq!(depth(&store.kits[0].tree), MAX_PATH_ITEMS);
    }

    #[test]
    fn reads_one_or_several_boots_and_keeps_them_off_every_path() {
        let mut legacy = kit("a", chain(&[6655]));
        legacy["boots"] = json!(3020);
        let mut none = kit("b", chain(&[6655]));
        none["boots"] = Value::Null;
        let mut several = kit("c", chain(&[6655, 3047]));
        several["boots"] = json!([3020, 3047, 3020, 0, 3111, 3158, 3006]);
        let store = KitStore::from_json(&json!({ "kits": [legacy, none, several] }).to_string());
        assert_eq!(store.kits[0].boots, vec![3020]);
        assert!(store.kits[1].boots.is_empty());
        // Repeats and non-items go, then the cap; a boots on a path goes with its subtree.
        assert_eq!(store.kits[2].boots, vec![3020, 3047, 3111, 3158]);
        assert!(store.kits[2].tree[0].children.is_empty());
    }

    #[test]
    fn drops_an_item_repeated_on_its_path_but_not_across_branches() {
        let tree = json!([{ "itemId": 6655, "children": [
            { "itemId": 6655, "children": [] },
            { "itemId": 3020, "children": [] },
            { "itemId": 4645, "children": [{ "itemId": 3157, "children": [] }] },
            { "itemId": 3157, "children": [] }
        ]}]);
        let store = KitStore::from_json(&json!({ "kits": [kit("a", tree)] }).to_string());
        let children: Vec<i64> = store.kits[0].tree[0]
            .children
            .iter()
            .map(|n| n.item_id)
            .collect();
        // The repeat and the boots (already in their slot) go; 3157 stays on both branches.
        assert_eq!(children, vec![4645, 3157]);
    }

    #[test]
    fn drops_a_kit_that_breaks_a_rule_and_keeps_the_others() {
        let mut same_spells = kit("b", chain(&[6655]));
        same_spells["spells"] = json!([4, 4]);
        let mut half_page = kit("c", chain(&[6655]));
        half_page["runes"]["statDefense"] = json!(0);
        let mut nameless = kit("d", chain(&[6655]));
        nameless["name"] = json!("   ");
        let body = json!({ "kits": [
            kit("a", chain(&[6655])), same_spells, half_page, nameless, "not a kit", kit("a", chain(&[3157]))
        ], "active": { "103:MIDDLE": "b", "103:TOP": "a" } });
        let store = KitStore::from_json(&body.to_string());
        assert_eq!(store.kits.len(), 1);
        assert_eq!(store.kits[0].tree[0].item_id, 6655);
        // "b" is gone, and "a" is not a TOP kit.
        assert!(store.active.is_empty());
    }

    #[test]
    fn trims_a_long_name() {
        let mut long = kit("a", chain(&[6655]));
        long["name"] = json!(format!("  {}  ", "x".repeat(60)));
        let store = KitStore::from_json(&json!({ "kits": [long] }).to_string());
        assert_eq!(store.kits[0].name.chars().count(), MAX_NAME_CHARS);
    }

    #[test]
    fn reads_nothing_from_a_broken_file() {
        assert_eq!(KitStore::from_json("not json"), KitStore::default());
        assert_eq!(KitStore::from_json("[]"), KitStore::default());
    }

    #[test]
    fn saves_what_it_reads() {
        let dir = std::env::temp_dir().join(format!("kits-{}", std::process::id()));
        let path = dir.join("kits.json");
        let body =
            json!({ "kits": [kit("a", chain(&[6655, 4645]))], "active": { "103:MIDDLE": "a" } });
        let store = KitStore::from_json(&body.to_string());
        store.save(&path).unwrap();
        assert_eq!(KitStore::load(&path), store);
        let written: Value = serde_json::from_str(&fs::read_to_string(&path).unwrap()).unwrap();
        assert_eq!(written["version"], KITS_VERSION);
        let _ = fs::remove_dir_all(dir);
    }
}
