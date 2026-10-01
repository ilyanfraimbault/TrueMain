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
/// reads the new one, and opening a row reads its timeline once.
#[derive(Default)]
pub struct GameCache {
    games: Mutex<HashMap<i64, Arc<HistoryGame>>>,
    timelines: Mutex<HashMap<i64, Arc<GameTimeline>>>,
}

/// How many games the dashboard reads at once — the client's own page of
/// history. The record is the latest page; older pages are read as the match
/// history is paged back.
const HISTORY_LENGTH: usize = 20;

/// Scoreboards asked for at once. The client forwards these to Riot, so they
/// are paced rather than fired twenty at a time.
const SCOREBOARDS_AT_ONCE: usize = 4;

fn attached(client: &SharedClient) -> Result<Arc<LcuClient>, String> {
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

/// The player's line of each game, completed from its scoreboard.
async fn player_games(
    client: &Arc<LcuClient>,
    cache: &GameCache,
    history: &[HistoryGame],
) -> Vec<PlayerGame> {
    let scoreboards = scoreboards(client, cache, history).await;
    history
        .iter()
        .filter_map(|game| {
            let line = PlayerGame::from_history(game)?;
            let participant = PlayerGame::participant_id(game)?;
            let scoreboard = scoreboards
                .get(&game.game_id)
                .and_then(|full| Scoreboard::of(full, participant));
            Some(line.with_scoreboard(scoreboard.as_ref()))
        })
        .collect()
}

/// One game opened in full: the scoreboard of all ten, and from its timeline
/// the build orders, skill orders and lane standings. A timeline that fails to
/// read leaves those empty rather than failing the panel.
#[tauri::command]
pub async fn player_game(
    client: tauri::State<'_, SharedClient>,
    cache: tauri::State<'_, GameCache>,
    game_id: i64,
) -> Result<GameDetail, String> {
    let client = attached(&client)?;

    let known = cache
        .games
        .lock()
        .expect("cache lock poisoned")
        .get(&game_id)
        .cloned();
    let game = match known {
        Some(game) => game,
        None => {
            let game = Arc::new(
                client
                    .game(game_id)
                    .await
                    .map_err(|error| format!("could not read the game: {error}"))?,
            );
            cache
                .games
                .lock()
                .expect("cache lock poisoned")
                .insert(game_id, game.clone());
            game
        }
    };

    let known = cache
        .timelines
        .lock()
        .expect("cache lock poisoned")
        .get(&game_id)
        .cloned();
    let timeline = match known {
        Some(timeline) => Some(timeline),
        None => match client.game_timeline(game_id).await {
            Ok(timeline) => {
                let timeline = Arc::new(timeline);
                cache
                    .timelines
                    .lock()
                    .expect("cache lock poisoned")
                    .insert(game_id, timeline.clone());
                Some(timeline)
            }
            Err(error) => {
                tracing::debug!(%error, game_id, "no timeline");
                None
            }
        },
    };

    Ok(GameDetail::from_game(&game, timeline.as_deref()))
}

/// The full scoreboard of every game in `history`, from the cache or the
/// client. A scoreboard that fails to read is simply missing.
async fn scoreboards(
    client: &Arc<LcuClient>,
    cache: &GameCache,
    history: &[HistoryGame],
) -> HashMap<i64, Arc<HistoryGame>> {
    let wanted: Vec<i64> = history.iter().map(|game| game.game_id).collect();
    let missing: Vec<i64> = {
        let known = cache.games.lock().expect("cache lock poisoned");
        wanted
            .iter()
            .filter(|id| !known.contains_key(id))
            .copied()
            .collect()
    };

    let mut reads = JoinSet::new();
    let mut read = Vec::new();
    for game_id in missing {
        if reads.len() >= SCOREBOARDS_AT_ONCE {
            read.extend(reads.join_next().await.and_then(Result::ok).flatten());
        }
        let client = client.clone();
        reads.spawn(async move {
            let game = client
                .game(game_id)
                .await
                .inspect_err(|error| tracing::debug!(%error, game_id, "no scoreboard"))
                .ok()?;
            Some((game_id, Arc::new(game)))
        });
    }
    while let Some(joined) = reads.join_next().await {
        read.extend(joined.ok().flatten());
    }

    let mut known = cache.games.lock().expect("cache lock poisoned");
    known.extend(read);
    wanted
        .into_iter()
        .filter_map(|id| Some((id, known.get(&id)?.clone())))
        .collect()
}
