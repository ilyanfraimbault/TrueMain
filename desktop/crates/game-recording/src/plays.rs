//! The plays the game announces itself on its live feed (#1766): an epic
//! monster stolen, an ace, first blood. The match history's timeline marks
//! none of them, so they are read from the feed noted during the game, with
//! the game's player list to tell the sides apart, and kept beside the video
//! in game time like the highlights.
//!
//! Only what the feed says: a play whose side cannot be told from the names
//! it gives is left out, never guessed.

use lcu::live::{ActivePlayer, LiveEvent, LivePlayer};
use serde::{Deserialize, Serialize};

use crate::moments::{DragonKind, ObjectiveKind};

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum PlayKind {
    /// An epic monster's last hit taken from the side that was on it.
    Steal,
    /// A whole side dead at once.
    Ace,
    FirstBlood,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Play {
    pub kind: PlayKind,
    pub game_time_ms: i64,
    /// Whether the player's side made it: stole, aced, drew first blood.
    pub ally: bool,
    /// The player made it themself: the steal's last hit, first blood. Never
    /// set for an ace, which is a side's.
    pub by_me: bool,
    /// A steal's monster.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub objective: Option<ObjectiveKind>,
    /// A stolen drake's element.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub dragon: Option<DragonKind>,
}

/// The player's own line in the game's player list, under any of their names.
pub fn find_me<'a>(me: &ActivePlayer, players: &'a [LivePlayer]) -> Option<&'a LivePlayer> {
    players.iter().find(|player| {
        [&me.riot_id, &me.riot_id_game_name, &me.summoner_name]
            .iter()
            .any(|name| player.is_named(name))
    })
}

/// The monster an epic monster's kill on the feed is.
fn monster(event: &LiveEvent) -> Option<ObjectiveKind> {
    match event.event_name.as_str() {
        "DragonKill" if event.dragon_type == "Elder" => Some(ObjectiveKind::Elder),
        "DragonKill" => Some(ObjectiveKind::Dragon),
        "BaronKill" => Some(ObjectiveKind::Baron),
        "HeraldKill" => Some(ObjectiveKind::Herald),
        "HordeKill" => Some(ObjectiveKind::Grubs),
        "AtakhanKill" => Some(ObjectiveKind::Atakhan),
        _ => None,
    }
}

/// The steals, aces and first blood of the live feed. `players` is the
/// game's player list; without the player in it, only the plays the player
/// made themself are told.
pub fn from_live(events: &[LiveEvent], me: &ActivePlayer, players: &[LivePlayer]) -> Vec<Play> {
    let my_team = find_me(me, players)
        .map(|player| player.team.as_str())
        .filter(|team| !team.is_empty());
    let ally_team = |team: &str| {
        my_team
            .filter(|_| !team.is_empty())
            .map(|mine| mine == team)
    };
    let ally_named = |name: &str| {
        players
            .iter()
            .find(|player| player.is_named(name))
            .and_then(|player| ally_team(&player.team))
    };
    let mut plays: Vec<Play> = events
        .iter()
        .filter_map(|event| {
            let game_time_ms = (event.event_time * 1000.0).round() as i64;
            let (kind, by_me, ally, objective) = match event.event_name.as_str() {
                LiveEvent::FIRST_BLOOD => {
                    let by_me = me.is_named(&event.recipient);
                    let ally = by_me || ally_named(&event.recipient)?;
                    (PlayKind::FirstBlood, by_me, ally, None)
                }
                LiveEvent::ACE => (PlayKind::Ace, false, ally_team(&event.acing_team)?, None),
                _ if event.is_stolen() => {
                    let objective = monster(event)?;
                    let by_me = me.is_named(&event.killer_name);
                    let ally = by_me || ally_named(&event.killer_name)?;
                    (PlayKind::Steal, by_me, ally, Some(objective))
                }
                _ => return None,
            };
            Some(Play {
                kind,
                game_time_ms,
                ally,
                by_me,
                objective,
                dragon: (objective == Some(ObjectiveKind::Dragon))
                    .then(|| DragonKind::from_dragon_type(&event.dragon_type))
                    .flatten(),
            })
        })
        .collect();
    plays.sort_by_key(|play| play.game_time_ms);
    plays
}

#[cfg(test)]
pub(crate) mod tests {
    use super::*;

    pub(crate) fn me() -> ActivePlayer {
        ActivePlayer {
            riot_id: "Me#EUW".into(),
            riot_id_game_name: "Me".into(),
            summoner_name: String::new(),
        }
    }

