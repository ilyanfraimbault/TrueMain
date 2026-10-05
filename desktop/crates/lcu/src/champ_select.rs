//! Hovering, locking and banning from the app (#1909) — on the player's click,
//! and only then.
//!
//! The client exposes a champion select as a list of turns, each a list of
//! actions (a ban or a pick by one cell). This module finds the local player's
//! own actions in it and decides, before anything is written, which action a
//! click may write to and whether it may at all. The write itself is the
//! caller's (`LcuClient::hover_champion`, `LcuClient::complete_action`), which
//! is called from a button and nowhere else: no timer, no phase change and no
//! "best pick" ever reaches it.

use serde::{Deserialize, Serialize};

use crate::model::{ChampSelectAction, ChampSelectSession};

/// The two kinds of action the app writes. Every other action type the client
/// lists (`ten_bans_reveal`, …) is not a turn of the player's and is ignored.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ActionKind {
    Pick,
    Ban,
}

impl ActionKind {
    pub fn parse(raw: &str) -> Option<Self> {
        match raw {
            "pick" => Some(Self::Pick),
            "ban" => Some(Self::Ban),
            _ => None,
        }
    }

    pub fn as_str(self) -> &'static str {
        match self {
            Self::Pick => "pick",
            Self::Ban => "ban",
        }
    }
}

/// The client's timer phase in which ranked players declare a pick intent
/// before the bans: no action is in progress, but the future pick can be
/// hovered for the allies to see.
pub const PLANNING: &str = "PLANNING";

/// One of the local player's actions, as the draft panel shows it.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct DraftAction {
    pub id: i64,
    /// `"pick"` or `"ban"`.
    pub kind: String,
    /// The champion the client shows on it — hovered while in progress,
    /// committed once completed.
    pub champion_id: Option<i64>,
    pub in_progress: bool,
    pub completed: bool,
}

impl From<&ChampSelectAction> for DraftAction {
    fn from(action: &ChampSelectAction) -> Self {
        Self {
            id: action.id,
            kind: action.kind.clone(),
            champion_id: (action.champion_id != 0).then_some(action.champion_id),
            in_progress: action.is_in_progress,
            completed: action.completed,
        }
    }
}

/// Where the player stands in the draft's turn order.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct Turn {
    /// The action of ours the client has open right now.
    pub current: Option<DraftAction>,
    /// The first action of ours still to come.
    pub next: Option<DraftAction>,
    /// How many turns before `next` opens; zero when it is in the open turn.
    pub turns_until_next: Option<i64>,
}

/// Why a click wrote nothing. Each is a distinct string the frontend matches
/// to tell the player why (`useChampSelectActions.ts`).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Refusal {
    /// No action of that kind is open for the local player.
    NotYourTurn,
    /// The client does not list the champion as pickable: not owned, disabled,
    /// or already taken.
    NotPickable,
    /// The client does not list the champion as bannable: already banned or
    /// picked.
    NotBannable,
    /// On a lock, the action does not carry the champion the player
    /// confirmed: the lock only ever commits what the client shows hovered.
    HoverMismatch,
}

impl Refusal {
    pub fn code(self) -> &'static str {
        match self {
            Self::NotYourTurn => "not-your-turn",
            Self::NotPickable => "not-pickable",
            Self::NotBannable => "not-bannable",
            Self::HoverMismatch => "hover-mismatch",
        }
    }
}

fn is_turn(action: &ChampSelectAction) -> bool {
    ActionKind::parse(&action.kind).is_some()
}

impl ChampSelectSession {
    fn is_mine(&self, action: &ChampSelectAction) -> bool {
        action.actor_cell_id == self.local_player_cell_id
            && action.is_ally_action
            && is_turn(action)
    }

    /// The draft's turns that are bans or picks, in order, with their index in
    /// that order — a reveal step between the bans and the picks is not a turn.
    fn turns(&self) -> impl Iterator<Item = (usize, &Vec<ChampSelectAction>)> {
        self.actions
            .iter()
            .filter(|group| group.iter().any(is_turn))
            .enumerate()
    }

    fn my_actions(&self) -> impl Iterator<Item = (usize, &ChampSelectAction)> {
        self.turns().flat_map(move |(index, group)| {
            group
                .iter()
                .filter(move |action| self.is_mine(action))
                .map(move |action| (index, action))
        })
    }

    /// The local player's open action of `kind`, if the client has one in
    /// progress.
    fn open_action(&self, kind: ActionKind) -> Option<&ChampSelectAction> {
        self.my_actions().map(|(_, action)| action).find(|action| {
            action.kind == kind.as_str() && action.is_in_progress && !action.completed
        })
    }

