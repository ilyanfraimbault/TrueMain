//! The loading screen (#1753): who is in the game and how each player has
//! been doing — games on the champion they are on and how they went, and
//! their latest games one by one — read through the player's own League
//! client, never on TrueMain's Riot key.
//!
//! Read once the phase is `InProgress`: champion select hides the other
//! team's names in ranked, the loading screen shows them, and that is the line
//! kept. The Live Client API does not answer until the game has loaded, so the
//! roster comes from the client's gameflow session. Each player's history is
//! one client request; three run at a time, ours first and our lane
//! opponent's next, so the most useful line is never the last to fill. Each
//! line is sent as it lands.
//!
//! The session lists the players by puuid, without their names since the
//! Riot ID migration: each name is read by puuid, with the history.
//!
//! A player who hides their name (Streamer Mode) stays anonymous: their
//! champion and lane show, never their name, and their history is not read.

use std::sync::{Arc, Mutex};
use std::time::Duration;

use lcu::{GameflowPhase, GameflowPlayer, LcuClient, PlayerForm, FORM_GAMES};
use serde::Serialize;
use shell_state::AppState;
use tauri::async_runtime::JoinHandle;
use tauri::{AppHandle, Emitter, Manager};
use tokio::sync::Semaphore;

use crate::record::SharedClient;

/// The players changed: the payload is the new `LoadingView`.
pub const EVENT: &str = "loading://players";

/// The session lists the players a moment after the phase turns; asked again
/// this often, this many times.
const SESSION_RETRY: Duration = Duration::from_secs(2);
const SESSION_TRIES: u32 = 15;
/// Client requests in flight at once.
const PARALLEL_READS: usize = 3;

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct LoadingPlayer {
    /// Empty for an anonymous player, and until the name is read.
    pub riot_id: String,
    pub champion_id: i64,
    /// `ORDER` (blue side) or `CHAOS`.
    pub team: &'static str,
    pub position: String,
    pub is_me: bool,
    /// The player hides their name: nothing about them is read.
    pub anonymous: bool,
    /// Absent until read, and for good when the client could not read it.
    pub form: Option<PlayerForm>,
    pub failed: bool,
}

#[derive(Debug, Clone, Default, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct LoadingView {
    pub players: Vec<LoadingPlayer>,
}

#[derive(Default)]
pub struct Loading {
    view: Mutex<LoadingView>,
    task: Mutex<Option<JoinHandle<()>>>,
}

pub type SharedLoading = Arc<Loading>;

/// Follow the phase: read the players when a game is in progress, clear
/// them when it is not. Called with every state the app publishes.
pub fn follow(app: &AppHandle, state: &AppState) {
    let loading = app.state::<SharedLoading>().inner().clone();
    let mut task = loading.task.lock().expect("loading task poisoned");
    if state.phase != GameflowPhase::InProgress {
        if let Some(running) = task.take() {
            running.abort();
        }
        let cleared = {
            let mut view = loading.view.lock().expect("loading view poisoned");
            !std::mem::take(&mut view.players).is_empty()
        };
        if cleared {
            let _ = app.emit(EVENT, LoadingView::default());
        }
        return;
    }
    if task.is_some() {
        return;
    }
    let client = app
        .state::<SharedClient>()
        .read()
        .expect("client lock poisoned")
        .clone();
    let Some(client) = client else {
        return;
    };
    let riot_id = state.riot_id.clone();
    let handle = app.clone();
    let shared = loading.clone();
    *task = Some(tauri::async_runtime::spawn(async move {
        let puuid = client
            .current_summoner()
            .await
            .map(|summoner| summoner.puuid)
            .unwrap_or_default();
        let me = Me {
            puuid,
            riot_id: riot_id.unwrap_or_default(),
        };
        read(&handle, &shared, &client, &me).await;
    }));
}

#[tauri::command]
pub fn loading_players(loading: tauri::State<'_, SharedLoading>) -> LoadingView {
    loading.view.lock().expect("loading view poisoned").clone()
}

fn publish(app: &AppHandle, loading: &Loading, change: impl FnOnce(&mut LoadingView)) {
    let view = {
        let mut view = loading.view.lock().expect("loading view poisoned");
        change(&mut view);
        view.clone()
    };
    let _ = app.emit(EVENT, view);
}

