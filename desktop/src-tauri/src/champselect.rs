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
//! simulator page through the dev server's relay (`sim.rs`), both through
//! `ChampSelectPort`. A tape replay is read-only: there is nothing to write to,
//! and the draft hides the controls.

use std::future::Future;
use std::pin::Pin;
use std::sync::Arc;

use lcu::{ActionKind, ChampSelectSession, Error, LcuClient, Refusal, SpellSelection};
use serde::Serialize;
use tauri::AppHandle;

use crate::contract::Refused;
use crate::record::SharedClient;
use crate::supervisor::Origin;
use crate::telemetry::{self, Feature};

type Answer<T> = Result<T, Refused<Refusal>>;

pub(crate) type Reply<'a, T> = Pin<Box<dyn Future<Output = lcu::Result<T>> + Send + 'a>>;

/// What champion select is read and written through: the League client, or the
/// draft simulator's relay standing in for it (`sim.rs`), with the same calls
/// on the same URIs.
pub trait ChampSelectPort: Send + Sync {
    fn session(&self) -> Reply<'_, Option<ChampSelectSession>>;
    fn allowed(&self, kind: ActionKind) -> Reply<'_, Vec<i64>>;
    fn hover(&self, action_id: i64, champion_id: i64) -> Reply<'_, ()>;
    fn complete(&self, action_id: i64) -> Reply<'_, ()>;
    fn set_spells<'a>(&'a self, selection: &'a SpellSelection) -> Reply<'a, ()>;
}

impl ChampSelectPort for LcuClient {
    fn session(&self) -> Reply<'_, Option<ChampSelectSession>> {
        Box::pin(self.champ_select_session())
    }

    fn allowed(&self, kind: ActionKind) -> Reply<'_, Vec<i64>> {
        match kind {
            ActionKind::Pick => Box::pin(self.pickable_champions()),
            ActionKind::Ban => Box::pin(self.bannable_champions()),
        }
    }

    fn hover(&self, action_id: i64, champion_id: i64) -> Reply<'_, ()> {
        Box::pin(self.hover_champion(action_id, champion_id))
    }

    fn complete(&self, action_id: i64) -> Reply<'_, ()> {
        Box::pin(self.complete_action(action_id))
    }

    fn set_spells<'a>(&'a self, selection: &'a SpellSelection) -> Reply<'a, ()> {
        Box::pin(self.set_summoner_spells(selection))
    }
}

/// Where the champion select writes go, fixed for the run by where its
/// sessions come from (`supervisor::Origin`).
pub enum ChampSelectWriter {
    /// The client the supervisor is attached to, whenever one is.
    Live(SharedClient),
    /// A source of its own that takes writes: the draft simulator, debug
    /// builds only.
    #[cfg_attr(not(debug_assertions), allow(dead_code))]
    Port(Arc<dyn ChampSelectPort>),
    /// A tape replay: nothing to write to.
    ReadOnly,
}

impl ChampSelectWriter {
    pub fn new(origin: &Origin, client: SharedClient) -> Self {
        match origin {
            Origin::Client => Self::Live(client),
            Origin::Tape(_) => Self::ReadOnly,
            #[cfg(debug_assertions)]
            Origin::Simulator(url) => Self::Port(Arc::new(crate::sim::SimClient::new(url.clone()))),
        }
    }

    fn target(&self) -> Result<Arc<dyn ChampSelectPort>, String> {
        match self {
            Self::Live(client) => {
                crate::record::attached(client).map(|client| client as Arc<dyn ChampSelectPort>)
            }
            Self::Port(port) => Ok(port.clone()),
            Self::ReadOnly => Err("a replay cannot be written to".to_string()),
        }
    }
}

fn read_failed(error: Error) -> Refused<Refusal> {
    tracing::warn!(%error, "champion select read failed");
    Refused::Other(error.to_string())
}

fn write_failed(error: Error) -> Refused<Refusal> {
    tracing::warn!(%error, "champion select write failed");
    match error {
        Error::UnexpectedStatus { .. } => Refused::Known(Refusal::ClientRefused),
        other => Refused::Other(other.to_string()),
    }
}

/// The session and the client's list for `kind`, read right before a write.
async fn read(
    target: &Arc<dyn ChampSelectPort>,
    kind: ActionKind,
) -> Answer<(ChampSelectSession, Vec<i64>)> {
    let (session, allowed) = tokio::join!(target.session(), target.allowed(kind));
    let session = session
        .map_err(read_failed)?
        .ok_or(Refused::Known(Refusal::NotYourTurn))?;
    Ok((session, allowed.map_err(read_failed)?))
}

/// Hover `champion_id` on the player's open action of `kind` — or, in the
/// ranked planning phase, declare it as the pick intent.
#[tauri::command]
#[specta::specta]
pub async fn champ_select_hover(
    app: AppHandle,
    writer: tauri::State<'_, ChampSelectWriter>,
    kind: ActionKind,
    champion_id: i64,
) -> Answer<()> {
    let target = writer.target()?;
    let (session, allowed) = read(&target, kind).await?;
    let action = session
        .plan_hover(kind, champion_id, &allowed)
        .map_err(Refused::Known)?;
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
#[specta::specta]
pub async fn champ_select_lock(
    app: AppHandle,
    writer: tauri::State<'_, ChampSelectWriter>,
    kind: ActionKind,
    champion_id: i64,
) -> Answer<()> {
    let target = writer.target()?;
    let (session, allowed) = read(&target, kind).await?;
    let action = session
        .plan_lock(kind, champion_id, &allowed)
        .map_err(Refused::Known)?;
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

/// Set a kit's two summoner spells in the player's champion select (#2185),
/// from its import button only. The session is read first: outside champion
/// select there is nothing to write to. The simulator does not model spells,
/// and a replay is read-only.
#[tauri::command]
#[specta::specta]
pub async fn champ_select_spells(
    writer: tauri::State<'_, ChampSelectWriter>,
    spell1_id: i64,
    spell2_id: i64,
) -> Answer<()> {
    let selection = SpellSelection::new(spell1_id, spell2_id)
        .ok_or(Refused::from("a kit needs two different spells"))?;
    let target = writer.target()?;
    target
        .session()
        .await
        .map_err(read_failed)?
        .ok_or(Refused::Known(Refusal::NotYourTurn))?;
    target.set_spells(&selection).await.map_err(write_failed)
}

/// What the client lets the player pick and ban right now, for the controls'
/// disabled states. A list the client did not answer is `None` — unknown, not
/// empty: the shell checks again before any write anyway.
#[derive(Debug, Default, Serialize, specta::Type)]
#[serde(rename_all = "camelCase")]
pub struct Choices {
    writable: bool,
    pickable: Option<Vec<i64>>,
    bannable: Option<Vec<i64>>,
}

#[tauri::command]
#[specta::specta]
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