    /// The pick a planning-phase intent is written to: our first pick still to
    /// come, while no turn is open yet.
    fn planned_pick(&self) -> Option<&ChampSelectAction> {
        if self.timer.phase != PLANNING {
            return None;
        }
        self.my_actions()
            .map(|(_, action)| action)
            .find(|action| action.kind == ActionKind::Pick.as_str() && !action.completed)
    }

    pub fn turn(&self) -> Turn {
        let current = self
            .my_actions()
            .map(|(_, action)| action)
            .find(|action| action.is_in_progress && !action.completed);
        let next = self
            .my_actions()
            .find(|(_, action)| !action.is_in_progress && !action.completed);
        // The turn the draft is on: the one in progress, else the first not
        // done (the planning phase opens none).
        let open = self
            .turns()
            .find(|(_, group)| {
                group
                    .iter()
                    .any(|action| is_turn(action) && action.is_in_progress)
            })
            .or_else(|| {
                self.turns().find(|(_, group)| {
                    group
                        .iter()
                        .any(|action| is_turn(action) && !action.completed)
                })
            })
            .map(|(index, _)| index);

        Turn {
            current: current.map(DraftAction::from),
            next: next.map(|(_, action)| DraftAction::from(action)),
            turns_until_next: next
                .zip(open)
                .map(|((index, _), open)| index.saturating_sub(open) as i64),
        }
    }

    /// The action a hover of `champion` writes to, given the champions the
    /// client lists for that kind (`allowed`).
    pub fn plan_hover(
        &self,
        kind: ActionKind,
        champion: i64,
        allowed: &[i64],
    ) -> Result<i64, Refusal> {
        let action = self
            .open_action(kind)
            .or_else(|| {
                (kind == ActionKind::Pick)
                    .then(|| self.planned_pick())
                    .flatten()
            })
            .ok_or(Refusal::NotYourTurn)?;
        check_allowed(kind, champion, allowed)?;
        Ok(action.id)
    }

    /// The action a lock of `champion` completes. Only an open action — a
    /// planning-phase intent cannot be locked — and only when the client
    /// already shows `champion` hovered on it.
    pub fn plan_lock(
        &self,
        kind: ActionKind,
        champion: i64,
        allowed: &[i64],
    ) -> Result<i64, Refusal> {
        let action = self.open_action(kind).ok_or(Refusal::NotYourTurn)?;
        check_allowed(kind, champion, allowed)?;
        if action.champion_id != champion {
            return Err(Refusal::HoverMismatch);
        }
        Ok(action.id)
    }
}

