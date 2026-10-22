//! The dashboard's read of the player's own record (`lcu::record`) and of one
//! game opened in full (`lcu::detail`).
//!
//! Asked for by the frontend rather than pushed with the state: the record only
//! moves when a game ends, and it costs a request per game the first time, so
//! following it on every client event would be all cost and no news.

use std::collections::HashMap;
use std::sync::{Arc, Mutex, RwLock};

use lcu::detail::GameTimeline;
use lcu::record::HistoryGame;
use lcu::{GameDetail, LcuClient, PlayerGame, PlayerRecord, Scoreboard};
use tokio::task::JoinSet;

/// The client the supervisor is attached to, for commands that read it on
/// demand. `None` whenever no client is — including a tape replay or the
/// simulator, which stand in for the client's events but not for its API.
pub type SharedClient = Arc<RwLock<Option<Arc<LcuClient>>>>;

/// Full scoreboards and timelines already read, by game. A finished game never
/// changes, so each is asked for once per launch: a refresh after a game only
/// reads the new one. Both are read with the game's row, since its performance
/// score grades the ten players on their scoreboard lines and their timeline
/// marks (#2075); opening the row then reads nothing new.
#[derive(Default)]
pub struct GameCache {
    games: Mutex<HashMap<i64, Arc<HistoryGame>>>,
    timelines: Mutex<HashMap<i64, Arc<GameTimeline>>>,
}

/// How many games the dashboard reads at once — the client's own page of
/// history. The record is the latest page; older pages are read as the match
/// history is paged back.
const HISTORY_LENGTH: usize = 20;

/// Games read at once, each its scoreboard and its timeline. The client
/// forwards these to Riot, so they are paced rather than fired twenty at a time.
const GAMES_AT_ONCE: usize = 4;

pub(crate) fn attached(client: &SharedClient) -> Result<Arc<LcuClient>, String> {
    client
        .read()
        .expect("client lock poisoned")
        .clone()
        .ok_or_else(|| "the League client is not running".to_string())
}

/// The player's standing, profile background and latest games.
///
/// Only the history is required: a failed ranked or profile read leaves its
/// card empty, and a failed scoreboard leaves that game's kill participation,
/// damage share and teams unknown — the rest of the page still draws.
#[tauri::command]
pub async fn player_record(
    client: tauri::State<'_, SharedClient>,
    cache: tauri::State<'_, GameCache>,
) -> Result<PlayerRecord, String> {
    let client = attached(&client)?;

    let (ranked, profile, history) = tokio::join!(
        client.ranked_stats(),
        client.summoner_profile(),
        client.match_history(0, HISTORY_LENGTH),
    );
    let history = history.map_err(|error| format!("could not read the match history: {error}"))?;
    let platform_id = Some(history.platform_id).filter(|id| !id.is_empty());
    let games = player_games(&client, &cache, &history.games.games).await;

    Ok(PlayerRecord {
        platform_id,
        ranked: ranked
            .inspect_err(|error| tracing::debug!(%error, "no ranked stats"))
            .map(|stats| stats.rift_queues())
            .unwrap_or_default(),
        background_skin_id: profile
            .ok()
            .map(|profile| profile.background_skin_id)
            .filter(|id| *id > 0),
        games,
    })
}

/// The page of the player's games after the latest `begin`, for the match
/// history paged back past the record. Empty once the client has no older
/// games to give.
#[tauri::command]
pub async fn player_history(
    client: tauri::State<'_, SharedClient>,
    cache: tauri::State<'_, GameCache>,
    begin: usize,
) -> Result<Vec<PlayerGame>, String> {
    let client = attached(&client)?;
    let history = client
        .match_history(begin, HISTORY_LENGTH)
        .await
        .map_err(|error| format!("could not read the match history: {error}"))?;
    Ok(player_games(&client, &cache, &history.games.games).await)
}

/// The player's line of each game, completed from its scoreboard and its
/// timeline's marks. A remake is never graded, so its timeline is not read.
async fn player_games(
    client: &Arc<LcuClient>,
    cache: &GameCache,
    history: &[HistoryGame],
) -> Vec<PlayerGame> {
    let lines: Vec<(PlayerGame, &HistoryGame)> = history
        .iter()
        .filter_map(|game| Some((PlayerGame::from_history(game)?, game)))
        .collect();
    let graded: Vec<i64> = lines
        .iter()
        .filter(|(line, _)| !line.remake)
        .map(|(line, _)| line.game_id)
        .collect();
    let (scoreboards, timelines) = reads(client, cache, history, &graded).await;
    lines
        .into_iter()
        .map(|(line, game)| {
            let scoreboard = scoreboards.get(&game.game_id).and_then(|full| {
                let timeline = timelines.get(&game.game_id).map(Arc::as_ref);
                Scoreboard::of(full, line.participant_id, timeline)
            });
            line.with_scoreboard(scoreboard.as_ref())
        })
        .collect()
}

