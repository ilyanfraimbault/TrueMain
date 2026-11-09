//! The loading screen (#1753) and the Game page's player lines (#1828): who
//! is in the game and how each player has been doing — games on the champion
//! they are on and how they went, the roles they were given, their streak and
//! latest games, and their ranked standing — read through the player's own
//! League client, never on TrueMain's Riot key.
//!
//! Read once the phase is `InProgress`: champion select hides the other
//! team's names in ranked, the loading screen shows them, and that is the line
//! kept. The Live Client API does not answer until the game has loaded, so the
//! roster comes from the client's gameflow session. Each player's history is
//! one client request and their standing another, queued after every history;
//! three requests run at a time, ours first and our lane
//! opponent's next, so the most useful line is never the last to fill. Each
//! line is sent as it lands. What each read stands at, and the board's own
//! status, are explicit states (`view.rs`, #2472): a read whose task dies
//! fails that field instead of leaving it pending, and once every read is over
//! nothing is left pending; a roster the client never lists says so.
//!
//! The players are kept through a reconnect, and cleared once no game runs.
//!
//! Once a player's history lands, their games on the champion they are on are
//! looked for further back if need be, and each one's timeline read for the
//! fifteen-minute gaps against its lane opponent (#1863) — queued behind every
//! history and standing, one timeline per permit.
//!
//! A player the session lists without their name has it read by puuid,
//! with their history.
//!
//! A player who hides their name (Streamer Mode) stays anonymous: their
//! champion and lane show, never their name, and their history is not read.
//! The session's teams leave such a player out; they are put back from its
//! champion selections (`GameflowGameData::teams`).

use std::collections::HashMap;
use std::sync::{Arc, Mutex};
use std::time::Duration;

use lcu::laning::LaneGaps;
use lcu::record::MatchHistory;
use lcu::{
    gaps_at_fifteen, laning_games, GameflowPlayer, LaningForm, LcuClient,
    FORM_GAMES, LANING_DEPTH, LANING_GAMES,
};
use shell_state::AppState;
use tauri::async_runtime::JoinHandle;
use tauri::{AppHandle, Manager};
use tokio::sync::Semaphore;
use tokio::task::{Id, JoinSet};

use crate::record::SharedClient;

mod view;

pub use view::LoadingView;
use view::{Answer, Field, Identity, Known, LoadingPlayer, Team};

/// The players changed.
#[derive(Clone, serde::Serialize, specta::Type, tauri_specta::Event)]
#[tauri_specta(event_name = "loading://players")]
pub struct LoadingChanged(pub LoadingView);

/// The session lists the players a moment after the phase turns; asked again
/// this often, this many times.
const SESSION_RETRY: Duration = Duration::from_secs(2);
const SESSION_TRIES: u32 = 15;
/// Client requests in flight at once.
const PARALLEL_READS: usize = 3;

#[derive(Default)]
pub struct Loading {
    view: Mutex<LoadingView>,
    task: Mutex<Option<JoinHandle<()>>>,
}

pub type SharedLoading = Arc<Loading>;