/// Who we are, to find our own line: by puuid, the session's key, or by name.
#[derive(Debug, Default)]
struct Me {
    puuid: String,
    riot_id: String,
}

impl Me {
    fn is(&self, player: &GameflowPlayer) -> bool {
        let riot_id = player.riot_id();
        (!self.puuid.is_empty() && self.puuid == player.puuid)
            || (!riot_id.is_empty() && self.riot_id.eq_ignore_ascii_case(&riot_id))
    }
}

async fn read(app: &AppHandle, loading: &SharedLoading, client: &Arc<LcuClient>, me: &Me) {
    let Some((players, puuids)) = roster(client, me).await else {
        tracing::info!("the game's roster could not be read from the client");
        return;
    };
    publish(app, loading, |view| view.players = players.clone());

    let gate = Arc::new(Semaphore::new(PARALLEL_READS));
    let mut reads = tokio::task::JoinSet::new();
    for (index, puuid) in puuids.into_iter().enumerate() {
        let Some(puuid) = puuid else {
            continue;
        };
        let (gate, client) = (gate.clone(), client.clone());
        let champion = players[index].champion_id;
        let named = !players[index].riot_id.is_empty();
        reads.spawn(async move {
            let _permit = gate.acquire_owned().await;
            let name = if named {
                None
            } else {
                match client.summoner_by_puuid(&puuid).await {
                    Ok(summoner) if !summoner.game_name.is_empty() => Some(summoner.riot_id()),
                    Ok(_) => None,
                    Err(error) => {
                        tracing::debug!(%error, "a player's name could not be read");
                        None
                    }
                }
            };
            let history = client.match_history_of(&puuid, FORM_GAMES).await;
            (
                index,
                name,
                history.map(|history| PlayerForm::from_history(&history, champion)),
            )
        });
    }
    while let Some(Ok((index, name, form))) = reads.join_next().await {
        publish(app, loading, |view| {
            if let Some(player) = view.players.get_mut(index) {
                if let Some(name) = name {
                    player.riot_id = name;
                }
                match form {
                    Ok(form) => player.form = Some(form),
                    Err(error) => {
                        tracing::debug!(%error, "a player's history could not be read");
                        player.failed = true;
                    }
                }
            }
        });
    }
}

/// The ten players, ours first and our lane opponent next, with their puuids
/// in the same order — none for an anonymous player, whose history is not
/// read. `None` when the session never listed them.
async fn roster(client: &LcuClient, me: &Me) -> Option<(Vec<LoadingPlayer>, Vec<Option<String>>)> {
    for _ in 0..SESSION_TRIES {
        if let Ok(Some(session)) = client.gameflow_session().await {
            let data = session.game_data;
            if !data.team_one.is_empty() || !data.team_two.is_empty() {
                return Some(ordered(&data.team_one, &data.team_two, me));
            }
        }
        tokio::time::sleep(SESSION_RETRY).await;
    }
    None
}

fn ordered(
    blue: &[GameflowPlayer],
    red: &[GameflowPlayer],
    me: &Me,
) -> (Vec<LoadingPlayer>, Vec<Option<String>>) {
    let mut rows: Vec<(LoadingPlayer, Option<String>)> = blue
        .iter()
        .map(|player| (player, "ORDER"))
        .chain(red.iter().map(|player| (player, "CHAOS")))
        .map(|(player, team)| {
            let riot_id = player.riot_id();
            let is_me = me.is(player);
            // Our own line is ours to read, whatever the others are shown.
            let anonymous = !is_me && player.is_anonymous();
            (
                LoadingPlayer {
                    riot_id: if anonymous { String::new() } else { riot_id },
                    champion_id: player.champion_id,
                    team,
                    position: player.selected_position.to_uppercase(),
                    is_me,
                    anonymous,
                    form: None,
                    failed: false,
                },
                (!anonymous).then(|| player.puuid.clone()),
            )
        })
        .collect();
    let ours = rows
        .iter()
        .find(|(player, _)| player.is_me)
        .map(|(player, _)| (player.team, player.position.clone()));
    let rank = |player: &LoadingPlayer| match &ours {
        _ if player.is_me => 0,
        Some((team, position))
            if player.team != *team && !position.is_empty() && player.position == *position =>
        {
            1
        }
        _ => 2,
    };
    // A stable sort: past the first two, the game's own order.
    rows.sort_by_key(|(player, _)| rank(player));
    rows.into_iter().unzip()
}

