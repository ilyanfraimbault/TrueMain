//! What the loading screen's board shows, as one explicit state per thing read
//! (#2472): the view's `LoadingStatus`, each player `Hidden` or `Known`, and
//! each of a known player's fields a `Read` — pending, read, or failed for
//! good. The reader (`super::read`) only hands answers to `LoadingView`; every
//! transition is decided here, and mirrored in `desktop/app/app/types/loading.ts`.

use lcu::record::{MatchHistory, RankedStats};
use lcu::{LaningForm, PlayerForm, RankedQueue};
use serde::Serialize;

/// One thing read about a player through their client.
#[derive(Debug, Clone, PartialEq, Serialize)]
#[serde(tag = "state", content = "value", rename_all = "camelCase")]
pub enum Read<T> {
    /// Asked, not answered yet.
    Pending,
    Ok(T),
    /// The client could not answer: for good, for this game.
    Failed,
}

impl<T> Read<T> {
    fn settle(&mut self) {
        if matches!(self, Self::Pending) {
            *self = Self::Failed;
        }
    }

    #[cfg(test)]
    pub(super) fn is_pending(&self) -> bool {
        matches!(self, Self::Pending)
    }
}

/// Where the board stands.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub enum LoadingStatus {
    /// No game is loading or running.
    #[default]
    Idle,
    /// The roster or its players' reads are under way.
    Reading,
    /// Every read answered or failed: nothing more is coming.
    Done,
    /// The client never listed the game's players: the board stays empty.
    RosterFailed,
}

#[derive(Debug, Clone, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct LoadingPlayer {
    pub champion_id: i64,
    /// `ORDER` (blue side) or `CHAOS`.
    pub team: &'static str,
    pub position: String,
    pub is_me: bool,
    #[serde(flatten)]
    pub identity: Identity,
}

/// Whether anything about the player is read.
#[derive(Debug, Clone, PartialEq, Serialize)]
#[serde(tag = "kind", rename_all = "camelCase")]
pub enum Identity {
    /// The player hides their name (Streamer Mode): their champion and lane
    /// show, never their name, and nothing about them is read.
    Hidden,
    Known(Box<Known>),
}

#[derive(Debug, Clone, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Known {
    /// Empty until read, when the session left it out.
    pub riot_id: String,
    /// The session's key for the player: what their reads are asked by.
    #[serde(skip)]
    pub puuid: String,
    pub form: Read<PlayerForm>,
    /// Solo/Duo, else a ranked Flex (`RankedStats::headline`); `Ok(None)` for
    /// an unranked player.
    pub rank: Read<Option<RankedQueue>>,
    /// How they fare on the champion they are on (#1863): read once their
    /// history is, failed with it.
    pub laning: Read<LaningForm>,
}

impl Known {
    pub fn new(riot_id: String, puuid: String) -> Self {
        Self {
            riot_id,
            puuid,
            form: Read::Pending,
            rank: Read::Pending,
            laning: Read::Pending,
        }
    }

    fn fail(&mut self, field: Field) {
        match field {
            Field::Form => {
                self.form.settle();
                self.laning.settle();
            }
            Field::Rank => self.rank.settle(),
            Field::Laning => self.laning.settle(),
        }
    }

    fn settle(&mut self) {
        self.form.settle();
        self.rank.settle();
        self.laning.settle();
    }
}

impl LoadingPlayer {
    pub fn known(&self) -> Option<&Known> {
        match &self.identity {
            Identity::Known(known) => Some(known),
            Identity::Hidden => None,
        }
    }

    fn known_mut(&mut self) -> Option<&mut Known> {
        match &mut self.identity {
            Identity::Known(known) => Some(known),
            Identity::Hidden => None,
        }
    }
}

#[derive(Debug, Clone, Default, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct LoadingView {
    pub status: LoadingStatus,
    pub players: Vec<LoadingPlayer>,
    /// The shard the game is played on, e.g. `EUW1`, from the first history
    /// read; empty until then. The true-main lookup is asked on it (#1910).
    pub platform_id: String,
}

/// Which of a player's reads an answer is for.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Field {
    Form,
    Rank,
    Laning,
}

