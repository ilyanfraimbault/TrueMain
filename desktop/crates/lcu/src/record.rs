//! The logged-in player's own record, as their client knows it: ranked
//! standing, profile background, and recent games.
//!
//! Read from the client rather than from TrueMain because the client knows
//! every player, and TrueMain only tracks true mains (#1682). The dashboard is
//! for whoever installed the app, so it cannot depend on being tracked.
//!
//! Like `model`, every payload is `#[serde(default)]`: match history is the
//! oldest part of the client's API, converted from a newer format on Riot's
//! side, and a field that goes missing must degrade one tile, not the page.

use serde::{Deserialize, Serialize};

/// `GET /lol-ranked/v1/current-ranked-stats`, narrowed to its queue list.
#[derive(Debug, Clone, Default, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct RankedStats {
    pub queues: Vec<RankedQueue>,
}

/// One queue's standing this season.
#[derive(Debug, Clone, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct RankedQueue {
    /// `RANKED_SOLO_5x5`, `RANKED_FLEX_SR`, …
    pub queue_type: String,
    /// `IRON` … `CHALLENGER`; empty or `NONE` while unranked.
    pub tier: String,
    /// `I` … `IV`; `NA` on the apex tiers, which have no divisions.
    pub division: String,
    pub league_points: i64,
    pub wins: i64,
    pub losses: i64,
    pub is_provisional: bool,
    pub provisional_games_remaining: i64,
}

impl RankedQueue {
    pub fn is_ranked(&self) -> bool {
        !self.tier.is_empty() && self.tier != "NONE"
    }
}

/// The queues the dashboard shows a standing for, in the order it shows them.
pub const RANKED_QUEUES: [&str; 2] = ["RANKED_SOLO_5x5", "RANKED_FLEX_SR"];

impl RankedStats {
    /// The standings worth a card: the two Summoner's Rift queues, ranked or
    /// on their placements. The rest (TFT, Arena) is not this app's subject.
    pub fn rift_queues(&self) -> Vec<RankedQueue> {
        RANKED_QUEUES
            .iter()
            .filter_map(|kind| self.queues.iter().find(|q| q.queue_type == *kind))
            .filter(|queue| queue.is_ranked() || queue.wins + queue.losses > 0)
            .cloned()
            .collect()
    }
}

/// `GET /lol-summoner/v1/current-summoner/summoner-profile`: the skin the
/// player chose as their profile background.
#[derive(Debug, Clone, Default, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct SummonerProfile {
    /// `championId * 1000 + skin number`; zero when the player never chose one.
    pub background_skin_id: i64,
}

/// `GET /lol-match-history/v1/products/lol/current-summoner/matches`.
#[derive(Debug, Clone, Default, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct MatchHistory {
    /// The shard the player's games are on, e.g. `EUW1`.
    pub platform_id: String,
    pub games: MatchHistoryPage,
}

#[derive(Debug, Clone, Default, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct MatchHistoryPage {
    pub games: Vec<HistoryGame>,
}

/// One game. In the history list its participants are the player alone; the
/// same shape from `/lol-match-history/v1/games/{id}` carries all ten.
#[derive(Debug, Clone, Default, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct HistoryGame {
    pub game_id: i64,
    /// Epoch milliseconds.
    pub game_creation: i64,
    pub game_duration: i64,
    pub queue_id: i64,
    pub map_id: i64,
    pub game_mode: String,
    pub game_version: String,
    pub participant_identities: Vec<HistoryIdentity>,
    pub participants: Vec<HistoryParticipant>,
}

impl HistoryGame {
    /// The game's length in seconds, whichever unit the client reported it in.
    pub fn duration_seconds(&self) -> i64 {
        if self.game_duration > DURATION_IN_MS_ABOVE {
            self.game_duration / 1000
        } else {
            self.game_duration
        }
    }

