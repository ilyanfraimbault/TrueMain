//! What the frontend receives about the running game.
//!
//! A **snapshot** when a game is first read (or when its roster cannot be
//! described as changes), then only **updates** — the changes one reading
//! brought, and nothing at all for a reading that changed nothing the app
//! shows. The game is polled every couple of seconds for half an hour; the
//! frontend hears about the few dozen moments something moved.
//!
//! Each update carries the revision it brings the state to. The frontend
//! applies one only on top of the revision before it, so an update it missed
//! (it mounted between two of them) is noticed and answered by re-reading the
//! whole state, never by drawing a board that skipped a purchase.
//!
//! One feed serves the live poll, a tape replay and the simulator alike: all
//! three hand it raw payloads and the phase, so a replay goes through the same
//! derivation, the same diff and the same rule for when a game ends.

use serde::{Deserialize, Serialize};

use crate::game::{GameChange, GameState};
use crate::model::AllGameData;

/// The changes of one reading.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct GameUpdate {
    /// The state's revision once these are applied.
    pub revision: u64,
    pub game_time: f64,
    pub changes: Vec<GameChange>,
}

/// What to send the frontend after a reading or a phase change.
#[derive(Debug, Clone, PartialEq)]
pub enum Emission {
    /// The whole state replaces the frontend's — `None` when no game runs.
    Snapshot(Option<GameState>),
    Update(GameUpdate),
}

#[derive(Debug, Default)]
pub struct GameFeed {
    /// The phase says a game is running: readings are taken.
    active: bool,
    current: Option<GameState>,
    /// Never reset, so a revision from a previous game can never pass for one
    /// of this game's.
    revision: u64,
}

impl GameFeed {
    /// Follow the gameflow phase. Readings count only while a game runs, and
    /// the state goes with the game: leaving `InProgress` clears the board
    /// rather than leaving the last game's on it.
    pub fn follow(&mut self, in_game: bool) -> Option<Emission> {
        self.active = in_game;
        if in_game || self.current.take().is_none() {
            return None;
        }
        self.revision += 1;
        Some(Emission::Snapshot(None))
    }

    /// Take one `allgamedata` reading. `None` when there is nothing to send:
    /// no game running, a payload that describes no game yet, or a reading in
    /// which nothing the app shows moved.
    pub fn ingest(&mut self, payload: &serde_json::Value) -> Option<Emission> {
        if !self.active {
            return None;
        }
        let data = AllGameData::deserialize(payload).ok()?;
        let mut next = GameState::from_payload(&data)?;
        // The pace is history no single reading holds: carried over from the
        // state the frontend has, plus this reading's minute when it is new.
        if let Some(current) = &self.current {
            next.pace.clone_from(&current.pace);
        }
        next.pace.record(&data);

        if let Some(current) = &mut self.current {
            if let Some(changes) = current.diff(&next) {
                // The clock moves on every reading, but is sent only with a
                // change or a snapshot: the frontend runs its own between them.
                current.game_time = next.game_time;
                if changes.is_empty() {
                    return None;
                }
                self.revision += 1;
                current.apply(&changes);
                current.revision = self.revision;
                return Some(Emission::Update(GameUpdate {
                    revision: self.revision,
                    game_time: next.game_time,
                    changes,
                }));
            }
        }

        self.revision += 1;
        next.revision = self.revision;
        self.current = Some(next.clone());
        Some(Emission::Snapshot(Some(next)))
    }

