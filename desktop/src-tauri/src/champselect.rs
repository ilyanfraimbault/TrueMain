//! Hover, lock and ban from the draft screen (#1909) — the commands the
//! buttons call, and only the buttons.
//!
//! The frontend names a kind (`pick` or `ban`) and a champion, never an action
//! id: each command re-reads the session and resolves the player's open action
//! itself (`lcu::champ_select`), so a stale id from an old event can never
//! write to the wrong action. Before writing, the champion is checked against
//! the client's own pickable or bannable list; a lock only ever completes the
//! champion the client already shows hovered. Nothing here runs on a timer, a
//! phase change or a suggestion's rank.
//!
//! The same commands write to a live client or, in development, to the draft
//! simulator page through the dev server's relay (`sim.rs`). A tape replay is
//! read-only: there is nothing to write to, and the draft hides the controls.

use std::sync::Arc;

use lcu::{ActionKind, ChampSelectSession, Error, LcuClient};
use serde::Serialize;
use tauri::AppHandle;

use crate::record::SharedClient;
use crate::telemetry::{self, Feature};

/// The client answered an error status to a write — the timer ran out between
/// the read and the write, or the phase moved on. Matched by the frontend.
const CLIENT_REFUSED: &str = "client-refused";

/// Where the champion select writes go, fixed for the app's run by how it was
/// started.
pub enum ChampSelectWriter {
    /// The client the supervisor is attached to, whenever one is.
    Live(SharedClient),
    /// The draft simulator's relay (`TRUEMAIN_LCU_SIM`), debug builds only.
    #[cfg(debug_assertions)]
    Sim(crate::sim::SimClient),
    /// A tape replay: nothing to write to.
    ReadOnly,
}

impl ChampSelectWriter {
    pub fn new(client: SharedClient) -> Self {
        #[cfg(debug_assertions)]
        if let Ok(url) = std::env::var(crate::sim::SIM_VAR) {
            return Self::Sim(crate::sim::SimClient::new(url));
        }
        if crate::supervisor::replaying() {
            return Self::ReadOnly;
        }
        Self::Live(client)
    }

    fn target(&self) -> Result<Target, String> {
        match self {
            Self::Live(client) => crate::record::attached(client).map(Target::Live),
            #[cfg(debug_assertions)]
            Self::Sim(sim) => Ok(Target::Sim(sim.clone())),
            Self::ReadOnly => Err("a replay cannot be written to".to_string()),
        }
    }
}

/// One write's destination, resolved for the length of a command.
enum Target {
    Live(Arc<LcuClient>),
    #[cfg(debug_assertions)]
    Sim(crate::sim::SimClient),
}

impl Target {
    async fn session(&self) -> lcu::Result<Option<ChampSelectSession>> {
        match self {
            Self::Live(client) => client.champ_select_session().await,
            #[cfg(debug_assertions)]
            Self::Sim(sim) => sim.champ_select_session().await,
        }
    }

    async fn allowed(&self, kind: ActionKind) -> lcu::Result<Vec<i64>> {
        match (self, kind) {
            (Self::Live(client), ActionKind::Pick) => client.pickable_champions().await,
            (Self::Live(client), ActionKind::Ban) => client.bannable_champions().await,
            #[cfg(debug_assertions)]
            (Self::Sim(sim), kind) => sim.allowed(kind).await,
        }
    }

    async fn hover(&self, action_id: i64, champion_id: i64) -> lcu::Result<()> {
        match self {
            Self::Live(client) => client.hover_champion(action_id, champion_id).await,
            #[cfg(debug_assertions)]
            Self::Sim(sim) => sim.hover_champion(action_id, champion_id).await,
        }
    }

    async fn complete(&self, action_id: i64) -> lcu::Result<()> {
        match self {
            Self::Live(client) => client.complete_action(action_id).await,
            #[cfg(debug_assertions)]
            Self::Sim(sim) => sim.complete_action(action_id).await,
        }
    }
}

fn parse_kind(kind: &str) -> Result<ActionKind, String> {
    ActionKind::parse(kind).ok_or_else(|| format!("{kind} is not an action the app writes"))
}

fn read_failed(error: Error) -> String {
    tracing::warn!(%error, "champion select read failed");
    error.to_string()
}

fn write_failed(error: Error) -> String {
    tracing::warn!(%error, "champion select write failed");
    match error {
        Error::UnexpectedStatus { .. } => CLIENT_REFUSED.to_string(),
        other => other.to_string(),
    }
}

/// The session and the client's list for `kind`, read right before a write.
async fn read(target: &Target, kind: ActionKind) -> Result<(ChampSelectSession, Vec<i64>), String> {
    let (session, allowed) = tokio::join!(target.session(), target.allowed(kind));
    let session = session
        .map_err(read_failed)?
        .ok_or_else(|| lcu::Refusal::NotYourTurn.code().to_string())?;
    Ok((session, allowed.map_err(read_failed)?))
}

/// Hover `champion_id` on the player's open action of `kind` — or, in the
/// ranked planning phase, declare it as the pick intent.
#[tauri::command]
pub async fn champ_select_hover(
    app: AppHandle,
    writer: tauri::State<'_, ChampSelectWriter>,
    kind: String,
    champion_id: i64,
) -> Result<(), String> {
    let kind = parse_kind(&kind)?;
    let target = writer.target()?;
    let (session, allowed) = read(&target, kind).await?;
    let action = session
        .plan_hover(kind, champion_id, &allowed)
        .map_err(|refusal| refusal.code().to_string())?;
    target
        .hover(action, champion_id)
        .await
        .map_err(write_failed)?;
    if kind == ActionKind::Pick {
        telemetry::feature(&app, Feature::ChampSelectHover);
    }
    Ok(())
}

/// Lock in — or, for a ban, ban — `champion_id`, which the player confirmed and
/// the client must already show hovered on their open action.
#[tauri::command]
pub async fn champ_select_lock(
    app: AppHandle,
    writer: tauri::State<'_, ChampSelectWriter>,
    kind: String,
    champion_id: i64,
) -> Result<(), String> {
    let kind = parse_kind(&kind)?;
    let target = writer.target()?;
    let (session, allowed) = read(&target, kind).await?;
    let action = session
        .plan_lock(kind, champion_id, &allowed)
        .map_err(|refusal| refusal.code().to_string())?;
    target.complete(action).await.map_err(write_failed)?;
    telemetry::feature(
        &app,
        match kind {
            ActionKind::Pick => Feature::ChampSelectLock,
            ActionKind::Ban => Feature::ChampSelectBan,
        },
    );
    Ok(())
}

/// What the client lets the player pick and ban right now, for the controls'
/// disabled states. A list the client did not answer is `None` — unknown, not
/// empty: the shell checks again before any write anyway.
#[derive(Debug, Default, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Choices {
    writable: bool,
    pickable: Option<Vec<i64>>,
    bannable: Option<Vec<i64>>,
}

#[tauri::command]
pub async fn champ_select_choices(
    writer: tauri::State<'_, ChampSelectWriter>,
) -> Result<Choices, String> {
    let Ok(target) = writer.target() else {
        return Ok(Choices {
            writable: !matches!(*writer, ChampSelectWriter::ReadOnly),
            ..Choices::default()
        });
    };
    let (pickable, bannable) = tokio::join!(
        target.allowed(ActionKind::Pick),
        target.allowed(ActionKind::Ban)
    );
    Ok(Choices {
        writable: true,
        pickable: pickable.ok(),
        bannable: bannable.ok(),
    })
}
