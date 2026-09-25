//! The slice of the client's payloads this app actually reads.
//!
//! Everything is `#[serde(default)]`: these payloads are not a versioned API and
//! Riot adds and removes fields between patches. A missing field has to degrade
//! into an empty draft, never into a failed deserialisation that blanks the
//! whole screen.

use serde::{Deserialize, Serialize};

/// Where the player is in the client, and what the app should therefore show.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum GameflowPhase {
    None,
    Lobby,
    Matchmaking,
    ReadyCheck,
    ChampSelect,
    InProgress,
    Reconnect,
    WaitingForStats,
    PreEndOfGame,
    EndOfGame,
    /// A phase this build does not know. Kept rather than rejected: a new phase
    /// name must not crash the shell.
    Unknown,
}

impl GameflowPhase {
    /// The client sends a bare JSON string, e.g. `"ChampSelect"`.
    pub fn from_client_value(raw: &str) -> Self {
        match raw.trim_matches('"') {
            "None" => Self::None,
            "Lobby" => Self::Lobby,
            "Matchmaking" => Self::Matchmaking,
            "ReadyCheck" => Self::ReadyCheck,
            "ChampSelect" => Self::ChampSelect,
            "InProgress" => Self::InProgress,
            "Reconnect" => Self::Reconnect,
            "WaitingForStats" => Self::WaitingForStats,
            "PreEndOfGame" => Self::PreEndOfGame,
            "EndOfGame" => Self::EndOfGame,
            _ => Self::Unknown,
        }
    }
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct ChampSelectPlayer {
    pub cell_id: i64,
    pub champion_id: i64,
    /// What the player is hovering before they lock. Zero once `champion_id`
    /// is set, so the two are read in order, never summed.
    pub champion_pick_intent: i64,
    /// Populated for our own side in queues with assigned roles, and left empty
    /// for the enemy — which is the whole reason the lane guesser exists.
    pub assigned_position: String,
}

impl ChampSelectPlayer {
    /// The champion to show: locked if there is one, hovered otherwise.
    ///
    /// Returns `None` for an empty slot rather than `0`, so the UI draws an
    /// unknown slot instead of looking up champion zero.
    pub fn displayed_champion(&self) -> Option<i64> {
        match (self.champion_id, self.champion_pick_intent) {
            (0, 0) => None,
            (0, intent) => Some(intent),
            (locked, _) => Some(locked),
        }
    }

    pub fn is_locked(&self) -> bool {
        self.champion_id != 0
    }
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct ChampSelectTimer {
    pub adjusted_time_left_in_phase: i64,
    pub phase: String,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct ChampSelectBans {
    pub my_team_bans: Vec<i64>,
    pub their_team_bans: Vec<i64>,
}

/// One step of the draft — a ban or a pick — by one cell. The client groups
/// them by turn, so the session carries a list of lists.
#[derive(Debug, Clone, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct ChampSelectAction {
    pub actor_cell_id: i64,
    /// Zero until the actor commits, or when a ban turn is skipped.
    pub champion_id: i64,
    pub completed: bool,
    pub is_ally_action: bool,
    /// `"ban"` or `"pick"`.
    #[serde(rename = "type")]
    pub kind: String,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct ChampSelectSession {
    pub local_player_cell_id: i64,
    pub my_team: Vec<ChampSelectPlayer>,
    pub their_team: Vec<ChampSelectPlayer>,
    pub bans: ChampSelectBans,
    pub actions: Vec<Vec<ChampSelectAction>>,
    pub timer: ChampSelectTimer,
}

/// What the draft panel needs, derived once in Rust so the frontend does not
/// re-derive it differently.
#[derive(Debug, Clone, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct DraftState {
    /// Our own lane, normalised upper-case (`MIDDLE`), empty when the queue
    /// does not assign one.
    pub my_position: String,
    pub my_champion: Option<i64>,
    pub my_champion_locked: bool,
    /// Allies other than us, in cell order.
    pub ally_champions: Vec<i64>,
    /// Enemy champions picked or hovered so far — the guesser's input, and the
    /// reason it must cope with fewer than five.
    pub enemy_champions: Vec<i64>,
    /// Every cell of our side, us included, empty ones kept: the panel draws
    /// the team as five slots, each under the lane the client assigned it.
    pub my_team: Vec<TeamSlot>,
    /// Bans per side, so each sits above its own team.
    pub ally_bans: Vec<i64>,
    pub enemy_bans: Vec<i64>,
    pub seconds_left: i64,
}

/// One cell of our side of champion select.
#[derive(Debug, Clone, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct TeamSlot {
    pub champion_id: Option<i64>,
    /// Upper-case lane (`MIDDLE`), empty in queues that assign none.
    pub position: String,
    pub locked: bool,
    pub is_me: bool,
}

/// The client pads unused ban slots with 0; a zero here would render as a
/// champion portrait lookup that cannot resolve.
fn real_bans(bans: &[i64]) -> Vec<i64> {
    bans.iter()
        .copied()
        .filter(|&champion| champion != 0)
        .collect()
}

impl ChampSelectSession {
    fn local_player(&self) -> Option<&ChampSelectPlayer> {
        self.my_team
            .iter()
            .find(|player| player.cell_id == self.local_player_cell_id)
    }