    pub(crate) fn players() -> Vec<LivePlayer> {
        let player = |name: &str, champion: &str, team: &str| LivePlayer {
            champion_name: champion.into(),
            riot_id: format!("{name}#EUW"),
            team: team.into(),
            ..LivePlayer::default()
        };
        vec![
            player("Me", "Ahri", "ORDER"),
            player("Ally", "Lee Sin", "ORDER"),
            player("Foe", "Zed", "CHAOS"),
        ]
    }

    /// A fixture of the feed in its own shape, as `eventdata` serves it.
    pub(crate) fn feed() -> Vec<LiveEvent> {
        serde_json::from_str(
            r#"[
              { "EventID": 0, "EventName": "GameStart", "EventTime": 0.03 },
              { "EventID": 4, "EventName": "FirstBlood", "EventTime": 185.4, "Recipient": "Me" },
              { "EventID": 5, "EventName": "ChampionKill", "EventTime": 185.4, "KillerName": "Me", "VictimName": "Foe", "Assisters": [] },
              { "EventID": 20, "EventName": "HeraldKill", "EventTime": 840.0, "Stolen": "True", "KillerName": "Foe" },
              { "EventID": 31, "EventName": "DragonKill", "EventTime": 1020.0, "DragonType": "Water", "Stolen": "True", "KillerName": "Ally" },
              { "EventID": 40, "EventName": "BaronKill", "EventTime": 1500.0, "Stolen": "False", "KillerName": "Me" },
              { "EventID": 52, "EventName": "Ace", "EventTime": 1612.0, "Acer": "Me", "AcingTeam": "ORDER" },
              { "EventID": 60, "EventName": "DragonKill", "EventTime": 1900.0, "DragonType": "Elder", "Stolen": "True", "KillerName": "Me" },
              { "EventID": 61, "EventName": "BaronKill", "EventTime": 2000.0, "Stolen": "True", "KillerName": "Stranger" },
              { "EventID": 70, "EventName": "Ace", "EventTime": 2100.0, "Acer": "Foe", "AcingTeam": "CHAOS" }
            ]"#,
        )
        .unwrap()
    }

    #[test]
    fn steals_aces_and_first_blood_with_the_side_that_made_them() {
        let plays = from_live(&feed(), &me(), &players());
        let summary: Vec<(PlayKind, i64, bool, bool, Option<ObjectiveKind>)> = plays
            .iter()
            .map(|p| (p.kind, p.game_time_ms / 1000, p.ally, p.by_me, p.objective))
            .collect();
        assert_eq!(
            summary,
            vec![
                (PlayKind::FirstBlood, 185, true, true, None),
                (
                    PlayKind::Steal,
                    840,
                    false,
                    false,
                    Some(ObjectiveKind::Herald)
                ),
                (
                    PlayKind::Steal,
                    1020,
                    true,
                    false,
                    Some(ObjectiveKind::Dragon)
                ),
                // A Baron taken, not stolen, is no play.
                (PlayKind::Ace, 1612, true, false, None),
                (
                    PlayKind::Steal,
                    1900,
                    true,
                    true,
                    Some(ObjectiveKind::Elder)
                ),
                // A steal by a name the player list does not hold is left out.
                (PlayKind::Ace, 2100, false, false, None),
            ]
        );
        assert_eq!(plays[2].dragon, Some(DragonKind::Ocean));
        assert_eq!(plays[4].dragon, None);
    }

    #[test]
    fn without_the_player_list_only_the_players_own_plays_are_told() {
        let plays = from_live(&feed(), &me(), &[]);
        let kinds: Vec<(PlayKind, i64)> = plays
            .iter()
            .map(|p| (p.kind, p.game_time_ms / 1000))
            .collect();
        assert_eq!(
            kinds,
            vec![(PlayKind::FirstBlood, 185), (PlayKind::Steal, 1900)]
        );
        assert!(plays.iter().all(|p| p.ally && p.by_me));
    }

    #[test]
    fn the_player_is_found_in_the_list_under_any_name() {
        let players = players();
        let found = find_me(&me(), &players).unwrap();
        assert_eq!(found.champion_name, "Ahri");
        let stranger = ActivePlayer {
            riot_id_game_name: "Nobody".into(),
            ..ActivePlayer::default()
        };
        assert!(find_me(&stranger, &players).is_none());
    }
}