fn check_allowed(kind: ActionKind, champion: i64, allowed: &[i64]) -> Result<(), Refusal> {
    if champion > 0 && allowed.contains(&champion) {
        return Ok(());
    }
    Err(match kind {
        ActionKind::Pick => Refusal::NotPickable,
        ActionKind::Ban => Refusal::NotBannable,
    })
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::model::ChampSelectTimer;

    fn action(
        id: i64,
        cell: i64,
        kind: &str,
        champion: i64,
        in_progress: bool,
        completed: bool,
    ) -> ChampSelectAction {
        ChampSelectAction {
            id,
            actor_cell_id: cell,
            champion_id: champion,
            completed,
            is_ally_action: cell < 5,
            is_in_progress: in_progress,
            kind: kind.into(),
        }
    }

    /// A ranked draft as the client lists it: one turn of ten simultaneous
    /// bans, the reveal, then the picks one or two at a time. We are cell 2.
    fn ranked(bans_open: bool, phase: &str) -> ChampSelectSession {
        let bans = (0..10)
            .map(|cell| action(cell + 1, cell, "ban", 0, bans_open, false))
            .collect();
        ChampSelectSession {
            local_player_cell_id: 2,
            actions: vec![
                bans,
                vec![action(11, -1, "ten_bans_reveal", 0, false, false)],
                vec![action(12, 0, "pick", 0, false, false)],
                vec![
                    action(13, 5, "pick", 0, false, false),
                    action(14, 6, "pick", 0, false, false),
                ],
                vec![
                    action(15, 1, "pick", 0, false, false),
                    action(16, 2, "pick", 0, false, false),
                ],
            ],
            timer: ChampSelectTimer {
                phase: phase.into(),
                ..Default::default()
            },
            ..Default::default()
        }
    }

    #[test]
    fn finds_our_ban_in_the_simultaneous_ban_turn() {
        let session = ranked(true, "BAN_PICK");
        assert_eq!(session.plan_hover(ActionKind::Ban, 157, &[157]), Ok(3));
        let turn = session.turn();
        assert_eq!(turn.current.map(|action| action.id), Some(3));
        assert_eq!(turn.next.map(|action| action.id), Some(16));
        assert_eq!(turn.turns_until_next, Some(3), "the reveal is not a turn");
    }

    #[test]
    fn an_action_of_another_cell_or_the_enemy_is_never_ours() {
        let mut session = ranked(false, "BAN_PICK");
        // The enemy's cell 2-equivalent and an ally's pick are open; ours is not.
        session.actions[4][0].is_in_progress = true;
        session.actions.push(vec![ChampSelectAction {
            actor_cell_id: 2,
            is_ally_action: false,
            is_in_progress: true,
            ..action(20, 2, "pick", 0, true, false)
        }]);
        assert_eq!(
            session.plan_hover(ActionKind::Pick, 103, &[103]),
            Err(Refusal::NotYourTurn)
        );
    }

    #[test]
    fn a_pick_is_refused_during_our_ban_and_a_ban_during_our_pick() {
        let banning = ranked(true, "BAN_PICK");
        assert_eq!(
            banning.plan_hover(ActionKind::Pick, 103, &[103]),
            Err(Refusal::NotYourTurn)
        );

        let mut picking = ranked(false, "BAN_PICK");
        for ban in &mut picking.actions[0] {
            ban.completed = true;
        }
        picking.actions[4][1].is_in_progress = true;
        assert_eq!(picking.plan_hover(ActionKind::Pick, 103, &[103]), Ok(16));
        assert_eq!(
            picking.plan_hover(ActionKind::Ban, 157, &[157]),
            Err(Refusal::NotYourTurn)
        );
        assert_eq!(
            picking.turn().turns_until_next,
            None,
            "no action of ours is left to come"
        );
    }

    #[test]
    fn a_completed_action_is_not_open_any_more() {
        let mut session = ranked(true, "BAN_PICK");
        session.actions[0][2].completed = true;
        assert_eq!(
            session.plan_hover(ActionKind::Ban, 157, &[157]),
            Err(Refusal::NotYourTurn)
        );
    }

    #[test]
    fn the_planning_phase_hovers_our_future_pick_but_never_locks_it() {
        let session = ranked(false, PLANNING);
        assert_eq!(session.plan_hover(ActionKind::Pick, 103, &[103]), Ok(16));
        assert_eq!(
            session.plan_lock(ActionKind::Pick, 103, &[103]),
            Err(Refusal::NotYourTurn)
        );
        assert_eq!(
            session.plan_hover(ActionKind::Ban, 157, &[157]),
            Err(Refusal::NotYourTurn)
        );
        let turn = session.turn();
        assert_eq!(turn.current, None, "planning opens no turn");
        assert_eq!(
            turn.next.map(|action| action.id),
            Some(3),
            "our ban comes first"
        );
        assert_eq!(turn.turns_until_next, Some(0));

        let outside = ranked(false, "BAN_PICK");
        assert_eq!(
            outside.plan_hover(ActionKind::Pick, 103, &[103]),
            Err(Refusal::NotYourTurn)
        );
    }

    #[test]
    fn a_champion_the_client_does_not_list_is_refused_by_kind() {
        let banning = ranked(true, "BAN_PICK");
        assert_eq!(
            banning.plan_hover(ActionKind::Ban, 157, &[1, 2]),
            Err(Refusal::NotBannable)
        );
        let planning = ranked(false, PLANNING);
        assert_eq!(
            planning.plan_hover(ActionKind::Pick, 103, &[1, 2]),
            Err(Refusal::NotPickable)
        );
        assert_eq!(
            planning.plan_hover(ActionKind::Pick, 0, &[0]),
            Err(Refusal::NotPickable)
        );
    }

    #[test]
    fn a_lock_commits_only_the_champion_the_client_shows_hovered() {
        let mut session = ranked(true, "BAN_PICK");
        session.actions[0][2].champion_id = 157;
        assert_eq!(session.plan_lock(ActionKind::Ban, 157, &[157, 238]), Ok(3));
        assert_eq!(
            session.plan_lock(ActionKind::Ban, 238, &[157, 238]),
            Err(Refusal::HoverMismatch)
        );

        session.actions[0][2].champion_id = 0;
        assert_eq!(
            session.plan_lock(ActionKind::Ban, 157, &[157]),
            Err(Refusal::HoverMismatch)
        );
    }

    #[test]
    fn only_bans_and_picks_are_actions_the_app_writes() {
        assert_eq!(ActionKind::parse("pick"), Some(ActionKind::Pick));
        assert_eq!(ActionKind::parse("ban"), Some(ActionKind::Ban));
        assert_eq!(ActionKind::parse("ten_bans_reveal"), None);
    }
}