    /// One side's bans, in the order they were made.
    ///
    /// The client writes a ban in two places — the `bans` summary and the
    /// completed ban action — and does not always fill the summary: a ranked
    /// draft with ten bans on the client's screen has reached the app with both
    /// summary lists empty. Read both and keep each champion once.
    fn bans(&self, ally: bool) -> Vec<i64> {
        let listed = if ally {
            &self.bans.my_team_bans
        } else {
            &self.bans.their_team_bans
        };
        let mut bans = real_bans(listed);
        let banned = self
            .actions
            .iter()
            .flatten()
            .filter(|action| action.kind == "ban" && action.completed)
            .filter(|action| action.is_ally_action == ally && action.champion_id != 0)
            .map(|action| action.champion_id);
        for champion in banned {
            if !bans.contains(&champion) {
                bans.push(champion);
            }
        }
        bans
    }

    pub fn draft_state(&self) -> DraftState {
        let me = self.local_player();

        let ally_champions = self
            .my_team
            .iter()
            .filter(|player| player.cell_id != self.local_player_cell_id)
            .filter_map(ChampSelectPlayer::displayed_champion)
            .collect();

        let enemy_champions = self
            .their_team
            .iter()
            .filter_map(ChampSelectPlayer::displayed_champion)
            .collect();

        let my_team = self
            .my_team
            .iter()
            .map(|player| TeamSlot {
                champion_id: player.displayed_champion(),
                position: player.assigned_position.to_uppercase(),
                locked: player.is_locked(),
                is_me: player.cell_id == self.local_player_cell_id,
            })
            .collect();

        DraftState {
            my_position: me
                .map(|player| player.assigned_position.to_uppercase())
                .unwrap_or_default(),
            my_champion: me.and_then(ChampSelectPlayer::displayed_champion),
            my_champion_locked: me.is_some_and(ChampSelectPlayer::is_locked),
            ally_champions,
            enemy_champions,
            my_team,
            ally_bans: self.bans(true),
            enemy_bans: self.bans(false),
            seconds_left: self.timer.adjusted_time_left_in_phase / 1000,
        }
    }
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct CurrentSummoner {
    pub game_name: String,
    pub tag_line: String,
    pub summoner_level: i64,
    pub profile_icon_id: i64,
}

impl CurrentSummoner {
    /// `Name#TAG`, the form every route in the site keys on.
    pub fn riot_id(&self) -> String {
        format!("{}#{}", self.game_name, self.tag_line)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn player(cell_id: i64, champion_id: i64, intent: i64, position: &str) -> ChampSelectPlayer {
        ChampSelectPlayer {
            cell_id,
            champion_id,
            champion_pick_intent: intent,
            assigned_position: position.to_string(),
        }
    }

    #[test]
    fn an_unknown_phase_name_does_not_crash_the_shell() {
        assert_eq!(
            GameflowPhase::from_client_value("\"SomeNewPhase\""),
            GameflowPhase::Unknown
        );
    }

    #[test]
    fn reads_the_phase_out_of_the_bare_json_string_the_client_sends() {
        assert_eq!(
            GameflowPhase::from_client_value("\"ChampSelect\""),
            GameflowPhase::ChampSelect
        );
    }

    #[test]
    fn a_hovered_champion_shows_before_it_is_locked() {
        let hovering = player(0, 0, 64, "jungle");
        assert_eq!(hovering.displayed_champion(), Some(64));
        assert!(!hovering.is_locked());
    }

    #[test]
    fn a_locked_champion_wins_over_a_stale_intent() {
        let locked = player(0, 157, 64, "middle");
        assert_eq!(locked.displayed_champion(), Some(157));
        assert!(locked.is_locked());
    }

    #[test]
    fn an_empty_slot_is_unknown_rather_than_champion_zero() {
        assert_eq!(player(0, 0, 0, "").displayed_champion(), None);
    }

    #[test]
    fn derives_the_draft_from_a_partial_enemy_side() {
        // Two enemies have shown a champion, three have not. The guesser has to
        // cope with this, so the state must carry two entries and not five.
        let session = ChampSelectSession {
            local_player_cell_id: 2,
            my_team: vec![
                player(0, 89, 0, "utility"),
                player(1, 0, 0, "bottom"),
                player(2, 103, 0, "middle"),
            ],
            their_team: vec![
                player(5, 157, 0, ""),
                player(6, 0, 64, ""),
                player(7, 0, 0, ""),
            ],
            bans: ChampSelectBans {
                my_team_bans: vec![1, 0],
                their_team_bans: vec![2],
            },
            actions: vec![],
            timer: ChampSelectTimer {
                adjusted_time_left_in_phase: 27_000,
                phase: "BAN_PICK".into(),
            },
        };

        let draft = session.draft_state();
        assert_eq!(
            draft.my_position, "MIDDLE",
            "position is normalised upper-case"
        );
        assert_eq!(draft.my_champion, Some(103));
        assert!(draft.my_champion_locked);
        assert_eq!(
            draft.ally_champions,
            vec![89],
            "an ally with no pick yet is omitted, not zero"
        );
        assert_eq!(
            draft.enemy_champions,
            vec![157, 64],
            "hovered enemies count, empty slots do not"
        );
        assert_eq!(draft.ally_bans, vec![1], "padding zeroes are dropped");
        assert_eq!(draft.enemy_bans, vec![2]);
        assert_eq!(
            draft.my_team,
            vec![
                TeamSlot {
                    champion_id: Some(89),
                    position: "UTILITY".into(),
                    locked: true,
                    is_me: false,
                },
                TeamSlot {
                    champion_id: None,
                    position: "BOTTOM".into(),
                    locked: false,
                    is_me: false,
                },
                TeamSlot {
                    champion_id: Some(103),
                    position: "MIDDLE".into(),
                    locked: true,
                    is_me: true,
                },
            ],
            "the team keeps its empty cells and its lanes"
        );
        assert_eq!(draft.seconds_left, 27);
    }

    fn ban(champion_id: i64, completed: bool, is_ally_action: bool) -> ChampSelectAction {
        ChampSelectAction {
            champion_id,
            completed,
            is_ally_action,
            kind: "ban".into(),
            ..Default::default()
        }
    }

    #[test]
    fn bans_are_read_from_the_completed_ban_actions_when_the_summary_is_empty() {
        // A ranked draft: ten bans on the client's screen, both summary lists
        // empty. The actions are the only record of them.
        let session = ChampSelectSession {
            actions: vec![
                vec![ban(122, true, true), ban(157, true, false)],
                // A ban still being hovered is not a ban yet; a skipped turn
                // completes with champion zero.
                vec![ban(238, false, true), ban(0, true, false)],
                // A pick is not a ban, whatever champion it names.
                vec![ChampSelectAction {
                    champion_id: 51,
                    completed: true,
                    is_ally_action: true,
                    kind: "pick".into(),
                    ..Default::default()
                }],
            ],
            ..Default::default()
        };

        let draft = session.draft_state();
        assert_eq!(draft.ally_bans, vec![122]);
        assert_eq!(draft.enemy_bans, vec![157]);
    }

    #[test]
    fn a_ban_listed_in_both_places_is_counted_once() {
        let session = ChampSelectSession {
            bans: ChampSelectBans {
                my_team_bans: vec![122, 0],
                their_team_bans: vec![],
            },
            actions: vec![vec![ban(122, true, true), ban(875, true, true)]],
            ..Default::default()
        };

        assert_eq!(session.draft_state().ally_bans, vec![122, 875]);
    }

    #[test]
    fn an_empty_session_yields_an_empty_draft_rather_than_panicking() {
        let draft = ChampSelectSession::default().draft_state();
        assert_eq!(draft.my_champion, None);
        assert!(draft.enemy_champions.is_empty());
    }

    #[test]
    fn a_payload_missing_fields_still_deserialises() {
        // Riot drops and renames fields between patches; the shell must survive it.
        let session: ChampSelectSession = serde_json::from_str(r#"{"myTeam":[]}"#).unwrap();
        assert!(session.their_team.is_empty());
    }

    #[test]
    fn builds_the_riot_id_the_site_keys_on() {
        let summoner = CurrentSummoner {
            game_name: "Phantasm".into(),
            tag_line: "EUW".into(),
            ..Default::default()
        };
        assert_eq!(summoner.riot_id(), "Phantasm#EUW");
    }
}
