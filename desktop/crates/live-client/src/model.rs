//! `GET /liveclientdata/allgamedata`, in the game's own shape.
//!
//! Only the fields the app reads are modelled; serde skips the rest (champion
//! stats, ability descriptions, runes), and the raw body is
//! what a tape records, so a later panel can start reading them without a new
//! recording. Every field defaults: the game answers with partial data around
//! the loading screen, and in spectator mode `activePlayer` is an error object
//! rather than a player — neither may fail the whole reading.

use serde::Deserialize;

#[derive(Debug, Clone, Default, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct AllGameData {
    pub active_player: ActivePlayer,
    pub all_players: Vec<Player>,
    pub events: Events,
    pub game_data: GameData,
}

/// The game's event feed: every event since the start, on every reading.
#[derive(Debug, Clone, Default, Deserialize)]
#[serde(default)]
pub struct Events {
    #[serde(rename = "Events")]
    pub events: Vec<Event>,
}

/// One event, in the feed's own PascalCase. Only the objectives' fields are
/// modelled; the rest of the feed (kills, multikills, aces) is skipped.
#[derive(Debug, Clone, Default, Deserialize)]
#[serde(rename_all = "PascalCase", default)]
pub struct Event {
    /// `TurretKilled`, `InhibKilled`, `DragonKill`, `BaronKill`, …
    pub event_name: String,
    /// Game time, in seconds.
    pub event_time: f64,
    /// The structure that fell: `Turret_T2_C_05_A` — `T1` is blue side's.
    pub turret_killed: String,
    /// `Barracks_T1_L1` — `T1` is blue side's.
    pub inhib_killed: String,
    /// `Fire`, `Water`, `Earth`, `Air`, `Hextech`, `Chemtech`, `Elder`.
    pub dragon_type: String,
    /// A player's name, or a minion's or turret's (`Minion_T2L1S16N3`).
    pub killer_name: String,
}

/// The player this game client belongs to. All three names are kept: which
/// of them identifies a player in `allPlayers` moved with the Riot ID
/// migration.
#[derive(Debug, Clone, Default, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct ActivePlayer {
    /// `Name#TAG`.
    pub riot_id: String,
    pub riot_id_game_name: String,
    pub summoner_name: String,
    /// Unspent gold, fractional: the game pays income by the tick.
    pub current_gold: f64,
}

#[derive(Debug, Clone, Default, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct Player {
    /// Localised display name (`Wukong`).
    pub champion_name: String,
    /// `game_character_displayname_MonkeyKing` — the champion's alias behind a
    /// fixed prefix, the same in every locale.
    pub raw_champion_name: String,
    pub riot_id: String,
    pub riot_id_game_name: String,
    pub summoner_name: String,
    /// `ORDER` (blue side) or `CHAOS` (red side).
    pub team: String,
    /// `TOP`, `JUNGLE`, `MIDDLE`, `BOTTOM`, `UTILITY`; empty in queues without
    /// lanes.
    pub position: String,
    pub level: i64,
    pub is_bot: bool,
    pub is_dead: bool,
    /// Seconds left before the player respawns; meaningful while `is_dead`.
    pub respawn_timer: f64,
    pub items: Vec<Item>,
    pub scores: Scores,
    pub summoner_spells: SummonerSpells,
}

#[derive(Debug, Clone, Default, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct Item {
    #[serde(rename = "itemID")]
    pub item_id: i64,
    /// `0..=5` the inventory, `6` the trinket.
    pub slot: i64,
    /// More than one for stacked consumables (potions, control wards).
    pub count: i64,
    pub display_name: String,
    /// The item's full cost in gold.
    pub price: i64,
}

#[derive(Debug, Clone, Default, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct Scores {
    pub kills: i64,
    pub deaths: i64,
    pub assists: i64,
    pub creep_score: i64,
    pub ward_score: f64,
}

#[derive(Debug, Clone, Default, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct SummonerSpells {
    pub summoner_spell_one: SummonerSpell,
    pub summoner_spell_two: SummonerSpell,
}

#[derive(Debug, Clone, Default, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct SummonerSpell {
    /// Localised (`Flash`).
    pub display_name: String,
    /// `GeneratedTip_SummonerSpell_SummonerFlash_DisplayName` — the spell's
    /// Data Dragon id behind a fixed prefix and suffix.
    pub raw_display_name: String,
}

#[derive(Debug, Clone, Default, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct GameData {
    /// Seconds since the game started.
    pub game_time: f64,
    /// `CLASSIC`, `ARAM`, …
    pub game_mode: String,
    /// `11` is Summoner's Rift, `12` the Howling Abyss.
    pub map_number: i64,
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn a_spectated_game_has_no_active_player_and_still_parses() {
        // The game answers this in spectator mode, in place of the player.
        let data: AllGameData = serde_json::from_value(serde_json::json!({
            "activePlayer": { "error": "Spectator mode doesn't currently support this feature" },
            "allPlayers": [{ "championName": "Ahri", "team": "ORDER", "items": [] }],
            "gameData": { "gameTime": 12.5 }
        }))
        .unwrap();
        assert!(data.active_player.riot_id.is_empty());
        assert_eq!(data.all_players.len(), 1);
        assert_eq!(data.game_data.game_time, 12.5);
    }

    #[test]
    fn reads_the_item_id_under_its_own_casing() {
        let item: Item = serde_json::from_value(serde_json::json!({
            "itemID": 3157, "slot": 2, "count": 1, "displayName": "Zhonya's Hourglass", "price": 3250
        }))
        .unwrap();
        assert_eq!(item.item_id, 3157);
        assert_eq!(item.price, 3250);
    }
}