    /// The role a participant was assigned, read from their slot. On a queue
    /// that assigns roles, Riot lists each team in role order — top, jungle,
    /// mid, bottom, support — as participants 1–5 for blue and 6–10 for red,
    /// the order the client's own scoreboard draws them in; TrueMain's copy of
    /// the same games (Riot's `teamPosition`) agrees player for player. `None`
    /// on a queue without roles (blind pick, ARAM, Arena, customs), where the
    /// order means nothing.
    ///
    /// The participant's `timeline.lane` is not read: it is Riot's old position
    /// guess, which files a roaming laner as a second jungler.
    pub fn position_of(&self, participant: &HistoryParticipant) -> Option<&'static str> {
        if self.map_id != SUMMONERS_RIFT || !ROLE_QUEUES.contains(&self.queue_id) {
            return None;
        }
        let first = match participant.team_id {
            100 => 1,
            200 => 6,
            _ => return None,
        };
        let slot = usize::try_from(participant.participant_id - first).ok()?;
        ROLES.get(slot).copied()
    }
}

const SUMMONERS_RIFT: i64 = 11;

/// The queues where champ select assigns every player a role: normal draft,
/// ranked Solo/Duo and Flex, Swiftplay, Quickplay and Clash.
const ROLE_QUEUES: [i64; 6] = [400, 420, 440, 480, 490, 700];

/// The roles in the order Riot lists each team, in the site's vocabulary.
const ROLES: [&str; 5] = ["TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"];

/// Who played a participant slot. The history list carries the player alone;
/// a full scoreboard names all ten.
#[derive(Debug, Clone, Default, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct HistoryIdentity {
    pub participant_id: i64,
    pub player: HistoryPlayer,
}

#[derive(Debug, Clone, Default, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct HistoryPlayer {
    pub game_name: String,
    pub tag_line: String,
    pub summoner_name: String,
}

#[derive(Debug, Clone, Default, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct HistoryParticipant {
    pub participant_id: i64,
    pub team_id: i64,
    pub champion_id: i64,
    pub spell1_id: i64,
    pub spell2_id: i64,
    pub stats: HistoryStats,
}

#[derive(Debug, Clone, Default, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct HistoryStats {
    pub win: bool,
    pub kills: i64,
    pub deaths: i64,
    pub assists: i64,
    pub champ_level: i64,
    pub total_minions_killed: i64,
    pub neutral_minions_killed: i64,
    pub gold_earned: i64,
    pub total_damage_dealt_to_champions: i64,
    pub vision_score: i64,
    pub largest_multi_kill: i64,
    pub game_ended_in_early_surrender: bool,
    pub item0: i64,
    pub item1: i64,
    pub item2: i64,
    pub item3: i64,
    pub item4: i64,
    pub item5: i64,
    pub item6: i64,
    /// Riot's role-bound slot, outside the six: a bot laner's boots once the
    /// role quest is done, the other roles' quest reward. Zero when empty.
    pub role_bound_item: i64,
    /// The keystone; `perk1`..`perk3` the rest of the primary tree, `perk4`
    /// and `perk5` the secondary.
    pub perk0: i64,
    pub perk1: i64,
    pub perk2: i64,
    pub perk3: i64,
    pub perk4: i64,
    pub perk5: i64,
    pub perk_primary_style: i64,
    pub perk_sub_style: i64,
    /// The three stat shards: offense, flex, defense.
    pub stat_perk0: i64,
    pub stat_perk1: i64,
    pub stat_perk2: i64,
}

/// A game ending this early was a remake: it counts for nothing, so it is
/// shown but kept out of every average. Riot opens the remake vote at three
/// minutes and a remade game ends well before five.
const REMAKE_SECONDS: i64 = 300;

/// The history reports seconds today; older client builds reported
/// milliseconds. No game lasts anywhere near this many seconds, so a value past
/// it can only be milliseconds.
const DURATION_IN_MS_ABOVE: i64 = 100_000;