#[cfg(test)]
mod tests {
    use super::*;

    fn me() -> Me {
        Me {
            puuid: String::new(),
            riot_id: "me#euw".into(),
        }
    }

    fn player(name: &str, position: &str) -> GameflowPlayer {
        GameflowPlayer {
            puuid: format!("puuid-{name}"),
            champion_id: 1,
            game_name: name.into(),
            tag_line: "EUW".into(),
            selected_position: position.into(),
            ..GameflowPlayer::default()
        }
    }

    #[test]
    fn ours_first_then_our_lane_opponent_then_the_games_order() {
        let blue = [player("Ally", "TOP"), player("Me", "MIDDLE")];
        let red = [player("Foe", "TOP"), player("Mirror", "MIDDLE")];
        let (players, puuids) = ordered(&blue, &red, &me());
        let names: Vec<&str> = players.iter().map(|p| p.riot_id.as_str()).collect();
        assert_eq!(names, ["Me#EUW", "Mirror#EUW", "Ally#EUW", "Foe#EUW"]);
        assert_eq!(puuids[1].as_deref(), Some("puuid-Mirror"));
        assert!(players[0].is_me);
        assert_eq!(players[1].team, "CHAOS");
    }

    #[test]
    fn an_anonymous_player_keeps_their_champion_and_lane_only() {
        let hidden = GameflowPlayer {
            puuid: String::new(),
            champion_id: 81,
            ..player("Streamer", "BOTTOM")
        };
        let blue = [player("Me", "MIDDLE")];
        let red = [hidden, player("Foe", "TOP")];
        let (players, puuids) = ordered(&blue, &red, &me());
        let anonymous = players
            .iter()
            .position(|p| p.anonymous)
            .expect("one anonymous");
        assert_eq!(
            players[anonymous].riot_id, "",
            "the name the client sent is never kept"
        );
        assert_eq!(players[anonymous].champion_id, 81);
        assert_eq!(players[anonymous].position, "BOTTOM");
        assert_eq!(puuids[anonymous], None, "their history is not read");
        assert_eq!(players.iter().filter(|p| p.anonymous).count(), 1);
    }

    #[test]
    fn a_ranked_session_marking_everyone_hidden_hides_no_one() {
        // The session keeps the champion select's `nameVisibilityType: HIDDEN`
        // on every ranked player, while sending their names and puuids.
        let hidden = |name: &str, position: &str| {
            serde_json::from_value::<GameflowPlayer>(serde_json::json!({
                "puuid": format!("puuid-{name}"),
                "gameName": name,
                "tagLine": "EUW",
                "selectedPosition": position,
                "nameVisibilityType": "HIDDEN",
            }))
            .expect("a gameflow player")
        };
        let blue = [hidden("Me", "MIDDLE"), hidden("Ally", "TOP")];
        let red = [hidden("Foe", "TOP"), hidden("Mirror", "MIDDLE")];
        let (players, puuids) = ordered(&blue, &red, &me());
        assert!(players
            .iter()
            .all(|p| !p.anonymous && !p.riot_id.is_empty()));
        assert!(puuids.iter().all(Option::is_some));
    }

    #[test]
    fn the_sessions_nameless_players_are_known_by_puuid_not_anonymous() {
        let nameless = |name: &str, position: &str| GameflowPlayer {
            game_name: String::new(),
            tag_line: String::new(),
            ..player(name, position)
        };
        let me = Me {
            puuid: "puuid-Me".into(),
            riot_id: "Me#EUW".into(),
        };
        let (players, puuids) = ordered(
            &[nameless("Ally", "TOP"), nameless("Me", "MIDDLE")],
            &[nameless("Mirror", "MIDDLE")],
            &me,
        );
        assert!(players[0].is_me, "ours, found by puuid");
        assert_eq!(
            puuids[1].as_deref(),
            Some("puuid-Mirror"),
            "our lane opponent next"
        );
        assert!(players.iter().all(|p| !p.anonymous));
        assert!(
            puuids.iter().all(Option::is_some),
            "every name and history is read"
        );
    }
}