    /// The state as the frontend has it, with the latest clock: what a
    /// frontend that just mounted reads before following the updates.
    pub fn current(&self) -> Option<&GameState> {
        self.current.as_ref()
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::game::GameChange;
    use lcu::{Played, Tape};

    fn fixture() -> Vec<serde_json::Value> {
        let tape = Tape::parse(include_str!(concat!(
            env!("CARGO_MANIFEST_DIR"),
            "/../../fixtures/ranked-game.jsonl"
        )))
        .expect("the committed game tape parses");
        tape.timeline()
            .into_iter()
            .filter_map(|(_, played)| match played {
                Played::Game(data) => Some(data),
                Played::Event(_) => None,
            })
            .collect()
    }

    fn played(feed: &mut GameFeed, readings: &[serde_json::Value]) -> Vec<Emission> {
        readings
            .iter()
            .filter_map(|reading| feed.ingest(reading))
            .collect()
    }

    #[test]
    fn nothing_is_read_before_the_phase_says_a_game_runs() {
        let mut feed = GameFeed::default();
        assert!(played(&mut feed, &fixture()).is_empty());
        assert!(feed.current().is_none());
    }

    #[test]
    fn the_committed_tape_opens_on_a_snapshot_then_sends_only_changes() {
        let readings = fixture();
        let mut feed = GameFeed::default();
        feed.follow(true);
        let emissions = played(&mut feed, &readings);

        let Some(Emission::Snapshot(Some(first))) = emissions.first() else {
            panic!("a game opens on a snapshot")
        };
        assert_eq!(first.players.len(), 10);
        assert_eq!(first.revision, 1);
        let me = first
            .players
            .iter()
            .find(|p| p.is_me)
            .expect("we are in it");
        assert_eq!(me.champion, "Ahri");
        assert!(emissions[1..]
            .iter()
            .all(|emission| matches!(emission, Emission::Update(_))));

        // Revisions follow each other with no gap, so a frontend can tell a
        // missed update.
        for (index, emission) in emissions.iter().enumerate() {
            if let Emission::Update(update) = emission {
                assert_eq!(update.revision, index as u64 + 1);
                assert!(!update.changes.is_empty());
            }
        }
    }

    #[test]
    fn the_tape_carries_a_purchase_a_death_and_the_respawn() {
        let mut feed = GameFeed::default();
        feed.follow(true);
        let changes: Vec<GameChange> = played(&mut feed, &fixture())
            .into_iter()
            .filter_map(|emission| match emission {
                Emission::Update(update) => Some(update.changes),
                Emission::Snapshot(_) => None,
            })
            .flatten()
            .collect();
        let orianna = 7;
        assert!(changes
            .iter()
            .any(|c| matches!(c, GameChange::Items { .. })));
        assert!(changes
            .iter()
            .any(|c| matches!(c, GameChange::Died { player, .. } if *player == orianna)));
        assert!(changes
            .iter()
            .any(|c| matches!(c, GameChange::Respawned { player } if *player == orianna)));
    }

    #[test]
    fn the_snapshot_and_its_updates_add_up_to_the_last_reading() {
        let readings = fixture();
        let mut feed = GameFeed::default();
        feed.follow(true);
        let mut frontend: Option<GameState> = None;
        for emission in played(&mut feed, &readings) {
            match emission {
                Emission::Snapshot(state) => frontend = state,
                Emission::Update(update) => {
                    let state = frontend.as_mut().expect("an update follows a snapshot");
                    assert_eq!(update.revision, state.revision + 1);
                    state.apply(&update.changes);
                    state.revision = update.revision;
                    state.game_time = update.game_time;
                }
            }
        }

        let last = AllGameData::deserialize(readings.last().unwrap()).unwrap();
        let expected = GameState::from_payload(&last).unwrap();
        let frontend = frontend.unwrap();
        // Respawn times are read at the death, not re-read on every poll.
        let strip = |state: &GameState| {
            state
                .players
                .iter()
                .map(|p| {
                    (
                        p.items.clone(),
                        p.level,
                        p.kills,
                        p.deaths,
                        p.assists,
                        p.dead,
                    )
                })
                .collect::<Vec<_>>()
        };
        assert_eq!(strip(&frontend), strip(&expected));
        assert_eq!(feed.current().unwrap().players, frontend.players);
    }

    #[test]
    fn a_reading_that_changes_nothing_shown_sends_nothing_but_moves_the_clock() {
        let readings = fixture();
        let mut feed = GameFeed::default();
        feed.follow(true);
        feed.ingest(&readings[0]);
        let mut later = readings[0].clone();
        later["gameData"]["gameTime"] = serde_json::json!(16.2);
        later["allPlayers"][2]["scores"]["creepScore"] = serde_json::json!(1);
        assert_eq!(feed.ingest(&later), None);
        assert_eq!(feed.current().unwrap().game_time, 16.2);
    }

    #[test]
    fn leaving_the_game_clears_the_board_once() {
        let readings = fixture();
        let mut feed = GameFeed::default();
        feed.follow(true);
        feed.ingest(&readings[0]);
        assert_eq!(feed.follow(false), Some(Emission::Snapshot(None)));
        assert!(feed.current().is_none());
        assert_eq!(feed.follow(false), None, "already cleared");
        assert_eq!(feed.ingest(&readings[1]), None, "the game is over");
    }

    #[test]
    fn the_next_game_opens_on_a_snapshot_whose_revision_never_went_back() {
        let readings = fixture();
        let mut feed = GameFeed::default();
        feed.follow(true);
        feed.ingest(&readings[0]);
        feed.follow(false);
        feed.follow(true);
        let Some(Emission::Snapshot(Some(next))) = feed.ingest(&readings[0]) else {
            panic!("a new game opens on a snapshot")
        };
        assert_eq!(next.revision, 3);
    }

    #[test]
    fn the_loading_screens_partial_answer_is_not_a_game_yet() {
        let mut feed = GameFeed::default();
        feed.follow(true);
        let partial = serde_json::json!({ "activePlayer": {}, "allPlayers": [], "events": { "Events": [] }, "gameData": { "gameTime": 0.0 } });
        assert_eq!(feed.ingest(&partial), None);
        assert_eq!(feed.ingest(&serde_json::json!("not a payload")), None);
        assert!(feed.current().is_none());
    }

    #[test]
    fn the_ten_players_before_the_game_starts_are_not_a_game_yet() {
        let mut loading = fixture().remove(0);
        loading["events"]["Events"] = serde_json::json!([]);
        loading["gameData"]["gameTime"] = serde_json::json!(0.0);
        let mut feed = GameFeed::default();
        feed.follow(true);
        assert_eq!(feed.ingest(&loading), None);
        assert!(feed.current().is_none());
        assert!(matches!(
            feed.ingest(&fixture()[0]),
            Some(Emission::Snapshot(Some(_)))
        ));
    }
}
