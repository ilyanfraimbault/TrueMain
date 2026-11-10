//! The webview's two doors onto TrueMain: a GET and a POST of a route the app
//! may read (`routes.rs`).

use std::collections::HashMap;
use std::sync::Mutex;

use tauri::State;

use super::routes::{self, Method};
use super::{ApiClient, ApiError, SharedReads};
use crate::contract::ApiJson;

/// The request in flight for each `replaces` key, so a newer one aborts it.
#[derive(Default)]
pub struct ReplacedReads(Mutex<HashMap<String, tokio::task::AbortHandle>>);

/// One read the webview asked for.
struct Asked {
    method: Method,
    path: String,
    query: Vec<(String, String)>,
    body: Option<serde_json::Value>,
    replaces: Option<String>,
}

async fn forward(
    client: &ApiClient,
    reads: &SharedReads,
    replaced: &ReplacedReads,
    asked: Asked,
) -> Result<ApiJson, ApiError> {
    let Asked {
        method,
        path,
        query,
        body,
        replaces,
    } = asked;
    let route = routes::route(method, &path).ok_or_else(|| ApiError::NotAllowed {
        message: format!("{path} is not readable from the app"),
    })?;
    let deadline = route.deadline();

    let Some(key) = replaces else {
        // Asked identically by two webviews at once, it is sent once.
        let shared = format!("{method:?} {path} {query:?} {body:?}");
        return reads
            .read(shared, || {
                client.read(method, &path, &query, body.as_ref(), deadline)
            })
            .await
            .map(ApiJson);
    };

    // Champion select moves faster than the API answers: a lock landing while
    // a request is out makes its answer obsolete, so a newer request under the
    // same key aborts it rather than queueing behind it.
    let client = client.clone();
    let task = tokio::spawn(async move {
        client
            .read(method, &path, &query, body.as_ref(), deadline)
            .await
    });
    let previous = replaced
        .0
        .lock()
        .expect("replaced reads poisoned")
        .insert(key, task.abort_handle());
    if let Some(previous) = previous {
        previous.abort();
    }
    match task.await {
        Ok(answer) => answer.map(ApiJson),
        Err(error) if error.is_cancelled() => Err(ApiError::Superseded),
        Err(error) => Err(ApiError::Unreachable {
            message: format!("the request failed: {error}"),
        }),
    }
}

/// One read-only GET of a route, with its query as key/value pairs.
#[tauri::command]
#[specta::specta]
pub async fn api_get(
    client: State<'_, ApiClient>,
    reads: State<'_, SharedReads>,
    replaced: State<'_, ReplacedReads>,
    path: String,
    query: Vec<(String, String)>,
) -> Result<ApiJson, ApiError> {
    let asked = Asked {
        method: Method::Get,
        path,
        query,
        body: None,
        replaces: None,
    };
    forward(&client, &reads, &replaced, asked).await
}

/// One POST read of a route — a read that takes a draft or a game as its body.
/// `replaces`: a key a newer request under the same key supersedes this one by.
#[tauri::command]
#[specta::specta]
pub async fn api_post(
    client: State<'_, ApiClient>,
    reads: State<'_, SharedReads>,
    replaced: State<'_, ReplacedReads>,
    path: String,
    query: Vec<(String, String)>,
    request: ApiJson,
    replaces: Option<String>,
) -> Result<ApiJson, ApiError> {
    let asked = Asked {
        method: Method::Post,
        path,
        query,
        body: Some(request.0),
        replaces,
    };
    forward(&client, &reads, &replaced, asked).await
}