/// One client answer about one player.
#[derive(Debug)]
pub enum Answer {
    /// The name read by puuid when the session left it out, and the history.
    Form {
        name: Option<String>,
        history: lcu::Result<MatchHistory>,
    },
    Rank(lcu::Result<RankedStats>),
    Laning(LaningForm),
}

impl LoadingView {
    /// A game is loading: nothing listed yet.
    pub fn reading() -> Self {
        Self {
            status: LoadingStatus::Reading,
            ..Self::default()
        }
    }

    pub fn roster_read(&mut self, players: Vec<LoadingPlayer>) {
        self.status = LoadingStatus::Reading;
        self.players = players;
    }

    pub fn roster_failed(&mut self) {
        self.status = LoadingStatus::RosterFailed;
        self.players.clear();
    }

    /// The player at `index`'s answer lands.
    pub fn answer(&mut self, index: usize, answer: Answer) {
        let Some(player) = self.players.get_mut(index) else {
            return;
        };
        let champion = player.champion_id;
        let Some(known) = player.known_mut() else {
            return;
        };
        match answer {
            Answer::Form { name, history } => {
                if let Some(name) = name {
                    known.riot_id = name;
                }
                match history {
                    Ok(history) => {
                        known.form = Read::Ok(PlayerForm::from_history(&history, champion));
                        if self.platform_id.is_empty() {
                            self.platform_id = history.platform_id;
                        }
                    }
                    Err(error) => {
                        tracing::debug!(%error, "a player's history could not be read");
                        known.fail(Field::Form);
                    }
                }
            }
            Answer::Rank(Ok(stats)) => known.rank = Read::Ok(stats.headline()),
            Answer::Rank(Err(error)) => {
                tracing::debug!(%error, "a player's rank could not be read");
                known.fail(Field::Rank);
            }
            Answer::Laning(form) => known.laning = Read::Ok(form),
        }
    }

    /// The read of the player at `index`'s `field` ended without an answer
    /// (its task panicked or was cancelled): failed, never left pending.
    pub fn failed(&mut self, index: usize, field: Field) {
        if let Some(known) = self
            .players
            .get_mut(index)
            .and_then(LoadingPlayer::known_mut)
        {
            known.fail(field);
        }
    }