/// One of the player's games, as the dashboard reads it. Derived here once so
/// the frontend never walks the client's payloads itself.
#[derive(Debug, Clone, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PlayerGame {
    pub game_id: i64,
    /// When the game started, epoch milliseconds.
    pub played_at: i64,
    pub duration_seconds: i64,
    pub queue_id: i64,
    pub map_id: i64,
    pub champion_id: i64,
    pub champion_level: i64,
    /// 100 (blue) or 200 (red), to tell the player's side in `participants`.
    pub team_id: i64,
    /// The assigned role (`HistoryGame::position_of`); `None` on a queue
    /// without roles.
    pub position: Option<String>,
    pub win: bool,
    pub remake: bool,
    pub kills: i64,
    pub deaths: i64,
    pub assists: i64,
    /// Lane minions and jungle camps together, as the scoreboard counts them.
    pub cs: i64,
    pub gold: i64,
    pub damage_to_champions: i64,
    pub vision_score: i64,
    pub largest_multi_kill: i64,
    /// The six inventory slots in order, zero where a slot is empty.
    pub items: Vec<i64>,
    pub trinket: i64,
    /// The role-bound slot, zero when empty.
    pub role_bound_item: i64,
    pub spells: [i64; 2],
    pub keystone: i64,
    pub primary_style: i64,
    pub sub_style: i64,
    /// The player's team totals, from the game's full scoreboard. `None` when
    /// that read failed: kill participation and damage share are then unknown
    /// for this game, never zero.
    pub team_kills: Option<i64>,
    pub team_damage_to_champions: Option<i64>,
    /// All ten, for the match row's team compositions; empty without the scoreboard.
    pub participants: Vec<GameParticipant>,
}

/// One participant of a game, as a match row's composition strip shows them.
#[derive(Debug, Clone, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct GameParticipant {
    pub champion_id: i64,
    pub team_id: i64,
    pub position: Option<String>,
    pub game_name: Option<String>,
    pub tag_line: Option<String>,
}

impl PlayerGame {
    /// The player's line of one game from the history list, where they are the
    /// only participant. `None` for a game with no participant at all.
    pub fn from_history(game: &HistoryGame) -> Option<Self> {
        let me = game.participants.first()?;
        let stats = &me.stats;
        let duration_seconds = game.duration_seconds();

        Some(Self {
            game_id: game.game_id,
            played_at: game.game_creation,
            duration_seconds,
            queue_id: game.queue_id,
            map_id: game.map_id,
            champion_id: me.champion_id,
            champion_level: stats.champ_level,
            team_id: me.team_id,
            position: game.position_of(me).map(str::to_string),
            win: stats.win,
            remake: stats.game_ended_in_early_surrender || duration_seconds < REMAKE_SECONDS,
            kills: stats.kills,
            deaths: stats.deaths,
            assists: stats.assists,
            cs: stats.total_minions_killed + stats.neutral_minions_killed,
            gold: stats.gold_earned,
            damage_to_champions: stats.total_damage_dealt_to_champions,
            vision_score: stats.vision_score,
            largest_multi_kill: stats.largest_multi_kill,
            items: vec![
                stats.item0,
                stats.item1,
                stats.item2,
                stats.item3,
                stats.item4,
                stats.item5,
            ],
            trinket: stats.item6,
            role_bound_item: stats.role_bound_item,
            spells: [me.spell1_id, me.spell2_id],
            keystone: stats.perk0,
            primary_style: stats.perk_primary_style,
            sub_style: stats.perk_sub_style,
            team_kills: None,
            team_damage_to_champions: None,
            participants: Vec::new(),
        })
    }

    /// The participant id the player had in this game, for finding them again
    /// in the full scoreboard.
    pub fn participant_id(game: &HistoryGame) -> Option<i64> {
        game.participants.first().map(|me| me.participant_id)
    }

    /// Fill in what the game's full scoreboard adds to the player's line.
    pub fn with_scoreboard(mut self, scoreboard: Option<&Scoreboard>) -> Self {
        if let Some(scoreboard) = scoreboard {
            self.team_kills = Some(scoreboard.team_kills);
            self.team_damage_to_champions = Some(scoreboard.team_damage_to_champions);
            self.participants.clone_from(&scoreboard.participants);
        }
        self
    }
}

/// What the player's line needs from a game's full scoreboard: their team's
/// totals, and who played what on both sides.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct Scoreboard {
    pub team_kills: i64,
    pub team_damage_to_champions: i64,
    pub participants: Vec<GameParticipant>,
}

