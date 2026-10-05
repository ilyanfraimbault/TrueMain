//! What a recording keeps of its game for the recap's win-probability curve
//! and turning points (#1911): the game's timeline reduced to what the shared
//! model reads, the player's side, and who played which champion.
//!
//! Written once, when the recording is finalised, in a file of its own beside
//! the video (`win-probability.json`): the client's match history only keeps
//! the player's latest games, and the recap has to keep working after this one
//! has left it. A file of its own rather than a field of `recording.json`,
//! since every listing of the library reads every recording's metadata and
//! only the recap needs this. A recording without it — the game never reached
//! the match history, its timeline did not read, or it was recorded by an
//! older build — simply has no curve.

use std::fs;
use std::io;

use lcu::detail::GameTimeline;
use lcu::record::HistoryGame;
use lcu::WinProbabilityTimeline;
use serde::{Deserialize, Serialize};

use crate::store::RecordingDir;

pub const WIN_PROBABILITY_FILE: &str = "win-probability.json";
pub(crate) const WIN_PROBABILITY_TEMPORARY: &str = "win-probability.json.tmp";

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct RecordedWinProbability {
    /// The player's side, 100 or 200: the curve reads for it.
    pub team_id: i64,
    /// Every participant's champion, to name the players in a turning point.
    pub champions: Vec<ParticipantChampion>,
    pub timeline: WinProbabilityTimeline,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ParticipantChampion {
    pub participant_id: i64,
    pub champion_id: i64,
}

impl RecordedWinProbability {
    /// From the game's full scoreboard (all ten — the history list carries the
    /// player alone) and its timeline; `team_id` is the player's side.
    pub fn from_game(scoreboard: &HistoryGame, timeline: &GameTimeline, team_id: i64) -> Self {
        Self {
            team_id,
            champions: scoreboard
                .participants
                .iter()
                .map(|p| ParticipantChampion {
                    participant_id: p.participant_id,
                    champion_id: p.champion_id,
                })
                .collect(),
            timeline: WinProbabilityTimeline::from_game(scoreboard, timeline),
        }
    }
}

impl RecordingDir {
    /// The recording's curve data; `None` when it has none or it does not read.
    pub fn read_win_probability(&self) -> Option<RecordedWinProbability> {
        let body = fs::read_to_string(self.path().join(WIN_PROBABILITY_FILE)).ok()?;
        serde_json::from_str(&body).ok()
    }

    /// Written through a temporary file, like the metadata.
    pub fn write_win_probability(&self, recorded: &RecordedWinProbability) -> io::Result<()> {
        let body = serde_json::to_string(recorded).map_err(io::Error::other)?;
        let temporary = self.path().join(WIN_PROBABILITY_TEMPORARY);
        fs::write(&temporary, body)?;
        fs::rename(&temporary, self.path().join(WIN_PROBABILITY_FILE))
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn scoreboard() -> HistoryGame {
        serde_json::from_str(
            r#"{ "gameId": 7, "gameDuration": 1800, "queueId": 420, "mapId": 11,
              "participants": [
                { "participantId": 1, "teamId": 100, "championId": 103 },
                { "participantId": 6, "teamId": 200, "championId": 238 }
              ] }"#,
        )
        .unwrap()
    }

    fn timeline() -> GameTimeline {
        serde_json::from_str(
            r#"{ "frames": [ { "timestamp": 0, "events": [
              { "type": "CHAMPION_KILL", "timestamp": 500, "killerId": 1, "victimId": 6 }
            ], "participantFrames": { "1": { "level": 1 } } } ] }"#,
        )
        .unwrap()
    }

    #[test]
    fn names_every_champion_and_reduces_the_timeline() {
        let recorded = RecordedWinProbability::from_game(&scoreboard(), &timeline(), 200);
        assert_eq!(recorded.team_id, 200);
        assert_eq!(
            recorded.champions[1],
            ParticipantChampion {
                participant_id: 6,
                champion_id: 238
            }
        );
        assert_eq!(recorded.timeline.duration_ms, 1_800_000);
        assert_eq!(recorded.timeline.events.len(), 1);
    }

    #[test]
    fn round_trips_through_the_recordings_folder() {
        let root = tempfile::tempdir().unwrap();
        let dir = RecordingDir::new(root.path().to_path_buf());
        assert_eq!(dir.read_win_probability(), None);
        let recorded = RecordedWinProbability::from_game(&scoreboard(), &timeline(), 100);
        dir.write_win_probability(&recorded).unwrap();
        assert_eq!(dir.read_win_probability(), Some(recorded));
        assert!(!root.path().join(WIN_PROBABILITY_TEMPORARY).exists());
    }
}