/// Follow the phase: read the players once the game is in progress, keep them
/// through a reconnect, clear them once no game runs. Called with every state
/// the app publishes.
pub fn follow(app: &AppHandle, state: &AppState) {
    let loading = app.state::<SharedLoading>().inner().clone();
    let mut task = loading.task.lock().expect("loading task poisoned");
    if !state.phase.is_game_running() {
        if let Some(running) = task.take() {
            running.abort();
        }
        let cleared = {
            let mut view = loading.view.lock().expect("loading view poisoned");
            std::mem::take(&mut *view) != LoadingView::default()
        };
        if cleared {
            crate::contract::emit(app, LoadingChanged(LoadingView::default()));
        }
        return;
    }
    if task.is_some() || !state.phase.is_game_api_up() {
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
    publish(app, &loading, |view| *view = LoadingView::reading());
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
#[specta::specta]
pub fn loading_players(loading: tauri::State<'_, SharedLoading>) -> LoadingView {
    loading.view.lock().expect("loading view poisoned").clone()
}

fn publish(app: &AppHandle, loading: &Loading, change: impl FnOnce(&mut LoadingView)) {
    let view = {
        let mut view = loading.view.lock().expect("loading view poisoned");
        change(&mut view);
        view.clone()
    };
    crate::contract::emit(app, LoadingChanged(view));
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

/// The reads in flight, each known by its task: a task that panics or is
/// cancelled still says whose read it was, so that field fails instead of
/// staying pending.
struct Reads {
    set: JoinSet<(usize, Answer)>,
    of: HashMap<Id, (usize, Field)>,
}

impl Reads {
    fn spawn(
        &mut self,
        index: usize,
        field: Field,
        read: impl std::future::Future<Output = Answer> + Send + 'static,
    ) {
        let task = self.set.spawn(async move { (index, read.await) });
        self.of.insert(task.id(), (index, field));
    }
}

async fn read(app: &AppHandle, loading: &SharedLoading, client: &Arc<LcuClient>, me: &Me) {
    let Some(players) = roster(client, me).await else {
        tracing::info!("the game's roster could not be read from the client");
        publish(app, loading, LoadingView::roster_failed);
        return;
    };
    let readable: Vec<(usize, Known, i64)> = players
        .iter()
        .enumerate()
        .filter_map(|(index, player)| {
            Some((index, Known::clone(player.known()?), player.champion_id))
        })
        .collect();
    publish(app, loading, |view| view.roster_read(players));

    let gate = Arc::new(Semaphore::new(PARALLEL_READS));
    let mut reads = Reads {
        set: JoinSet::new(),
        of: HashMap::new(),
    };
    // Every history is queued before any standing: the semaphore hands out its
    // permits in order, so the standings never hold up the form lines.
    for (index, known, _) in &readable {
        let (gate, client) = (gate.clone(), client.clone());
        let (puuid, named) = (known.puuid.clone(), !known.riot_id.is_empty());
        reads.spawn(*index, Field::Form, async move {
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
            Answer::Form { name, history }
        });
    }
    for (index, known, _) in &readable {
        let (gate, client) = (gate.clone(), client.clone());
        let puuid = known.puuid.clone();
        reads.spawn(*index, Field::Rank, async move {
            let _permit = gate.acquire_owned().await;
            Answer::Rank(client.ranked_stats_of(&puuid).await)
        });
    }
    while let Some(joined) = reads.set.join_next_with_id().await {
        let (id, index, answer) = match joined {
            Ok((id, (index, answer))) => (id, index, answer),
            Err(error) => {
                if let Some((index, field)) = reads.of.remove(&error.id()) {
                    tracing::warn!(%error, ?field, "a player's read ended without an answer");
                    publish(app, loading, |view| view.failed(index, field));
                }
                continue;
            }
        };
        reads.of.remove(&id);
        if let Answer::Form {
            history: Ok(history),
            ..
        } = &answer
        {
            if let Some((_, known, champion)) = readable.iter().find(|(i, ..)| *i == index) {
                let (gate, client) = (gate.clone(), client.clone());
                let (history, puuid, champion) = (history.clone(), known.puuid.clone(), *champion);
                reads.spawn(index, Field::Laning, async move {
                    Answer::Laning(laning(&gate, &client, &puuid, history, champion).await)
                });
            }
        }
        publish(app, loading, |view| view.answer(index, answer));
    }
    publish(app, loading, LoadingView::done);
}

/// The player's form on `champion`: their games on it in `history`, looked for
/// deeper while fewer than `LANING_GAMES` turn up, and each one's gaps at
/// fifteen minutes. A page or a timeline the client does not serve only
/// narrows what is weighed.
async fn laning(
    gate: &Semaphore,
    client: &LcuClient,
    puuid: &str,
    mut history: MatchHistory,
    champion: i64,
) -> LaningForm {
    let mut read = history.games.games.len();
    while laning_games(&history, champion).len() < LANING_GAMES
        && (FORM_GAMES..LANING_DEPTH).contains(&read)
    {
        let page = {
            let _permit = gate.acquire().await;
            client.match_history_page_of(puuid, read, FORM_GAMES).await
        };
        let Ok(page) = page else { break };
        let more = page.games.games.len();
        history.games.games.extend(page.games.games);
        read += more;
        if more < FORM_GAMES {
            break;
        }
    }
    let games = laning_games(&history, champion);
    let mut gaps: Vec<Option<LaneGaps>> = Vec::with_capacity(games.len());
    for game in &games {
        let Some((me, opponent)) = game.slots else {
            gaps.push(None);
            continue;
        };
        let timeline = {
            let _permit = gate.acquire().await;
            client.game_timeline(game.game_id).await
        };
        gaps.push(
            timeline
                .ok()
                .and_then(|timeline| gaps_at_fifteen(&timeline, me, opponent)),
        );
    }
    LaningForm::new(&games, &gaps)
}

/// The ten players, ours first and our lane opponent next. `None` when the
/// session never listed them.
async fn roster(client: &LcuClient, me: &Me) -> Option<Vec<LoadingPlayer>> {
    for _ in 0..SESSION_TRIES {
        if let Ok(Some(session)) = client.gameflow_session().await {
            let data = session.game_data;
            if !data.team_one.is_empty() || !data.team_two.is_empty() {
                let (blue, red) = data.teams(&me.puuid);
                return Some(ordered(&blue, &red, me));
            }
        }
        tokio::time::sleep(SESSION_RETRY).await;
    }
    None
}

fn ordered(blue: &[GameflowPlayer], red: &[GameflowPlayer], me: &Me) -> Vec<LoadingPlayer> {
    let mut rows: Vec<LoadingPlayer> = blue
        .iter()
        .map(|player| (player, Team::Order))
        .chain(red.iter().map(|player| (player, Team::Chaos)))
        .map(|(player, team)| {
            let is_me = me.is(player);
            // Our own line is ours to read, whatever the others are shown.
            let identity = if !is_me && player.is_anonymous() {
                Identity::Hidden
            } else {
                Identity::Known(Box::new(Known::new(player.riot_id(), player.puuid.clone())))
            };
            LoadingPlayer {
                champion_id: player.champion_id,
                team,
                position: player.selected_position.to_uppercase(),
                is_me,
                identity,
            }
        })
        .collect();
    let ours = rows
        .iter()
        .find(|player| player.is_me)
        .map(|player| (player.team, player.position.clone()));
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
    rows.sort_by_key(rank);
    rows
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

    fn name(player: &LoadingPlayer) -> &str {
        player.known().map_or("", |known| known.riot_id.as_str())
    }

    fn puuid(player: &LoadingPlayer) -> Option<&str> {
        player.known().map(|known| known.puuid.as_str())
    }

    #[test]
    fn ours_first_then_our_lane_opponent_then_the_games_order() {
        let blue = [player("Ally", "TOP"), player("Me", "MIDDLE")];
        let red = [player("Foe", "TOP"), player("Mirror", "MIDDLE")];
        let players = ordered(&blue, &red, &me());
        let names: Vec<&str> = players.iter().map(name).collect();
        assert_eq!(names, ["Me#EUW", "Mirror#EUW", "Ally#EUW", "Foe#EUW"]);
        assert_eq!(puuid(&players[1]), Some("puuid-Mirror"));
        assert!(players[0].is_me);
        assert_eq!(players[1].team, Team::Chaos);
        assert!(players
            .iter()
            .filter_map(LoadingPlayer::known)
            .all(|known| known.form.is_pending() && known.rank.is_pending()));
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
        let players = ordered(&blue, &red, &me());
        let anonymous = players
            .iter()
            .position(|p| p.identity == Identity::Hidden)
            .expect("one anonymous");
        assert_eq!(players[anonymous].champion_id, 81);
        assert_eq!(players[anonymous].position, "BOTTOM");
        assert_eq!(
            puuid(&players[anonymous]),
            None,
            "neither their name nor their history is kept or read"
        );
        assert_eq!(players.iter().filter(|p| p.known().is_none()).count(), 1);
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
        let players = ordered(&blue, &red, &me());
        assert!(players.iter().all(|p| !name(p).is_empty()));
        assert!(players.iter().all(|p| puuid(p).is_some()));
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
        let players = ordered(
            &[nameless("Ally", "TOP"), nameless("Me", "MIDDLE")],
            &[nameless("Mirror", "MIDDLE")],
            &me,
        );
        assert!(players[0].is_me, "ours, found by puuid");
        assert_eq!(
            puuid(&players[1]),
            Some("puuid-Mirror"),
            "our lane opponent next"
        );
        assert!(
            players.iter().all(|p| puuid(p).is_some()),
            "every name and history is read"
        );
    }
}