impl Scoreboard {
    /// Read the scoreboard from the side of `participant_id`. `None` when the
    /// player is not in it — a scoreboard for another game, or a truncated one.
    pub fn of(game: &HistoryGame, participant_id: i64) -> Option<Self> {
        let team = game
            .participants
            .iter()
            .find(|p| p.participant_id == participant_id)?
            .team_id;
        let mine = game.participants.iter().filter(|p| p.team_id == team);
        let name = |id: i64| {
            game.participant_identities
                .iter()
                .find(|identity| identity.participant_id == id)
                .map(|identity| &identity.player)
        };
        let non_empty = |value: &str| Some(value.to_string()).filter(|v| !v.is_empty());

        Some(Self {
            team_kills: mine.clone().map(|p| p.stats.kills).sum(),
            team_damage_to_champions: mine.map(|p| p.stats.total_damage_dealt_to_champions).sum(),
            participants: game
                .participants
                .iter()
                .map(|p| GameParticipant {
                    champion_id: p.champion_id,
                    team_id: p.team_id,
                    position: game.position_of(p).map(str::to_string),
                    game_name: name(p.participant_id).and_then(|n| non_empty(&n.game_name)),
                    tag_line: name(p.participant_id).and_then(|n| non_empty(&n.tag_line)),
                })
                .collect(),
        })
    }
}

/// Everything the dashboard draws about the player, in one answer.
#[derive(Debug, Clone, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PlayerRecord {
    /// The player's shard (`EUW1`), as their history names it.
    pub platform_id: Option<String>,
    pub ranked: Vec<RankedQueue>,
    /// `None` when the player never chose one; the dashboard then falls back
    /// to their most-mastered champion.
    pub background_skin_id: Option<i64>,
    /// Newest first, as the client lists them.
    pub games: Vec<PlayerGame>,
}

#[cfg(test)]
mod tests {
    use super::*;