/// One game opened in full: the scoreboard of all ten, and from its timeline
/// the lane standings (it lists no purchases or skill points). The dashboard's
/// fallback for a game TrueMain has not ingested. A timeline that fails to
/// read leaves those empty rather than failing the panel.
#[tauri::command]
pub async fn player_game(
    client: tauri::State<'_, SharedClient>,
    cache: tauri::State<'_, GameCache>,
    game_id: i64,
) -> Result<GameDetail, String> {
    let client = attached(&client)?;

    let game = match cached(&cache.games, game_id) {
        Some(game) => game,
        None => {
            let game = Arc::new(
                client
                    .game(game_id)
                    .await
                    .map_err(|error| format!("could not read the game: {error}"))?,
            );
            remember(&cache.games, game_id, game.clone());
            game
        }
    };

    let timeline = match cached(&cache.timelines, game_id) {
        Some(timeline) => Some(timeline),
        None => read_timeline(&client, game_id)
            .await
            .inspect(|timeline| remember(&cache.timelines, game_id, timeline.clone())),
    };

    Ok(GameDetail::from_game(&game, timeline.as_deref()))
}

type Reads = (
    HashMap<i64, Arc<HistoryGame>>,
    HashMap<i64, Arc<GameTimeline>>,
);

/// The full scoreboard of every game in `history`, and the timeline of the
/// `graded` ones, from the cache or the client. One game's two reads go out
/// together, a few games at a time; a read that fails is simply missing.
async fn reads(
    client: &Arc<LcuClient>,
    cache: &GameCache,
    history: &[HistoryGame],
    graded: &[i64],
) -> Reads {
    let wanted: Vec<(i64, bool, bool)> = {
        let games = cache.games.lock().expect("cache lock poisoned");
        let timelines = cache.timelines.lock().expect("cache lock poisoned");
        history
            .iter()
            .map(|game| game.game_id)
            .map(|id| {
                let timeline = graded.contains(&id) && !timelines.contains_key(&id);
                (id, !games.contains_key(&id), timeline)
            })
            .filter(|(_, game, timeline)| *game || *timeline)
            .collect()
    };

    let mut reads = JoinSet::new();
    let mut read = Vec::new();
    for (game_id, game, timeline) in wanted {
        if reads.len() >= GAMES_AT_ONCE {
            read.extend(reads.join_next().await.and_then(Result::ok));
        }
        let client = client.clone();
        reads.spawn(async move {
            let (game, timeline) = tokio::join!(
                async {
                    if !game {
                        return None;
                    }
                    client
                        .game(game_id)
                        .await
                        .inspect_err(|error| tracing::debug!(%error, game_id, "no scoreboard"))
                        .ok()
                        .map(Arc::new)
                },
                async {
                    if timeline {
                        read_timeline(&client, game_id).await
                    } else {
                        None
                    }
                },
            );
            (game_id, game, timeline)
        });
    }
    while let Some(joined) = reads.join_next().await {
        read.extend(joined.ok());
    }

    for (game_id, game, timeline) in read {
        if let Some(game) = game {
            remember(&cache.games, game_id, game);
        }
        if let Some(timeline) = timeline {
            remember(&cache.timelines, game_id, timeline);
        }
    }
    let games = cache.games.lock().expect("cache lock poisoned");
    let timelines = cache.timelines.lock().expect("cache lock poisoned");
    let ids = history.iter().map(|game| game.game_id);
    (
        ids.clone()
            .filter_map(|id| Some((id, games.get(&id)?.clone())))
            .collect(),
        ids.filter_map(|id| Some((id, timelines.get(&id)?.clone())))
            .collect(),
    )
}

async fn read_timeline(client: &LcuClient, game_id: i64) -> Option<Arc<GameTimeline>> {
    client
        .game_timeline(game_id)
        .await
        .inspect_err(|error| tracing::debug!(%error, game_id, "no timeline"))
        .ok()
        .map(Arc::new)
}

fn cached<T>(cache: &Mutex<HashMap<i64, Arc<T>>>, game_id: i64) -> Option<Arc<T>> {
    cache
        .lock()
        .expect("cache lock poisoned")
        .get(&game_id)
        .cloned()
}

fn remember<T>(cache: &Mutex<HashMap<i64, Arc<T>>>, game_id: i64, value: Arc<T>) {
    cache
        .lock()
        .expect("cache lock poisoned")
        .insert(game_id, value);
}