    /// Every read is over: whatever is still pending will never come.
    pub fn done(&mut self) {
        self.status = LoadingStatus::Done;
        for known in self.players.iter_mut().filter_map(LoadingPlayer::known_mut) {
            known.settle();
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn line(name: &str, champion_id: i64) -> LoadingPlayer {
        LoadingPlayer {
            champion_id,
            team: "ORDER",
            position: "MIDDLE".into(),
            is_me: false,
            identity: Identity::Known(Box::new(Known::new(name.into(), format!("puuid-{name}")))),
        }
    }

    fn hidden() -> LoadingPlayer {
        LoadingPlayer {
            identity: Identity::Hidden,
            ..line("", 81)
        }
    }

    fn view() -> LoadingView {
        let mut view = LoadingView::reading();
        view.roster_read(vec![line("Me#EUW", 103), hidden()]);
        view
    }

    fn known(view: &LoadingView, index: usize) -> &Known {
        view.players[index].known().expect("a known player")
    }

    fn history(platform: &str) -> MatchHistory {
        MatchHistory {
            platform_id: platform.into(),
            ..MatchHistory::default()
        }
    }

    fn unreachable() -> lcu::Error {
        lcu::Error::UnexpectedStatus {
            status: 500,
            path: "/x".into(),
        }
    }

    #[test]
    fn the_view_is_idle_outside_a_game_and_reading_from_its_start() {
        assert_eq!(LoadingView::default().status, LoadingStatus::Idle);
        let view = view();
        assert_eq!(view.status, LoadingStatus::Reading);
        assert!(known(&view, 0).form.is_pending());
        assert!(known(&view, 0).rank.is_pending());
        assert!(known(&view, 0).laning.is_pending());
        assert_eq!(view.players[1].identity, Identity::Hidden);
    }

    #[test]
    fn a_history_read_fills_the_form_and_the_platform_once() {
        let mut view = view();
        view.answer(
            0,
            Answer::Form {
                name: None,
                history: Ok(history("EUW1")),
            },
        );
        view.answer(
            0,
            Answer::Form {
                name: None,
                history: Ok(history("NA1")),
            },
        );
        assert!(matches!(known(&view, 0).form, Read::Ok(_)));
        assert_eq!(view.platform_id, "EUW1");
        assert!(
            known(&view, 0).laning.is_pending(),
            "the laning read comes after"
        );
    }

    #[test]
    fn a_name_read_by_puuid_replaces_the_empty_one() {
        let mut view = LoadingView::reading();
        view.roster_read(vec![line("", 1)]);
        view.answer(
            0,
            Answer::Form {
                name: Some("Found#EUW".into()),
                history: Err(unreachable()),
            },
        );
        assert_eq!(known(&view, 0).riot_id, "Found#EUW");
    }

    #[test]
    fn a_failed_history_fails_the_laning_with_it() {
        let mut view = view();
        view.answer(
            0,
            Answer::Form {
                name: None,
                history: Err(unreachable()),
            },
        );
        assert_eq!(known(&view, 0).form, Read::Failed);
        assert_eq!(known(&view, 0).laning, Read::Failed);
        assert!(
            known(&view, 0).rank.is_pending(),
            "the standing is its own read"
        );
    }

    #[test]
    fn an_unranked_player_is_read_not_failed() {
        let mut view = view();
        view.answer(0, Answer::Rank(Ok(RankedStats::default())));
        assert_eq!(known(&view, 0).rank, Read::Ok(None));
        let mut view = self::view();
        view.answer(0, Answer::Rank(Err(unreachable())));
        assert_eq!(known(&view, 0).rank, Read::Failed);
    }

    #[test]
    fn a_read_that_ended_without_an_answer_fails_its_field_only() {
        let mut view = view();
        view.answer(0, Answer::Rank(Ok(RankedStats::default())));
        view.failed(0, Field::Rank);
        assert_eq!(
            known(&view, 0).rank,
            Read::Ok(None),
            "an answered field stays"
        );
        view.failed(0, Field::Laning);
        assert_eq!(known(&view, 0).laning, Read::Failed);
        assert!(known(&view, 0).form.is_pending());
        view.failed(0, Field::Form);
        assert_eq!(known(&view, 0).form, Read::Failed);
    }

    #[test]
    fn done_settles_every_pending_read_as_failed() {
        let mut view = view();
        view.answer(0, Answer::Laning(LaningForm::default()));
        view.done();
        assert_eq!(view.status, LoadingStatus::Done);
        assert_eq!(known(&view, 0).form, Read::Failed);
        assert_eq!(known(&view, 0).rank, Read::Failed);
        assert_eq!(known(&view, 0).laning, Read::Ok(LaningForm::default()));
        assert_eq!(view.players[1].identity, Identity::Hidden);
    }

    #[test]
    fn a_roster_never_listed_is_a_status_of_its_own() {
        let mut view = LoadingView::reading();
        view.roster_failed();
        assert_eq!(view.status, LoadingStatus::RosterFailed);
        assert!(view.players.is_empty());
    }

    #[test]
    fn an_answer_for_a_hidden_or_missing_player_changes_nothing() {
        let mut view = view();
        let before = view.clone();
        view.answer(1, Answer::Rank(Ok(RankedStats::default())));
        view.answer(9, Answer::Rank(Ok(RankedStats::default())));
        view.failed(1, Field::Form);
        assert_eq!(view, before);
    }

    #[test]
    fn the_wire_shape_matches_the_apps_unions() {
        let mut view = view();
        view.answer(0, Answer::Rank(Ok(RankedStats::default())));
        let json = serde_json::to_value(&view).expect("serialises");
        assert_eq!(json["status"], "reading");
        let me = &json["players"][0];
        assert_eq!(me["kind"], "known");
        assert_eq!(me["riotId"], "Me#EUW");
        assert_eq!(me["championId"], 103);
        assert!(me.get("puuid").is_none(), "the puuid stays in the shell");
        assert_eq!(me["form"], serde_json::json!({ "state": "pending" }));
        assert_eq!(
            me["rank"],
            serde_json::json!({ "state": "ok", "value": null })
        );
        assert_eq!(
            json["players"][1],
            serde_json::json!({ "kind": "hidden", "championId": 81, "team": "ORDER", "position": "MIDDLE", "isMe": false })
        );
        let mut done = view.clone();
        done.roster_failed();
        assert_eq!(
            serde_json::to_value(&done).expect("serialises")["status"],
            "rosterFailed"
        );
    }
}