    const HISTORY: &str = r#"{
      "games": { "games": [
        {
          "gameId": 7001, "gameCreation": 1759150000000, "gameDuration": 1745,
          "queueId": 420, "mapId": 11,
          "participants": [{
            "participantId": 8, "teamId": 200, "championId": 103,
            "spell1Id": 4, "spell2Id": 14,
            "stats": {
              "win": true, "kills": 9, "deaths": 2, "assists": 11, "champLevel": 17,
              "totalMinionsKilled": 212, "neutralMinionsKilled": 8, "goldEarned": 13250,
              "totalDamageDealtToChampions": 31000, "visionScore": 24,
              "item0": 3165, "item1": 3020, "item2": 0, "item3": 4645, "item4": 0, "item5": 0, "item6": 3364,
              "roleBoundItem": 3006,
              "perk0": 8112, "perkSubStyle": 8200, "largestMultiKill": 2
            },
            "timeline": { "lane": "JUNGLE", "role": "NONE" }
          }]
        },
        {
          "gameId": 7000, "gameCreation": 1759140000000, "gameDuration": 190,
          "queueId": 420, "mapId": 11,
          "participants": [{ "participantId": 1, "championId": 103, "stats": { "win": false } }]
        },
        { "gameId": 6999, "participants": [] }
      ] }
    }"#;

    fn history() -> Vec<HistoryGame> {
        serde_json::from_str::<MatchHistory>(HISTORY)
            .unwrap()
            .games
            .games
    }

    #[test]
    fn reads_the_players_line_out_of_the_history() {
        let game = PlayerGame::from_history(&history()[0]).unwrap();
        assert_eq!(game.champion_id, 103);
        assert_eq!(game.position.as_deref(), Some("MIDDLE"));
        assert_eq!((game.kills, game.deaths, game.assists), (9, 2, 11));
        assert_eq!(game.cs, 220);
        assert_eq!(game.items, vec![3165, 3020, 0, 4645, 0, 0]);
        assert_eq!(game.trinket, 3364);
        assert_eq!(game.role_bound_item, 3006);
        assert_eq!(game.spells, [4, 14]);
        assert!(game.win && !game.remake);
        assert_eq!(game.team_kills, None);
    }

    #[test]
    fn a_game_over_before_five_minutes_is_a_remake() {
        let game = PlayerGame::from_history(&history()[1]).unwrap();
        assert!(game.remake);
    }

    #[test]
    fn a_game_without_the_player_is_skipped_not_invented() {
        assert!(PlayerGame::from_history(&history()[2]).is_none());
    }

    #[test]
    fn a_duration_in_milliseconds_is_read_as_seconds() {
        let mut game = history().remove(0);
        game.game_duration = 1_745_000;
        assert_eq!(
            PlayerGame::from_history(&game).unwrap().duration_seconds,
            1745
        );
    }

    #[test]
    fn the_role_is_the_slot_on_a_queue_that_assigns_roles() {
        let game = |queue_id: i64, map_id: i64| HistoryGame {
            queue_id,
            map_id,
            ..HistoryGame::default()
        };
        let player = |participant_id: i64, team_id: i64| HistoryParticipant {
            participant_id,
            team_id,
            ..HistoryParticipant::default()
        };
        let ranked = game(420, 11);
        assert_eq!(ranked.position_of(&player(1, 100)), Some("TOP"));
        assert_eq!(ranked.position_of(&player(5, 100)), Some("UTILITY"));
        assert_eq!(ranked.position_of(&player(7, 200)), Some("JUNGLE"));
        assert_eq!(ranked.position_of(&player(6, 100)), None);
        assert_eq!(ranked.position_of(&player(3, 200)), None);
        assert_eq!(game(430, 11).position_of(&player(1, 100)), None);
        assert_eq!(game(450, 12).position_of(&player(1, 100)), None);
    }

    #[test]
    fn a_scoreboard_sums_the_players_side_and_names_both() {
        let game: HistoryGame = serde_json::from_str(
            r#"{ "gameId": 7001,
              "participantIdentities": [
                { "participantId": 1, "player": { "gameName": "Foe", "tagLine": "EUW" } },
                { "participantId": 4, "player": { "gameName": "Me", "tagLine": "TAG" } }
              ],
              "participants": [
                { "participantId": 1, "teamId": 100, "championId": 1, "stats": { "kills": 5, "totalDamageDealtToChampions": 9000 } },
                { "participantId": 4, "teamId": 200, "championId": 103, "stats": { "kills": 9, "totalDamageDealtToChampions": 31000 },
                  "timeline": { "lane": "MIDDLE", "role": "SOLO" } },
                { "participantId": 7, "teamId": 200, "championId": 22, "stats": { "kills": 12, "totalDamageDealtToChampions": 20000 } }
              ] }"#,
        )
        .unwrap();
        let scoreboard = Scoreboard::of(&game, 4).unwrap();
        assert_eq!(
            (scoreboard.team_kills, scoreboard.team_damage_to_champions),
            (21, 51000)
        );
        assert_eq!(scoreboard.participants.len(), 3);
        assert_eq!(scoreboard.participants[1].game_name.as_deref(), Some("Me"));
        assert_eq!(scoreboard.participants[1].position, None);
        assert_eq!(scoreboard.participants[2].game_name, None);
        assert!(Scoreboard::of(&game, 9).is_none());
    }

    #[test]
    fn only_the_rift_queues_with_a_season_get_a_standing() {
        let stats: RankedStats = serde_json::from_str(
            r#"{ "queues": [
              { "queueType": "RANKED_TFT", "tier": "GOLD", "wins": 3 },
              { "queueType": "RANKED_FLEX_SR", "tier": "", "wins": 0, "losses": 0 },
              { "queueType": "RANKED_SOLO_5x5", "tier": "EMERALD", "division": "II", "leaguePoints": 41, "wins": 30, "losses": 25 }
            ] }"#,
        )
        .unwrap();
        let queues = stats.rift_queues();
        assert_eq!(queues.len(), 1);
        assert_eq!(queues[0].queue_type, "RANKED_SOLO_5x5");
        assert_eq!(queues[0].league_points, 41);
    }
}
